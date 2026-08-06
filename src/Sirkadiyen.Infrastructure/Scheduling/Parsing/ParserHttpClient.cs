using System.Text;
using System.Text.Json;
using Sirkadiyen.Application.Scheduling.Parsing;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;

namespace Sirkadiyen.Infrastructure.Scheduling.Parsing;

/// <summary>Strict HTTP adapter for the versioned Python parser contract.</summary>
public sealed class ParserHttpClient(HttpClient httpClient) : IScheduleParserClient
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public async Task<ParseSnapshotResponse> ParseAsync(
        ParseSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpRequestMessage message = new(HttpMethod.Post, "v1/parse")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };
        using HttpResponseMessage response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ParserClientException(response.StatusCode, SafeBody(body));
        }

        ParseSnapshotResponse parsed = JsonSerializer.Deserialize<ParseSnapshotResponse>(
            body,
            JsonOptions)
            ?? throw new InvalidDataException("The parser returned an empty response document.");

        ValidateEchoes(request, parsed);
        return parsed;
    }

    private static void ValidateEchoes(
        ParseSnapshotRequest request,
        ParseSnapshotResponse response)
    {
        if (!string.Equals(response.ContractVersion, request.ContractVersion, StringComparison.Ordinal)
            || !string.Equals(response.CorrelationId, request.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(response.SourceId, request.Snapshot.SourceId, StringComparison.Ordinal)
            || !string.Equals(response.SnapshotId, request.Snapshot.SnapshotId, StringComparison.Ordinal)
            || !string.Equals(
                response.ParserProfile.Name,
                request.ParserProfile.Name,
                StringComparison.Ordinal)
            || !string.Equals(
                response.ParserProfile.Version,
                request.ParserProfile.Version,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The parser response does not echo the request contract identifiers exactly.");
        }
    }

    private static string SafeBody(string body)
    {
        const int maximumLength = 1000;
        string normalized = string.IsNullOrWhiteSpace(body)
            ? "The parser returned no error document."
            : body.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
