namespace Crap4DotNet.Core.Analysis;

/// <summary>
/// One scored method in the cargo-crap report shape. <see cref="Coverage"/> is a percent,
/// or null when the coverage input has no entry for the method.
/// </summary>
public sealed record ScoreEntry
{
    public required string File { get; init; }
    public required string Function { get; init; }
    public required int Line { get; init; }
    public double Cyclomatic { get; init; }
    public double? Coverage { get; init; }
    public required double Crap { get; init; }
}

public enum ScoreStatus
{
    New,
    Regressed,
    Improved,
    Unchanged,
    Moved
}

public sealed record ScoreDeltaEntry
{
    public required ScoreEntry Current { get; init; }
    public double? BaselineCrap { get; init; }
    public double? Delta { get; init; }
    public required ScoreStatus Status { get; init; }
    public string? PreviousFile { get; init; }
}

public sealed record ScoreRemovedEntry
{
    public required string Function { get; init; }
    public required string File { get; init; }
    public required double BaselineCrap { get; init; }
}

public sealed record ScoreDelta
{
    public required IReadOnlyList<ScoreDeltaEntry> Entries { get; init; }
    public required IReadOnlyList<ScoreRemovedEntry> Removed { get; init; }
}

public enum ScoreSort
{
    Crap,
    File
}

/// <summary>
/// Baseline comparison with cargo-crap 0.5.0 semantics (<c>src/delta.rs</c>). Three passes:
/// exact <c>(file, function, line)</c>; same-name entries whose files are each other's unique
/// longest common path suffix; name-only when the name is unmatched exactly once on each side.
/// Paths compare with <c>\</c> normalized to <c>/</c>.
/// </summary>
public static class ScoreDeltaCalculator
{
    public const double DefaultEpsilon = 0.01;

    public static ScoreDelta Compute(
        IReadOnlyList<ScoreEntry> current,
        IReadOnlyList<ScoreEntry> baseline,
        double epsilon = DefaultEpsilon)
    {
        var matched = new HashSet<(string, string, int)>();

        // Pass 1: exact key. A duplicate baseline key keeps the last entry, as cargo-crap does.
        var index = new Dictionary<(string, string, int), ScoreEntry>();
        foreach (var b in baseline)
            index[Key(b)] = b;

        var entries = current.Select(c =>
        {
            if (!index.TryGetValue(Key(c), out var b))
                return new ScoreDeltaEntry { Current = c, Status = ScoreStatus.New };
            matched.Add(Key(c));
            return Paired(c, b, epsilon);
        }).ToArray();

        // Pass 2: mutual unique best path suffix among same-name entries, from a snapshot.
        var newByName = NewIndicesByName(entries);
        var baselineByName = UnmatchedByName(baseline, matched);
        foreach (var (name, curGroup) in newByName)
        {
            if (!baselineByName.TryGetValue(name, out var basGroup))
                continue;
            foreach (var idx in curGroup)
            {
                var bi = UniqueBestBySuffix(entries[idx].Current.File, basGroup.Select(b => b.File).ToList());
                if (bi is null)
                    continue;
                var ci = UniqueBestBySuffix(basGroup[bi.Value].File, curGroup.Select(i => entries[i].Current.File).ToList());
                if (ci is null || curGroup[ci.Value] != idx)
                    continue;
                entries[idx] = Paired(entries[idx].Current, basGroup[bi.Value], epsilon);
                matched.Add(Key(basGroup[bi.Value]));
            }
        }

        // Pass 3: name-only, when the name is left exactly once on each side.
        newByName = NewIndicesByName(entries);
        baselineByName = UnmatchedByName(baseline, matched);
        foreach (var (name, idxs) in newByName)
        {
            if (idxs.Count != 1 || !baselineByName.TryGetValue(name, out var basGroup) || basGroup.Count != 1)
                continue;
            var b = basGroup[0];
            var paired = Paired(entries[idxs[0]].Current, b, epsilon);
            entries[idxs[0]] = paired with
            {
                PreviousFile = b.File,
                Status = paired.Status == ScoreStatus.Unchanged ? ScoreStatus.Moved : paired.Status
            };
            matched.Add(Key(b));
        }

        var removed = baseline
            .Where(b => !matched.Contains(Key(b)))
            .Select(b => new ScoreRemovedEntry { Function = b.Function, File = b.File, BaselineCrap = b.Crap })
            .ToList();

        return new ScoreDelta { Entries = entries, Removed = removed };
    }

