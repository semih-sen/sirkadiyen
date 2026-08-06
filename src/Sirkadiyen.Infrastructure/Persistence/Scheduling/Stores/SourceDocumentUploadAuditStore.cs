using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;

/// <summary>Append-only storage for administrative document uploads (ADR-080).</summary>
public sealed class SourceDocumentUploadAuditStore(SirkadiyenDbContext dbContext)
    : ISourceDocumentUploadAuditStore
{
    public async Task AppendAsync(
        SourceDocumentUpload upload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);

        dbContext.SourceDocumentUploads.Add(upload);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceDocumentUpload>> ListForSourceAsync(
        SourceId sourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.SourceDocumentUploads
            .Where(upload => upload.SourceId == sourceId)
            .OrderByDescending(upload => upload.UploadedAtUtc)
            .ThenByDescending(upload => upload.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
