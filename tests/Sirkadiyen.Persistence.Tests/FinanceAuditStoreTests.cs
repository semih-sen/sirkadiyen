using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class FinanceAuditStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public void TheLogIsAppendOnly()
    {
        // No update or delete method exists on IFinanceAuditStore at all — the append-only
        // guarantee is that the interface offers no way to violate it, not a runtime check.
        Type storeType = typeof(IFinanceAuditStore);
        Assert.DoesNotContain(
            storeType.GetMethods(),
            method => method.Name.Contains("Update", StringComparison.Ordinal)
                || method.Name.Contains("Delete", StringComparison.Ordinal)
                || method.Name.Contains("Remove", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SequenceIsStrictlyIncreasing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("sequence");
        Guid accountId = await CreateAccountAsync("sequence", actor);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore ledgerStore = new(context);
        await ledgerStore.RecordIncomeAsync(
            accountId, 10m, FinanceCategory.Donation, Today, "One", null, null,
            actor.UserId, actor.Email, null, Now, Token);
        await ledgerStore.RecordIncomeAsync(
            accountId, 20m, FinanceCategory.Donation, Today, "Two", null, null,
            actor.UserId, actor.Email, null, Now.AddSeconds(1), Token);
        await ledgerStore.RecordIncomeAsync(
            accountId, 30m, FinanceCategory.Donation, Today, "Three", null, null,
            actor.UserId, actor.Email, null, Now.AddSeconds(2), Token);

        FinanceAuditStore auditStore = new(context);
        PagedResult<FinanceAuditListItem> page = await auditStore.ListAsync(
            new FinanceAuditQuery { PageSize = 10 },
            Token);

        // ListAsync orders newest-first, so consecutive items must strictly decrease.
        List<long> sequences = [.. page.Items.Select(item => item.Sequence)];
        Assert.True(sequences.Count >= 3);
        for (int index = 1; index < sequences.Count; index++)
        {
            Assert.True(sequences[index - 1] > sequences[index]);
        }
    }

    [Fact]
    public async Task HistoryReturnsCreateThenUpdateThenDeleteInOrder()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("history");
        Guid accountId = await CreateAccountAsync("history", actor);

        Guid transactionId;
        uint rowVersion;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            FinanceTransactionMutationResult created = await store.RecordIncomeAsync(
                accountId, 100m, FinanceCategory.Donation, Today, "Original", null, null,
                actor.UserId, actor.Email, null, Now, Token);
            transactionId = created.TransactionId!.Value;
            rowVersion = (await context.FinanceTransactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, Token)).RowVersion;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            var edit = new FinanceTransactionEdit
            {
                Kind = FinanceTransactionKind.Income,
                Category = FinanceCategory.Donation,
                Amount = 150m,
                OccurredOn = Today,
                Description = "Corrected",
                AccountId = accountId,
            };
            await store.UpdateTransactionAsync(
                transactionId, edit, rowVersion, "Fixed amount.",
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token);
            rowVersion = (await context.FinanceTransactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, Token)).RowVersion;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            await store.DeleteTransactionAsync(
                transactionId, rowVersion, "No longer needed.",
                actor.UserId, actor.Email, null, Now.AddMinutes(2), Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            FinanceAuditStore auditStore = new(context);
            IReadOnlyList<FinanceAuditDetail> history = await auditStore.GetHistoryAsync(
                "FinanceTransaction",
                transactionId,
                Token);

            Assert.Equal(3, history.Count);
            Assert.Equal(FinanceAuditAction.TransactionCreated, history[0].Summary.Action);
            Assert.Equal(FinanceAuditAction.TransactionUpdated, history[1].Summary.Action);
            Assert.Equal(FinanceAuditAction.TransactionDeleted, history[2].Summary.Action);
            Assert.Contains("Amount", history[1].Summary.ChangedFields);
            Assert.Equal(50m, history[1].Summary.AmountDelta);
        }
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
