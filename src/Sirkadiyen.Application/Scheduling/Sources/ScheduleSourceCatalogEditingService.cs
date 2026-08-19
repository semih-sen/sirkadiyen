using System.Text.Json;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// Reads, previews and applies administrative edits to the schedule source catalog (ADR-114).
/// </summary>
/// <remarks>
/// The catalog was a repository file that only a deployment could change. That made every
/// correction — a moved spreadsheet, a new practice group, a parser profile bump — a code change
/// and a redeploy, which is why the file drifted from what the faculty actually publishes. This
/// service makes the document editable by a SuperAdmin under the same guarantees a deployment gave
/// it and a few it never had:
/// <list type="bullet">
/// <item>The submitted document is validated by the same loader the worker uses at startup, so an
/// edit accepted here cannot leave the worker unable to start.</item>
/// <item>The operator confirms a plan, not a text box. The plan hash binds the confirmation to the
/// exact pair of documents it was computed from, and the on-disk hash binds it to the file that
/// was actually read, so two administrators cannot silently overwrite each other.</item>
/// <item>The file write is atomic and is rolled back if the database commit fails, so the running
/// worker never reads a catalog the database does not agree with.</item>
/// <item>A source that disappears from the document has its polling disabled, never its rows,
/// revisions or calendar events deleted (AI_GUIDELINE §13).</item>
/// <item>Every applied document is retained in full, so the previous state is restorable without
/// a repository checkout.</item>
/// </list>
/// </remarks>
public sealed class ScheduleSourceCatalogEditingService(
    IScheduleSourceCatalogFile file,
    IScheduleSourceCatalogSerializer serializer,
    IScheduleSourceCatalogRevisionStore revisions,
    TimeProvider timeProvider)
{
    /// <summary>How many stored revisions the history view returns.</summary>
    public const int RevisionHistoryLimit = 50;

    public async Task<ScheduleSourceCatalogDocument> ReadAsync(CancellationToken cancellationToken)
    {
        ScheduleSourceCatalogFileContent content = await file.ReadAsync(cancellationToken);
        ScheduleSourceCatalog? catalog = TryParse(content.Content, out string? error);

        return new ScheduleSourceCatalogDocument
        {
            Path = file.Path,
            Content = content.Content,
            ContentHash = content.ContentHash,
            LastModifiedUtc = content.LastModifiedUtc,
            IsWritable = await file.IsWritableAsync(cancellationToken),
            IsValid = catalog is not null,
            ValidationError = error,
            CatalogVersion = catalog?.CatalogVersion,
            SourceCount = catalog?.Sources.Count,
        };
    }

    /// <summary>
    /// Works out what a submitted document would change, writing nothing.
    /// </summary>
    /// <remarks>
    /// The base hash is required rather than optional: an operator who started editing before
    /// someone else saved must be told so at preview time, not discover it at confirmation.
    /// </remarks>
    public async Task<ScheduleSourceCatalogPlan> PreviewAsync(
        string content,
        string baseContentHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentHash);

        string normalized = ScheduleSourceCatalogPlanner.Normalize(
            content ?? throw new ArgumentNullException(nameof(content)));
        ScheduleSourceCatalog proposed = Parse(normalized);

        ScheduleSourceCatalogFileContent current = await file.ReadAsync(cancellationToken);
        RequireUnchanged(current.ContentHash, baseContentHash);

        return ScheduleSourceCatalogPlanner.Plan(
            TryParse(current.Content, out _),
            current.ContentHash,
            proposed,
            normalized);
    }

    /// <summary>Writes the confirmed document and brings the persisted sources into step with it.</summary>
    public async Task<ScheduleSourceCatalogApplyResult> ApplyAsync(
        ScheduleSourceCatalogApplyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.BaseContentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PlanHash);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ScheduleSourceCatalogValidationException(
                "Katalog değişikliği için gerekçe zorunludur.");
        }

        if (command.Reason.Trim().Length > ScheduleSourceCatalogRevision.MaximumReasonLength)
        {
            throw new ScheduleSourceCatalogValidationException(
                "Gerekçe en fazla "
                + $"{ScheduleSourceCatalogRevision.MaximumReasonLength} karakter olabilir.");
        }

        string normalized = ScheduleSourceCatalogPlanner.Normalize(command.Content);
        ScheduleSourceCatalog proposed = Parse(normalized);

        ScheduleSourceCatalogFileContent current = await file.ReadAsync(cancellationToken);
        RequireUnchanged(current.ContentHash, command.BaseContentHash);

        ScheduleSourceCatalog? currentCatalog = TryParse(current.Content, out _);
        ScheduleSourceCatalogPlan plan = ScheduleSourceCatalogPlanner.Plan(
            currentCatalog,
            current.ContentHash,
            proposed,
            normalized);

        if (!string.Equals(plan.PlanHash, command.PlanHash, StringComparison.Ordinal))
        {
            throw new ScheduleSourceCatalogConflictException(
                "Onaylanan plan, gönderilen belgeye ait değil. Lütfen yeniden ön izleme alın.");
        }

        if (!plan.HasChanges)
        {
            throw new ScheduleSourceCatalogValidationException(
                "Gönderilen belge diskteki katalogla aynı; yazılacak bir değişiklik yok.");
        }

        IReadOnlyList<SourceId> disabled =
        [
            .. plan.Removed.Select(removed => SourceId.Parse(removed.SourceId)),
        ];
        DateTimeOffset now = timeProvider.GetUtcNow();
        ScheduleSourceCatalogRevision revision = ScheduleSourceCatalogRevision.Edit(
            now,
            normalized,
            plan.ProposedContentHash,
            current.Exists ? current.ContentHash : null,
            proposed.Sources.Count,
            command.ActorUserId,
            command.ActorEmail,
            command.Reason.Trim(),
            command.CorrelationId,
            Summarize(plan));

        // The file is written first so that the moment the database says a source is configured
        // this way, the file a restarting worker reads already says the same. A failed commit
        // therefore has to put the file back: a catalog the database does not know about would
        // reappear silently at the next worker restart.
        await file.WriteAsync(normalized, cancellationToken);

        int rowsChanged;
        try
        {
            rowsChanged = await revisions.CommitAsync(
                new ScheduleSourceCatalogCommit
                {
                    Revision = revision,
                    Baseline = current.Exists
                        ? new ScheduleSourceCatalogBaselineDraft
                        {
                            Content = current.Content,
                            ContentHash = current.ContentHash,
                            SourceCount = currentCatalog?.Sources.Count ?? 0,
                            RecordedAtUtc = current.LastModifiedUtc ?? now,
                        }
                        : null,
                    Sources = [.. proposed.Sources.Select(source => source.ToScheduleSource())],
                    PollingDisabled = disabled,
                },
                cancellationToken);
        }
        catch
        {
            if (current.Exists)
            {
                await file.WriteAsync(current.Content, CancellationToken.None);
            }

            throw;
        }

        return new ScheduleSourceCatalogApplyResult
        {
            RevisionId = revision.Id,
            ContentHash = plan.ProposedContentHash,
            AppliedAtUtc = now,
            SourceRowsChanged = rowsChanged,
            PollingDisabledSourceIds = [.. disabled.Select(sourceId => sourceId.Value)],
            Plan = plan,
        };
    }

    public async Task<IReadOnlyList<ScheduleSourceCatalogRevisionSummary>> ListRevisionsAsync(
        CancellationToken cancellationToken)
    {
        ScheduleSourceCatalogFileContent current = await file.ReadAsync(cancellationToken);
        return await revisions.ListAsync(
            RevisionHistoryLimit,
            current.ContentHash,
            cancellationToken);
    }

    public async Task<ScheduleSourceCatalogRevisionDetail?> FindRevisionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        ScheduleSourceCatalogFileContent current = await file.ReadAsync(cancellationToken);
        return await revisions.FindAsync(id, current.ContentHash, cancellationToken);
    }

    private ScheduleSourceCatalog Parse(string content)
    {
        try
        {
            return serializer.Parse(content);
        }
        catch (ScheduleSourceCatalogValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or ArgumentException
                or FormatException or UriFormatException)
        {
            // Translated rather than swallowed: the operator is the person who can fix it, and the
            // loader's message names the source and the rule (AI_GUIDELINE §16).
            throw new ScheduleSourceCatalogValidationException(exception.Message);
        }
    }

    private ScheduleSourceCatalog? TryParse(string content, out string? error)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Katalog dosyası boş.";
            return null;
        }

        try
        {
            error = null;
            return Parse(content);
        }
        catch (ScheduleSourceCatalogValidationException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static void RequireUnchanged(string currentHash, string baseContentHash)
    {
        if (!string.Equals(currentHash, baseContentHash, StringComparison.Ordinal))
        {
            throw new ScheduleSourceCatalogConflictException(
                "Katalog dosyası siz düzenlerken değişti. Değişikliklerinizi kaybetmemek için "
                + "belgeyi yeniden yükleyip düzenlemenizi tekrar uygulayın.");
        }
    }

    /// <summary>
    /// The compact record of what the operator confirmed, stored beside the revision.
    /// </summary>
    /// <remarks>
    /// The full documents are both retained, so this is not the evidence — it is what makes a
    /// history list readable without diffing two thirty-kilobyte files in one's head.
    /// </remarks>
    private static string Summarize(ScheduleSourceCatalogPlan plan) => JsonSerializer.Serialize(
        new
        {
            added = plan.Added.Select(change => change.SourceId),
            removed = plan.Removed.Select(change => change.SourceId),
            modified = plan.Modified.Select(change => new
            {
                sourceId = change.SourceId,
                fields = change.Fields.Select(field => field.Field),
            }),
            unchanged = plan.UnchangedCount,
            highRisk = plan.HasHighRiskChange,
            warnings = plan.Warnings.Select(warning => warning.Code),
        });
}

