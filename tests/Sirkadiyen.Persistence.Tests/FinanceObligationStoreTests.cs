using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class FinanceObligationStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task SettlingCreatesTheCashTransactionAndTheSettlementAtomically()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("settle-atomic");
        Guid accountId = await CreateAccountAsync("settle-atomic", actor);

        Guid obligationId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult created = await store.CreateAsync(
                FinanceObligationDirection.Receivable, FinanceCategory.LicenseSales, "A Corp", null,
                500m, Today, null, actor.UserId, actor.Email, Now, Token);
            obligationId = created.ObligationId!.Value;
        }

        FinanceObligationMutationResult settled;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            settled = await store.SettleAsync(
                obligationId, accountId, 200m, Today, null, actor.UserId, actor.Email, null, Now.AddDays(1), Token);
        }

        Assert.Equal(FinanceObligationOutcome.Settled, settled.Outcome);

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        FinanceObligation obligation = await verify.FinanceObligations.SingleAsync(o => o.Id == obligationId, Token);
        Assert.Equal(FinanceObligationStatus.PartiallySettled, obligation.Status);
        Assert.Equal(200m, obligation.SettledAmount);

        bool transactionExists = await verify.FinanceTransactions
            .AnyAsync(t => t.Id == settled.TransactionId, Token);
        Assert.True(transactionExists);

        FinanceSettlement settlement = await verify.FinanceSettlements
            .SingleAsync(s => s.FinanceObligationId == obligationId, Token);
        Assert.Equal(settled.TransactionId, settlement.FinanceTransactionId);
        Assert.Equal(200m, settlement.Amount);
    }

    [Fact]
    public async Task DetailIncludesSettlementIdentityAndTransactionReference()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("detail-settlements");
        Guid accountId = await CreateAccountAsync("detail-settlements", actor);

        Guid obligationId;
        FinanceObligationMutationResult settled;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult created = await store.CreateAsync(
                FinanceObligationDirection.Receivable, FinanceCategory.Sponsorship, "Sponsor", null,
                750m, Today, null, actor.UserId, actor.Email, Now, Token);
            obligationId = created.ObligationId!.Value;
            settled = await store.SettleAsync(
                obligationId, accountId, 250m, Today, "INV-2026-41", actor.UserId, actor.Email,
                null, Now.AddHours(1), Token);
        }

        await using SirkadiyenDbContext readContext = fixture.CreateProductionLikeContext();
        FinanceObligationListItem detail = Assert.IsType<FinanceObligationListItem>(
            await new FinanceObligationStore(readContext).FindAsync(obligationId, Token));
        FinanceObligationSettlementListItem item = Assert.Single(detail.Settlements);

        Assert.Equal(settled.SettlementId, item.SettlementId);
        Assert.Equal(settled.TransactionId, item.TransactionId);
        Assert.Equal(250m, item.Amount);
        Assert.Equal("INV-2026-41", item.Reference);
    }

    [Fact]
    public async Task OverSettlementRollsBackEverything()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("over-settle");
        Guid accountId = await CreateAccountAsync("over-settle", actor);

        Guid obligationId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult created = await store.CreateAsync(
                FinanceObligationDirection.Receivable, FinanceCategory.LicenseSales, "A Corp", null,
                100m, Today, null, actor.UserId, actor.Email, Now, Token);
            obligationId = created.ObligationId!.Value;
        }

        int transactionCountBefore;
        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            transactionCountBefore = await context.FinanceTransactions.CountAsync(Token);
        }

        FinanceObligationMutationResult result;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            result = await store.SettleAsync(
                obligationId, accountId, 150m, Today, null, actor.UserId, actor.Email, null, Now.AddDays(1), Token);
        }

        Assert.Equal(FinanceObligationOutcome.OverSettlement, result.Outcome);

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        int transactionCountAfter = await verify.FinanceTransactions.CountAsync(Token);
        Assert.Equal(transactionCountBefore, transactionCountAfter);
        Assert.False(await verify.FinanceSettlements.AnyAsync(s => s.FinanceObligationId == obligationId, Token));

        FinanceObligation obligation = await verify.FinanceObligations.SingleAsync(o => o.Id == obligationId, Token);
        Assert.Equal(0m, obligation.SettledAmount);
    }

    [Fact]
    public async Task SettlingOntoAClosedAccountIsRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("closed-account");
        Guid accountId = await CreateAccountAsync("closed-account", actor);

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            await new FinanceLedgerStore(context).CloseAccountAsync(
                accountId, "No longer used.", Now, Token);
        }

        Guid obligationId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult created = await store.CreateAsync(
                FinanceObligationDirection.Payable, FinanceCategory.Servers, "Host Inc", null,
                80m, Today, null, actor.UserId, actor.Email, Now, Token);
            obligationId = created.ObligationId!.Value;
        }

        await using SirkadiyenDbContext settleContext = fixture.CreateProductionLikeContext();
        FinanceObligationMutationResult result = await new FinanceObligationStore(settleContext).SettleAsync(
            obligationId, accountId, 80m, Today, null, actor.UserId, actor.Email, null, Now.AddDays(1), Token);

        Assert.Equal(FinanceObligationOutcome.AccountClosed, result.Outcome);
    }

    [Fact]
    public async Task SettledAmountRecomputedFromSettlementsMatchesTheObligationsOwnField()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("recompute");
        Guid accountId = await CreateAccountAsync("recompute", actor);

        Guid obligationId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult created = await store.CreateAsync(
                FinanceObligationDirection.Receivable, FinanceCategory.Donation, "Ada", null,
                300m, Today, null, actor.UserId, actor.Email, Now, Token);
            obligationId = created.ObligationId!.Value;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            await store.SettleAsync(
                obligationId, accountId, 100m, Today, null, actor.UserId, actor.Email, null, Now.AddDays(1), Token);
            await store.SettleAsync(
                obligationId, accountId, 200m, Today, null, actor.UserId, actor.Email, null, Now.AddDays(2), Token);
        }

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        FinanceObligation obligation = await verify.FinanceObligations.SingleAsync(o => o.Id == obligationId, Token);
        decimal? recomputed = await verify.FinanceSettlements
            .Where(s => s.FinanceObligationId == obligationId)
            .SumAsync(s => (decimal?)s.Amount, Token);

        Assert.Equal(obligation.SettledAmount, recomputed ?? 0m);
        Assert.Equal(FinanceObligationStatus.Settled, obligation.Status);
    }

    [Fact]
    public async Task CancellingASettlementUnlinksItWithoutTouchingTheCashTransaction()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("cancel-settlement");
        Guid accountId = await CreateAccountAsync("cancel-settlement", actor);

        Guid obligationId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult created = await store.CreateAsync(
                FinanceObligationDirection.Receivable, FinanceCategory.Donation, "Ada", null,
                100m, Today, null, actor.UserId, actor.Email, Now, Token);
            obligationId = created.ObligationId!.Value;
        }

        Guid settlementId;
        Guid transactionId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult settled = await store.SettleAsync(
                obligationId, accountId, 100m, Today, null, actor.UserId, actor.Email, null, Now.AddDays(1), Token);
            settlementId = settled.SettlementId!.Value;
            transactionId = settled.TransactionId!.Value;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceObligationStore store = new(context);
            FinanceObligationMutationResult cancelled = await store.CancelSettlementAsync(
                obligationId, settlementId, "Wrong obligation linked.", actor.UserId, actor.Email, null,
                Now.AddDays(2), Token);
            Assert.Equal(FinanceObligationOutcome.SettlementCancelled, cancelled.Outcome);
        }

        await using SirkadiyenDbContext verify = fixture.CreateContext();
        FinanceObligation obligation = await verify.FinanceObligations.SingleAsync(o => o.Id == obligationId, Token);
        Assert.Equal(FinanceObligationStatus.Open, obligation.Status);
        Assert.Equal(0m, obligation.SettledAmount);
        Assert.False(await verify.FinanceSettlements.AnyAsync(s => s.Id == settlementId, Token));

        // The cash transaction is real money that moved; cancelling the link must not touch it.
        Assert.True(await verify.FinanceTransactions.AnyAsync(t => t.Id == transactionId, Token));

        FinanceObligationListItem detail = Assert.IsType<FinanceObligationListItem>(
            await new FinanceObligationStore(verify).FindAsync(obligationId, Token));
        Assert.Empty(detail.Settlements);
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
