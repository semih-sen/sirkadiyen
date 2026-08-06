using Sirkadiyen.Domain.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>Appends the immutable record of an administrative upload.</summary>
public interface ISourceDocumentUploadAuditStore
{
    Task AppendAsync(SourceDocumentUpload upload, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceDocumentUpload>> ListForSourceAsync(
        SourceId sourceId,
        int limit,
        CancellationToken cancellationToken);
}
