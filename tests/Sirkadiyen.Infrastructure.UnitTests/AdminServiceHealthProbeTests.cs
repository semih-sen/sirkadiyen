using System.Net;
using System.Text;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Infrastructure.Observability;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class AdminServiceHealthProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReportsHealthyWorkerAndParserEndpoints()
    {
        AdminServiceHealthProbe probe = CreateProbe(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("""
                    {
                      "status":"healthy",
                      "instanceId":"test:1",
                      "startedAtUtc":"2026-08-04T17:00:00Z",
                      "lastActivityAtUtc":"2026-08-04T17:59:50Z",
                      "currentStage":"waiting"
                    }
                    """),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json("{\"status\":\"healthy\"}"),
            });

        AdminServiceHealthSnapshot result = await probe.GetAsync(Token);

        Assert.Equal(ServiceHealthState.Healthy, result.Worker.State);
        Assert.Equal(ServiceHealthState.Healthy, result.Parser.State);
        Assert.Equal(Now.AddSeconds(-10), result.Worker.LastSeenAtUtc);
        Assert.Contains("waiting", result.Worker.Detail, StringComparison.Ordinal);
        Assert.Equal(Now, result.CheckedAtUtc);
    }

    [Fact]
    public async Task DoesNotClaimHealthWhenWorkerIsNotReadyOrParserIsUnavailable()
    {
        AdminServiceHealthProbe probe = CreateProbe(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = Json("""
                    {
                      "status":"starting",
                      "instanceId":"test:1",
                      "startedAtUtc":"2026-08-04T18:00:00Z",
                      "lastActivityAtUtc":"2026-08-04T18:00:00Z",
                      "currentStage":"seeding-source-catalog"
                    }
                    """),
            },
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = Json("{\"status\":\"unhealthy\"}"),
            });

        AdminServiceHealthSnapshot result = await probe.GetAsync(Token);

        Assert.Equal(ServiceHealthState.Unhealthy, result.Worker.State);
        Assert.Equal(ServiceHealthState.Unhealthy, result.Parser.State);
    }

    [Fact]
    public async Task ReportsBothServicesUnhealthyWhenInternalEndpointsCannotBeReached()
    {
        HttpClient client = new(new ThrowingHandler());
        AdminServiceHealthProbe probe = new(
            client,
            Options,
            new FixedTimeProvider(Now));

        AdminServiceHealthSnapshot result = await probe.GetAsync(Token);

        Assert.Equal(ServiceHealthState.Unhealthy, result.Worker.State);
        Assert.Equal(ServiceHealthState.Unhealthy, result.Parser.State);
    }

    private static AdminServiceHealthProbe CreateProbe(
        HttpResponseMessage workerResponse,
        HttpResponseMessage parserResponse)
    {
        HttpClient client = new(new RoutingHandler(workerResponse, parserResponse));
        return new AdminServiceHealthProbe(client, Options, new FixedTimeProvider(Now));
    }

    private static AdminServiceHealthProbeOptions Options => new()
    {
        WorkerBaseUrl = new Uri("http://worker.internal/"),
        ParserBaseUrl = new Uri("http://parser.internal/"),
    };

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private static CancellationToken Token => CancellationToken.None;

    private sealed class RoutingHandler(
        HttpResponseMessage workerResponse,
        HttpResponseMessage parserResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(request.RequestUri);
            return Task.FromResult(
                request.RequestUri.Host == "worker.internal"
                    ? workerResponse
                    : parserResponse);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
