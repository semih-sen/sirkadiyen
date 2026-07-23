using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleParsing;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using ContractAudienceScope = Sirkadiyen.Contracts.Parsing.AudienceScope;
using ContractEventType = Sirkadiyen.Contracts.Parsing.ScheduleEventType;
using ContractProgramLanguage = Sirkadiyen.Contracts.Parsing.ProgramLanguage;
using DomainAudienceScope = Sirkadiyen.Domain.SchedulePublication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.SchedulePublication.ScheduleEventType;
using DomainProgramLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

namespace Sirkadiyen.Infrastructure.Persistence;

/// <summary>Persists parser attempts and atomically creates candidate revisions.</summary>
public sealed class ScheduleParseResultStore(SirkadiyenDbContext dbContext)
    : IScheduleParseResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public async Task<BeginParseRunResult> BeginOrResumeAsync(
        SourceSnapshot snapshot,
        ScheduleSource source,
        string correlationId,
        DateTimeOffset startedAtUtc,
        TimeSpan staleRunTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(source);

        if (snapshot.ScheduleSourceId != source.Id || snapshot.SourceId != source.SourceId)
        {
            throw new InvalidOperationException(
                "A parse run must use the source that owns the stored snapshot.");
        }

        ParseRun? existing = await FindRunAsync(snapshot.Id, source, cancellationToken);
        if (existing is null)
        {
            ParseRun created = new(
                snapshot.Id,
                source.ParserProfile,
                source.ParserProfileVersion,
                correlationId,
                startedAtUtc);
            dbContext.ParseRuns.Add(created);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return Started(created);
            }
            catch (DbUpdateException)
            {
                // A concurrent delivery may have inserted the deterministic run
                // first. Read that row and treat it as the authoritative result.
                dbContext.ChangeTracker.Clear();
                existing = await FindRunAsync(snapshot.Id, source, cancellationToken);
                if (existing is null)
                {
                    throw;
                }
            }
        }

        if (existing.Status is ParseRunStatus.Failed)
        {
            existing.Resume(correlationId, startedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Started(existing);
        }

        // A run still marked running after the timeout belongs to a worker that
        // is gone. Left alone it would block this snapshot forever, and the
        // schedule change it carries would never reach a calendar. Recovery
        // reuses the run, so no second run or revision can appear for it.
        if (existing.IsStale(startedAtUtc, staleRunTimeout))
        {
            existing.RecoverStale(correlationId, startedAtUtc, staleRunTimeout);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Started(existing, ParseRunStartKind.RecoveredStale);
        }

        return new BeginParseRunResult
        {
            ParseRunId = existing.Id,
            Status = existing.Status,
            ShouldInvokeParser = false,
        };
    }

    public Task<ScheduleRevision?> CompleteAsync(
        Guid parseRunId,
        ParseSnapshotResponse response,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        return RetriableTransaction.ExecuteAsync(
            dbContext,
            () => CompleteCoreAsync(parseRunId, response, completedAtUtc, cancellationToken));
    }

    private async Task<ScheduleRevision?> CompleteCoreAsync(
        Guid parseRunId,
        ParseSnapshotResponse response,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        ParseRun run = await dbContext.ParseRuns.SingleAsync(
            candidate => candidate.Id == parseRunId,
            cancellationToken);
        SourceSnapshot snapshot = await dbContext.SourceSnapshots.SingleAsync(
            candidate => candidate.Id == run.SourceSnapshotId,
            cancellationToken);
        ScheduleSource source = await dbContext.ScheduleSources.SingleAsync(
            candidate => candidate.Id == snapshot.ScheduleSourceId,
            cancellationToken);

        ValidateResponse(run, snapshot, source, response);

        int errorCount = response.Warnings.Count(
            static warning => warning.Severity is ParserWarningSeverity.Error);
        run.Complete(
            MapStatus(response.Status),
            completedAtUtc,
            JsonSerializer.Serialize(response, JsonOptions),
            response.Candidates.Count,
            response.Warnings.Count,
            errorCount);

        if (response.Status is ParserResultStatus.Rejected)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        ScheduleRevision revision = new(source.Id, source.SourceId, run.Id, completedAtUtc);
        List<CanonicalScheduleRecord> records = response.Candidates
            .Select(candidate => MapCandidate(revision.Id, source, candidate))
            .ToList();
        revision.SetRecordCount(records.Count);

        dbContext.ScheduleRevisions.Add(revision);
        dbContext.CanonicalScheduleRecords.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    public async Task FailAsync(
        Guid parseRunId,
        DateTimeOffset completedAtUtc,
        string failureReason,
        CancellationToken cancellationToken)
    {
        // CompleteAsync may have mutated tracked entities before a candidate or
        // database constraint rejected the transaction. Reload authoritative
        // state after rollback so that failure is not mistaken for completion.
        dbContext.ChangeTracker.Clear();
        ParseRun run = await dbContext.ParseRuns.SingleAsync(
            candidate => candidate.Id == parseRunId,
            cancellationToken);
        if (run.Status is ParseRunStatus.Running)
        {
            run.Fail(completedAtUtc, failureReason);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private Task<ParseRun?> FindRunAsync(
        Guid snapshotId,
        ScheduleSource source,
        CancellationToken cancellationToken) =>
        dbContext.ParseRuns.SingleOrDefaultAsync(
            run => run.SourceSnapshotId == snapshotId
                && run.ParserProfile == source.ParserProfile
                && run.ParserProfileVersion == source.ParserProfileVersion,
            cancellationToken);

    private static BeginParseRunResult Started(
        ParseRun run,
        ParseRunStartKind kind = ParseRunStartKind.Started) => new()
        {
            ParseRunId = run.Id,
            Status = run.Status,
            ShouldInvokeParser = true,
            StartKind = kind,
        };

    private static void ValidateResponse(
        ParseRun run,
        SourceSnapshot snapshot,
        ScheduleSource source,
        ParseSnapshotResponse response)
    {
        if (run.Status is not ParseRunStatus.Running)
        {
            throw new InvalidOperationException("Only a running parse can be completed.");
        }

        if (!string.Equals(response.ContractVersion, ParserContractVersions.V1, StringComparison.Ordinal)
            || !string.Equals(response.CorrelationId, run.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(response.SourceId, source.SourceId.Value, StringComparison.Ordinal)
            || !string.Equals(response.SnapshotId, snapshot.ExternalSnapshotId, StringComparison.Ordinal)
            || !string.Equals(response.ParserProfile.Name, run.ParserProfile, StringComparison.Ordinal)
            || !string.Equals(
                response.ParserProfile.Version,
                run.ParserProfileVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The parser response identifiers do not match the persisted parse run.");
        }
    }

    private static CanonicalScheduleRecord MapCandidate(
        Guid revisionId,
        ScheduleSource source,
        CanonicalScheduleCandidate candidate)
    {
        if (!string.Equals(candidate.AcademicYear, source.AcademicYear, StringComparison.Ordinal)
            || candidate.ClassYear != source.ClassYear
            || MapLanguage(candidate.ProgramLanguage) != source.ProgramLanguage
            || !string.Equals(candidate.TimeZoneId, source.TimeZoneId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Candidate '{candidate.CandidateId}' contradicts its configured source context.");
        }

        return new CanonicalScheduleRecord(
            revisionId,
            source.SourceId,
            candidate.CandidateId,
            candidate.Status switch
            {
                CandidateRecordStatus.Scheduled => CanonicalRecordStatus.Scheduled,
                CandidateRecordStatus.Cancelled => CanonicalRecordStatus.Cancelled,
                _ => throw new InvalidDataException(
                    $"Unsupported candidate status '{candidate.Status}'."),
            },
            candidate.AcademicYear,
            candidate.ClassYear,
            MapLanguage(candidate.ProgramLanguage),
            MapEventType(candidate.EventType),
            MapAudienceScope(candidate.Audience.Scope),
            JsonSerializer.Serialize(candidate.Audience.Selectors, JsonOptions),
            candidate.DisplayTitle,
            candidate.NormalizedCourseIdentity,
            candidate.LocalDate,
            candidate.StartLocalTime,
            candidate.EndLocalTime,
            candidate.TimeZoneId,
            candidate.StableIdentity,
            candidate.ContentHash,
            candidate.Confidence,
            JsonSerializer.Serialize(candidate.Evidence, JsonOptions),
            candidate.Instructor,
            candidate.Location,
            candidate.CurriculumBlock,
            candidate.Departments);
    }

    private static ParseRunStatus MapStatus(ParserResultStatus status) => status switch
    {
        ParserResultStatus.Completed => ParseRunStatus.Completed,
        ParserResultStatus.CompletedWithWarnings => ParseRunStatus.CompletedWithWarnings,
        ParserResultStatus.Rejected => ParseRunStatus.Rejected,
        _ => throw new InvalidDataException($"Unsupported parser status '{status}'."),
    };

    private static DomainProgramLanguage MapLanguage(ContractProgramLanguage language) =>
        language switch
        {
            ContractProgramLanguage.Turkish => DomainProgramLanguage.Turkish,
            ContractProgramLanguage.English => DomainProgramLanguage.English,
            _ => throw new InvalidDataException($"Unsupported program language '{language}'."),
        };

    private static DomainEventType MapEventType(ContractEventType eventType) => eventType switch
    {
        ContractEventType.Theory => DomainEventType.Theory,
        ContractEventType.Practice => DomainEventType.Practice,
        ContractEventType.AnatomyPractice => DomainEventType.AnatomyPractice,
        ContractEventType.BedsidePractice => DomainEventType.BedsidePractice,
        ContractEventType.FacultyPractice => DomainEventType.FacultyPractice,
        ContractEventType.VerticalCorridor => DomainEventType.VerticalCorridor,
        ContractEventType.IntegratedSession => DomainEventType.IntegratedSession,
        ContractEventType.Exam => DomainEventType.Exam,
        ContractEventType.Other => DomainEventType.Other,
        _ => throw new InvalidDataException($"Unsupported event type '{eventType}'."),
    };

    private static DomainAudienceScope MapAudienceScope(ContractAudienceScope scope) => scope switch
    {
        ContractAudienceScope.AllStudentsInProgram => DomainAudienceScope.AllStudentsInProgram,
        ContractAudienceScope.SelectedGroups => DomainAudienceScope.SelectedGroups,
        _ => throw new InvalidDataException($"Unsupported audience scope '{scope}'."),
    };
}
