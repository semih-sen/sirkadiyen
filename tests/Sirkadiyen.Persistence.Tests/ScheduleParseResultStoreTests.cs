using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;
using ContractAudienceScope = Sirkadiyen.Contracts.Parsing.AudienceScope;
using ContractEventType = Sirkadiyen.Contracts.Parsing.ScheduleEventType;
using ContractLanguage = Sirkadiyen.Contracts.Parsing.ProgramLanguage;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ScheduleParseResultStoreTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CompletingAParseCreatesOneTraceableCandidateRevision()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, SourceSnapshot snapshot) = await AddSourceAndSnapshotAsync(context);
        ScheduleParseResultStore store = new(context);
        DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

        var begun = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-1",
            now,
            StaleAfter,
            Token);
        ScheduleRevision? revision = await store.CompleteAsync(
            begun.ParseRunId,
            Response(source, snapshot, "correlation-1"),
            now.AddSeconds(2),
            Token);

        Assert.NotNull(revision);
        context.ChangeTracker.Clear();
        // Scoped to this test's own snapshot and revision: the fixture database
        // is shared by the whole collection, so a whole-table query would only
        // pass while this happened to be the first test to run.
        ParseRun run = await context.ParseRuns.SingleAsync(
            candidate => candidate.SourceSnapshotId == snapshot.Id,
            Token);
        CanonicalScheduleRecord record = await context.CanonicalScheduleRecords.SingleAsync(
            candidate => candidate.ScheduleRevisionId == revision.Id,
            Token);

        Assert.Equal(ParseRunStatus.CompletedWithWarnings, run.Status);
        Assert.Equal(1, run.AttemptCount);
        Assert.Equal("candidate-1", record.CandidateId);
        Assert.Equal(CanonicalRecordStatus.Cancelled, record.RecordStatus);
        Assert.Equal(RevisionState.Parsed, revision.State);
        Assert.Contains("practiceGroup", record.AudienceSelectors);

        // The parser states the curriculum block and every department it named,
        // and both have to survive persistence: the diff engine reads the
        // department back out to recognize a lesson whose time moved (ADR-035),
        // and the calendar shows a student who teaches an integrated session.
        Assert.Equal("HÜCRE DİLİMİ", record.CurriculumBlock);
        Assert.Equal(["TIBBİ BİYOLOJİ AD.", "BİYOFİZİK AD."], record.Departments);
        Assert.Null(record.ComparableDepartment);
    }

    [Fact]
    public async Task ARunLeftRunningByAVanishedWorkerIsRecoveredIntoTheSameRun()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, SourceSnapshot snapshot) = await AddSourceAndSnapshotAsync(context);
        ScheduleParseResultStore store = new(context);
        DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

        var abandoned = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-1",
            now,
            StaleAfter,
            Token);

        // No Complete and no Fail: this is what a killed worker leaves behind.
        var recovered = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-2",
            now.Add(StaleAfter),
            StaleAfter,
            Token);

        Assert.Equal(abandoned.ParseRunId, recovered.ParseRunId);
        Assert.True(recovered.ShouldInvokeParser);
        Assert.Equal(ParseRunStartKind.RecoveredStale, recovered.StartKind);

        context.ChangeTracker.Clear();
        ParseRun run = await context.ParseRuns.SingleAsync(
            candidate => candidate.SourceSnapshotId == snapshot.Id,
            Token);
        Assert.Equal(ParseRunStatus.Running, run.Status);
        Assert.Equal(2, run.AttemptCount);
        Assert.Equal("correlation-2", run.CorrelationId);
        Assert.Equal(now.Add(StaleAfter), run.LastStaleRecoveryAtUtc);
    }

    [Fact]
    public async Task ARunStillWithinItsTimeoutIsLeftAloneForTheWorkerThatOwnsIt()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, SourceSnapshot snapshot) = await AddSourceAndSnapshotAsync(context);
        ScheduleParseResultStore store = new(context);
        DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

        var running = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-1",
            now,
            StaleAfter,
            Token);
        var second = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-2",
            now.Add(StaleAfter) - TimeSpan.FromSeconds(1),
            StaleAfter,
            Token);

        Assert.Equal(running.ParseRunId, second.ParseRunId);
        Assert.False(second.ShouldInvokeParser);
        Assert.Equal(ParseRunStatus.Running, second.Status);

        context.ChangeTracker.Clear();
        ParseRun run = await context.ParseRuns.SingleAsync(
            candidate => candidate.SourceSnapshotId == snapshot.Id,
            Token);
        Assert.Equal(1, run.AttemptCount);
        Assert.Equal("correlation-1", run.CorrelationId);
        Assert.Null(run.LastStaleRecoveryAtUtc);
    }

    [Fact]
    public async Task TheLoserOfARecoveryRaceCannotCompleteTheRunItLost()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, SourceSnapshot snapshot) = await AddSourceAndSnapshotAsync(context);
        ScheduleParseResultStore store = new(context);
        DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

        var abandoned = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-1",
            now,
            StaleAfter,
            Token);
        await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-2",
            now.Add(StaleAfter),
            StaleAfter,
            Token);

        // The original worker was slow rather than dead and answers after the
        // recovery. Its response belongs to a superseded attempt, so it must not
        // create a revision; the recovering attempt is the only one that may.
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CompleteAsync(
            abandoned.ParseRunId,
            Response(source, snapshot, "correlation-1"),
            now.Add(StaleAfter).AddSeconds(5),
            Token));

        context.ChangeTracker.Clear();
        Assert.False(
            await context.ScheduleRevisions.AnyAsync(
                revision => revision.ScheduleSourceId == source.Id,
                Token));
    }

    [Fact]
    public async Task AFailedTransportAttemptResumesTheSameLogicalParseRun()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await using SirkadiyenDbContext context = fixture.CreateContext();
        (ScheduleSource source, SourceSnapshot snapshot) = await AddSourceAndSnapshotAsync(context);
        ScheduleParseResultStore store = new(context);
        DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

        var first = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-1",
            now,
            StaleAfter,
            Token);
        await store.FailAsync(first.ParseRunId, now.AddSeconds(1), "HTTP timeout", Token);
        var resumed = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-2",
            now.AddMinutes(1),
            StaleAfter,
            Token);

        Assert.Equal(first.ParseRunId, resumed.ParseRunId);
        Assert.True(resumed.ShouldInvokeParser);
        ParseRun run = await context.ParseRuns.SingleAsync(
            candidate => candidate.SourceSnapshotId == snapshot.Id,
            Token);
        Assert.Equal(ParseRunStatus.Running, run.Status);
        Assert.Equal(2, run.AttemptCount);
        Assert.Equal("correlation-2", run.CorrelationId);
        Assert.Null(run.LastStaleRecoveryAtUtc);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The stale timeout these tests use. It is passed explicitly rather than
    /// read from configuration, because the recovery boundary is what is under
    /// test and not how the host happens to be configured.
    /// </summary>
    private static TimeSpan StaleAfter => TimeSpan.FromMinutes(30);

    private static async Task<(ScheduleSource Source, SourceSnapshot Snapshot)>
        AddSourceAndSnapshotAsync(SirkadiyenDbContext context)
    {
        SourceId sourceId = SourceId.Parse($"G1-PARSE-{Guid.NewGuid():N}"[..24]);
        ScheduleSource source = new(
            sourceId,
            "Parse result test source",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://example.invalid/sheet",
            "grade1_yearly_v1",
            "1.0.0",
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            "Europe/Istanbul",
            "spreadsheet-1",
            1);
        SourceSnapshot snapshot = new(
            source.Id,
            source.SourceId,
            "snapshot-1",
            "spreadsheet-1",
            source.AcademicYear,
            new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero),
            $"sha256:{Guid.NewGuid():N}",
            "1.0",
            "{}",
            1,
            1,
            0);
        context.ScheduleSources.Add(source);
        context.SourceSnapshots.Add(snapshot);
        await context.SaveChangesAsync(Token);
        return (source, snapshot);
    }

    private static ParseSnapshotResponse Response(
        ScheduleSource source,
        SourceSnapshot snapshot,
        string correlationId) => new()
        {
            ContractVersion = ParserContractVersions.V1,
            CorrelationId = correlationId,
            SourceId = source.SourceId.Value,
            SnapshotId = snapshot.ExternalSnapshotId,
            ParserProfile = new ParserProfileDescriptor
            {
                Name = source.ParserProfile,
                Version = source.ParserProfileVersion,
            },
            Status = ParserResultStatus.CompletedWithWarnings,
            Candidates =
            [
                new CanonicalScheduleCandidate
                {
                    CandidateId = "candidate-1",
                    AcademicYear = source.AcademicYear,
                    ClassYear = source.ClassYear,
                    ProgramLanguage = ContractLanguage.Turkish,
                    Audience = new ScheduleAudienceCandidate
                    {
                        Scope = ContractAudienceScope.SelectedGroups,
                        Selectors =
                        [
                            new AudienceSelector
                            {
                                Dimension = "practiceGroup",
                                Value = "A",
                            },
                        ],
                    },
                    EventType = ContractEventType.Practice,
                    Status = CandidateRecordStatus.Cancelled,
                    NormalizedCourseIdentity = "clinical-skills",
                    DisplayTitle = "Clinical Skills",
                    LocalDate = new DateOnly(2026, 7, 23),
                    StartLocalTime = new TimeOnly(9, 0),
                    EndLocalTime = new TimeOnly(9, 45),
                    TimeZoneId = source.TimeZoneId,
                    CurriculumBlock = "HÜCRE DİLİMİ",
                    Departments = ["TIBBİ BİYOLOJİ AD.", "BİYOFİZİK AD."],
                    StableIdentity = "sha256:identity",
                    ContentHash = "sha256:content",
                    Confidence = 0.95m,
                },
            ],
            Warnings =
            [
                new ParserWarning
                {
                    Severity = ParserWarningSeverity.Warning,
                    Code = "test.warning",
                    Message = "Fixture warning",
                },
            ],
        };
}
