using System.Text.Json;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleParsing;
using Sirkadiyen.Domain.SchedulePublication;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;
using DomainAudienceScope = Sirkadiyen.Domain.SchedulePublication.AudienceScope;
using DomainEventType = Sirkadiyen.Domain.SchedulePublication.ScheduleEventType;
using DomainLanguage = Sirkadiyen.Domain.ScheduleSources.ProgramLanguage;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Builds the published-revision history the diff tests compare, so that both
/// the calculation tests and the review tests exercise the same real rows.
/// </summary>
internal static class ScheduleDiffScenario
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<ScheduleSource> AddSourceAsync(SirkadiyenDbContext context)
    {
        ScheduleSource source = new(
            SourceId.Parse($"G1-DIFF-{Guid.NewGuid():N}"[..24]),
            "Diff test source",
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

        context.ScheduleSources.Add(source);
        await context.SaveChangesAsync(Token);
        return source;
    }

    /// <summary>
    /// Adds a validated revision holding one record per stable identity.
    /// </summary>
    /// <param name="changedContentIdentities">
    /// Identities whose content hash differs from the default, which is how a
    /// room or instructor change reaches the diff as an update.
    /// </param>
    public static async Task<ScheduleRevision> AddRevisionAsync(
        SirkadiyenDbContext context,
        ScheduleSource source,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string> identities,
        IReadOnlyCollection<string>? changedContentIdentities = null)
    {
        SourceSnapshot snapshot = new(
            source.Id,
            source.SourceId,
            $"snapshot-{Guid.NewGuid():N}",
            "spreadsheet-1",
            source.AcademicYear,
            createdAtUtc,
            $"sha256:{Guid.NewGuid():N}",
            "1.0",
            "{}",
            1,
            identities.Count,
            0);

        ParseRun run = new(
            snapshot.Id,
            source.ParserProfile,
            source.ParserProfileVersion,
            $"c-{Guid.NewGuid():N}",
            createdAtUtc);

        ScheduleRevision revision = new(source.Id, source.SourceId, run.Id, createdAtUtc);
        revision.SetRecordCount(identities.Count);
        revision.TransitionTo(RevisionState.Validating, createdAtUtc);
        revision.TransitionTo(RevisionState.Validated, createdAtUtc, "All validation rules passed.");

        context.SourceSnapshots.Add(snapshot);
        context.ParseRuns.Add(run);
        context.ScheduleRevisions.Add(revision);

        foreach (string identity in identities)
        {
            context.CanonicalScheduleRecords.Add(Materialize(
                revision.Id,
                source.SourceId,
                identity,
                changedContentIdentities?.Contains(identity) == true
                    ? $"sha256:changed-{identity}"
                    : $"sha256:{identity}"));
        }

        await context.SaveChangesAsync(Token);
        return revision;
    }

    public static async Task<ScheduleRevision> PublishAsync(
        SirkadiyenDbContext context,
        ScheduleSource source,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string> identities,
        IReadOnlyCollection<string>? changedContentIdentities = null)
    {
        ScheduleRevision revision = await AddRevisionAsync(
            context,
            source,
            createdAtUtc,
            identities,
            changedContentIdentities);

        context.ChangeTracker.Clear();
        RevisionPublicationResult result = await new ScheduleRevisionPublicationStore(context)
            .PublishAsync(revision.Id, createdAtUtc, Token);
        Assert.Equal(RevisionPublicationOutcome.Published, result.Outcome);

        context.ChangeTracker.Clear();
        return revision;
    }

    public static CanonicalScheduleRecord Materialize(
        Guid revisionId,
        SourceId sourceId,
        string identity,
        string contentHash)
    {
        IReadOnlyList<AudienceSelector> audience =
        [
            new AudienceSelector { Dimension = "practiceGroup", Value = "A" },
        ];

        return new CanonicalScheduleRecord(
            revisionId,
            sourceId,
            $"candidate-{identity}",
            CanonicalRecordStatus.Scheduled,
            "2025-2026",
            1,
            DomainLanguage.Turkish,
            DomainEventType.Theory,
            DomainAudienceScope.SelectedGroups,
            JsonSerializer.Serialize(audience, ContractJson.CreateOptions()),
            $"Lesson {identity}",
            null,
            new DateOnly(2025, 10, 3),
            new TimeOnly(9, 0),
            new TimeOnly(10, 50),
            isAllDay: false,
            "Europe/Istanbul",
            identity,
            contentHash,
            1.0m,
            "[]");
    }

    public sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
