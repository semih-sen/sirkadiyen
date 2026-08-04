using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Whole-table sweeps proving the non-declarative invariants domain-enforced today hold across
/// everything actually in the database, mirroring <c>SchedulePipelineIntegrityTests</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FinanceIntegrityTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task NoTransferHasANonZeroEntrySum()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await SeedRepresentativeLedgerAsync("transfer-zero-sum");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        List<decimal> transferSums = await context.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.Kind == FinanceTransactionKind.Transfer)
            .GroupBy(entry => entry.FinanceTransactionId)
            .Select(group => group.Sum(entry => entry.Amount))
            .ToListAsync(Token);

        Assert.NotEmpty(transferSums);
        Assert.All(transferSums, sum => Assert.Equal(0m, sum));
    }

    [Fact]
    public async Task NoEntryDisagreesWithItsTransactionsKindOrOccurredOn()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await SeedRepresentativeLedgerAsync("denormalization");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        List<FinanceLedgerEntry> entries = await context.FinanceLedgerEntries.AsNoTracking().ToListAsync(Token);
        Dictionary<Guid, FinanceTransaction> transactions = await context.FinanceTransactions
            .AsNoTracking()
            .ToDictionaryAsync(transaction => transaction.Id, Token);

        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            FinanceTransaction transaction = transactions[entry.FinanceTransactionId];
            Assert.Equal(transaction.Kind, entry.Kind);
            Assert.Equal(transaction.OccurredOn, entry.OccurredOn);
        });
    }

    [Fact]
    public async Task NoAccountHasTwoOpeningBalances()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await SeedRepresentativeLedgerAsync("single-opening-balance");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        List<int> openingBalanceCounts = await context.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.Kind == FinanceTransactionKind.OpeningBalance)
            .GroupBy(entry => entry.FinanceAccountId)
            .Select(group => group.Count())
            .ToListAsync(Token);

        Assert.NotEmpty(openingBalanceCounts);
        Assert.All(openingBalanceCounts, count => Assert.Equal(1, count));
    }

    private async Task SeedRepresentativeLedgerAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        UserSession actor = await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.SuperAdmin,
            Now,
            Token);

        FinanceLedgerStore store = new(context);
        FinanceAccountHolderMutationResult holder = await store.CreateHolderAsync(
            $"Holder-{prefix}-{nonce}", null, 0, Now, Token);
        FinanceAccountMutationResult accountA = await store.OpenAccountAsync(
            holder.HolderId!.Value, $"A-{prefix}", FinanceAccountKind.Cash, Today, Now, Token);
        FinanceAccountMutationResult accountB = await store.OpenAccountAsync(
            holder.HolderId!.Value, $"B-{prefix}", FinanceAccountKind.Bank, Today, Now, Token);

        await store.RecordOpeningBalanceAsync(
            accountA.AccountId!.Value, 1000m, Today, "Opening", actor.UserId, actor.Email, null, Now, Token);
        await store.RecordIncomeAsync(
            accountA.AccountId!.Value, 200m, FinanceCategory.Donation, Today, "Income", null, null,
            actor.UserId, actor.Email, null, Now, Token);
        await store.RecordExpenseAsync(
            accountA.AccountId!.Value, 50m, FinanceCategory.Servers, Today, "Expense", null, null,
            actor.UserId, actor.Email, null, Now, Token);
        await store.RecordTransferAsync(
            accountA.AccountId!.Value, accountB.AccountId!.Value, 100m, Today, "Transfer", null,
            actor.UserId, actor.Email, null, Now, Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
