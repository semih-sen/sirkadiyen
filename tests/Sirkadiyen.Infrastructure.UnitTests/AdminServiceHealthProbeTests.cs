using System.Net;
using System.Text;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Infrastructure.Observability;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class AdminServiceHealthProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReportsCurrentWorkerHeartbeatAndHealthyParser()
    {
        FakeHeartbeatStore heartbeats = new(new ServiceHeartbeatSnapshot
        {
            ServiceName = "worker",
            InstanceId = "test:1",
            StartedAtUtc = Now.AddHours(-1),
            LastSeenAtUtc = Now.AddSeconds(-10),
        });
        AdminServiceHealthProbe probe = CreateProbe(
            heartbeats,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"healthy\"}", Encoding.UTF8, "application/json"),
            });

        AdminServiceHealthSnapshot result = await probe.GetAsync(Token);

        Assert.Equal(ServiceHealthState.Healthy, result.Worker.State);
        Assert.Equal(ServiceHealthState.Healthy, result.Parser.State);
        Assert.Equal(Now, result.CheckedAtUtc);
    }

    [Fact]
    public async Task DoesNotClaimHealthForStaleWorkerOrUnavailableParser()
    {
        FakeHeartbeatStore heartbeats = new(new ServiceHeartbeatSnapshot
        {
            ServiceName = "worker",
            InstanceId = "test:1",
            StartedAtUtc = Now.AddHours(-1),
            LastSeenAtUtc = Now.AddMinutes(-2),
        });
        HttpClient client = new(new ThrowingHandler()) { BaseAddress = new Uri("http://parser/") };
        AdminServiceHealthProbe probe = new(client, heartbeats, new FixedTimeProvider(Now));

        AdminServiceHealthSnapshot result = await probe.GetAsync(Token);

        Assert.Equal(ServiceHealthState.Unhealthy, result.Worker.State);
        Assert.Equal(ServiceHealthState.Unhealthy, result.Parser.State);
    }

    private static AdminServiceHealthProbe CreateProbe(
        IServiceHeartbeatStore heartbeats,
        HttpResponseMessage response)
    {
        HttpClient client = new(new StubHandler(response)) { BaseAddress = new Uri("http://parser/") };
        return new AdminServiceHealthProbe(client, heartbeats, new FixedTimeProvider(Now));
    }

    private static CancellationToken Token => CancellationToken.None;

    private sealed class FakeHeartbeatStore(ServiceHeartbeatSnapshot? value) : IServiceHeartbeatStore
    {
        public Task<ServiceHeartbeatSnapshot?> FindAsync(string serviceName, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task RecordAsync(string serviceName, string instanceId, DateTimeOffset startedAtUtc, DateTimeOffset seenAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
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
