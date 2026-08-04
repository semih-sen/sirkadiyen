using System.Net.Http.Json;
using System.Text.Json;
using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Infrastructure.Observability;

public sealed class AdminServiceHealthProbe(
    HttpClient httpClient,
    IServiceHeartbeatStore heartbeatStore,
    TimeProvider timeProvider) : IAdminServiceHealthProbe
{
    private static readonly TimeSpan WorkerStaleAfter = TimeSpan.FromSeconds(45);

    public async Task<AdminServiceHealthSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset checkedAtUtc = timeProvider.GetUtcNow();
        ServiceHeartbeatSnapshot? worker = await heartbeatStore.FindAsync("worker", cancellationToken);
        ServiceHealthView workerHealth = worker is null
            ? new ServiceHealthView
            {
                Service = "worker",
                State = ServiceHealthState.Unknown,
                Detail = "No worker heartbeat has been recorded yet.",
            }
            : new ServiceHealthView
            {
                Service = "worker",
                State = checkedAtUtc - worker.LastSeenAtUtc <= WorkerStaleAfter
                    ? ServiceHealthState.Healthy
                    : ServiceHealthState.Unhealthy,
                LastSeenAtUtc = worker.LastSeenAtUtc,
                Detail = checkedAtUtc - worker.LastSeenAtUtc <= WorkerStaleAfter
                    ? "Heartbeat is current."
                    : "The worker heartbeat is stale.",
            };

        ServiceHealthView parserHealth;
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync("health", cancellationToken);
            ParserHealthResponse? body = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ParserHealthResponse>(cancellationToken)
                : null;
            bool healthy = response.IsSuccessStatusCode
                && string.Equals(body?.Status, "healthy", StringComparison.OrdinalIgnoreCase);
            parserHealth = new ServiceHealthView
            {
                Service = "parser",
                State = healthy ? ServiceHealthState.Healthy : ServiceHealthState.Unhealthy,
                LastSeenAtUtc = healthy ? checkedAtUtc : null,
                Detail = healthy
                    ? "Parser /health responded healthy."
                    : $"Parser /health returned HTTP {(int)response.StatusCode}.",
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            parserHealth = new ServiceHealthView
            {
                Service = "parser",
                State = ServiceHealthState.Unhealthy,
                Detail = "Parser /health could not be reached.",
            };
        }

        return new AdminServiceHealthSnapshot
        {
            CheckedAtUtc = checkedAtUtc,
            Worker = workerHealth,
            Parser = parserHealth,
        };
    }

    private sealed record ParserHealthResponse
    {
        public required string Status { get; init; }
    }
}
