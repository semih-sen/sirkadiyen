using System.Globalization;
using System.Text.Json;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;
using DomainAudienceScope = Sirkadiyen.Domain.SchedulePublication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.SchedulePublication.ScheduleEventType;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the boundary of every rule that can quarantine a revision.
/// </summary>
/// <remarks>
/// These rules are the only thing standing between a misparsed source and
/// hundreds of wrong student calendar events, so each threshold is asserted on
/// both sides rather than only where it triggers.
/// </remarks>
public sealed class ScheduleRevisionValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnEmptyRevisionIsRejectedRatherThanHeldForReview()
    {
        // Rejection is terminal, and it is correct here: no approval could make
        // an empty revision publishable, and publishing it would delete every
        // event the source owns.
        RevisionValidationResult result = Validate([]);

        Assert.Equal(RevisionState.Rejected, result.Outcome);
        Assert.Equal(RevisionValidationRule.EmptyRevision, result.Findings.Single().Rule);
    }

    [Fact]
    public void AStraightforwardRevisionIsValidated()
    {
        RevisionValidationResult result = Validate([Record("r1"), Record("r2", hour: 13)]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ValidationNeverPublishesARevision()
    {
        // Publication is a separate, later step. If validation could reach
        // Published, a suspicious revision would go live without the review the
        // whole pipeline exists to enforce.
        foreach (RevisionValidationResult result in new[]
        {
            Validate([]),
            Validate([Record("r1")]),
            Validate([Record("r1"), Record("r1-again")]),
        })
        {
            Assert.NotEqual(RevisionState.Published, result.Outcome);
        }
    }

    [Theory]
    // Exactly the configured share does not trigger the rule (ADR-025).
    [InlineData(20, 100, false)]
    [InlineData(21, 100, true)]
    // The absolute floor stops a small source from tripping on the share alone.
    [InlineData(9, 20, false)]
    [InlineData(10, 20, true)]
    // Both conditions must hold: a big absolute count under the share is fine.
    [InlineData(15, 200, false)]
    public void DeletionNeedsBothTheShareAndTheAbsoluteFloor(
        int disappeared,
        int priorCount,
        bool quarantined)
    {
        List<string> priorIdentities = Enumerable.Range(0, priorCount)
            .Select(index => $"identity-{index}")
            .ToList();

        // Everything that did not disappear is still present in the new revision.
        // Each record gets its own date so that this fixture exercises the
        // deletion rule alone rather than also colliding with the overlap rule.
        List<CanonicalScheduleRecord> records = priorIdentities
            .Skip(disappeared)
            .Select((identity, index) => Record(
                $"r{index}",
                date: new DateOnly(2025, 10, 3).AddDays(index),
                stableIdentity: identity))
            .ToList();

        RevisionValidationResult result = Validate(
            records,
            hasPreviousPublishedRevision: true,
            previouslyPublishedIdentities: priorIdentities);

        RevisionValidationFinding finding = Assert.Single(
            result.Findings,
            candidate => candidate.Rule is RevisionValidationRule.MassDeletion);

        Assert.Equal(disappeared, finding.AffectedRecordCount);
        Assert.Equal(
            quarantined ? RevisionState.ReviewRequired : RevisionState.Validated,
            result.Outcome);
    }

    [Fact]
    public void TheFirstRevisionOfASourceCannotTripTheDeletionRule()
    {
        // Nothing has been published, so nothing can have disappeared. Without
        // this the very first revision of every source would look like a total
        // deletion.
        RevisionValidationResult result = Validate(
            [Record("r1")],
            hasPreviousPublishedRevision: false,
            previouslyPublishedIdentities: ["gone-1", "gone-2"]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Rule is RevisionValidationRule.MassDeletion);
    }

    [Fact]
    public void AnUndeclaredSelectorSetLeavesTheRuleUnenforced()
    {
        // A source that has not been surveyed must not be quarantined for it.
        RevisionValidationResult result = Validate(
            [Record("r1", selectors: [("practiceGroup", "Z")])],
            supported: null);

        Assert.Equal(RevisionState.Validated, result.Outcome);
    }

    [Fact]
    public void ASelectorTheSourceDoesNotDeclareHoldsTheRevision()
    {
        RevisionValidationResult result = Validate(
            [
                Record("r1", selectors: [("practiceGroup", "A")]),
                Record("r2", hour: 13, selectors: [("practiceSubgroup", "D3")]),
            ],
            supported: new Dictionary<string, IReadOnlyList<string>>
            {
                ["practiceGroup"] = ["A", "B"],
                ["practiceSubgroup"] = ["A1", "A2"],
            });

        Assert.Equal(RevisionState.ReviewRequired, result.Outcome);
        RevisionValidationFinding finding = Assert.Single(
            result.Findings,
            candidate => candidate.Rule is RevisionValidationRule.UnknownAudienceSelector);
        Assert.Contains("practiceSubgroup:D3", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredButEmptyDimensionRejectsEverySelectorInIt()
    {
        // Declaring a dimension with no values is a positive statement that it
        // must not appear, unlike declaring nothing at all.
        RevisionValidationResult result = Validate(
            [Record("r1", selectors: [("practiceGroup", "A")])],
            supported: new Dictionary<string, IReadOnlyList<string>>
            {
                ["practiceGroup"] = [],
            });

        Assert.Equal(RevisionState.ReviewRequired, result.Outcome);
    }

    [Fact]
    public void TwoRecordsClaimingOneStableIdentityHoldTheRevision()
    {
        RevisionValidationResult result = Validate(
            [
                Record("r1", stableIdentity: "same"),
                Record("r2", hour: 13, stableIdentity: "same"),
            ]);

        Assert.Equal(RevisionState.ReviewRequired, result.Outcome);
        Assert.Contains(
            result.Findings,
            finding => finding.Rule is RevisionValidationRule.DuplicateStableIdentity);
    }

    [Fact]
    public void OneSameCourseOverlapIsReportedButDoesNotHoldTheRevision()
    {
        // A single same-course overlap is usually a source typo an operator should
        // see without the schedule being withheld from students (ADR-025, ADR-068).
        RevisionValidationResult result = Validate(
            [
                Record("r1", hour: 9, courseIdentity: "anatomy"),
                Record("r2", hour: 10, courseIdentity: "anatomy"),
            ]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        RevisionValidationFinding finding = Assert.Single(result.Findings);
        Assert.Equal(RevisionValidationRule.AudienceOverlap, finding.Rule);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void MultipleSameCourseOverlapsHoldTheRevision()
    {
        // The same course booked over itself repeatedly is a parsing duplication
        // that would put one lesson on a calendar several times (ADR-068).
        RevisionValidationResult result = Validate(
            [
                Record("r1", hour: 9, courseIdentity: "anatomy"),
                Record("r2", hour: 10, courseIdentity: "anatomy"),
                Record("r3", hour: 11, courseIdentity: "anatomy"),
            ]);

        Assert.Equal(RevisionState.ReviewRequired, result.Outcome);
        RevisionValidationFinding finding = Assert.Single(
            result.Findings,
            f => f.Rule == RevisionValidationRule.AudienceOverlap);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void MultipleFreeStudyOverlapsAreWarningsAndDoNotHoldTheRevision()
    {
        RevisionValidationResult result = Validate(
            [
                Record(
                    "r1",
                    hour: 13,
                    durationMinutes: 110,
                    courseIdentity: "serbest-calisma",
                    eventType: DomainEventType.FreeStudy,
                    displayTitle: "SERBEST ÇALIŞMA"),
                Record(
                    "r2",
                    hour: 14,
                    durationMinutes: 110,
                    courseIdentity: "serbest-calisma",
                    eventType: DomainEventType.FreeStudy,
                    displayTitle: "SERBEST ÇALIŞMA"),
                Record(
                    "r3",
                    hour: 15,
                    durationMinutes: 110,
                    courseIdentity: "serbest-calisma",
                    eventType: DomainEventType.FreeStudy,
                    displayTitle: "SERBEST ÇALIŞMA"),
            ]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        RevisionValidationFinding finding = Assert.Single(result.Findings);
        Assert.Equal(RevisionValidationRule.AudienceOverlap, finding.Rule);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Contains("free-study availability", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParallelOfferingsOfDifferentCoursesDoNotHoldTheRevision()
    {
        // Different courses at the same time for the whole audience are a legitimate
        // parallel offering the source deliberately schedules — electives, or a
        // make-up exam beside the regular one — so any number of them is a
        // non-blocking warning, never a quarantine (ADR-068).
        RevisionValidationResult result = Validate(
            [
                Record("r1", hour: 9),
                Record("r2", hour: 10),
                Record("r3", hour: 11),
            ]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        RevisionValidationFinding finding = Assert.Single(result.Findings);
        Assert.Equal(RevisionValidationRule.AudienceOverlap, finding.Rule);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void DifferentAudiencesAtTheSameTimeDoNotOverlap()
    {
        RevisionValidationResult result = Validate(
            [
                Record("r1", hour: 9, selectors: [("practiceGroup", "A")]),
                Record("r2", hour: 9, selectors: [("practiceGroup", "B")]),
            ]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// An all-day holiday has no duration, so the lesson-length rule must not read
    /// one from it (ADR-046). Before this, a published holiday would have measured
    /// as zero minutes and quarantined every revision that carried one.
    /// </summary>
    [Fact]
    public void AnAllDayItemIsNotMeasuredAgainstTheLessonLengthRule()
    {
        RevisionValidationResult result = Validate([Record("r1", allDay: true)]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AnAllDayItemDoesNotOverlapTheLessonsOnItsDate()
    {
        // A half-day holiday coexists with the teaching around it, and a closure
        // that booked its whole date would report every one of those as a clash.
        RevisionValidationResult result = Validate(
            [
                Record("holiday", allDay: true),
                Record("r1", hour: 9),
                Record("r2", hour: 13),
            ]);

        Assert.Equal(RevisionState.Validated, result.Outcome);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(10, false)]
    [InlineData(600, false)]
    [InlineData(601, true)]
    public void ImplausibleLessonLengthsHoldTheRevision(int minutes, bool quarantined)
    {
        RevisionValidationResult result = Validate(
            [Record("r1", durationMinutes: minutes)]);

        Assert.Equal(
            quarantined ? RevisionState.ReviewRequired : RevisionState.Validated,
            result.Outcome);
    }

    [Fact]
    public void ADateFarOutsideTheAcademicYearHoldsTheRevision()
    {
        // A misread date is the most common way a parser silently invents
        // lessons, so it must never publish unreviewed.
        RevisionValidationResult result = Validate(
            [Record("r1", date: new DateOnly(2021, 3, 1))]);

        Assert.Equal(RevisionState.ReviewRequired, result.Outcome);
        Assert.Contains(
            result.Findings,
            finding => finding.Rule is RevisionValidationRule.RecordDateOutsideAcademicYear);
    }

    [Fact]
    public void AnUnreadableAcademicYearIsReportedRatherThanSkippedSilently()
    {
        RevisionValidationResult result = Validate(
            [Record("r1")],
            source: Source("not-a-year"));

        RevisionValidationFinding finding = Assert.Single(result.Findings);
        Assert.Equal(RevisionValidationRule.RecordDateOutsideAcademicYear, finding.Rule);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal(RevisionState.Validated, result.Outcome);
    }

    [Fact]
    public void ALowConfidenceRecordHoldsTheRevision()
    {
        RevisionValidationResult result = Validate([Record("r1", confidence: 0.25m)]);

        Assert.Equal(RevisionState.ReviewRequired, result.Outcome);
        Assert.Contains(
            result.Findings,
            finding => finding.Rule is RevisionValidationRule.LowConfidenceRecord);
    }

    [Fact]
    public void FindingsReadTheSameWhateverCultureProducedThem()
    {
        // Findings are stored and read back by whoever reviews the revision,
        // possibly on another machine. A Turkish host rendering the threshold as
        // "0,50" would make the evidence depend on where it was written.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            RevisionValidationResult confidence = Validate([Record("r1", confidence: 0.25m)]);
            RevisionValidationFinding lowConfidence = Assert.Single(
                confidence.Findings,
                finding => finding.Rule is RevisionValidationRule.LowConfidenceRecord);
            Assert.Contains("0.50", lowConfidence.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("0,50", lowConfidence.Message, StringComparison.Ordinal);

            RevisionValidationResult deletion = Validate(
                [Record("r1")],
                hasPreviousPublishedRevision: true,
                previouslyPublishedIdentities: ["identity-r1", "gone-1", "gone-2", "gone-3"]);
            RevisionValidationFinding massDeletion = Assert.Single(
                deletion.Findings,
                finding => finding.Rule is RevisionValidationRule.MassDeletion);
            Assert.Contains("75.0", massDeletion.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static RevisionValidationResult Validate(
        IReadOnlyList<CanonicalScheduleRecord> records,
        bool hasPreviousPublishedRevision = false,
        IReadOnlyCollection<string>? previouslyPublishedIdentities = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? supported = null,
        ScheduleSource? source = null)
    {
        ScheduleSource effectiveSource = source ?? Source(supported: supported);
        ScheduleRevision revision = new(
            effectiveSource.Id,
            effectiveSource.SourceId,
            Guid.CreateVersion7(),
            Now);

        ScheduleRevisionValidator validator = new(new RevisionValidationOptions());
        return validator.Validate(
            new RevisionValidationInput
            {
                Revision = revision,
                Source = effectiveSource,
                Records = records,
                HasPreviousPublishedRevision = hasPreviousPublishedRevision,
                PreviouslyPublishedIdentities = previouslyPublishedIdentities ?? [],
            },
            Now);
    }

    private static ScheduleSource Source(
        string academicYear = "2025-2026",
        IReadOnlyDictionary<string, IReadOnlyList<string>>? supported = null) => new(
            SourceId.Parse("G1-TR-PRACTICE"),
            "Grade 1 Turkish practice",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://docs.google.com/spreadsheets/d/example",
            "grade1_practice_v1",
            "1.0.0",
            academicYear,
            1,
            DomainLanguage.Turkish,
            "Europe/Istanbul",
            "spreadsheet-1",
            1,
            supported);

    private static CanonicalScheduleRecord Record(
        string candidateId,
        int hour = 9,
        int durationMinutes = 110,
        DateOnly? date = null,
        string? stableIdentity = null,
        string? courseIdentity = null,
        decimal confidence = 1.0m,
        IReadOnlyList<(string Dimension, string Value)>? selectors = null,
        bool allDay = false,
        DomainEventType? eventType = null,
        string? displayTitle = null)
    {
        IReadOnlyList<AudienceSelector> audience = (selectors ?? [("practiceGroup", "A")])
            .Select(selector => new AudienceSelector
            {
                Dimension = selector.Dimension,
                Value = selector.Value,
            })
            .ToList();

        TimeOnly start = new(hour, 0);

        return new CanonicalScheduleRecord(
            Guid.CreateVersion7(),
            SourceId.Parse("G1-TR-PRACTICE"),
            candidateId,
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            eventType ?? (allDay ? DomainEventType.Other : DomainEventType.Practice),
            DomainAudienceScope.SelectedGroups,
            JsonSerializer.Serialize(audience, ContractJson.CreateOptions()),
            displayTitle ?? $"Lesson {candidateId}",
            courseIdentity,
            date ?? new DateOnly(2025, 10, 3),
            allDay ? null : start,
            allDay ? null : start.AddMinutes(durationMinutes),
            allDay,
            "Europe/Istanbul",
            stableIdentity ?? $"identity-{candidateId}",
            $"sha256:{candidateId}",
            confidence,
            "[]");
    }
}
