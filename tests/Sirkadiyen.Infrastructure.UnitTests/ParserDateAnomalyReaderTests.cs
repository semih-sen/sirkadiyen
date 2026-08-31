using System.Text.Json;
using Sirkadiyen.Application.Scheduling.Publication;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the one input revision validation takes from the parser's account of
/// itself rather than from the records (ADR-139).
/// </summary>
/// <remarks>
/// A stored parse response is data the parser produced, not a shape this side
/// controls over time, so the reader has to survive whatever it finds there.
/// Revision validation is the safety boundary in front of student calendars: it
/// must not fail closed on the shape of a diagnostic.
/// </remarks>
public sealed class ParserDateAnomalyReaderTests
{
    [Fact]
    public void ARepairedDateIsReadWithTheAnchorsThatBoundedIt()
    {
        IReadOnlyList<ParserDateAnomaly> anomalies = ParserDateAnomalyReader.Read(Response(
            new
            {
                severity = "warning",
                code = ParserDateAnomalyReader.RepairedCode,
                message = "read as a mistyped year",
                evidence = Evidence("A69"),
                detail = new
                {
                    original = "2020-11-20",
                    applied = "2026-11-20",
                    lowerAnchor = "2026-11-19",
                    upperAnchor = "2026-11-20",
                    reason = "repaired",
                    candidates = new[]
                    {
                        new { value = "2026-11-20", rule = "sequenceYearSubstitution", weekdayMatches = (bool?)null },
                    },
                },
            }));

        ParserDateAnomaly anomaly = Assert.Single(anomalies);
        Assert.True(anomaly.Repaired);
        Assert.Equal(new DateOnly(2020, 11, 20), anomaly.Original);
        Assert.Equal(new DateOnly(2026, 11, 20), anomaly.Applied);
        Assert.Equal(new DateOnly(2026, 11, 19), anomaly.LowerAnchor);
        Assert.Equal("A69", anomaly.Cell);
        Assert.Equal(new DateOnly(2026, 11, 20), Assert.Single(anomaly.Candidates).Value);
    }

    [Fact]
    public void ARefusedDateIsReadWithEveryReadingItMayHaveMeant()
    {
        IReadOnlyList<ParserDateAnomaly> anomalies = ParserDateAnomalyReader.Read(Response(
            new
            {
                severity = "warning",
                code = ParserDateAnomalyReader.SuggestedCode,
                message = "published as written",
                evidence = Evidence("A248"),
                detail = new
                {
                    original = "2026-05-21",
                    applied = (string?)null,
                    lowerAnchor = (string?)null,
                    upperAnchor = "2027-05-24",
                    reason = "candidateContradictsTheStatedWeekday",
                    candidates = new[]
                    {
                        new { value = "2027-05-21", rule = "sequenceYearSubstitution", weekdayMatches = (bool?)false },
                        new { value = "2027-05-20", rule = "sequenceWeekdayAlternative", weekdayMatches = (bool?)true },
                    },
                },
            }));

        ParserDateAnomaly anomaly = Assert.Single(anomalies);
        Assert.False(anomaly.Repaired);
        Assert.Null(anomaly.LowerAnchor);
        Assert.Equal("candidateContradictsTheStatedWeekday", anomaly.Reason);
        Assert.Equal(
            [new DateOnly(2027, 5, 21), new DateOnly(2027, 5, 20)],
            anomaly.Candidates.Select(candidate => candidate.Value));
        Assert.False(anomaly.Candidates[0].WeekdayMatches);
        Assert.True(anomaly.Candidates[1].WeekdayMatches);
    }

    [Fact]
    public void WarningsOfEveryOtherKindAreIgnored()
    {
        // Almost every warning a parse records is something else, and every one of
        // them states no detail at all.
        IReadOnlyList<ParserDateAnomaly> anomalies = ParserDateAnomalyReader.Read(Response(
            new
            {
                severity = "warning",
                code = "rowsIgnored",
                message = "Rows were ignored.",
                evidence = Evidence("A12"),
            }));

        Assert.Empty(anomalies);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void AnUnreadableResponseYieldsNoAnomaliesRatherThanThrowing(string? payload)
    {
        Assert.Empty(ParserDateAnomalyReader.Read(payload));
    }

    [Fact]
    public void ADetailWithoutAnOriginalDateCarriesNoDecisionAndIsSkipped()
    {
        // Without the date the source states there is nothing to correct from, so
        // the warning states no decision an operator could make.
        IReadOnlyList<ParserDateAnomaly> anomalies = ParserDateAnomalyReader.Read(Response(
            new
            {
                severity = "warning",
                code = ParserDateAnomalyReader.SuggestedCode,
                message = "published as written",
                evidence = Evidence("A9"),
                detail = new { reason = "noCandidateFitsTheAnchors" },
            }));

        Assert.Empty(anomalies);
    }

    [Fact]
    public void ACandidateWithAnUnreadableValueIsDroppedWithoutLosingTheOthers()
    {
        IReadOnlyList<ParserDateAnomaly> anomalies = ParserDateAnomalyReader.Read(Response(
            new
            {
                severity = "warning",
                code = ParserDateAnomalyReader.SuggestedCode,
                message = "published as written",
                evidence = Evidence("A9"),
                detail = new
                {
                    original = "2026-05-21",
                    reason = "severalCandidatesFitTheAnchors",
                    candidates = new object[]
                    {
                        new { value = "not-a-date", rule = "sequenceYearSubstitution" },
                        new { value = "2027-05-21", rule = "sequenceYearSubstitution" },
                    },
                },
            }));

        ParserDateAnomaly anomaly = Assert.Single(anomalies);
        Assert.Equal(new DateOnly(2027, 5, 21), Assert.Single(anomaly.Candidates).Value);
    }

    private static object Evidence(string range) => new
    {
        sheetId = "1",
        sheetTitle = "Sayfa1",
        range,
        rawText = (string?)null,
        extractionRule = "dateSequence",
    };

    /// <summary>A stored parse response carrying the given warnings and nothing else.</summary>
    private static string Response(params object[] warnings) =>
        JsonSerializer.Serialize(new
        {
            contractVersion = "1.0",
            correlationId = "test",
            sourceId = "G1-TR-PRACTICE",
            snapshotId = "snap-1",
            parserProfile = new { name = "grade1_practice_v1", version = "1.2.0" },
            status = "completedWithWarnings",
            candidates = Array.Empty<object>(),
            warnings,
            metrics = Array.Empty<object>(),
            confidenceIndicators = Array.Empty<object>(),
        });
}
