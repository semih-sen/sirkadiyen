using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Sirkadiyen.Worker.Composition;

internal static class WorkerHostExtensions
{
    private const string DefaultHealthUrl = "http://127.0.0.1:5081";

    public static void ConfigureWorkerHost(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        string healthUrl = builder.Configuration["SIRKADIYEN_WORKER:HEALTH_URL"]
            ?? DefaultHealthUrl;
        builder.WebHost.UseUrls(healthUrl);
    }
}
