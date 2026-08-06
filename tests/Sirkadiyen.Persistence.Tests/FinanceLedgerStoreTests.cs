using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Persistence.Finance.Stores;
using Sirkadiyen.Infrastructure.Persistence.Identity.Stores;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class FinanceLedgerStoreTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task IncomeExpenseAndTransferMoveTheDerivedBalance()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("balance-flow");
        (Guid accountA, Guid accountB) = await CreateTwoAccountsAsync("balance-flow", actor);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore store = new(context);

        await store.RecordIncomeAsync(
            accountA, 500m, FinanceCategory.Donation, Today, "Donation", null, null,
            actor.UserId, actor.Email, null, Now, Token);
        await store.RecordExpenseAsync(
            accountA, 100m, FinanceCategory.Servers, Today, "Server bill", null, null,
            actor.UserId, actor.Email, null, Now, Token);
        FinanceTransactionMutationResult transfer = await store.RecordTransferAsync(
            accountA, accountB, 200m, Today, "Move funds", null,
            actor.UserId, actor.Email, null, Now, Token);

        Assert.Equal(FinanceTransactionOutcome.Recorded, transfer.Outcome);

        decimal balanceA = await GetBalanceAsync(context, accountA, Today);
        decimal balanceB = await GetBalanceAsync(context, accountB, Today);
        Assert.Equal(200m, balanceA);
        Assert.Equal(200m, balanceB);
    }

    [Fact]
    public async Task ATransferBiggerThanTheSourceBalanceIsRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("insufficient");
        (Guid accountA, Guid accountB) = await CreateTwoAccountsAsync("insufficient", actor);

        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore store = new(context);
        await store.RecordIncomeAsync(
            accountA, 50m, FinanceCategory.Donation, Today, "Donation", null, null,
            actor.UserId, actor.Email, null, Now, Token);

        FinanceTransactionMutationResult transfer = await store.RecordTransferAsync(
            accountA, accountB, 100m, Today, "Too much", null,
            actor.UserId, actor.Email, null, Now, Token);

        Assert.Equal(FinanceTransactionOutcome.InsufficientBalance, transfer.Outcome);
        Assert.Equal(50m, await GetBalanceAsync(context, accountA, Today));
    }

    [Fact]
    public async Task EditReplacesEveryEntryAndTheBalanceFollows()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("edit-flow");
        Guid accountId = await CreateAccountAsync("edit-flow", actor);

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
                Category = FinanceCategory.Sponsorship,
                Amount = 175m,
                OccurredOn = Today,
                Description = "Corrected",
                AccountId = accountId,
            };

            FinanceTransactionMutationResult updated = await store.UpdateTransactionAsync(
                transactionId, edit, rowVersion, "Wrong amount entered.",
                actor.UserId, actor.Email, null, Now.AddMinutes(5), Token);

            Assert.Equal(FinanceTransactionOutcome.Updated, updated.Outcome);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            List<FinanceLedgerEntry> entries = await context.FinanceLedgerEntries
                .Where(e => e.FinanceTransactionId == transactionId)
                .ToListAsync(Token);
            FinanceTransaction transaction = await context.FinanceTransactions
                .SingleAsync(t => t.Id == transactionId, Token);

            Assert.Equal(175m, Assert.Single(entries).Amount);
            Assert.Equal(FinanceCategory.Sponsorship, transaction.Category);
            Assert.Equal(2, transaction.RevisionNumber);
            Assert.Equal(175m, await GetBalanceAsync(context, accountId, Today));
        }
    }

    [Fact]
    public async Task AStaleRowVersionIsRefusedAndChangesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("stale-edit");
        Guid accountId = await CreateAccountAsync("stale-edit", actor);

        Guid transactionId;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            FinanceTransactionMutationResult created = await store.RecordIncomeAsync(
                accountId, 100m, FinanceCategory.Donation, Today, "Original", null, null,
                actor.UserId, actor.Email, null, Now, Token);
            transactionId = created.TransactionId!.Value;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            var edit = new FinanceTransactionEdit
            {
                Kind = FinanceTransactionKind.Income,
                Category = FinanceCategory.Donation,
                Amount = 999m,
                OccurredOn = Today,
                Description = "Should not apply",
                AccountId = accountId,
            };

            FinanceTransactionMutationResult updated = await store.UpdateTransactionAsync(
                transactionId, edit, expectedRowVersion: 0, "Stale.",
                actor.UserId, actor.Email, null, Now.AddMinutes(5), Token);

            Assert.Equal(FinanceTransactionOutcome.ConcurrentUpdate, updated.Outcome);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            FinanceTransaction transaction = await context.FinanceTransactions
                .SingleAsync(t => t.Id == transactionId, Token);
            Assert.Equal(100m, transaction.Amount);
            Assert.Equal(1, transaction.RevisionNumber);
        }
    }

    [Fact]
    public async Task DeleteRemovesEntriesAndTransactionAndTheBalanceFollows()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("delete-flow");
        Guid accountId = await CreateAccountAsync("delete-flow", actor);

        Guid transactionId;
        uint rowVersion;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            FinanceTransactionMutationResult created = await store.RecordExpenseAsync(
                accountId, 60m, FinanceCategory.Servers, Today, "To delete", null, null,
                actor.UserId, actor.Email, null, Now, Token);
            transactionId = created.TransactionId!.Value;
            rowVersion = (await context.FinanceTransactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, Token)).RowVersion;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            FinanceTransactionMutationResult deleted = await store.DeleteTransactionAsync(
                transactionId, rowVersion, "Entered by mistake.",
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token);

            Assert.Equal(FinanceTransactionOutcome.Deleted, deleted.Outcome);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            Assert.False(await context.FinanceTransactions.AnyAsync(t => t.Id == transactionId, Token));
            Assert.False(
                await context.FinanceLedgerEntries.AnyAsync(e => e.FinanceTransactionId == transactionId, Token));
            Assert.Equal(0m, await GetBalanceAsync(context, accountId, Today));
        }
    }

    [Fact]
    public async Task TheDeletionAuditRowReconstructsTheDeletedTransaction()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("delete-audit");
        Guid accountId = await CreateAccountAsync("delete-audit", actor);

        Guid transactionId;
        uint rowVersion;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            FinanceTransactionMutationResult created = await store.RecordIncomeAsync(
                accountId, 321m, FinanceCategory.Donation, Today, "To reconstruct", "REF-9", "Ada",
                actor.UserId, actor.Email, "corr-1", Now, Token);
            transactionId = created.TransactionId!.Value;
            rowVersion = (await context.FinanceTransactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, Token)).RowVersion;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            await store.DeleteTransactionAsync(
                transactionId, rowVersion, "Duplicate entry.",
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token);
        }

        await using (SirkadiyenDbContext context = fixture.CreateContext())
        {
            List<FinanceAudit> audits = await context.FinanceAudits
                .Where(a => a.SubjectId == transactionId)
                .OrderBy(a => a.Sequence)
                .ToListAsync(Token);

            Assert.Equal(2, audits.Count);
            FinanceAudit deletionAudit = audits[1];
            Assert.Equal(FinanceAuditAction.TransactionDeleted, deletionAudit.Action);
            Assert.Equal("Duplicate entry.", deletionAudit.Reason);
            Assert.NotNull(deletionAudit.BeforeState);
            Assert.Null(deletionAudit.AfterState);

            FinanceTransactionSnapshot reconstructed = FinanceSnapshotSerializer.Deserialize(deletionAudit.BeforeState!);
            Assert.Equal(321m, reconstructed.Amount);
            Assert.Equal("To reconstruct", reconstructed.Description);
            Assert.Equal(321m, Assert.Single(reconstructed.Entries).Amount);
        }
    }

    [Fact]
    public async Task EditingADistributionPayoutIsRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("distribution-guard");
        Guid accountId = await CreateAccountAsync("distribution-guard", actor);

        Guid transactionId;
        uint rowVersion;
        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            // The FK from finance_transactions.FinanceDistributionId to finance_distributions is
            // real (added once Phase 6 introduced that table), so the payout needs a genuine parent
            // row rather than a dangling Guid.
            FinanceDistribution distribution = FinanceDistribution.Execute(
                Today, Today, accountId, 100m, Guid.CreateVersion7(), new string('a', 64),
                "Test distribution.", actor.UserId, actor.Email, Now);
            context.FinanceDistributions.Add(distribution);

            FinancePosting posting = FinanceTransaction.RecordDistributionPayout(
                accountId, distribution.Id, 100m, Today, "Partner payout", "Partner A",
                actor.UserId, actor.Email, Now);
            context.FinanceTransactions.Add(posting.Transaction);
            context.FinanceLedgerEntries.AddRange(posting.Entries);
            await context.SaveChangesAsync(Token);
            transactionId = posting.Transaction.Id;
            rowVersion = posting.Transaction.RowVersion;
        }

        await using (SirkadiyenDbContext context = fixture.CreateProductionLikeContext())
        {
            FinanceLedgerStore store = new(context);
            var edit = new FinanceTransactionEdit
            {
                Kind = FinanceTransactionKind.Expense,
                Category = FinanceCategory.Operational,
                Amount = 100m,
                OccurredOn = Today,
                Description = "Should not work",
                AccountId = accountId,
            };

            FinanceTransactionMutationResult updated = await store.UpdateTransactionAsync(
                transactionId, edit, rowVersion, "Attempt.",
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token);
            FinanceTransactionMutationResult deleted = await store.DeleteTransactionAsync(
                transactionId, rowVersion, "Attempt.",
                actor.UserId, actor.Email, null, Now.AddMinutes(1), Token);

            Assert.Equal(FinanceTransactionOutcome.TransactionIsADistributionPayout, updated.Outcome);
            Assert.Equal(FinanceTransactionOutcome.TransactionIsADistributionPayout, deleted.Outcome);
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

    private async Task<(Guid AccountA, Guid AccountB)> CreateTwoAccountsAsync(string prefix, UserSession actor)
    {
        await using SirkadiyenDbContext context = fixture.CreateProductionLikeContext();
        FinanceLedgerStore store = new(context);
        FinanceAccountHolderMutationResult holder = await store.CreateHolderAsync(
            $"Holder-{prefix}-{Guid.NewGuid():N}", null, 0, Now, Token);
        FinanceAccountMutationResult accountA = await store.OpenAccountAsync(
            holder.HolderId!.Value, $"Account-A-{prefix}", FinanceAccountKind.Cash, Today, Now, Token);
        FinanceAccountMutationResult accountB = await store.OpenAccountAsync(
            holder.HolderId!.Value, $"Account-B-{prefix}", FinanceAccountKind.Bank, Today, Now, Token);
        return (accountA.AccountId!.Value, accountB.AccountId!.Value);
    }

    private static async Task<decimal> GetBalanceAsync(SirkadiyenDbContext context, Guid accountId, DateOnly asOfOn)
    {
        decimal? sum = await context.FinanceLedgerEntries
            .Where(entry => entry.FinanceAccountId == accountId && entry.OccurredOn <= asOfOn)
            .SumAsync(entry => (decimal?)entry.Amount, Token);
        return sum ?? 0m;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
