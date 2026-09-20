using Sirkadiyen.Api.Administration;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

public sealed class SelectorQueryReaderTests
{
    [Fact]
    public void PairsAreReadIntoTheSelectorsTheyState()
    {
        Assert.True(SelectorQueryReader.TryRead(
            ["curriculumGroup:3-A", "facultyPracticeGroup:A5"],
            out Dictionary<string, string> selectors,
            out string? problem));

        Assert.Null(problem);
        Assert.Equal("3-A", selectors["curriculumGroup"]);
        Assert.Equal("A5", selectors["facultyPracticeGroup"]);
    }

    [Fact]
    public void OnlyTheFirstColonSeparates()
    {
        // A value is free to contain a colon; only the first one names the key.
        Assert.True(SelectorQueryReader.TryRead(
            ["block:A:1"],
            out Dictionary<string, string> selectors,
            out _));

        Assert.Equal("A:1", selectors["block"]);
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.True(SelectorQueryReader.TryRead(
            [" curriculumGroup : 3-A "],
            out Dictionary<string, string> selectors,
            out _));

        Assert.Equal("3-A", selectors["curriculumGroup"]);
    }

    [Theory]
    [InlineData("curriculumGroup")] // No separator at all.
    [InlineData(":3-A")] // No key.
    [InlineData("curriculumGroup:")] // No value.
    public void AMalformedPairIsRefusedRatherThanSkipped(string value)
    {
        // Dropping one silently would answer a wider question than the operator asked, and
        // nothing on the screen would say so.
        Assert.False(SelectorQueryReader.TryRead([value], out _, out string? problem));

        Assert.NotNull(problem);
        Assert.Contains(value, problem);
    }

    [Fact]
    public void BlankEntriesAreIgnored()
    {
        Assert.True(SelectorQueryReader.TryRead(
            ["curriculumGroup:3-A", "   "],
            out Dictionary<string, string> selectors,
            out _));

        Assert.Single(selectors);
    }

    [Fact]
    public void NoParameterIsAnEmptyCohortRatherThanAFailure()
    {
        Assert.True(SelectorQueryReader.TryRead(null, out Dictionary<string, string> selectors, out _));

        Assert.Empty(selectors);
    }
}
