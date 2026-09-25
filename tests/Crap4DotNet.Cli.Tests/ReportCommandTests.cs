using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Crap4DotNet.Cli.Tests;

/// <summary>
/// Runs <c>report</c> against the agent-skills conformance scenarios (copied under
/// <c>Scenarios/</c>) and pins the cargo-crap 0.5.0 statuses its fixtures record.
/// </summary>
[Collection("Console")]
public sealed class ReportCommandTests : IDisposable
{
    private static readonly string ScenarioDir = Path.Combine(AppContext.BaseDirectory, "Scenarios");
    private readonly string _tempDir;

    /// <summary>cargo-crap 0.5.0's statuses for the Rust twin (fixture <c>rust-main.delta.json</c>).</summary>
    private static readonly string[] MainDeltaStatuses =
    [
            "unchanged Probe.App.Program.Main(string[])",
            "unchanged Probe.App.Program.Main(string[])",
            "unchanged Probe.Extra.Small()",
            "unchanged Probe.Gate.Covered(int)",
            "regressed Probe.Gate.Crosser(int)",
            "new Probe.Gate.Fresh(int)",
            "improved Probe.Gate.Improver(int)",
            "regressed Probe.Gate.OldMonster(int)",
            "unchanged Probe.Gate.Stable(int)",
            "moved Probe.Mover.Run(int)",
            "unchanged Probe.Shapes.Area(double)",
            "unchanged Probe.Shapes.Area(int)",
            "new Probe.Shapes.Perimeter(int)"
    ];

    public ReportCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "crap4dotnet-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Delta_MatchesCargoCrapStatuses()
    {
        var (root, before) = BeforeReport();

        var delta = Report(Stage("dotnet-main", "after", root), "--baseline", before);

        Statuses(delta).Should().Equal(MainDeltaStatuses);
        var moved = Entry(delta, "Probe.Mover.Run(int)");
        moved["previous_file"]!.GetValue<string>().Should()
            .Be(Path.Combine(root, "src", "Probe", "Old", "Mover.cs"));
        Entry(delta, "Probe.Gate.Crosser(int)")["baseline_crap"]!.GetValue<double>().Should().Be(30);
        delta["removed"]!.AsArray().Select(r => r!["function"]!.GetValue<string>())
            .Should().Equal("Probe.Legacy.Gone()");
    }

