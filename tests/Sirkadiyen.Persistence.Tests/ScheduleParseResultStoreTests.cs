using Microsoft.EntityFrameworkCore;
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
            Token);
        await store.FailAsync(first.ParseRunId, now.AddSeconds(1), "HTTP timeout", Token);
        var resumed = await store.BeginOrResumeAsync(
            snapshot,
            source,
            "correlation-2",
            now.AddMinutes(1),
            Token);

        Assert.Equal(first.ParseRunId, resumed.ParseRunId);
        Assert.True(resumed.ShouldInvokeParser);
        ParseRun run = await context.ParseRuns.SingleAsync(
            candidate => candidate.SourceSnapshotId == snapshot.Id,
            Token);
        Assert.Equal(ParseRunStatus.Running, run.Status);
        Assert.Equal(2, run.AttemptCount);
        Assert.Equal("correlation-2", run.CorrelationId);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
