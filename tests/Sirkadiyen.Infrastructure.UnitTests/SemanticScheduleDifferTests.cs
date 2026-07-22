using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Regression coverage for every semantic diff classification and for the
/// ambiguity boundary that prevents destructive calendar writes.
/// </summary>
public sealed class SemanticScheduleDifferTests
{
    private static readonly Guid PreviousRevisionId = Guid.CreateVersion7();
    private static readonly Guid CurrentRevisionId = Guid.CreateVersion7();

    [Fact]
    public void ExactIdentityAndContentRemainUnchanged()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "same",
            contentHash: "content");
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "same",
            contentHash: "content");

        ScheduleDiffEntry entry = Assert.Single(Differ().Diff([previous], [current]));

        Assert.Equal(ScheduleDiffChange.Unchanged, entry.Change);
        Assert.Equal(ScheduleDiffMatch.ExactStableIdentity, entry.Match);
        Assert.Equal(1m, entry.MatchScore);
    }

    [Fact]
    public void ExactIdentityWithChangedContentIsAnUpdate()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "same",
            contentHash: "old-content");
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "same",
            contentHash: "new-content");

        ScheduleDiffEntry entry = Assert.Single(Differ().Diff([previous], [current]));

        Assert.Equal(ScheduleDiffChange.Updated, entry.Change);
        Assert.Equal(ScheduleDiffMatch.ExactStableIdentity, entry.Match);
    }

    [Fact]
    public void UnmatchedRecordsAreCreatedAndDeleted()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "gone",
            instructor: null,
            department: null);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new",
            instructor: null,
            department: null);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff([previous], [current]);

        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Created);
        Assert.DoesNotContain(entries, entry => entry.Change is ScheduleDiffChange.Updated);
    }

    [Fact]
    public void ATimeChangeWithSmallSpellingDifferencesIsMatchedSemantically()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "09:00-physiology",
            title: "Dolaşım Fizyolojisi",
            normalizedTitle: "dolasim-fizyolojisi",
            instructor: "Prof. Dr. Ayşe Yılmaz",
            department: "Fizyoloji Anabilim Dalı",
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "10:00-physiology",
            title: "Dolaşım Fizyolojisii",
            normalizedTitle: "dolasim-fizyolojisii",
            instructor: "Prof Dr Ayşe Yılmaz",
            department: "Fizyoloji Anabilim Dali",
            hour: 10);

        ScheduleDiffEntry entry = Assert.Single(Differ().Diff([previous], [current]));

        Assert.Equal(ScheduleDiffChange.Updated, entry.Change);
        Assert.Equal(ScheduleDiffMatch.SecondaryAttributes, entry.Match);
        Assert.True(entry.MatchScore >= 0.88m);
        Assert.NotNull(entry.TitleScore);
        Assert.NotNull(entry.InstructorScore);
        Assert.NotNull(entry.DepartmentScore);
    }

    [Fact]
    public void SecondaryMatchingRequiresAnExplicitDepartment()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "old-time",
            department: null,
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new-time",
            department: null,
            hour: 10);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff([previous], [current]);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Created);
    }

    [Fact]
    public void DifferentAudiencesCannotBeSecondaryMatches()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "old-time",
            audienceSelectors: "[{\"dimension\":\"practiceGroup\",\"value\":\"A\"}]",
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new-time",
            audienceSelectors: "[{\"dimension\":\"practiceGroup\",\"value\":\"B\"}]",
            hour: 10);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff([previous], [current]);

        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Created);
    }

    [Fact]
    public void SeveralPlausibleMatchesStayAmbiguousWithoutCreateOrDelete()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "old-time",
            hour: 9);
        CanonicalScheduleRecord currentOne = Record(
            CurrentRevisionId,
            "current-one",
            stableIdentity: "new-time-one",
            hour: 10);
        CanonicalScheduleRecord currentTwo = Record(
            CurrentRevisionId,
            "current-two",
            stableIdentity: "new-time-two",
            hour: 11);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff(
            [previous],
            [currentOne, currentTwo]);

        Assert.Equal(2, entries.Count);
        Assert.All(
            entries,
            entry => Assert.Equal(ScheduleDiffChange.Ambiguous, entry.Change));
        Assert.DoesNotContain(entries, entry => entry.Change is ScheduleDiffChange.Created);
        Assert.DoesNotContain(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
    }

    [Fact]
    public void DuplicateInputIdentityIsRejectedBeforeDiffing()
    {
        CanonicalScheduleRecord first = Record(
            PreviousRevisionId,
            "first",
            stableIdentity: "duplicate");
        CanonicalScheduleRecord second = Record(
            PreviousRevisionId,
            "second",
            stableIdentity: "duplicate",
            hour: 12);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Differ().Diff([first, second], []));

        Assert.Equal("previous", exception.ParamName);
    }

    [Fact]
    public void ThresholdWeightsMustAddUpToOne()
    {
        SemanticDiffOptions options = new()
        {
            TitleWeight = 0.5m,
            InstructorWeight = 0.5m,
            DepartmentWeight = 0.5m,
        };

        Assert.Throws<ArgumentException>(() => new SemanticScheduleDiffer(options));
    }

    private static SemanticScheduleDiffer Differ() => new(new SemanticDiffOptions());

    private static CanonicalScheduleRecord Record(
        Guid revisionId,
        string candidateId,
        string stableIdentity,
        string contentHash = "content",
        string title = "Dolaşım Fizyolojisi",
        string? normalizedTitle = "dolasim-fizyolojisi",
        string? instructor = "Prof. Dr. Ayşe Yılmaz",
        string? department = "Fizyoloji Anabilim Dalı",
        string audienceSelectors = "[]",
        int hour = 9) => new(
            revisionId,
            SourceId.Parse("G1-TR-ANNUAL"),
            candidateId,
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            ScheduleEventType.Theory,
            AudienceScope.AllStudentsInProgram,
            audienceSelectors,
            title,
            normalizedTitle,
            new DateOnly(2025, 10, 3),
            new TimeOnly(hour, 0),
            new TimeOnly(hour + 1, 0),
            "Europe/Istanbul",
            stableIdentity,
            contentHash,
            1m,
            "[]",
            instructor,
            null,
            department);
}
