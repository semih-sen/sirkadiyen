using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sirkadiyen.Worker;

internal sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Sirkadiyen worker started.");

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
