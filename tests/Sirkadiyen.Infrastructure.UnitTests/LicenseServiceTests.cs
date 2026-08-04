using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Domain.Licensing;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class LicenseServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HashCollisionGeneratesANewCodeWithoutReturningTheFirstPlaintext()
    {
        QueueCodeService codes = new(
            Generated("SRK-AAAAA-AAAAA", 1),
            Generated("SRK-BBBBB-BBBBB", 2));
        StubStore store = new() { CreationCollisionsRemaining = 1 };
        LicenseService service = new(codes, store, new FixedTimeProvider(Now));

        CreatedLicense created = await service.CreateAsync(
            Guid.NewGuid(),
            "admin@example.com",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("SRK-BBBBB-BBBBB", created.PlaintextCode);
        Assert.Equal(2, codes.GenerationCount);
        Assert.Equal(2, store.CreationAttempts);
    }

    [Fact]
    public async Task MalformedRedemptionDoesNotReachPersistence()
    {
        QueueCodeService codes = new() { AcceptHashes = false };
        StubStore store = new();
        LicenseService service = new(codes, store, new FixedTimeProvider(Now));

        LicenseRedemptionResult result = await service.RedeemAsync(
            "not-a-code",
            Guid.NewGuid(),
            "student@example.com",
            CancellationToken.None);

        Assert.Equal(LicenseRedemptionOutcome.Invalid, result.Outcome);
        Assert.Equal(0, store.RedemptionAttempts);
    }

    private static GeneratedLicenseCode Generated(string plaintext, byte fill) => new()
    {
        PlaintextCode = plaintext,
        CodeHash = Enumerable.Repeat(fill, License.CodeHashLength).ToArray(),
    };

    private sealed class QueueCodeService(params GeneratedLicenseCode[] generated)
        : ILicenseCodeService
    {
        private readonly Queue<GeneratedLicenseCode> codes = new(generated);

        public bool AcceptHashes { get; init; } = true;

        public int GenerationCount { get; private set; }

        public GeneratedLicenseCode Generate()
        {
            GenerationCount++;
            return codes.Dequeue();
        }

        public bool TryHash(string plaintextCode, out byte[] codeHash)
        {
            codeHash = AcceptHashes
                ? Enumerable.Repeat((byte)7, License.CodeHashLength).ToArray()
                : [];
            return AcceptHashes;
        }
    }

    private sealed class StubStore : ILicenseStore
    {
        public int CreationCollisionsRemaining { get; init; }

        public int CreationAttempts { get; private set; }

        public int RedemptionAttempts { get; private set; }

        public Task SaveCreatedAsync(
            License license,
            CancellationToken cancellationToken)
        {
            CreationAttempts++;
            if (CreationAttempts <= CreationCollisionsRemaining)
            {
                throw new LicenseHashCollisionException("Synthetic collision.");
            }

            return Task.CompletedTask;
        }

        public Task<LicenseRedemptionResult> RedeemAsync(
            byte[] codeHash,
            Guid userId,
            string userEmail,
            DateTimeOffset redeemedAtUtc,
            CancellationToken cancellationToken)
        {
            RedemptionAttempts++;
            return Task.FromResult(new LicenseRedemptionResult
            {
                Outcome = LicenseRedemptionOutcome.Redeemed,
            });
        }

        public Task<LicenseRevocationResult> RevokeAsync(
            Guid licenseId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset revokedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManualLicenseActivationResult> ActivateManuallyAsync(
            Guid userId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset activatedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserLicenseState> GetUserLicenseStateAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserLicenseSummary?> GetUserLicenseSummaryAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
