using Sirkadiyen.Contracts.Parsing;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>Invokes the isolated deterministic parser service.</summary>
public interface IScheduleParserClient
{
    Task<ParseSnapshotResponse> ParseAsync(
        ParseSnapshotRequest request,
        CancellationToken cancellationToken);
}
