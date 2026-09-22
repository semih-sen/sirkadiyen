using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;
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

    /// <summary>
    /// More than half of the lessons the sources publish name no department, so
    /// requiring one would send every time change they contain to the calendar
    /// as a delete and a create (ADR-035 as amended).
    /// </summary>
    [Fact]
    public void ATimeChangeWithoutAnyDepartmentIsStillMatchedOnTitleAndInstructor()
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

        ScheduleDiffEntry entry = Assert.Single(Differ().Diff([previous], [current]));

        Assert.Equal(ScheduleDiffChange.Updated, entry.Change);
        Assert.Equal(ScheduleDiffMatch.SecondaryAttributes, entry.Match);

        // Null is the record of which basis was used: an operator reviewing a
        // held diff can tell this from a three-attribute match.
        Assert.Null(entry.DepartmentScore);
        Assert.NotNull(entry.TitleScore);
        Assert.NotNull(entry.InstructorScore);
    }

    /// <summary>
    /// An all-day closure and a timed lesson are different logical entries even
    /// when everything a similarity score reads is identical (ADR-046).
    /// </summary>
    [Fact]
    public void AnAllDayItemIsNeverMatchedToATimedLesson()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "was-timed",
            title: "KURBAN BAYRAMI",
            normalizedTitle: "kurban-bayrami",
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "now-all-day",
            title: "KURBAN BAYRAMI",
            normalizedTitle: "kurban-bayrami",
            allDay: true);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff([previous], [current]);

        // A delete and a create, not an update: the shape of the entry changed,
        // and the calendar has to replace it rather than patch its times away.
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Created);
        Assert.DoesNotContain(entries, entry => entry.Change is ScheduleDiffChange.Updated);
    }

    /// <summary>
    /// An integrated session ("entegre oturum") names several departments. The
    /// list is kept for the student to read but is not a comparable value, so
    /// matching falls back to the two-attribute rule.
    /// </summary>
    [Fact]
    public void AnIntegratedSessionIsMatchedWithoutComparingItsDepartmentList()
    {
        IReadOnlyList<string> departments = ["Tıbbi Biyoloji AD.", "Biyofizik AD."];
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "old-time",
            departments: departments,
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new-time",
            departments: departments,
            hour: 10);

        ScheduleDiffEntry entry = Assert.Single(Differ().Diff([previous], [current]));

        Assert.Equal(ScheduleDiffChange.Updated, entry.Change);
        Assert.Null(entry.DepartmentScore);
    }

    /// <summary>
    /// Matching without a department is weaker evidence, so it has to clear a
    /// higher composite bar than a match that has one.
    /// </summary>
    [Fact]
    public void AWeakerTitleWithoutADepartmentDoesNotClearTheHigherBar()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "old-time",
            normalizedTitle: "dolasim-fizyolojisi",
            department: null,
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new-time",
            normalizedTitle: "dolasim-fizyoloj",
            department: null,
            hour: 10);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff([previous], [current]);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Created);
    }

    /// <summary>
    /// Two lessons that each name one department, and disagree about it, must not
    /// be re-scored without the attribute that ruled them out.
    /// </summary>
    [Fact]
    public void DisagreeingSingleDepartmentsAreNotRescoredWithoutTheDepartment()
    {
        CanonicalScheduleRecord previous = Record(
            PreviousRevisionId,
            "previous",
            stableIdentity: "old-time",
            department: "Fizyoloji Anabilim Dalı",
            hour: 9);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new-time",
            department: "Anatomi Anabilim Dalı",
            hour: 10);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff([previous], [current]);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        Assert.Contains(entries, entry => entry.Change is ScheduleDiffChange.Created);
    }

    [Fact]
    public void MatchingWithoutADepartmentMayNotBeEasierThanMatchingWithOne()
    {
        SemanticDiffOptions options = new()
        {
            MinimumCompositeSimilarity = 0.88m,
            MinimumCompositeSimilarityWithoutDepartment = 0.80m,
        };

        Assert.Throws<ArgumentException>(() => new SemanticScheduleDiffer(options));
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

        // One entry per record drawn into the contested set, not one per pair:
        // the old lesson and both candidates for it, each classified once.
        Assert.Equal(3, entries.Count);
        Assert.All(
            entries,
            entry => Assert.Equal(ScheduleDiffChange.Ambiguous, entry.Change));
        Assert.DoesNotContain(entries, entry => entry.Change is ScheduleDiffChange.Created);
        Assert.DoesNotContain(entries, entry => entry.Change is ScheduleDiffChange.Deleted);
        AssertEachRecordClassifiedOnce(entries);
        Assert.Equal([previous.Id], PreviousIds(entries));
        Assert.Equal(new[] { currentOne.Id, currentTwo.Id }.Order(), CurrentIds(entries));
    }

    [Fact]
    public void SeveralOldLessonsMatchingOneNewOneStayAmbiguousOncePerRecord()
    {
        CanonicalScheduleRecord previousOne = Record(
            PreviousRevisionId,
            "previous-one",
            stableIdentity: "old-time-one",
            hour: 9);
        CanonicalScheduleRecord previousTwo = Record(
            PreviousRevisionId,
            "previous-two",
            stableIdentity: "old-time-two",
            hour: 11);
        CanonicalScheduleRecord current = Record(
            CurrentRevisionId,
            "current",
            stableIdentity: "new-time",
            hour: 10);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff(
            [previousOne, previousTwo],
            [current]);

        Assert.Equal(3, entries.Count);
        Assert.All(
            entries,
            entry => Assert.Equal(ScheduleDiffChange.Ambiguous, entry.Change));
        AssertEachRecordClassifiedOnce(entries);
        Assert.Equal(new[] { previousOne.Id, previousTwo.Id }.Order(), PreviousIds(entries));
        Assert.Equal([current.Id], CurrentIds(entries));
    }

    [Fact]
    public void AnAmbiguousSetDoesNotSuppressACleanMatchBesideIt()
    {
        CanonicalScheduleRecord contestedPrevious = Record(
            PreviousRevisionId,
            "contested-previous",
            stableIdentity: "old-contested",
            hour: 9);
        CanonicalScheduleRecord contestedCurrentOne = Record(
            CurrentRevisionId,
            "contested-current-one",
            stableIdentity: "new-contested-one",
            hour: 10);
        CanonicalScheduleRecord contestedCurrentTwo = Record(
            CurrentRevisionId,
            "contested-current-two",
            stableIdentity: "new-contested-two",
            hour: 11);
        CanonicalScheduleRecord cleanPrevious = Record(
            PreviousRevisionId,
            "clean-previous",
            stableIdentity: "old-clean",
            title: "Klinik Biyokimya",
            normalizedTitle: "klinik-biyokimya",
            instructor: "Dr. Zeynep Aksoy",
            hour: 13);
        CanonicalScheduleRecord cleanCurrent = Record(
            CurrentRevisionId,
            "clean-current",
            stableIdentity: "new-clean",
            title: "Klinik Biyokimya",
            normalizedTitle: "klinik-biyokimya",
            instructor: "Dr. Zeynep Aksoy",
            hour: 14);

        IReadOnlyList<ScheduleDiffEntry> entries = Differ().Diff(
            [contestedPrevious, cleanPrevious],
            [contestedCurrentOne, contestedCurrentTwo, cleanCurrent]);

        ScheduleDiffEntry updated = Assert.Single(
            entries,
            entry => entry.Change is ScheduleDiffChange.Updated);
        Assert.Equal(cleanPrevious.Id, updated.PreviousRecordId);
        Assert.Equal(cleanCurrent.Id, updated.CurrentRecordId);
        Assert.Equal(3, entries.Count(entry => entry.Change is ScheduleDiffChange.Ambiguous));
        AssertEachRecordClassifiedOnce(entries);
    }

    /// <summary>
    /// The invariant the entry table enforces: within one diff a record is named
    /// at most once on the previous side and at most once on the current side.
    /// </summary>
    private static void AssertEachRecordClassifiedOnce(IEnumerable<ScheduleDiffEntry> entries)
    {
        List<ScheduleDiffEntry> materialized = entries.ToList();

        Assert.Equal(
            materialized.Count(entry => entry.PreviousRecordId is not null),
            PreviousIds(materialized).Count);
        Assert.Equal(
            materialized.Count(entry => entry.CurrentRecordId is not null),
            CurrentIds(materialized).Count);
    }

    private static IReadOnlyList<Guid> PreviousIds(IEnumerable<ScheduleDiffEntry> entries) =>
        entries
            .Select(entry => entry.PreviousRecordId)
            .OfType<Guid>()
            .Distinct()
            .Order()
            .ToList();

    private static IReadOnlyList<Guid> CurrentIds(IEnumerable<ScheduleDiffEntry> entries) =>
        entries
            .Select(entry => entry.CurrentRecordId)
            .OfType<Guid>()
            .Distinct()
            .Order()
            .ToList();

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
        IReadOnlyList<string>? departments = null,
        string audienceSelectors = "[]",
        int hour = 9,
        bool allDay = false) => new(
            revisionId,
            SourceId.Parse("G1-TR-ANNUAL"),
            candidateId,
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            allDay ? ScheduleEventType.Other : ScheduleEventType.Theory,
            AudienceScope.AllStudentsInProgram,
            audienceSelectors,
            title,
            normalizedTitle,
            new DateOnly(2025, 10, 3),
            allDay ? null : new TimeOnly(hour, 0),
            allDay ? null : new TimeOnly(hour + 1, 0),
            allDay,
            "Europe/Istanbul",
            stableIdentity,
            contentHash,
            1m,
            "[]",
            instructor,
            null,
            null,
            // A test names either one department through `department` or a whole
            // list through `departments`; the list wins when it is given.
            departments ?? (department is null ? [] : [department]));
}
