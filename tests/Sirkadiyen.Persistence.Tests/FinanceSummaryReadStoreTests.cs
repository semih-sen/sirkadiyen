using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// A seeded scenario (2 holders, 2 accounts, opening balances, income/expense in the reporting
/// month, one transfer, one receivable collected within the period, one receivable and one payable
/// left outstanding past the period end) exercising all ten summary figures.
/// </summary>
/// <remarks>
/// Receivables/Debts/Collections/Payments are global — an obligation is not scoped to an account —
/// so every assertion here is a before/after delta across the seed, not an absolute value: the
/// fixture's database is shared across the whole collection, and other tests' obligations are real
/// rows the query correctly includes.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class FinanceSummaryReadStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset SeedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly PeriodStart = new(2026, 2, 1);

    private static readonly DateOnly PeriodEnd = new(2026, 2, 28);

    [Fact]
    public async Task AllTenFiguresMatchTheSeededScenarioForAllAccounts()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        FinanceSummaryReadStore store = new(fixture.CreateContext());
        DateOnly asOf = new(2026, 4, 1);

        FinanceSummary before = await store.GetSummaryAsync(PeriodStart, PeriodEnd, asOf, null, Token);
        await SeedAsync("all-ten");
        FinanceSummary after = await store.GetSummaryAsync(PeriodStart, PeriodEnd, asOf, null, Token);

        Assert.Equal(1500m, after.CarriedOver - before.CarriedOver);
        Assert.Equal(550m, after.Income - before.Income);
        Assert.Equal(160m, after.Expenses - before.Expenses);
        Assert.Equal(390m, after.Balance - before.Balance);
        Assert.Equal(1890m, after.ToBeCarriedOver - before.ToBeCarriedOver);
        Assert.Equal(400m, after.Receivables - before.Receivables);
        Assert.Equal(250m, after.Collections - before.Collections);
        Assert.Equal(80m, after.Debts - before.Debts);
        Assert.Equal(60m, after.Payments - before.Payments);
        Assert.Equal(2210m, after.CurrentBalance - before.CurrentBalance);
        Assert.True(after.PeriodIsClosed);
        Assert.False(after.PeriodStartsInFuture);
    }

    [Fact]
    public async Task TheClosingBalanceIsTheOpeningBalancePlusTheNetResultForAllAccounts()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        await SeedAsync("identity-all");

        FinanceSummaryReadStore store = new(fixture.CreateContext());
        FinanceSummary summary = await store.GetSummaryAsync(
            PeriodStart,
            PeriodEnd,
            new DateOnly(2026, 4, 1),
            accountId: null,
            Token);

        // This identity holds over the whole table (transfers always net to zero across all
        // accounts), so it is safe to assert on the absolute totals even in a shared database.
        Assert.Equal(summary.ToBeCarriedOver, summary.CarriedOver + summary.Balance);
    }

    [Fact]
    public async Task TheSingleAccountIdentityDiffersByExactlyTheTransferNet()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        Scenario scenario = await SeedAsync("identity-single");

        // Scoped to one account, so no other test's data can contaminate this query.
        FinanceSummaryReadStore store = new(fixture.CreateContext());
        FinanceSummary summary = await store.GetSummaryAsync(
            PeriodStart,
            PeriodEnd,
            new DateOnly(2026, 4, 1),
            scenario.AccountA,
            Token);

        // Account A is the transfer's From leg: -150 in the period, which Income/Expenses never
        // captures because a transfer is neither.
        const decimal transferNetForAccountA = -150m;
        Assert.Equal(
            summary.ToBeCarriedOver,
            summary.CarriedOver + summary.Balance + transferNetForAccountA);
        Assert.Equal(1000m, summary.CarriedOver);
        Assert.Equal(300m, summary.Income);
        Assert.Equal(100m, summary.Expenses);
        Assert.Equal(1050m, summary.ToBeCarriedOver);
    }

    [Fact]
    public async Task AFuturePeriodIsFlaggedAndCurrentDiffersFromCarriedOver()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        FinanceSummaryReadStore store = new(fixture.CreateContext());
        DateOnly futureStart = new(2027, 1, 1);
        DateOnly futureEnd = new(2027, 1, 31);

        // "Today" sits before the scenario's March settlements, so the future period's
        // CarriedOver (which sums everything up to 2027) includes them while "today" does not.
        DateOnly today = new(2026, 3, 1);
        FinanceSummary before = await store.GetSummaryAsync(futureStart, futureEnd, today, null, Token);
        await SeedAsync("future-period");
        FinanceSummary after = await store.GetSummaryAsync(futureStart, futureEnd, today, null, Token);

        Assert.True(after.PeriodStartsInFuture);
        decimal carriedOverDelta = after.CarriedOver - before.CarriedOver;
        decimal currentDelta = after.CurrentBalance - before.CurrentBalance;
        Assert.NotEqual(carriedOverDelta, currentDelta);
    }

    [Fact]
    public async Task APastPeriodIsFlaggedAndCurrentDiffersFromToBeCarriedOver()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        FinanceSummaryReadStore store = new(fixture.CreateContext());
        DateOnly asOf = new(2026, 4, 1);

        FinanceSummary before = await store.GetSummaryAsync(PeriodStart, PeriodEnd, asOf, null, Token);
        await SeedAsync("past-period");
        FinanceSummary after = await store.GetSummaryAsync(PeriodStart, PeriodEnd, asOf, null, Token);

        Assert.True(after.PeriodIsClosed);
        decimal toBeCarriedOverDelta = after.ToBeCarriedOver - before.ToBeCarriedOver;
        decimal currentDelta = after.CurrentBalance - before.CurrentBalance;
        Assert.NotEqual(toBeCarriedOverDelta, currentDelta);
    }

    [Fact]
    public async Task ReceivablesAreMeasuredAtThePeriodEndNotToday()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        FinanceSummaryReadStore store = new(fixture.CreateContext());

        // "Today" (April) is well after the receivable settled in March, but the period end is
        // still February: the obligation was outstanding at that instant and must still count.
        DateOnly asOf = new(2026, 4, 1);
        FinanceSummary before = await store.GetSummaryAsync(PeriodStart, PeriodEnd, asOf, null, Token);
        await SeedAsync("receivables-as-of");
        FinanceSummary after = await store.GetSummaryAsync(PeriodStart, PeriodEnd, asOf, null, Token);

        Assert.Equal(400m, after.Receivables - before.Receivables);
    }

    [Fact]
    public async Task TrendPutsFebruarysIncomeAndExpensesInFebruarysBucket()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        FinanceSummaryReadStore store = new(fixture.CreateContext());
        DateOnly today = new(2026, 3, 15);

        List<FinanceTrendPoint> before = [.. await store.GetTrendAsync(3, today, Token)];
        await SeedAsync("trend");
        List<FinanceTrendPoint> after = [.. await store.GetTrendAsync(3, today, Token)];

        Assert.Equal(3, after.Count);
        FinanceTrendPoint februaryBefore = before.Single(point => point.Year == 2026 && point.Month == 2);
        FinanceTrendPoint februaryAfter = after.Single(point => point.Year == 2026 && point.Month == 2);

        // 300 direct income + 250 from the in-period settlement; 100 direct expense + 60 from the
        // in-period payable settlement — the same figures the period summary reports for February.
        Assert.Equal(550m, februaryAfter.Income - februaryBefore.Income);
        Assert.Equal(160m, februaryAfter.Expenses - februaryBefore.Expenses);
        Assert.Equal(390m, februaryAfter.Net - februaryBefore.Net);

        FinanceTrendPoint januaryAfter = after.Single(point => point.Year == 2026 && point.Month == 1);
        FinanceTrendPoint januaryBefore = before.Single(point => point.Year == 2026 && point.Month == 1);
        Assert.Equal(0m, januaryAfter.Income - januaryBefore.Income);
    }

    private async Task<Scenario> SeedAsync(string prefix)
    {
        await using SirkadiyenDbContext userContext = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        UserSession actor = await new UserStore(userContext).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.SuperAdmin,
            SeedNow,
            Token);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore ledger = new(context);
        FinanceObligationStore obligations = new(context);

        FinanceAccountHolderMutationResult holderA = await ledger.CreateHolderAsync(
            $"H1-{prefix}-{nonce}", null, 0, SeedNow, Token);
        FinanceAccountHolderMutationResult holderB = await ledger.CreateHolderAsync(
            $"H2-{prefix}-{nonce}", null, 0, SeedNow, Token);
        FinanceAccountMutationResult accountA = await ledger.OpenAccountAsync(
            holderA.HolderId!.Value, $"A1-{prefix}", FinanceAccountKind.Cash, new DateOnly(2026, 1, 1), SeedNow,
            Token);
        FinanceAccountMutationResult accountB = await ledger.OpenAccountAsync(
            holderB.HolderId!.Value, $"A2-{prefix}", FinanceAccountKind.Bank, new DateOnly(2026, 1, 1), SeedNow,
            Token);
        Guid a1 = accountA.AccountId!.Value;
        Guid a2 = accountB.AccountId!.Value;

        // Before the period: opening balances only, so they land purely in CarriedOver.
        await ledger.RecordOpeningBalanceAsync(
            a1, 1000m, new DateOnly(2026, 1, 1), "Opening", actor.UserId, actor.Email, null, SeedNow, Token);
        await ledger.RecordOpeningBalanceAsync(
            a2, 500m, new DateOnly(2026, 1, 1), "Opening", actor.UserId, actor.Email, null, SeedNow, Token);

        // Within the period.
        DateTimeOffset feb10 = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);
        await ledger.RecordIncomeAsync(
            a1, 300m, FinanceCategory.Donation, new DateOnly(2026, 2, 10), "Donation", null, null,
            actor.UserId, actor.Email, null, feb10, Token);
        await ledger.RecordExpenseAsync(
            a1, 100m, FinanceCategory.Servers, new DateOnly(2026, 2, 12), "Server bill", null, null,
            actor.UserId, actor.Email, null, feb10.AddDays(2), Token);
        await ledger.RecordTransferAsync(
            a1, a2, 150m, new DateOnly(2026, 2, 15), "Rebalance", null,
            actor.UserId, actor.Email, null, feb10.AddDays(5), Token);

        // Receivable settled fully within the period (Collections).
        FinanceObligationMutationResult receivableSettledInPeriod = await obligations.CreateAsync(
            FinanceObligationDirection.Receivable, FinanceCategory.LicenseSales, "Client A", null,
            250m, new DateOnly(2026, 2, 1), null, actor.UserId, actor.Email, SeedNow, Token);
        await obligations.SettleAsync(
            receivableSettledInPeriod.ObligationId!.Value, a2, 250m, new DateOnly(2026, 2, 20), null,
            actor.UserId, actor.Email, null, feb10.AddDays(10), Token);

        // Receivable left outstanding at the period end; settled only afterward, in March.
        FinanceObligationMutationResult receivableOutstanding = await obligations.CreateAsync(
            FinanceObligationDirection.Receivable, FinanceCategory.Donation, "Client B", null,
            400m, new DateOnly(2026, 2, 5), null, actor.UserId, actor.Email, SeedNow, Token);
        await obligations.SettleAsync(
            receivableOutstanding.ObligationId!.Value, a1, 400m, new DateOnly(2026, 3, 10), null,
            actor.UserId, actor.Email, null, new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero), Token);

        // Payable settled fully within the period (Payments).
        FinanceObligationMutationResult payableSettledInPeriod = await obligations.CreateAsync(
            FinanceObligationDirection.Payable, FinanceCategory.Operational, "Vendor A", null,
            60m, new DateOnly(2026, 2, 1), null, actor.UserId, actor.Email, SeedNow, Token);
        await obligations.SettleAsync(
            payableSettledInPeriod.ObligationId!.Value, a2, 60m, new DateOnly(2026, 2, 18), null,
            actor.UserId, actor.Email, null, feb10.AddDays(8), Token);

        // Payable left outstanding at the period end; settled only afterward, in March.
        FinanceObligationMutationResult payableOutstanding = await obligations.CreateAsync(
            FinanceObligationDirection.Payable, FinanceCategory.Servers, "Vendor B", null,
            80m, new DateOnly(2026, 2, 1), null, actor.UserId, actor.Email, SeedNow, Token);
        await obligations.SettleAsync(
            payableOutstanding.ObligationId!.Value, a1, 80m, new DateOnly(2026, 3, 5), null,
            actor.UserId, actor.Email, null, new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero), Token);

        return new Scenario(a1, a2);
    }

    private sealed record Scenario(Guid AccountA, Guid AccountB);

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
