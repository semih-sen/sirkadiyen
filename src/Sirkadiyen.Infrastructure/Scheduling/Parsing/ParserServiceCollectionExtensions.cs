using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.Scheduling.Parsing;

namespace Sirkadiyen.Infrastructure.Scheduling.Parsing;

public static class ParserServiceCollectionExtensions
{
    public static IServiceCollection AddSirkadiyenParserClient(
        this IServiceCollection services,
        Uri baseAddress,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("Parser base address must be absolute.", nameof(baseAddress));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        services.AddHttpClient<IScheduleParserClient, ParserHttpClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = timeout;
        });
        return services;
    }
}
