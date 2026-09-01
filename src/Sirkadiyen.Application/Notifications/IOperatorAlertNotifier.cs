namespace Sirkadiyen.Application.Notifications;

/// <summary>
/// Sends an <see cref="OperatorAlert"/> somewhere a person will see it (ADR-144).
/// </summary>
/// <remarks>
/// The thing that reports trouble must never become trouble. Every implementation therefore
/// swallows its own failures: a send that fails is logged by the adapter and reported nowhere
/// else, because the alternative is a messaging outage stopping a schedule from reaching a
/// student's calendar. A caller never needs a <c>try</c> around this, and must never make the
/// success of its own work depend on it.
/// </remarks>
public interface IOperatorAlertNotifier
{
    /// <summary>
    /// Delivers the alert, or quietly does not. Never throws, including on cancellation.
    /// </summary>
    Task SendAsync(OperatorAlert alert, CancellationToken cancellationToken);
}