/// <summary>Everything an applied catalog edit needs, so no call site can forget the actor.</summary>
public sealed record ScheduleSourceCatalogApplyCommand
{
    public required string Content { get; init; }

    public required string BaseContentHash { get; init; }

    public required string PlanHash { get; init; }

    public required string Reason { get; init; }

    public required Guid ActorUserId { get; init; }

    public required string ActorEmail { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>
/// One atomic catalog commit: the revision, the sources it configures, and the sources it retires.
/// </summary>
/// <remarks>
/// These travel together because they must succeed or fail together. A persisted source row that
/// no revision explains, or a revision whose sources were never applied, are both states in which
/// "what is the system actually running" has two answers.
/// </remarks>
public sealed record ScheduleSourceCatalogCommit
{
    public required ScheduleSourceCatalogRevision Revision { get; init; }

    /// <summary>
    /// The content that was on disk before this edit, recorded as the first history entry when the
    /// history is still empty. Ignored once any revision exists.
    /// </summary>
    public required ScheduleSourceCatalogBaselineDraft? Baseline { get; init; }

    public required IReadOnlyCollection<ScheduleSource> Sources { get; init; }

    /// <summary>Sources the document no longer declares; their polling is turned off.</summary>
    public required IReadOnlyCollection<SourceId> PollingDisabled { get; init; }
}

public sealed record ScheduleSourceCatalogBaselineDraft
{
    public required string Content { get; init; }

    public required string ContentHash { get; init; }

    public required int SourceCount { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }
}
