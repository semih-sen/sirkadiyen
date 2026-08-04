using System.Net.Http.Json;
using System.Text.Json;
using Sirkadiyen.Application.Administration;

namespace Sirkadiyen.Infrastructure.Observability;

public sealed class AdminServiceHealthProbe(
    HttpClient httpClient,
    AdminServiceHealthProbeOptions options,
    TimeProvider timeProvider) : IAdminServiceHealthProbe
{
    public async Task<AdminServiceHealthSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset checkedAtUtc = timeProvider.GetUtcNow();
        Task<ServiceHealthView> worker = ProbeWorkerAsync(cancellationToken);
        Task<ServiceHealthView> parser = ProbeParserAsync(cancellationToken);
        await Task.WhenAll(worker, parser);

        return new AdminServiceHealthSnapshot
        {
            CheckedAtUtc = checkedAtUtc,
            Worker = await worker,
            Parser = await parser,
        };
    }

    private async Task<ServiceHealthView> ProbeWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                new Uri(options.WorkerBaseUrl, "health/ready"),
                cancellationToken);
            WorkerHealthResponse? body = await response.Content
                .ReadFromJsonAsync<WorkerHealthResponse>(cancellationToken);
            bool healthy = response.IsSuccessStatusCode
                && string.Equals(body?.Status, "healthy", StringComparison.OrdinalIgnoreCase);
            return new ServiceHealthView
            {
                Service = "worker",
                State = healthy ? ServiceHealthState.Healthy : ServiceHealthState.Unhealthy,
                LastSeenAtUtc = body?.LastActivityAtUtc,
                Detail = healthy
                    ? $"Worker is in stage '{body!.CurrentStage}'."
                    : $"Worker /health/ready returned HTTP {(int)response.StatusCode}.",
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ServiceHealthView
            {
                Service = "worker",
                State = ServiceHealthState.Unhealthy,
                Detail = "Worker /health/ready could not be reached.",
            };
        }
    }

    private async Task<ServiceHealthView> ProbeParserAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                new Uri(options.ParserBaseUrl, "health"),
                cancellationToken);
            ParserHealthResponse? body = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ParserHealthResponse>(cancellationToken)
                : null;
            bool healthy = response.IsSuccessStatusCode
                && string.Equals(body?.Status, "healthy", StringComparison.OrdinalIgnoreCase);
            return new ServiceHealthView
            {
                Service = "parser",
                State = healthy ? ServiceHealthState.Healthy : ServiceHealthState.Unhealthy,
                LastSeenAtUtc = healthy ? timeProvider.GetUtcNow() : null,
                Detail = healthy
                    ? "Parser /health responded healthy."
                    : $"Parser /health returned HTTP {(int)response.StatusCode}.",
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ServiceHealthView
            {
                Service = "parser",
                State = ServiceHealthState.Unhealthy,
                Detail = "Parser /health could not be reached.",
            };
        }
    }

    private sealed record WorkerHealthResponse
    {
        public required string Status { get; init; }
        public DateTimeOffset LastActivityAtUtc { get; init; }
        public required string CurrentStage { get; init; }
    }

    private sealed record ParserHealthResponse
    {
        public required string Status { get; init; }
    }
}

public sealed record AdminServiceHealthProbeOptions
{
    public required Uri WorkerBaseUrl { get; init; }
    public required Uri ParserBaseUrl { get; init; }
}
