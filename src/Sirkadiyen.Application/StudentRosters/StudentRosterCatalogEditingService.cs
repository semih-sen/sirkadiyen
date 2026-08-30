using System.Text.Json;
using Sirkadiyen.Domain.StudentRosters;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// Reads, previews and applies administrative edits to the student roster catalog (ADR-134).
/// </summary>
/// <remarks>
/// The roster catalog was a repository file only a deployment could change, which is the state the
/// schedule source catalog was in before ADR-114. It is the wrong state for the same reason: the
/// faculty republishes a student list, or moves a column, and the correction is a code change and
/// a redeploy — so the file drifts from what is actually published, and the drift is only visible
/// as students being unable to find themselves during onboarding.
/// <para>
/// This service makes the document editable by a SuperAdmin under the guarantees ADR-114 defined:
/// </para>
/// <list type="bullet">
/// <item>The submitted document is validated by the same loader the lookup uses, so an edit
/// accepted here cannot leave the lookup unable to read its own configuration.</item>
/// <item>The operator confirms a plan, not a text box. The plan hash binds the confirmation to the
/// exact pair of documents it was computed from, and the on-disk hash binds it to the file that
/// was actually read, so two administrators cannot silently overwrite each other.</item>
/// <item>The file write is atomic, and every applied document is retained in full, so the previous
/// state is restorable without a repository checkout.</item>
/// <item>Nothing a student already saved is revisited. A roster suggests profile values at
/// onboarding; it does not own them afterwards, so an edit here changes what the next lookup
/// answers and nothing else.</item>
/// </list>
/// <para>
/// It differs from the source catalog's service in one way: there are no persisted rows to bring
/// into step with the document, because the file is the whole configuration. What it does instead
/// is drop the held reading of the lists, so the edit takes effect at the next lookup rather than
/// up to an hour later.
/// </para>
/// </remarks>
public sealed class StudentRosterCatalogEditingService(
    IStudentRosterCatalogFile file,
    IStudentRosterCatalogSerializer serializer,
    IStudentRosterCatalogRevisionStore revisions,
    IStudentRosterIndex index,
    TimeProvider timeProvider)
{
    /// <summary>How many stored revisions the history view returns.</summary>
    public const int RevisionHistoryLimit = 50;

    public async Task<StudentRosterCatalogDocument> ReadAsync(CancellationToken cancellationToken)
    {
        StudentRosterCatalogFileContent content = await file.ReadAsync(cancellationToken);
        StudentRosterCatalog? catalog = TryParse(content.Content, out string? error);

        return new StudentRosterCatalogDocument
        {
            Path = file.Path,
            Content = content.Content,
            ContentHash = content.ContentHash,
            LastModifiedUtc = content.LastModifiedUtc,
            IsWritable = await file.IsWritableAsync(cancellationToken),
            IsValid = catalog is not null,
            ValidationError = error,
            CatalogVersion = catalog?.CatalogVersion,
            RosterCount = catalog?.Rosters.Count,
        };
    }

    /// <summary>
    /// Works out what a submitted document would change, writing nothing.
    /// </summary>
    /// <remarks>
    /// The base hash is required rather than optional: an operator who started editing before
    /// someone else saved must be told so at preview time, not discover it at confirmation.
    /// </remarks>
    public async Task<StudentRosterCatalogPlan> PreviewAsync(
        string content,
        string baseContentHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentHash);

        string normalized = StudentRosterCatalogPlanner.Normalize(
            content ?? throw new ArgumentNullException(nameof(content)));
        StudentRosterCatalog proposed = Parse(normalized);

        StudentRosterCatalogFileContent current = await file.ReadAsync(cancellationToken);
        RequireUnchanged(current.ContentHash, baseContentHash);

        return StudentRosterCatalogPlanner.Plan(
            TryParse(current.Content, out _),
            current.ContentHash,
            proposed,
            normalized);
    }

    /// <summary>Writes the confirmed document and records the revision behind it.</summary>
    public async Task<StudentRosterCatalogApplyResult> ApplyAsync(
        StudentRosterCatalogApplyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.BaseContentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PlanHash);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new StudentRosterCatalogValidationException(
                "Katalog değişikliği için gerekçe zorunludur.");
        }

        if (command.Reason.Trim().Length > StudentRosterCatalogRevision.MaximumReasonLength)
        {
            throw new StudentRosterCatalogValidationException(
                "Gerekçe en fazla "
                + $"{StudentRosterCatalogRevision.MaximumReasonLength} karakter olabilir.");
        }

        string normalized = StudentRosterCatalogPlanner.Normalize(command.Content);
        StudentRosterCatalog proposed = Parse(normalized);

        StudentRosterCatalogFileContent current = await file.ReadAsync(cancellationToken);
        RequireUnchanged(current.ContentHash, command.BaseContentHash);

        StudentRosterCatalog? currentCatalog = TryParse(current.Content, out _);
        StudentRosterCatalogPlan plan = StudentRosterCatalogPlanner.Plan(
            currentCatalog,
            current.ContentHash,
            proposed,
            normalized);

        if (!string.Equals(plan.PlanHash, command.PlanHash, StringComparison.Ordinal))
        {
            throw new StudentRosterCatalogConflictException(
                "Onaylanan plan, gönderilen belgeye ait değil. Lütfen yeniden ön izleme alın.");
        }

        if (!plan.HasChanges)
        {
            throw new StudentRosterCatalogValidationException(
                "Gönderilen belge diskteki katalogla aynı; yazılacak bir değişiklik yok.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        StudentRosterCatalogRevision revision = StudentRosterCatalogRevision.Edit(
            now,
            normalized,
            plan.ProposedContentHash,
            current.Exists ? current.ContentHash : null,
            proposed.Rosters.Count,
            command.ActorUserId,
            command.ActorEmail,
            command.Reason.Trim(),
            command.CorrelationId,
            Summarize(plan));

        // The file is written first, then the history. The order matters less here than it does
        // for the source catalog - no other process reads this file - but a failed commit must
        // still put the file back, or the document in force would be one that no revision
        // explains and that no history entry can restore from.
        await file.WriteAsync(normalized, cancellationToken);

        try
        {
            await revisions.CommitAsync(
                new StudentRosterCatalogCommit
                {
                    Revision = revision,
                    Baseline = current.Exists
                        ? new StudentRosterCatalogBaselineDraft
                        {
                            Content = current.Content,
                            ContentHash = current.ContentHash,
                            RosterCount = currentCatalog?.Rosters.Count ?? 0,
                            RecordedAtUtc = current.LastModifiedUtc ?? now,
                        }
                        : null,
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

        // The lists are read once an hour and held, so without this the panel would report an
        // applied edit while every lookup kept answering from the documents the old catalog named.
        // This drops the reading in this process, which is the only one that holds it.
        index.Invalidate();

        return new StudentRosterCatalogApplyResult
        {
            RevisionId = revision.Id,
            ContentHash = plan.ProposedContentHash,
            AppliedAtUtc = now,
            ReadingInvalidated = true,
            Plan = plan,
        };
    }

    public async Task<IReadOnlyList<StudentRosterCatalogRevisionSummary>> ListRevisionsAsync(
        CancellationToken cancellationToken)
    {
        StudentRosterCatalogFileContent current = await file.ReadAsync(cancellationToken);
        return await revisions.ListAsync(
            RevisionHistoryLimit,
            current.ContentHash,
            cancellationToken);
    }

    public async Task<StudentRosterCatalogRevisionDetail?> FindRevisionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        StudentRosterCatalogFileContent current = await file.ReadAsync(cancellationToken);
        return await revisions.FindAsync(id, current.ContentHash, cancellationToken);
    }

    private StudentRosterCatalog Parse(string content)
    {
        try
        {
            return serializer.Parse(content);
        }
        catch (StudentRosterCatalogValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or ArgumentException
                or FormatException or UriFormatException)
        {
            // Translated rather than swallowed: the operator is the person who can fix it, and the
            // loader's message names the roster and the rule (AI_GUIDELINE §16).
            throw new StudentRosterCatalogValidationException(exception.Message);
        }
    }

    private StudentRosterCatalog? TryParse(string content, out string? error)
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
        catch (StudentRosterCatalogValidationException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static void RequireUnchanged(string currentHash, string baseContentHash)
    {
        if (!string.Equals(currentHash, baseContentHash, StringComparison.Ordinal))
        {
            throw new StudentRosterCatalogConflictException(
                "Katalog dosyası siz düzenlerken değişti. Değişikliklerinizi kaybetmemek için "
                + "belgeyi yeniden yükleyip düzenlemenizi tekrar uygulayın.");
        }
    }

    /// <summary>
    /// The compact record of what the operator confirmed, stored beside the revision.
    /// </summary>
    /// <remarks>
    /// The full documents are both retained, so this is not the evidence — it is what makes a
    /// history list readable without diffing two documents in one's head.
    /// </remarks>
    private static string Summarize(StudentRosterCatalogPlan plan) => JsonSerializer.Serialize(
        new
        {
            added = plan.Added.Select(change => change.RosterId),
            removed = plan.Removed.Select(change => change.RosterId),
            modified = plan.Modified.Select(change => new
            {
                rosterId = change.RosterId,
                fields = change.Fields.Select(field => field.Field),
            }),
            unchanged = plan.UnchangedCount,
            highRisk = plan.HasHighRiskChange,
            warnings = plan.Warnings.Select(warning => warning.Code),
        });
}
