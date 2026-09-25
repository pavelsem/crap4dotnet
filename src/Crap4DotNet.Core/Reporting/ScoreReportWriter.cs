using System.Text.Json;
using System.Text.Json.Serialization;
using Crap4DotNet.Core.Analysis;
using Crap4DotNet.Core.Calculation;
using Crap4DotNet.Core.Matching;
using Crap4DotNet.Core.Models;

namespace Crap4DotNet.Core.Reporting;

/// <summary>How a method with no coverage entry is scored (cargo-crap's <c>--missing</c>).</summary>
public enum MissingCoveragePolicy
{
    Pessimistic,
    Optimistic,
    Skip
}

/// <summary>
/// Writes and reads the cargo-crap-semantics report of the <c>report</c> command: a plain
/// report (<c>entries</c>) or, with a baseline, a delta report (<c>entries</c> with
/// <c>baseline_crap</c> / <c>delta</c> / <c>status</c>, plus <c>removed</c>). Both carry the
/// native <c>warnings</c>.
/// </summary>
public static class ScoreReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    /// <summary>
    /// One entry per method. A method with no coverage entry reports <c>coverage: null</c>,
    /// scored at 0% (pessimistic) or 100% (optimistic), or is dropped (skip).
    /// </summary>
    public static IReadOnlyList<ScoreEntry> ToEntries(ProjectCrapData data, MissingCoveragePolicy missing) =>
        data.Methods
            .Where(m => m.HasCoverage || missing != MissingCoveragePolicy.Skip)
            .Select(m => new ScoreEntry
            {
                File = m.Identity.FilePath ?? string.Empty,
                Function = m.Identity.FullName,
                Line = m.Identity.LineNumber ?? 0,
                Cyclomatic = m.Complexity,
                Coverage = m.HasCoverage ? m.Coverage * 100 : null,
                Crap = m.HasCoverage || missing == MissingCoveragePolicy.Pessimistic
                    ? m.CrapScore
                    : CrapCalculator.Calculate(m.Complexity, 1.0)
            })
            .ToList();

    public static string Write(
        string version,
        IReadOnlyList<ScoreEntry> entries,
        IReadOnlyList<DiagnosticWarning> warnings) =>
        JsonSerializer.Serialize(new PlainReport
        {
            Version = version,
            Entries = entries.Select(ToJson).ToList(),
            Warnings = warnings.Select(ToJson).ToList()
        }, SerializerOptions);

    public static string Write(
        string version,
        ScoreDelta delta,
        IReadOnlyList<DiagnosticWarning> warnings) =>
        JsonSerializer.Serialize(new DeltaReport
        {
            Version = version,
            Entries = delta.Entries.Select(e => new JsonDeltaEntry
            {
                File = e.Current.File,
                Function = e.Current.Function,
                Line = e.Current.Line,
                Cyclomatic = e.Current.Cyclomatic,
                Coverage = e.Current.Coverage,
                Crap = e.Current.Crap,
                BaselineCrap = e.BaselineCrap,
                Delta = e.Delta,
                Status = e.Status,
                PreviousFile = e.PreviousFile
            }).ToList(),
            Removed = delta.Removed.Select(r => new JsonRemoved
            {
                Function = r.Function,
                File = r.File,
                BaselineCrap = r.BaselineCrap
            }).ToList(),
            Warnings = warnings.Select(ToJson).ToList()
        }, SerializerOptions);

    /// <summary>
    /// Reads the <c>entries</c> of a plain report written by this command or by
    /// <c>cargo crap --format json</c>. Only <c>file</c>, <c>function</c>, <c>line</c> and
    /// <c>crap</c> are required.
    /// </summary>
    /// <exception cref="FormatException">The document is not such a report.</exception>
    public static IReadOnlyList<ScoreEntry> ReadEntries(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("entries").EnumerateArray()
                .Select(e => new ScoreEntry
                {
                    File = e.GetProperty("file").GetString() ?? throw new FormatException("entry 'file' is null"),
                    Function = e.GetProperty("function").GetString() ?? throw new FormatException("entry 'function' is null"),
                    Line = e.GetProperty("line").GetInt32(),
                    Crap = e.GetProperty("crap").GetDouble()
                })
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"not a report with entries[] of file/function/line/crap: {ex.Message}", ex);
        }
    }

    private static JsonEntry ToJson(ScoreEntry e) => new()
    {
        File = e.File,
        Function = e.Function,
        Line = e.Line,
        Cyclomatic = e.Cyclomatic,
        Coverage = e.Coverage,
        Crap = e.Crap
    };

    private static JsonWarning ToJson(DiagnosticWarning w) => new() { Code = w.Code, Message = w.Message };

    // --- JSON DTOs ---

    private sealed class PlainReport
    {
        public required string Version { get; init; }
        public required List<JsonEntry> Entries { get; init; }
        public required List<JsonWarning> Warnings { get; init; }
    }

    private sealed class DeltaReport
    {
        public required string Version { get; init; }
        public required List<JsonDeltaEntry> Entries { get; init; }
        public required List<JsonRemoved> Removed { get; init; }
        public required List<JsonWarning> Warnings { get; init; }
    }

    private sealed class JsonEntry
    {
        public required string File { get; init; }
        public required string Function { get; init; }
        public int Line { get; init; }
        public double Cyclomatic { get; init; }
        public double? Coverage { get; init; }
        public double Crap { get; init; }
    }

    private sealed class JsonDeltaEntry
    {
        public required string File { get; init; }
        public required string Function { get; init; }
        public int Line { get; init; }
        public double Cyclomatic { get; init; }
        public double? Coverage { get; init; }
        public double Crap { get; init; }
        public double? BaselineCrap { get; init; }
        public double? Delta { get; init; }
        public ScoreStatus Status { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PreviousFile { get; init; }
    }

    private sealed class JsonRemoved
    {
        public required string Function { get; init; }
        public required string File { get; init; }
        public double BaselineCrap { get; init; }
    }

    private sealed class JsonWarning
    {
        public required string Code { get; init; }
        public required string Message { get; init; }
    }
}
