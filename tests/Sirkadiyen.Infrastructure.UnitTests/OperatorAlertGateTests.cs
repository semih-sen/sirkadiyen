using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Infrastructure.Notifications;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// What reaches the channel and what is dropped on the way (ADR-144).
/// </summary>
public sealed class OperatorAlertGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnAlertBelowTheConfiguredSeverityIsNotSent()
    {
        RecordingNotifier inner = new();
        MovableTimeProvider clock = new(Now);
        OperatorAlertGate gate = new(
            inner,
            Options(minimum: OperatorAlertSeverity.Warning),
            clock);

        await gate.SendAsync(Alert("a", OperatorAlertSeverity.Info), CancellationToken.None);
        await gate.SendAsync(Alert("b", OperatorAlertSeverity.Warning), CancellationToken.None);
        await gate.SendAsync(Alert("c", OperatorAlertSeverity.Error), CancellationToken.None);

        Assert.Equal(["b", "c"], inner.Sent.Select(alert => alert.DedupeKey));
    }

    [Fact]
    public async Task DifferentKeysAreAllSentBecauseEachNamesADifferentEvent()
    {
        // Every alert about a specific revision or diff carries that identifier in its key, so
        // suppression must never collapse two real changes into one message.
        RecordingNotifier inner = new();
        OperatorAlertGate gate = new(inner, Options(), new MovableTimeProvider(Now));

        await gate.SendAsync(Alert("revision-created:1"), CancellationToken.None);
        await gate.SendAsync(Alert("revision-created:2"), CancellationToken.None);
        await gate.SendAsync(Alert("revision-created:3"), CancellationToken.None);

        Assert.Equal(3, inner.Sent.Count);
    }

    [Fact]
    public async Task TheSameKeyWithinTheCooldownIsSentOnce()
    {
        RecordingNotifier inner = new();
        MovableTimeProvider clock = new(Now);
        OperatorAlertGate gate = new(
            inner,
            Options(cooldown: TimeSpan.FromHours(6)),
            clock);

        await gate.SendAsync(Alert("pipeline-stalled"), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(5));
        await gate.SendAsync(Alert("pipeline-stalled"), CancellationToken.None);

        Assert.Single(inner.Sent);
    }

    [Fact]
    public async Task AStandingConditionIsRepeatedOnceTheCooldownHasPassed()
    {
        // A stall that survives a working day has to be said again, or the one message announcing
        // it scrolls away and the pipeline is blocked in silence — the fault ADR-143 exists for.
        RecordingNotifier inner = new();
        MovableTimeProvider clock = new(Now);
        OperatorAlertGate gate = new(
            inner,
            Options(cooldown: TimeSpan.FromHours(6)),
            clock);

        await gate.SendAsync(Alert("pipeline-stalled"), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromSeconds(1));
        await gate.SendAsync(Alert("pipeline-stalled"), CancellationToken.None);

        Assert.Equal(2, inner.Sent.Count);
    }

    [Fact]
    public async Task AZeroCooldownSuppressesNothing()
    {
        RecordingNotifier inner = new();
        OperatorAlertGate gate = new(
            inner,
            Options(cooldown: TimeSpan.Zero),
            new MovableTimeProvider(Now));

        await gate.SendAsync(Alert("k"), CancellationToken.None);
        await gate.SendAsync(Alert("k"), CancellationToken.None);

        Assert.Equal(2, inner.Sent.Count);
    }

    [Fact]
    public async Task ExpiredSuppressionsAreDiscardedSoTheRecordCannotGrowForever()
    {
        // Almost every key names one revision or one diff and is never seen again. Without
        // pruning, a worker that runs for a term accumulates one entry per change it ever
        // announced.
        RecordingNotifier inner = new();
        MovableTimeProvider clock = new(Now);
        OperatorAlertGate gate = new(
            inner,
            Options(cooldown: TimeSpan.FromMinutes(30)),
            clock);

        for (int index = 0; index < 200; index++)
        {
            await gate.SendAsync(Alert($"diff:{index}"), CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromHours(1));
        await gate.SendAsync(Alert("diff:0"), CancellationToken.None);

        Assert.Equal(201, inner.Sent.Count);
        Assert.Equal(1, gate.SuppressedKeyCount);
    }

    private static TelegramAlertOptions Options(
        OperatorAlertSeverity minimum = OperatorAlertSeverity.Info,
        TimeSpan? cooldown = null) => new()
        {
            BotToken = "token",
            ChatIds = [1],
            MinimumSeverity = minimum,
            RepeatCooldown = cooldown ?? TimeSpan.FromHours(6),
        };

    private static OperatorAlert Alert(
        string key,
        OperatorAlertSeverity severity = OperatorAlertSeverity.Warning) => new()
        {
            Title = "Başlık",
            Severity = severity,
            DedupeKey = key,
        };

    private sealed class RecordingNotifier : IOperatorAlertNotifier
    {
        public List<OperatorAlert> Sent { get; } = [];

        public Task SendAsync(OperatorAlert alert, CancellationToken cancellationToken)
        {
            Sent.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan amount) => current += amount;
    }
}
