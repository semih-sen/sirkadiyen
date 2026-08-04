using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.Identity;
using Sirkadiyen.Domain.Finance;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

/// <summary>
/// Raw-SQL inserts proving each finance check constraint actually bites, straight past the domain,
/// the way a future producer or a repair script would reach the tables.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FinanceConstraintTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task AZeroAmountLedgerEntryIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (Guid transactionId, Guid accountId) = await SeedIncomeAsync("zero-amount");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_ledger_entries ("Id", "FinanceTransactionId", "FinanceAccountId", "Kind", "Leg", "Amount", "OccurredOn")
                VALUES ({Guid.CreateVersion7()}, {transactionId}, {accountId}, 'Income', 'Single', 0, {Today})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task AnIncomeEntryWithAFromLegIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (Guid transactionId, Guid accountId) = await SeedIncomeAsync("income-from-leg");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_ledger_entries ("Id", "FinanceTransactionId", "FinanceAccountId", "Kind", "Leg", "Amount", "OccurredOn")
                VALUES ({Guid.CreateVersion7()}, {transactionId}, {accountId}, 'Income', 'From', -10, {Today})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task ATransferEntryWithASingleLegIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (Guid transactionId, Guid accountId) = await SeedIncomeAsync("transfer-single-leg");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_ledger_entries ("Id", "FinanceTransactionId", "FinanceAccountId", "Kind", "Leg", "Amount", "OccurredOn")
                VALUES ({Guid.CreateVersion7()}, {transactionId}, {accountId}, 'Transfer', 'Single', 10, {Today})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task AFromLegWithAPositiveAmountIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        (Guid transactionId, Guid accountId) = await SeedIncomeAsync("from-leg-positive");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_ledger_entries ("Id", "FinanceTransactionId", "FinanceAccountId", "Kind", "Leg", "Amount", "OccurredOn")
                VALUES ({Guid.CreateVersion7()}, {transactionId}, {accountId}, 'Transfer', 'From', 10, {Today})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task AnIncomeCategoryOnAnExpenseTransactionIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("category-mismatch");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_transactions
                ("Id", "Kind", "Category", "Amount", "OccurredOn", "Description", "RevisionNumber",
                 "CreatedByUserId", "CreatedByEmail", "CreatedAtUtc", "UpdatedByUserId", "UpdatedByEmail", "UpdatedAtUtc")
                VALUES
                ({Guid.CreateVersion7()}, 'Expense', 'Donation', 10, {Today}, 'Bad category', 1,
                 {actor.UserId}, {actor.Email}, {Now}, {actor.UserId}, {actor.Email}, {Now})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task ACategoryOnATransferTransactionIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("transfer-category");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_transactions
                ("Id", "Kind", "Category", "Amount", "OccurredOn", "Description", "RevisionNumber",
                 "CreatedByUserId", "CreatedByEmail", "CreatedAtUtc", "UpdatedByUserId", "UpdatedByEmail", "UpdatedAtUtc")
                VALUES
                ({Guid.CreateVersion7()}, 'Transfer', 'Donation', 10, {Today}, 'Bad category', 1,
                 {actor.UserId}, {actor.Email}, {Now}, {actor.UserId}, {actor.Email}, {Now})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task ACurrencyOtherThanTryIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        Guid holderId = await SeedHolderAsync("bad-currency");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_accounts
                ("Id", "FinanceAccountHolderId", "Name", "Kind", "CurrencyCode", "Status", "OpenedOn", "CreatedAtUtc")
                VALUES ({Guid.CreateVersion7()}, {holderId}, 'USD box', 'Cash', 'USD', 'Active', {Today}, {Now})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task ARevisionNumberBelowOneIsRejected()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("revision-zero");

        await using SirkadiyenDbContext context = fixture.CreateContext();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sirkadiyen.finance_transactions
                ("Id", "Kind", "Category", "Amount", "OccurredOn", "Description", "RevisionNumber",
                 "CreatedByUserId", "CreatedByEmail", "CreatedAtUtc", "UpdatedByUserId", "UpdatedByEmail", "UpdatedAtUtc")
                VALUES
                ({Guid.CreateVersion7()}, 'Income', 'Donation', 10, {Today}, 'Bad revision', 0,
                 {actor.UserId}, {actor.Email}, {Now}, {actor.UserId}, {actor.Email}, {Now})
                """,
                Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task NumericEighteenCommaTwoSilentlyRoundsAThreeDecimalInsert()
    {
        // Documents the observed behaviour: the database rounds, it does not reject. FinanceAmount
        // is what actually protects a two-decimal ledger — see FinanceAmountTests.
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        UserSession actor = await CreateUserAsync("silent-rounding");
        Guid transactionId = Guid.CreateVersion7();

        await using SirkadiyenDbContext context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sirkadiyen.finance_transactions
            ("Id", "Kind", "Category", "Amount", "OccurredOn", "Description", "RevisionNumber",
             "CreatedByUserId", "CreatedByEmail", "CreatedAtUtc", "UpdatedByUserId", "UpdatedByEmail", "UpdatedAtUtc")
            VALUES
            ({transactionId}, 'Income', 'Donation', 10.005, {Today}, 'Three decimals', 1,
             {actor.UserId}, {actor.Email}, {Now}, {actor.UserId}, {actor.Email}, {Now})
            """,
            Token);

        decimal? stored = await context.FinanceTransactions
            .Where(t => t.Id == transactionId)
            .Select(t => (decimal?)t.Amount)
            .SingleAsync(Token);

        // Postgres numeric column truncation rounds half-away-from-zero, not half-to-even.
        Assert.Equal(10.01m, stored);
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

    private async Task<Guid> SeedHolderAsync(string prefix)
    {
        await using SirkadiyenDbContext context = fixture.CreateContext();
        FinanceAccountHolder holder = FinanceAccountHolder.Create(
            $"Holder-{prefix}-{Guid.NewGuid():N}",
            null,
            0,
            Now);
        context.FinanceAccountHolders.Add(holder);
        await context.SaveChangesAsync(Token);
        return holder.Id;
    }

    private async Task<(Guid TransactionId, Guid AccountId)> SeedIncomeAsync(string prefix)
    {
        UserSession actor = await CreateUserAsync(prefix);
        Guid holderId = await SeedHolderAsync(prefix);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        FinanceAccount account = FinanceAccount.Open(
            holderId,
            $"Account-{prefix}",
            FinanceAccountKind.Cash,
            Today,
            Now);
        context.FinanceAccounts.Add(account);

        FinancePosting posting = FinanceTransaction.RecordIncome(
            account.Id,
            50m,
            FinanceCategory.Donation,
            Today,
            "Seed",
            null,
            null,
            actor.UserId,
            actor.Email,
            Now);
        context.FinanceTransactions.Add(posting.Transaction);
        context.FinanceLedgerEntries.AddRange(posting.Entries);
        await context.SaveChangesAsync(Token);

        return (posting.Transaction.Id, account.Id);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
