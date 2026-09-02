using Microsoft.Extensions.Hosting;
using Sirkadiyen.Infrastructure.Observability;

namespace Sirkadiyen.Api.Observability;

/// <summary>
/// Drives the shared <see cref="ServerResourceMonitor"/> on a fixed cadence so the admin dashboard's
/// resource history is filled with real samples. Sampling lives here, in the host, because the monitor
/// deliberately carries no scheduling of its own — the same instance answers the endpoint's reads.
/// </summary>
public sealed class ServerResourceSamplingService(ServerResourceMonitor monitor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        // A baseline first, so the interval that follows yields the first real CPU delta.
        monitor.Sample();

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(monitor.SampleIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                monitor.Sample();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
