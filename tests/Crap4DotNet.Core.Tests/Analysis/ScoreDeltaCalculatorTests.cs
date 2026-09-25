using Crap4DotNet.Core.Analysis;
using FluentAssertions;
using Xunit;

namespace Crap4DotNet.Core.Tests.Analysis;

public sealed class ScoreDeltaCalculatorTests
{
    private static ScoreEntry E(string function, double crap, string file = "/r/src/a.cs", int line = 1) =>
        new() { File = file, Function = function, Line = line, Crap = crap };

    [Theory]
    [InlineData(5.01, ScoreStatus.Unchanged)] // |delta| == epsilon is unchanged
    [InlineData(5.02, ScoreStatus.Regressed)]
    [InlineData(4.98, ScoreStatus.Improved)]
    public void Pass1_ClassifiesAgainstEpsilon(double current, ScoreStatus expected)
    {
        var delta = ScoreDeltaCalculator.Compute([E("f", current)], [E("f", 5.0)], 0.01);

        delta.Entries.Single().Status.Should().Be(expected);
        delta.Entries.Single().BaselineCrap.Should().Be(5.0);
    }

    [Fact]
    public void Pass1_ComparesPathsWithBackslashNormalized()
    {
        var delta = ScoreDeltaCalculator.Compute([E("f", 5, @"C:\r\src\a.cs")], [E("f", 5, "C:/r/src/a.cs")]);

        delta.Entries.Single().Status.Should().Be(ScoreStatus.Unchanged);
        delta.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Pass2_RootRemapIsNotAMove()
    {
        var delta = ScoreDeltaCalculator.Compute([E("f", 5, "/wt/src/a.cs", 9)], [E("f", 5, "src/a.cs", 1)]);

        delta.Entries.Single().Status.Should().Be(ScoreStatus.Unchanged);
        delta.Entries.Single().PreviousFile.Should().BeNull();
    }

    [Fact]
    public void Pass2_TiedSuffixAndDuplicateNameStayNew()
    {
        var delta = ScoreDeltaCalculator.Compute(
            [E("f", 5, "/wt/src/a.cs", 3), E("f", 7, "/wt/src/a.cs", 9)],
            [E("f", 5, "src/a.cs", 3), E("f", 7, "src/a.cs", 9)]);

        delta.Entries.Select(e => e.Status).Should().Equal(ScoreStatus.New, ScoreStatus.New);
        delta.Removed.Should().HaveCount(2);
    }

    [Fact]
    public void Pass3_UniqueNameInAnotherFileIsMoved_OrKeepsItsScoreStatus()
    {
        var delta = ScoreDeltaCalculator.Compute(
            [E("f", 5, "/r/b.cs"), E("g", 9, "/r/b.cs")],
            [E("f", 5, "/r/a.cs"), E("g", 3, "/r/c.cs")]);

        delta.Entries.Select(e => (e.Status, e.PreviousFile)).Should().Equal(
            (ScoreStatus.Moved, "/r/a.cs"), (ScoreStatus.Regressed, "/r/c.cs"));
    }

    [Fact]
    public void SortFile_OrdersEntriesAndRemoved()
    {
        var delta = ScoreDeltaCalculator.Compute(
            [E("b", 1, "/r/z.cs"), E("a", 1, "/r/z.cs", 2), E("a", 1, "/r/z.cs", 1)],
            [E("y", 1, "/r/y.cs"), E("x", 1, "/r/x.cs")]);

        var sorted = ScoreDeltaCalculator.Sort(delta, ScoreSort.File);

        sorted.Entries.Select(e => (e.Current.Function, e.Current.Line)).Should().Equal(("a", 1), ("a", 2), ("b", 1));
        sorted.Removed.Select(r => r.Function).Should().Equal("x", "y");
    }
}
