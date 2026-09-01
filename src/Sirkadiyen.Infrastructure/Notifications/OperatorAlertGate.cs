using System.Collections.Concurrent;
using Sirkadiyen.Application.Notifications;

namespace Sirkadiyen.Infrastructure.Notifications;

/// <summary>
/// Decides whether an alert is sent at all, and forwards the ones that are (ADR-144).
/// </summary>
/// <remarks>
/// Two rules, both about a channel staying readable rather than about correctness:
/// <list type="number">
/// <item>An alert below the configured minimum severity is dropped, so a deployment that wants
/// only problems can stop being told about every routine revision.</item>
/// <item>An alert whose <see cref="OperatorAlert.DedupeKey"/> was sent within the cooldown is
/// dropped. A key naming a specific revision or diff is unique and always passes; a key naming a
/// standing condition is what this suppresses, because the stall watch deliberately repeats
/// itself every cycle and a chat is not a journal.</item>
/// </list>
/// <para>
/// The record of what has been sent is in memory, per worker instance. That is honest about what
/// it is worth: a restart re-announces a standing condition once, which is the harmless direction
/// to be wrong in, and it costs no table, no migration and no round trip on a path whose whole
/// job is to stay off the critical path. Two worker instances would each alert once per cooldown,
/// which is a smaller problem than two instances running at all (ADR-124 reports that separately).
/// </para>
/// </remarks>
public sealed class OperatorAlertGate(
    IOperatorAlertNotifier inner,
    TelegramAlertOptions options,
    TimeProvider timeProvider) : IOperatorAlertNotifier
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> sentUntil = new(StringComparer.Ordinal);

    /// <summary>
    /// How many suppressions are currently held. Exists so a test can assert that expired ones
    /// are discarded, which is the only thing bounding this.
    /// </summary>
    internal int SuppressedKeyCount => sentUntil.Count;

    public Task SendAsync(OperatorAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (alert.Severity < options.MinimumSeverity)
        {
            return Task.CompletedTask;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Prune(now);

        if (options.RepeatCooldown > TimeSpan.Zero
            && !sentUntil.TryAdd(alert.DedupeKey, now + options.RepeatCooldown))
        {
            return Task.CompletedTask;
        }

        return inner.SendAsync(alert, cancellationToken);
    }

    /// <summary>
    /// Drops expired suppressions, which is also what bounds the dictionary.
    /// </summary>
    /// <remarks>
    /// Most keys name one revision or one diff and are never seen again, so without this the map
    /// would grow for the lifetime of the process. Every entry expires after the cooldown, so
    /// pruning on each send keeps it to what a cooldown's worth of alerts can hold.
    /// </remarks>
    private void Prune(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, DateTimeOffset> entry in sentUntil)
        {
            if (entry.Value <= now)
            {
                // Removed by value, so a key re-added between the read and this call survives.
                ((ICollection<KeyValuePair<string, DateTimeOffset>>)sentUntil).Remove(entry);
            }
        }
    }
}
