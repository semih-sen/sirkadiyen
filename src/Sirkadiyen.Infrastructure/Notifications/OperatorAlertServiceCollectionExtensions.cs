using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sirkadiyen.Application.Notifications;

namespace Sirkadiyen.Infrastructure.Notifications;

public static class OperatorAlertServiceCollectionExtensions
{
    /// <summary>
    /// Registers the operator alert channel, or the silent one when none is configured (ADR-144).
    /// </summary>
    /// <remarks>
    /// Alerting is optional on purpose. A host with no bot token still resolves
    /// <see cref="IOperatorAlertNotifier"/> and still calls it from every stage; the calls simply
    /// go nowhere. Making a missing token fatal would mean a messaging credential could stop
    /// schedules from reaching calendars, which is precisely backwards.
    /// </remarks>
    public static IServiceCollection AddSirkadiyenOperatorAlerts(
        this IServiceCollection services,
        TelegramAlertOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        services.AddSingleton(options);

        if (!options.IsConfigured)
        {
            services.AddSingleton<IOperatorAlertNotifier>(provider =>
            {
                provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(OperatorAlertServiceCollectionExtensions))
                    .LogInformation(
                        "No Telegram alert channel is configured. Operator alerts are written to "
                        + "the journal only. Set SIRKADIYEN_TELEGRAM__BOT_TOKEN and "
                        + "SIRKADIYEN_TELEGRAM__CHAT_IDS to enable them.");
                return NullOperatorAlertNotifier.Instance;
            });
            return services;
        }

        services.AddHttpClient(TelegramOperatorAlertNotifier.HttpClientName, client =>
            {
                client.BaseAddress = TelegramOperatorAlertNotifier.BaseAddress;
                client.Timeout = options.Timeout;
            })
            // The bot token is a path segment, so the default request logging would write the
            // credential into the log at Information on every alert (ADR-144). The adapter logs
            // what is safe instead.
            .RemoveAllLoggers();

        services.AddSingleton<TelegramOperatorAlertNotifier>();
        services.AddSingleton<IOperatorAlertNotifier>(provider =>
        {
            // Said once at startup so an operator can confirm the channel took effect. Describe()
            // is the reason this is safe to log: the bot token is not in it.
            provider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(OperatorAlertServiceCollectionExtensions))
                .LogInformation(
                    "Operator alerts are sent to Telegram: {AlertChannel}.",
                    options.Describe());
            return new OperatorAlertGate(
                provider.GetRequiredService<TelegramOperatorAlertNotifier>(),
                options,
                provider.GetRequiredService<TimeProvider>());
        });

        return services;
    }
}