    public static IReadOnlyList<ScoreEntry> Sort(IEnumerable<ScoreEntry> entries, ScoreSort sort) =>
        Order(entries, e => e, sort);

    /// <summary>
    /// <c>file</c> orders entries by <c>(file, function, line)</c> and removed entries by
    /// <c>(file, function)</c>; <c>crap</c> orders entries by score descending and keeps removed order.
    /// </summary>
    public static ScoreDelta Sort(ScoreDelta delta, ScoreSort sort) => new()
    {
        Entries = Order(delta.Entries, e => e.Current, sort),
        Removed = sort == ScoreSort.File
            ? delta.Removed.OrderBy(r => PathKey(r.File), StringComparer.Ordinal)
                .ThenBy(r => r.Function, StringComparer.Ordinal).ToList()
            : delta.Removed
    };

    private static List<T> Order<T>(IEnumerable<T> items, Func<T, ScoreEntry> entry, ScoreSort sort) =>
        sort == ScoreSort.File
            ? items.OrderBy(i => PathKey(entry(i).File), StringComparer.Ordinal)
                .ThenBy(i => entry(i).Function, StringComparer.Ordinal)
                .ThenBy(i => entry(i).Line).ToList()
            : items.OrderByDescending(i => entry(i).Crap).ToList();

    public static string PathKey(string path) => path.Replace('\\', '/');

    private static (string, string, int) Key(ScoreEntry e) => (PathKey(e.File), e.Function, e.Line);

    private static ScoreDeltaEntry Paired(ScoreEntry current, ScoreEntry baseline, double epsilon)
    {
        var d = current.Crap - baseline.Crap;
        var status = d > epsilon ? ScoreStatus.Regressed
            : d < -epsilon ? ScoreStatus.Improved
            : ScoreStatus.Unchanged;
        return new ScoreDeltaEntry { Current = current, BaselineCrap = baseline.Crap, Delta = d, Status = status };
    }

    private static Dictionary<string, List<int>> NewIndicesByName(ScoreDeltaEntry[] entries)
    {
        var byName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Status != ScoreStatus.New)
                continue;
            if (!byName.TryGetValue(entries[i].Current.Function, out var list))
                byName[entries[i].Current.Function] = list = [];
            list.Add(i);
        }
        return byName;
    }

    private static Dictionary<string, List<ScoreEntry>> UnmatchedByName(
        IReadOnlyList<ScoreEntry> baseline, HashSet<(string, string, int)> matched) =>
        baseline.Where(b => !matched.Contains(Key(b)))
            .GroupBy(b => b.Function, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    private static int CommonSuffixLength(string a, string b) =>
        Enumerable.Reverse(PathKey(a).Split('/'))
            .Zip(Enumerable.Reverse(PathKey(b).Split('/')))
            .TakeWhile(p => p.First == p.Second)
            .Count();

    /// <summary>The candidate sharing the longest path suffix (at least the file name), or null on a tie.</summary>
    private static int? UniqueBestBySuffix(string from, List<string> candidates)
    {
        int? best = null;
        var bestScore = 0;
        var tied = false;
        for (var i = 0; i < candidates.Count; i++)
        {
            var score = CommonSuffixLength(from, candidates[i]);
            if (score == 0)
                continue;
            if (best is null || score > bestScore)
            {
                (best, bestScore, tied) = (i, score, false);
            }
            else if (score == bestScore)
            {
                tied = true;
            }
        }
        return tied ? null : best;
    }
}
