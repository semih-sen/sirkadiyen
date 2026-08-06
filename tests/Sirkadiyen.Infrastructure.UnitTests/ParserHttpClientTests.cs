using System.Net;
using System.Text;
using System.Text.Json;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Infrastructure.Scheduling.Parsing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class ParserHttpClientTests
{
    [Fact]
    public async Task AcceptsAResponseThatEchoesEveryIdentifier()
    {
        ParseSnapshotRequest request = Request();
        ParseSnapshotResponse response = Response(request);
        ParserHttpClient client = Client(HttpStatusCode.OK, response);

        ParseSnapshotResponse actual = await client.ParseAsync(
            request,
            CancellationToken.None);

        Assert.Equal(request.CorrelationId, actual.CorrelationId);
    }

    [Fact]
    public async Task RejectsASuccessResponseForAnotherSnapshot()
    {
        ParseSnapshotRequest request = Request();
        ParseSnapshotResponse response = Response(request) with { SnapshotId = "wrong" };
        ParserHttpClient client = Client(HttpStatusCode.OK, response);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.ParseAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PreservesTheParserHttpStatusOnFailure()
    {
        ParserHttpClient client = new(new HttpClient(new StubHandler(
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\"detail\":\"bad profile\"}"),
            }))
        {
            BaseAddress = new Uri("http://parser.invalid/"),
        });

        ParserClientException exception = await Assert.ThrowsAsync<ParserClientException>(
            () => client.ParseAsync(Request(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    private static ParserHttpClient Client(HttpStatusCode status, ParseSnapshotResponse response)
    {
        string json = JsonSerializer.Serialize(response, ContractJson.CreateOptions());
        return new ParserHttpClient(new HttpClient(new StubHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }))
        {
            BaseAddress = new Uri("http://parser.invalid/"),
        });
    }

    private static ParseSnapshotRequest Request() => new()
    {
        ContractVersion = ParserContractVersions.V1,
        CorrelationId = "correlation-1",
        ParserProfile = new ParserProfileDescriptor { Name = "grade1_yearly_v1", Version = "1.0.0" },
        SourceContext = new ParseSourceContext
        {
            AcademicYear = "2025-2026",
            ClassYear = 1,
            ProgramLanguage = ProgramLanguage.Turkish,
            TimeZoneId = "Europe/Istanbul",
        },
        Snapshot = new NormalizedSpreadsheetSnapshot
        {
            ContractVersion = SpreadsheetContractVersions.V1,
            SourceId = "G1-TR-ANNUAL",
            SnapshotId = "snapshot-1",
            SpreadsheetId = "spreadsheet-1",
            AcquiredAtUtc = new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero),
            ContentHash = "sha256:content",
            ContentHashAlgorithm = "SHA-256",
        },
    };

    private static ParseSnapshotResponse Response(ParseSnapshotRequest request) => new()
    {
        ContractVersion = request.ContractVersion,
        CorrelationId = request.CorrelationId,
        SourceId = request.Snapshot.SourceId,
        SnapshotId = request.Snapshot.SnapshotId,
        ParserProfile = request.ParserProfile,
        Status = ParserResultStatus.Completed,
    };

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
