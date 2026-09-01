namespace Sirkadiyen.Application.Notifications;

/// <summary>
/// What a host registers when no alert channel is configured (ADR-144).
/// </summary>
/// <remarks>
/// Alerting is optional, and an unconfigured deployment must run exactly as it did before the
/// channel existed rather than fail to start. Every stage still calls the notifier unconditionally;
/// this is where those calls go when nobody is listening.
/// </remarks>
public sealed class NullOperatorAlertNotifier : IOperatorAlertNotifier
{
    public static readonly NullOperatorAlertNotifier Instance = new();

    public Task SendAsync(OperatorAlert alert, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
