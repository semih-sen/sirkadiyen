using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Each test uses its own distribution period (a distinct year/month no other finance test touches)
/// because non-repeatability and the distributable-amount figure are both global, not scoped to one
/// test's accounts.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FinanceDistributionStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset SeedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteWritesTheDistributionSharesTransactionsAndAuditsInOneCommit()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (DateOnly start, DateOnly end) = Period(2030, 1);
        Scenario scenario = await SeedPartnersAndIncomeAsync("execute-atomic", start, 1000m);

        (FinanceDistributionPlan plan, FinanceDistributionResult result) =
            await PreviewAndExecuteAsync(scenario, start, end, "Q1 distribution.");

        Assert.Equal(FinanceDistributionOutcome.Executed, result.Outcome);

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        List<FinanceDistributionShare> shares = await verify.FinanceDistributionShares
            .Where(share => share.FinanceDistributionId == result.DistributionId)
            .ToListAsync(Token);

        Assert.Equal(2, shares.Count);
        Assert.Equal(plan.DistributableAmount, shares.Sum(share => share.AllocatedAmount));

        foreach (FinanceDistributionShare share in shares)
        {
            bool transactionExists = await verify.FinanceTransactions
                .AnyAsync(t => t.Id == share.FinanceTransactionId, Token);
            Assert.True(transactionExists);
        }

        bool distributionAuditExists = await verify.FinanceAudits.AnyAsync(
            audit => audit.SubjectId == result.DistributionId
                && audit.Action == FinanceAuditAction.DistributionExecuted,
            Token);
        Assert.True(distributionAuditExists);
    }

    [Fact]
    public async Task TheSourceBalanceDropsByExactlyTheDistributableAmount()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (DateOnly start, DateOnly end) = Period(2030, 2);
        Scenario scenario = await SeedPartnersAndIncomeAsync("balance-drop", start, 500m);

        decimal balanceBefore;
        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            balanceBefore = await context.FinanceLedgerEntries
                .Where(entry => entry.FinanceAccountId == scenario.SourceAccountId)
                .SumAsync(entry => (decimal?)entry.Amount, Token) ?? 0m;
        }

        (FinanceDistributionPlan plan, FinanceDistributionResult result) =
            await PreviewAndExecuteAsync(scenario, start, end, "Q1 distribution.");
        Assert.Equal(FinanceDistributionOutcome.Executed, result.Outcome);

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        decimal balanceAfter = await verify.FinanceLedgerEntries
            .Where(entry => entry.FinanceAccountId == scenario.SourceAccountId)
            .SumAsync(entry => (decimal?)entry.Amount, Token) ?? 0m;

        Assert.Equal(balanceBefore - plan.DistributableAmount, balanceAfter);
    }

    [Fact]
    public async Task ReplayingTheSameConfirmationTokenReturnsTheSameDistributionAndCreatesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (DateOnly start, DateOnly end) = Period(2030, 3);
        Scenario scenario = await SeedPartnersAndIncomeAsync("replay", start, 700m);

        FinanceDistributionPlan plan;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            plan = await new FinanceDistributionStore(context, new FinanceSummaryReadStore(context))
                .PreviewAsync(start, end, scenario.SourceAccountId, Token);
        }

        FinanceDistributionResult first;
        FinanceDistributionResult second;
        int transactionCountBefore;
        int transactionCountAfter;
        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            transactionCountBefore = await context.FinanceTransactions.CountAsync(Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceDistributionStore store = new(context, new FinanceSummaryReadStore(context));
            first = await store.ExecuteAsync(
                start, end, scenario.SourceAccountId, plan.ConfirmationToken!.Value, plan.PlanHash!,
                plan.ExpectedConfirmationPhrase!, "First.", scenario.Actor.UserId, scenario.Actor.Email, null,
                SeedNow, Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceDistributionStore store = new(context, new FinanceSummaryReadStore(context));
            second = await store.ExecuteAsync(
                start, end, scenario.SourceAccountId, plan.ConfirmationToken!.Value, plan.PlanHash!,
                plan.ExpectedConfirmationPhrase!, "Replayed.", scenario.Actor.UserId, scenario.Actor.Email, null,
                SeedNow.AddMinutes(1), Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            transactionCountAfter = await context.FinanceTransactions.CountAsync(Token);
        }

        Assert.Equal(FinanceDistributionOutcome.Executed, first.Outcome);
        Assert.Equal(FinanceDistributionOutcome.ReplayedExistingExecution, second.Outcome);
        Assert.Equal(first.DistributionId, second.DistributionId);
        Assert.Equal(transactionCountBefore + 2, transactionCountAfter);
    }

    [Fact]
    public async Task ASecondDistributionForTheSamePeriodIsRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (DateOnly start, DateOnly end) = Period(2030, 4);
        Scenario scenario = await SeedPartnersAndIncomeAsync("second-for-period", start, 400m);

        (_, FinanceDistributionResult first) = await PreviewAndExecuteAsync(scenario, start, end, "First.");
        Assert.Equal(FinanceDistributionOutcome.Executed, first.Outcome);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        FinanceDistributionPlan secondPreview = await new FinanceDistributionStore(
            context,
            new FinanceSummaryReadStore(context))
            .PreviewAsync(start, end, scenario.SourceAccountId, Token);

        Assert.Equal(FinanceDistributionPlanOutcome.AlreadyDistributedForPeriod, secondPreview.Outcome);
    }

    [Fact]
    public async Task AfterReversalThePeriodCanBeDistributedAgain()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (DateOnly start, DateOnly end) = Period(2030, 5);
        Scenario scenario = await SeedPartnersAndIncomeAsync("after-reversal", start, 300m);

        (_, FinanceDistributionResult first) = await PreviewAndExecuteAsync(scenario, start, end, "First.");
        Assert.Equal(FinanceDistributionOutcome.Executed, first.Outcome);

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceDistributionResult reversed = await new FinanceDistributionStore(
                context,
                new FinanceSummaryReadStore(context))
                .ReverseAsync(
                    first.DistributionId!.Value, "Undo for retest.", scenario.Actor.UserId, scenario.Actor.Email,
                    null, SeedNow.AddDays(1), Token);
            Assert.Equal(FinanceDistributionOutcome.Reversed, reversed.Outcome);
        }

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        FinanceDistributionPlan secondPreview = await new FinanceDistributionStore(
            verify,
            new FinanceSummaryReadStore(verify))
            .PreviewAsync(start, end, scenario.SourceAccountId, Token);

        // The distributable amount is unaffected: the reversed distribution's own payout
        // transactions are Distribution-kind and never counted as Income/Expense.
        Assert.Equal(FinanceDistributionPlanOutcome.Ready, secondPreview.Outcome);
    }

    private async Task<(FinanceDistributionPlan Plan, FinanceDistributionResult Result)> PreviewAndExecuteAsync(
        Scenario scenario,
        DateOnly periodStartOn,
        DateOnly periodEndOn,
        string reason)
    {
        FinanceDistributionPlan plan;
        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            plan = await new FinanceDistributionStore(context, new FinanceSummaryReadStore(context))
                .PreviewAsync(periodStartOn, periodEndOn, scenario.SourceAccountId, Token);
        }

        Assert.Equal(FinanceDistributionPlanOutcome.Ready, plan.Outcome);

        await using SirkadiyenDbContext executeContext = fixture.CreateProductionLikeContext();
        FinanceDistributionResult result = await new FinanceDistributionStore(
            executeContext,
            new FinanceSummaryReadStore(executeContext))
            .ExecuteAsync(
                periodStartOn, periodEndOn, scenario.SourceAccountId, plan.ConfirmationToken!.Value,
                plan.PlanHash!, plan.ExpectedConfirmationPhrase!, reason, scenario.Actor.UserId,
                scenario.Actor.Email, null, SeedNow, Token);

        return (plan, result);
    }

    private async Task<Scenario> SeedPartnersAndIncomeAsync(string prefix, DateOnly incomeOn, decimal incomeAmount)
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

        // Partner shares are a global invariant (they must sum to exactly 10000 basis points at any
        // moment, ADR-093 §7) — not scoped per test — so any partner left active by an earlier test
        // in this shared database would break that sum. Deactivating them first makes each test's
        // own pair the only active partners, matching how the real system is actually used: one
        // fixed partner roster at a time.
        List<Guid> otherActivePartnerIds = await context.FinanceAccountHolders
            .Where(holder => holder.Status == FinanceAccountHolderStatus.Active && holder.ShareBasisPoints > 0)
            .Select(holder => holder.Id)
            .ToListAsync(Token);
        foreach (Guid holderId in otherActivePartnerIds)
        {
            await ledger.DeactivateHolderAsync(holderId, SeedNow, Token);
        }

        FinanceAccountHolderMutationResult holder1 = await ledger.CreateHolderAsync(
            $"P1-{prefix}-{nonce}", null, 6000, SeedNow, Token);
        FinanceAccountHolderMutationResult holder2 = await ledger.CreateHolderAsync(
            $"P2-{prefix}-{nonce}", null, 4000, SeedNow, Token);
        FinanceAccountMutationResult account = await ledger.OpenAccountAsync(
            holder1.HolderId!.Value, $"Source-{prefix}", FinanceAccountKind.Cash, incomeOn.AddDays(-10), SeedNow,
            Token);
        Guid sourceAccountId = account.AccountId!.Value;

        await ledger.RecordOpeningBalanceAsync(
            sourceAccountId, incomeAmount * 2, incomeOn.AddDays(-5), "Opening", actor.UserId, actor.Email, null,
            SeedNow, Token);
        await ledger.RecordIncomeAsync(
            sourceAccountId, incomeAmount, FinanceCategory.Donation, incomeOn, "Donation", null, null,
            actor.UserId, actor.Email, null, SeedNow, Token);

        return new Scenario(actor, sourceAccountId, holder1.HolderId!.Value, holder2.HolderId!.Value);
    }

    private static (DateOnly Start, DateOnly End) Period(int year, int month)
    {
        DateOnly start = new(year, month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private sealed record Scenario(UserSession Actor, Guid SourceAccountId, Guid Holder1Id, Guid Holder2Id);

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
