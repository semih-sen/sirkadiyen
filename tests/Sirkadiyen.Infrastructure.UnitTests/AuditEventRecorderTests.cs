using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Auditing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class AuditEventRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordsMaskedIpAndEncryptsTheFullAddress()
    {
        CapturingStore store = new();
        AuditEventRecorder recorder = new(store, new ReversingProtector(), FixedClock());

        await recorder.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.SignIn,
                ActorEmail = "student@example.com",
                ClientIp = "203.0.113.42",
                UserAgent = "unit-test-agent",
            },
            CancellationToken.None);

        AuditEvent recorded = Assert.IsType<AuditEvent>(store.Appended);
        Assert.Equal(AuditEventCategory.SignIn, recorded.Category);
        Assert.Equal(Now, recorded.OccurredAtUtc);
        Assert.Equal("203.0.113.0", recorded.MaskedIp);
        Assert.Equal("enc:203.0.113.42", recorded.ProtectedIp);
        Assert.Equal("unit-test-agent", recorded.UserAgent);
    }

    [Fact]
    public async Task OmitsIpFieldsAndNeverEncryptsWhenNoAddressIsGiven()
    {
        CapturingStore store = new();
        AuditEventRecorder recorder = new(store, new ThrowingProtector(), FixedClock());

        await recorder.RecordAsync(
            new AuditEventDraft
            {
                Category = AuditEventCategory.ReconcileRequested,
                ActorEmail = "student@example.com",
            },
            CancellationToken.None);

        AuditEvent recorded = Assert.IsType<AuditEvent>(store.Appended);
        Assert.Null(recorded.MaskedIp);
        Assert.Null(recorded.ProtectedIp);
    }

    private static TimeProvider FixedClock() => new FixedTimeProvider(Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingStore : IAuditEventStore
    {
        public AuditEvent? Appended { get; private set; }

        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Appended = auditEvent;
            return Task.CompletedTask;
        }

        public Task<PagedResult<AuditEventView>> QueryAsync(
            AuditEventQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuditEventView?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetProtectedIpAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ReversingProtector : IAuditIpProtector
    {
        public string Protect(string plaintextIp) => $"enc:{plaintextIp}";

        public string Unprotect(string ciphertext) => ciphertext["enc:".Length..];
    }

    private sealed class ThrowingProtector : IAuditIpProtector
    {
        public string Protect(string plaintextIp) =>
            throw new InvalidOperationException("Protect must not be called without an IP.");

        public string Unprotect(string ciphertext) => throw new NotSupportedException();
    }
}
