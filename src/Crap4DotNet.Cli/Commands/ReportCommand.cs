using System.CommandLine;
using System.Reflection;
using Crap4DotNet.Core.Analysis;
using Crap4DotNet.Core.Configuration;
using Crap4DotNet.Core.Coverage;
using Crap4DotNet.Core.Reporting;

namespace Crap4DotNet.Cli.Commands;

/// <summary>
/// Scores methods with cargo-crap 0.5.0's report and delta semantics: <c>--baseline</c>,
/// <c>--missing</c>, <c>--epsilon</c>, <c>--sort</c>, <c>--output</c> and <c>--format json</c>
/// mean what they mean to <c>cargo crap</c>. <c>file</c> is absolute, resolved from the
/// full path of the positional argument, so the process working directory does not matter.
/// </summary>
internal static class ReportCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string>("path", "Path to .cs file, directory, .csproj, .sln, or .slnx");
        var coverageOpt = new Option<string[]>("--coverage", "Path(s) to Cobertura XML coverage file(s)")
        {
            AllowMultipleArgumentsPerToken = true
        };
        var baselineOpt = new Option<string?>("--baseline", "Report from a previous run; adds per-method delta and status");
        var missingOpt = new Option<MissingCoveragePolicy>("--missing", () => MissingCoveragePolicy.Pessimistic,
            "Scoring of methods with no coverage entry: pessimistic (0%), optimistic (100%), or skip");
        var epsilonOpt = new Option<double>("--epsilon", () => ScoreDeltaCalculator.DefaultEpsilon,
            "Score deltas at or below this magnitude are unchanged");
        var sortOpt = new Option<ScoreSort>("--sort", () => ScoreSort.Crap,
            "Entry order: crap (score descending) or file (file, function, line)");
        var formatOpt = new Option<string>("--format", () => "json", "Output format (json only)")
            .FromAmong("json");
        var outputOpt = new Option<string?>("--output", "Write JSON to file instead of stdout");
        var thresholdOpt = new Option<int>("--threshold", () => 30, "CRAP threshold for exit code 1 (default: 30)");

        var command = new Command("report", "Score methods in cargo-crap's report / delta semantics")
        {
            pathArg,
            coverageOpt,
            baselineOpt,
            missingOpt,
            epsilonOpt,
            sortOpt,
            formatOpt,
            outputOpt,
            thresholdOpt
        };

        command.SetHandler(context =>
        {
            var parse = context.ParseResult;
            Environment.ExitCode = Execute(
                parse.GetValueForArgument(pathArg),
                parse.GetValueForOption(coverageOpt) ?? [],
                parse.GetValueForOption(baselineOpt),
                parse.GetValueForOption(missingOpt),
                parse.GetValueForOption(epsilonOpt),
                parse.GetValueForOption(sortOpt),
                parse.GetValueForOption(outputOpt),
                parse.GetValueForOption(thresholdOpt));
        });
        return command;
    }

    internal static string ToolVersion
    {
        get
        {
            var version = typeof(ReportCommand).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? version : version[..plus];
        }
    }

    private static int Execute(
        string path,
        string[] coveragePaths,
        string? baselinePath,
        MissingCoveragePolicy missing,
        double epsilon,
        ScoreSort sort,
        string? outputPath,
        int threshold)
    {
        if (epsilon < 0 || double.IsNaN(epsilon))
            return Fail("INVALID_CONFIGURATION", $"--epsilon must be a non-negative number, got: {epsilon}");
        if (threshold <= 0)
            return Fail("INVALID_THRESHOLD", $"Threshold must be greater than 0, got: {threshold}");

        List<string> sourceFiles;
        string projectName;
        try
        {
            (sourceFiles, projectName) = AnalyzeCommand.ResolveSourceFiles(Path.GetFullPath(path));
        }
        catch (CrapCliException ex)
        {
            return Fail(ex.Code, ex.Message, ex.FilePath);
        }

        if (sourceFiles.Count == 0)
            return Fail("NO_SOURCE_FILES", "No .cs source files found in the specified path", path);

        var coverageEntries = new List<CoberturaMethodCoverage>();
        foreach (var covPath in coveragePaths)
        {
            if (!File.Exists(covPath))
                return Fail("COVERAGE_FILE_NOT_FOUND", $"Coverage file not found: {covPath}", covPath);
            try
            {
                using var stream = File.OpenRead(covPath);
                coverageEntries.AddRange(CoberturaCoverageReader.Read(stream));
            }
            catch (Exception ex)
            {
                return Fail("COVERAGE_PARSE_ERROR", $"Failed to parse coverage file: {ex.Message}", covPath);
            }
        }

        IReadOnlyList<ScoreEntry>? baseline = null;
        if (baselinePath is not null)
        {
            try
            {
                baseline = ScoreReportWriter.ReadEntries(File.ReadAllText(baselinePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                return Fail("BASELINE_PARSE_ERROR", $"Failed to read baseline: {ex.Message}", baselinePath);
            }
        }

        AnalysisResult result;
        try
        {
            result = CrapAnalyzer.Analyze(sourceFiles, coverageEntries, new CrapOptions { Threshold = threshold }, projectName);
        }
        catch (Exception ex)
        {
            return Fail("SOURCE_PARSE_ERROR", $"Failed to analyze source: {ex.Message}");
        }

        var entries = ScoreReportWriter.ToEntries(result.Data, missing);
        var json = baseline is null
            ? ScoreReportWriter.Write(ToolVersion, ScoreDeltaCalculator.Sort(entries, sort), result.Warnings)
            : ScoreReportWriter.Write(ToolVersion,
                ScoreDeltaCalculator.Sort(ScoreDeltaCalculator.Compute(entries, baseline, epsilon), sort),
                result.Warnings);

        if (outputPath is null)
            Console.WriteLine(json);
        else
            File.WriteAllText(outputPath, json + Environment.NewLine);

        return entries.Any(e => e.Crap > threshold) ? 1 : 0;
    }

    private static int Fail(string code, string message, string? file = null)
    {
        ErrorOutput.WriteError(code, message, file: file);
        return 2;
    }
}
