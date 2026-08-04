using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class FinanceConcurrencyTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task TwoTransfersFromOneAccountWithEnoughBalanceForOneLeaveExactlyOneSucceeding()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("transfer-race");
        Guid sourceId = await CreateAccountAsync("transfer-race-source", actor);
        Guid destinationOneId = await CreateAccountAsync("transfer-race-dest-1", actor);
        Guid destinationTwoId = await CreateAccountAsync("transfer-race-dest-2", actor);

        await using (SirkadiyenDbContext seedContext = fixture.CreateProductionLikeContext())
        {
            await new FinanceLedgerStore(seedContext).RecordOpeningBalanceAsync(
                sourceId, 100m, Today, "Opening", actor.UserId, actor.Email, null, Now, Token);
        }

        await using SirkadiyenDbContext contextOne = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext contextTwo = fixture.CreateProductionLikeContext();

        FinanceTransactionMutationResult[] results = await Task.WhenAll(
            new FinanceLedgerStore(contextOne).RecordTransferAsync(
                sourceId, destinationOneId, 80m, Today, "First", null,
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token),
            new FinanceLedgerStore(contextTwo).RecordTransferAsync(
                sourceId, destinationTwoId, 80m, Today, "Second", null,
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token));

        Assert.Single(results, result => result.Outcome == FinanceTransactionOutcome.Recorded);
        Assert.Single(results, result => result.Outcome == FinanceTransactionOutcome.InsufficientBalance);

        await using SirkadiyenDbContext verifyContext = fixture.CreateContext();
        decimal? remaining = await verifyContext.FinanceLedgerEntries
            .Where(entry => entry.FinanceAccountId == sourceId)
            .SumAsync(entry => (decimal?)entry.Amount, Token);
        Assert.Equal(20m, remaining);
    }

    [Fact]
    public async Task TwoParallelEditsOfOneTransactionLeaveExactlyOneWinner()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("edit-race");
        Guid accountId = await CreateAccountAsync("edit-race", actor);

        Guid transactionId;
        uint rowVersion;
        await using (SirkadiyenDbContext seedContext = fixture.CreateProductionLikeContext())
        {
            FinanceTransactionMutationResult created = await new FinanceLedgerStore(seedContext).RecordIncomeAsync(
                accountId, 100m, FinanceCategory.Donation, Today, "Original", null, null,
                actor.UserId, actor.Email, null, Now, Token);
            transactionId = created.TransactionId!.Value;
            rowVersion = (await seedContext.FinanceTransactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, Token)).RowVersion;
        }

        await using SirkadiyenDbContext contextOne = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext contextTwo = fixture.CreateProductionLikeContext();

        var editOne = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Donation,
            Amount = 150m,
            OccurredOn = Today,
            Description = "First edit",
            AccountId = accountId,
        };
        var editTwo = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Donation,
            Amount = 175m,
            OccurredOn = Today,
            Description = "Second edit",
            AccountId = accountId,
        };

        FinanceTransactionMutationResult[] results = await Task.WhenAll(
            new FinanceLedgerStore(contextOne).UpdateTransactionAsync(
                transactionId, editOne, rowVersion, "First.", actor.UserId, actor.Email, null,
                Now.AddMinutes(1), Token),
            new FinanceLedgerStore(contextTwo).UpdateTransactionAsync(
                transactionId, editTwo, rowVersion, "Second.", actor.UserId, actor.Email, null,
                Now.AddMinutes(1), Token));

        Assert.Single(results, result => result.Outcome == FinanceTransactionOutcome.Updated);
        Assert.Single(results, result => result.Outcome == FinanceTransactionOutcome.ConcurrentUpdate);

        await using SirkadiyenDbContext verifyContext = fixture.CreateContext();
        FinanceTransaction final = await verifyContext.FinanceTransactions
            .SingleAsync(t => t.Id == transactionId, Token);
        Assert.Equal(2, final.RevisionNumber);
        Assert.True(final.Amount is 150m or 175m);
    }

    [Fact]
    public async Task AnEditAndADeleteOfOneTransactionDoNotBothSucceed()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("edit-delete-race");
        Guid accountId = await CreateAccountAsync("edit-delete-race", actor);

        Guid transactionId;
        uint rowVersion;
        await using (SirkadiyenDbContext seedContext = fixture.CreateProductionLikeContext())
        {
            FinanceTransactionMutationResult created = await new FinanceLedgerStore(seedContext).RecordIncomeAsync(
                accountId, 100m, FinanceCategory.Donation, Today, "Original", null, null,
                actor.UserId, actor.Email, null, Now, Token);
            transactionId = created.TransactionId!.Value;
            rowVersion = (await seedContext.FinanceTransactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, Token)).RowVersion;
        }

        await using SirkadiyenDbContext contextOne = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext contextTwo = fixture.CreateProductionLikeContext();

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Donation,
            Amount = 150m,
            OccurredOn = Today,
            Description = "Edit",
            AccountId = accountId,
        };

        FinanceTransactionMutationResult[] results = await Task.WhenAll(
            new FinanceLedgerStore(contextOne).UpdateTransactionAsync(
                transactionId, edit, rowVersion, "Edit attempt.", actor.UserId, actor.Email, null,
                Now.AddMinutes(1), Token),
            new FinanceLedgerStore(contextTwo).DeleteTransactionAsync(
                transactionId, rowVersion, "Delete attempt.", actor.UserId, actor.Email, null,
                Now.AddMinutes(1), Token));

        int successCount = results.Count(result =>
            result.Outcome is FinanceTransactionOutcome.Updated or FinanceTransactionOutcome.Deleted);
        Assert.Equal(1, successCount);
    }

    [Fact]
    public async Task TwoParallelExecutionsWithOneTokenLeaveExactlyOneDistribution()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        DateOnly start = new(2031, 1, 1);
        DateOnly end = new(2031, 1, 31);
        (UserSession actor, Guid sourceAccountId) = await SeedPartnersAndIncomeAsync("one-token", start, 100m);

        FinanceDistributionPlan plan;
        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            plan = await new FinanceDistributionStore(context, new FinanceSummaryReadStore(context))
                .PreviewAsync(start, end, sourceAccountId, Token);
        }

        Assert.Equal(FinanceDistributionPlanOutcome.Ready, plan.Outcome);

        await using SirkadiyenDbContext contextOne = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext contextTwo = fixture.CreateProductionLikeContext();

        FinanceDistributionResult[] results = await Task.WhenAll(
            new FinanceDistributionStore(contextOne, new FinanceSummaryReadStore(contextOne)).ExecuteAsync(
                start, end, sourceAccountId, plan.ConfirmationToken!.Value, plan.PlanHash!,
                plan.ExpectedConfirmationPhrase!, "First.", actor.UserId, actor.Email, null, Now, Token),
            new FinanceDistributionStore(contextTwo, new FinanceSummaryReadStore(contextTwo)).ExecuteAsync(
                start, end, sourceAccountId, plan.ConfirmationToken!.Value, plan.PlanHash!,
                plan.ExpectedConfirmationPhrase!, "Second.", actor.UserId, actor.Email, null, Now, Token));

        Assert.Single(results, result => result.Outcome == FinanceDistributionOutcome.Executed);
        Assert.Single(results, result => result.Outcome == FinanceDistributionOutcome.ReplayedExistingExecution);
        Assert.Equal(results[0].DistributionId, results[1].DistributionId);

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        int distributionCount = await verify.FinanceDistributions
            .CountAsync(d => d.PeriodStartOn == start && d.PeriodEndOn == end, Token);
        Assert.Equal(1, distributionCount);
    }

    [Fact]
    public async Task TwoParallelDistributionsForOnePeriodLeaveExactlyOne()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        DateOnly start = new(2031, 2, 1);
        DateOnly end = new(2031, 2, 28);
        (UserSession actor, Guid sourceAccountId) = await SeedPartnersAndIncomeAsync("one-period", start, 100m);

        FinanceDistributionPlan planOne;
        FinanceDistributionPlan planTwo;
        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            FinanceDistributionStore previewStore = new(context, new FinanceSummaryReadStore(context));
            planOne = await previewStore.PreviewAsync(start, end, sourceAccountId, Token);
            planTwo = await previewStore.PreviewAsync(start, end, sourceAccountId, Token);
        }

        Assert.Equal(FinanceDistributionPlanOutcome.Ready, planOne.Outcome);
        Assert.Equal(FinanceDistributionPlanOutcome.Ready, planTwo.Outcome);
        Assert.NotEqual(planOne.ConfirmationToken, planTwo.ConfirmationToken);

        await using SirkadiyenDbContext contextOne = fixture.CreateProductionLikeContext();
        await using SirkadiyenDbContext contextTwo = fixture.CreateProductionLikeContext();

        FinanceDistributionResult[] results = await Task.WhenAll(
            new FinanceDistributionStore(contextOne, new FinanceSummaryReadStore(contextOne)).ExecuteAsync(
                start, end, sourceAccountId, planOne.ConfirmationToken!.Value, planOne.PlanHash!,
                planOne.ExpectedConfirmationPhrase!, "First.", actor.UserId, actor.Email, null, Now, Token),
            new FinanceDistributionStore(contextTwo, new FinanceSummaryReadStore(contextTwo)).ExecuteAsync(
                start, end, sourceAccountId, planTwo.ConfirmationToken!.Value, planTwo.PlanHash!,
                planTwo.ExpectedConfirmationPhrase!, "Second.", actor.UserId, actor.Email, null, Now, Token));

        Assert.Single(results, result => result.Outcome == FinanceDistributionOutcome.Executed);
        Assert.Single(
            results,
            result => result.Outcome is FinanceDistributionOutcome.AlreadyDistributedForPeriod
                or FinanceDistributionOutcome.ReplayedExistingExecution);

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        int distributionCount = await verify.FinanceDistributions
            .CountAsync(
                d => d.PeriodStartOn == start && d.PeriodEndOn == end
                    && d.Status == FinanceDistributionStatus.Executed,
                Token);
        Assert.Equal(1, distributionCount);
    }

    private async Task<(UserSession Actor, Guid SourceAccountId)> SeedPartnersAndIncomeAsync(
        string prefix,
        DateOnly incomeOn,
        decimal incomeAmount)
    {
        UserSession actor = await CreateUserAsync(prefix);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore ledger = new(context);

        // Partner shares are global (must sum to exactly 10000 basis points at any moment), so any
        // partner left active by another test in this shared database is deactivated first.
        List<Guid> otherActivePartnerIds = await context.FinanceAccountHolders
            .Where(holder => holder.Status == FinanceAccountHolderStatus.Active && holder.ShareBasisPoints > 0)
            .Select(holder => holder.Id)
            .ToListAsync(Token);
        foreach (Guid holderId in otherActivePartnerIds)
        {
            await ledger.DeactivateHolderAsync(holderId, Now, Token);
        }

        string nonce = Guid.NewGuid().ToString("N");
        FinanceAccountHolderMutationResult holder = await ledger.CreateHolderAsync(
            $"Partner-{prefix}-{nonce}", null, 10_000, Now, Token);
        FinanceAccountMutationResult account = await ledger.OpenAccountAsync(
            holder.HolderId!.Value, $"Source-{prefix}", FinanceAccountKind.Cash, incomeOn.AddDays(-5), Now, Token);
        Guid sourceAccountId = account.AccountId!.Value;

        await ledger.RecordOpeningBalanceAsync(
            sourceAccountId, incomeAmount * 2, incomeOn.AddDays(-3), "Opening", actor.UserId, actor.Email, null,
            Now, Token);
        await ledger.RecordIncomeAsync(
            sourceAccountId, incomeAmount, FinanceCategory.Donation, incomeOn, "Donation", null, null,
            actor.UserId, actor.Email, null, Now, Token);

        return (actor, sourceAccountId);
    }

    private async Task<UserSession> CreateUserAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        string nonce = Guid.NewGuid().ToString("N");
        return await new UserStore(context).SignInWithGoogleAsync(
            new GoogleIdentity
            {
                Subject = $"{prefix}-{nonce}",
                Email = $"{prefix}-{nonce}@example.com",
                EmailVerified = true,
            },
            UserRole.SuperAdmin,
            Now,
            Token);
    }

    private async Task<Guid> CreateAccountAsync(string prefix, UserSession actor)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore store = new(context);
        FinanceAccountHolderMutationResult holder = await store.CreateHolderAsync(
            $"Holder-{prefix}-{Guid.NewGuid():N}", null, 0, Now, Token);
        FinanceAccountMutationResult account = await store.OpenAccountAsync(
            holder.HolderId!.Value, $"Account-{prefix}", FinanceAccountKind.Cash, Today, Now, Token);
        return account.AccountId!.Value;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