    [Fact]
    public void PlainReport_IsAbsoluteAndMarksMissingCoverage()
    {
        var root = Path.Combine(_tempDir, "root");
        var report = Report(Stage("dotnet-main", "after", root));

        report["removed"].Should().BeNull();
        report["version"]!.GetValue<string>().Should().Be("0.2.0-esonic.1");
        report["entries"]!.AsArray().Should().OnlyContain(e =>
            e!["file"]!.GetValue<string>().StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        var small = Entry(report, "Probe.Extra.Small()");
        small["coverage"].Should().BeNull();
        small["crap"]!.GetValue<double>().Should().Be(2);
        report["warnings"]!.AsArray().Select(w => w!["code"]!.GetValue<string>())
            .Should().Contain("UNMATCHED_METHODS");
    }

    [Fact]
    public void SameMethodInTwoFiles_TakesItsOwnFilesCoverage()
    {
        var report = Report(Stage("dotnet-main", "after", Path.Combine(_tempDir, "root")));

        report["entries"]!.AsArray()
            .Where(e => e!["function"]!.GetValue<string>() == "Probe.App.Program.Main(string[])")
            .Select(e => e!["coverage"]!.GetValue<double>())
            .Should().BeEquivalentTo([0.0, 50.0]);
    }

    [Fact]
    public void MissingPolicy_OptimisticScoresAt100_SkipDrops()
    {
        var root = Stage("dotnet-main", "after", Path.Combine(_tempDir, "root"));

        Entry(Report(root, "--missing", "optimistic"), "Probe.Extra.Small()")["crap"]!.GetValue<double>()
            .Should().Be(1);
        Report(root, "--missing", "skip")["entries"]!.AsArray()
            .Should().NotContain(e => e!["function"]!.GetValue<string>() == "Probe.Extra.Small()");
    }

    [Fact]
    public void MixedSeparatorBaseline_MatchesExactly()
    {
        var (root, before) = BeforeReport();
        var doc = JsonNode.Parse(File.ReadAllText(before))!;
        var i = 0;
        foreach (var e in doc["entries"]!.AsArray())
            e!["file"] = i++ % 2 == 0 ? e["file"]!.GetValue<string>().Replace('\\', '/') : e["file"]!.GetValue<string>();
        File.WriteAllText(before, doc.ToJsonString());

        var delta = Report(Stage("dotnet-main", "after", root), "--baseline", before);

        Statuses(delta).Count(s => s.StartsWith("unchanged", StringComparison.Ordinal)).Should().Be(7);
        Entry(delta, "Probe.Mover.Run(int)")["status"]!.GetValue<string>().Should().Be("moved");
    }

    [Fact]
    public void SameNameInOneFile_PairsOnlyAgainstReRootedBaseline()
    {
        var root = Stage("dotnet-same-name", "tree", Path.Combine(_tempDir, "root"));
        var rerooted = Path.Combine(_tempDir, "rerooted.json");
        Report(root, "--output", rerooted);
        var relative = JsonNode.Parse(File.ReadAllText(rerooted))!;
        foreach (var e in relative["entries"]!.AsArray())
            e!["file"] = Path.GetRelativePath(root, e["file"]!.GetValue<string>()).Replace('\\', '/');
        var relativePath = Path.Combine(_tempDir, "relative.json");
        File.WriteAllText(relativePath, relative.ToJsonString());

        Statuses(Report(root, "--baseline", rerooted)).Should()
            .Contain(["unchanged Probe.Twins.Helper(int)", "unchanged Probe.Twins.Helper(int)"]);
        Statuses(Report(root, "--baseline", relativePath)).Should()
            .Contain(["new Probe.Twins.Helper(int)", "new Probe.Twins.Helper(int)", "unchanged Probe.Twins.A(int)"]);
    }

    [Fact]
    public void FilePaths_FollowTheSolutionPath_NotTheWorkingDirectory()
    {
        var root = Stage("dotnet-same-name", "tree", Path.Combine(_tempDir, "root"));
        var elsewhere = Directory.CreateDirectory(Path.Combine(_tempDir, "elsewhere")).FullName;
        var original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = elsewhere;
            var output = Path.Combine(_tempDir, "cwd.json");
            Invoke(["report", Path.GetRelativePath(elsewhere, Solution(root)), "--output", output]);

            JsonNode.Parse(File.ReadAllText(output))!["entries"]!.AsArray().Should().OnlyContain(e =>
                e!["file"]!.GetValue<string>() == Path.Combine(root, "src", "Probe", "Twins.cs"));
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void CargoCrapBaseline_IsReadable()
    {
        var root = Stage("dotnet-same-name", "tree", Path.Combine(_tempDir, "root"));
        var baseline = Path.Combine(_tempDir, "cargo.json");
        var file = JsonSerializer.Serialize(Path.Combine(root, "src", "Probe", "Twins.cs"));
        File.WriteAllText(baseline, $$"""
            {"$schema": "https://raw.githubusercontent.com/minikin/cargo-crap/main/schemas/report-v1.json",
             "version": "0.5.0",
             "entries": [{"file": {{file}}, "function": "Probe.Twins.A(int)", "line": 5,
                          "cyclomatic": 1.0, "coverage": null, "crap": 2.0, "crate": "x"}]}
            """);

        Entry(Report(root, "--baseline", baseline), "Probe.Twins.A(int)")["status"]!.GetValue<string>()
            .Should().Be("unchanged");
    }

    [Fact]
    public void UnreadableBaseline_Exits2()
    {
        var root = Stage("dotnet-same-name", "tree", Path.Combine(_tempDir, "root"));
        var baseline = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(baseline, """{"methods": []}""");

        var (exitCode, _, stderr) = Invoke(["report", Solution(root), "--baseline", baseline]);

        exitCode.Should().Be(2);
        stderr.Should().Contain("BASELINE_PARSE_ERROR");
    }

    [Theory]
    [InlineData("report", "--no-such-flag")]
    [InlineData("no-such-command")]
    [InlineData("report", "x.sln", "--missing", "maybe")]
    public void ParseErrors_ExitNonzero(params string[] args)
    {
        Invoke(args).exitCode.Should().NotBe(0);
    }

    // --- helpers ---

    private (string root, string baseline) BeforeReport()
    {
        var root = Stage("dotnet-main", "before", Path.Combine(_tempDir, "root"));
        var baseline = Path.Combine(_tempDir, "before.json");
        Report(root, "--output", baseline);
        return (root, baseline);
    }

    private static string Stage(string scenario, string tree, string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        var src = Path.Combine(ScenarioDir, scenario, tree);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(root, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
        return root;
    }

    private static string Solution(string root) =>
        Directory.GetFiles(root).Single(f => f.EndsWith(".sln", StringComparison.Ordinal) || f.EndsWith(".slnx", StringComparison.Ordinal));

    /// <summary>The sweep's argv: contract flags plus <paramref name="extra"/>; returns the parsed report.</summary>
    private JsonNode Report(string root, params string[] extra)
    {
        var output = extra.Contains("--output") ? null : Path.Combine(_tempDir, $"out-{Guid.NewGuid():N}.json");
        var args = new List<string>
        {
            "report", Solution(root), "--coverage", Path.Combine(root, "coverage.cobertura.xml"),
            "--format", "json", "--epsilon", "0.01", "--sort", "file"
        };
        if (!extra.Contains("--missing"))
            args.AddRange(["--missing", "pessimistic"]);
        if (output is not null)
            args.AddRange(["--output", output]);
        args.AddRange(extra);

        var (exitCode, _, stderr) = Invoke([.. args]);
        exitCode.Should().BeOneOf([0, 1], stderr);
        var path = output ?? extra[Array.IndexOf(extra, "--output") + 1];
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static List<string> Statuses(JsonNode report) =>
        report["entries"]!.AsArray()
            .Select(e => $"{e!["status"]!.GetValue<string>()} {e["function"]!.GetValue<string>()}")
            .ToList();

    private static JsonNode Entry(JsonNode report, string function) =>
        report["entries"]!.AsArray().Single(e => e!["function"]!.GetValue<string>() == function)!;

    private static (int exitCode, string stdout, string stderr) Invoke(string[] args)
    {
        Environment.ExitCode = 0;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (originalOut, originalErr) = (Console.Out, Console.Error);
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Run(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }
}
