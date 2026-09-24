using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Vault;

namespace Sirkadiyen.Infrastructure.Persistence.Vault;

/// <summary>PostgreSQL store for personal vault note jobs (ADR-169).</summary>
public sealed class VaultJobStore(SirkadiyenDbContext dbContext) : IVaultJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] FinishedStatuses =
        [nameof(VaultJobStatus.Succeeded), nameof(VaultJobStatus.Failed)];

    public async Task AddAsync(VaultJobRecord job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        VaultNoteJobRow row = new()
        {
            Id = job.View.Id,
            Source = job.Origin.Source.ToString(),
            RequestedBy = job.Origin.RequestedBy,
            Prompt = job.Request.Prompt,
            Folder = job.Request.Folder,
            Title = job.Request.Title,
            CreatedAtUtc = job.View.CreatedAtUtc,
        };
        Apply(row, job.View);

        dbContext.VaultNoteJobs.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<VaultJobRecord?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        VaultNoteJobRow? row = await dbContext.VaultNoteJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<VaultJobView?> UpdateAsync(
        Guid id,
        Func<VaultJobView, VaultJobView> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        VaultNoteJobRow? row = await dbContext.VaultNoteJobs
            .SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        VaultJobView updated = change(ToView(row));
        Apply(row, updated);
        await dbContext.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<VaultJobRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        List<VaultNoteJobRow> rows = await dbContext.VaultNoteJobs
            .AsNoTracking()
            .OrderByDescending(job => job.CreatedAtUtc)
            .ThenByDescending(job => job.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<VaultJobRecord>> ListUnfinishedAsync(CancellationToken cancellationToken)
    {
        List<VaultNoteJobRow> rows = await dbContext.VaultNoteJobs
            .AsNoTracking()
            .Where(job => !FinishedStatuses.Contains(job.Status))
            .OrderBy(job => job.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    private static void Apply(VaultNoteJobRow row, VaultJobView view)
    {
        row.Status = view.Status.ToString();
        row.CompletedAtUtc = view.CompletedAtUtc;
        row.NotePath = view.NotePath;
        row.NoteLink = view.NoteLink;
        row.Backlinks = JsonSerializer.Serialize(view.Backlinks, SerializerOptions);
        row.Warnings = JsonSerializer.Serialize(view.Warnings, SerializerOptions);
        row.Error = view.Error;
    }

    private static VaultJobRecord ToRecord(VaultNoteJobRow row)
    {
        // The request was validated when it was accepted; validating it again only restores the type.
        // A row edited by hand into something the rules refuse keeps its prompt and loses the rest.
        VaultNoteRequest request = VaultNoteRequest.Create(row.Prompt, row.Folder, row.Title, out _)
            ?? VaultNoteRequest.Create(row.Prompt, null, null, out _)
            ?? throw new InvalidOperationException($"Vault job {row.Id} holds an empty prompt.");

        return new VaultJobRecord(
            ToView(row),
            request,
            new VaultJobOrigin(Enum.Parse<VaultJobSource>(row.Source), row.RequestedBy));
    }

    private static VaultJobView ToView(VaultNoteJobRow row) => new()
    {
        Id = row.Id,
        Status = Enum.Parse<VaultJobStatus>(row.Status),
        CreatedAtUtc = row.CreatedAtUtc,
        CompletedAtUtc = row.CompletedAtUtc,
        NotePath = row.NotePath,
        NoteLink = row.NoteLink,
        Backlinks = JsonSerializer.Deserialize<List<VaultBacklinkOutcome>>(row.Backlinks, SerializerOptions) ?? [],
        Warnings = JsonSerializer.Deserialize<List<string>>(row.Warnings, SerializerOptions) ?? [],
        Error = row.Error,
    };
}
