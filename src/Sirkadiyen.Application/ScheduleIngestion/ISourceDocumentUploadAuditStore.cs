using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleIngestion;

/// <summary>Appends the immutable record of an administrative upload.</summary>
public interface ISourceDocumentUploadAuditStore
{
    Task AppendAsync(SourceDocumentUpload upload, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceDocumentUpload>> ListForSourceAsync(
        SourceId sourceId,
        int limit,
        CancellationToken cancellationToken);
}
