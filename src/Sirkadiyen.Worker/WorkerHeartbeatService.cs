using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Operations;

namespace Sirkadiyen.Worker;

/// <summary>Writes a process heartbeat independently of the potentially long source cycle.</summary>
internal sealed class WorkerHeartbeatService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WorkerHeartbeatService> logger) : BackgroundService
{
    internal const string ServiceName = "worker";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private readonly DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
    private readonly string instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IServiceHeartbeatStore store = scope.ServiceProvider
                    .GetRequiredService<IServiceHeartbeatStore>();
                await store.RecordAsync(
                    ServiceName,
                    instanceId,
                    startedAtUtc,
                    timeProvider.GetUtcNow(),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Worker heartbeat could not be persisted.");
            }

            await Task.Delay(Interval, timeProvider, stoppingToken);
        }
    }
}
