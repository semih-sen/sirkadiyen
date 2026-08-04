using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void OpeningBalancePostsOneSingleLegEntryWithTheSignedAmount()
    {
        Guid accountId = Guid.NewGuid();

        FinancePosting posting = FinanceTransaction.RecordOpeningBalance(
            accountId,
            -500m,
            Today,
            "Opening balance",
            Actor,
            "admin@example.com",
            Now);

        FinanceLedgerEntry entry = Assert.Single(posting.Entries);
        Assert.Equal(FinanceLedgerLeg.Single, entry.Leg);
        Assert.Equal(-500m, entry.Amount);
        Assert.Equal(500m, posting.Transaction.Amount);
        Assert.Equal(FinanceTransactionKind.OpeningBalance, posting.Transaction.Kind);
        Assert.Null(posting.Transaction.Category);
    }

    [Fact]
    public void IncomePostsAPositiveSingleLegEntry()
    {
        FinancePosting posting = FinanceTransaction.RecordIncome(
            Guid.NewGuid(),
            250m,
            FinanceCategory.Donation,
            Today,
            "Donation",
            reference: null,
            counterpartyName: null,
            Actor,
            "admin@example.com",
            Now);

        FinanceLedgerEntry entry = Assert.Single(posting.Entries);
        Assert.Equal(FinanceLedgerLeg.Single, entry.Leg);
        Assert.Equal(250m, entry.Amount);
    }

    [Fact]
    public void ExpensePostsANegativeSingleLegEntry()
    {
        FinancePosting posting = FinanceTransaction.RecordExpense(
            Guid.NewGuid(),
            80m,
            FinanceCategory.Servers,
            Today,
            "Server bill",
            reference: null,
            counterpartyName: null,
            Actor,
            "admin@example.com",
            Now);

        FinanceLedgerEntry entry = Assert.Single(posting.Entries);
        Assert.Equal(FinanceLedgerLeg.Single, entry.Leg);
        Assert.Equal(-80m, entry.Amount);
    }

    [Fact]
    public void IncomeRejectsAnExpenseCategory()
    {
        Assert.Throws<ArgumentException>(() => FinanceTransaction.RecordIncome(
            Guid.NewGuid(),
            10m,
            FinanceCategory.Servers,
            Today,
            "wrong",
            null,
            null,
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void ExpenseRejectsAnIncomeCategory()
    {
        Assert.Throws<ArgumentException>(() => FinanceTransaction.RecordExpense(
            Guid.NewGuid(),
            10m,
            FinanceCategory.Donation,
            Today,
            "wrong",
            null,
            null,
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void TransferPostsAFromLegAndAToLegThatSumToZero()
    {
        Guid from = Guid.NewGuid();
        Guid to = Guid.NewGuid();

        FinancePosting posting = FinanceTransaction.RecordTransfer(
            from,
            to,
            100m,
            Today,
            "Move funds",
            null,
            Actor,
            "admin@example.com",
            Now);

        Assert.Equal(2, posting.Entries.Count);
        FinanceLedgerEntry fromEntry = posting.Entries.Single(entry => entry.Leg == FinanceLedgerLeg.From);
        FinanceLedgerEntry toEntry = posting.Entries.Single(entry => entry.Leg == FinanceLedgerLeg.To);
        Assert.Equal(from, fromEntry.FinanceAccountId);
        Assert.Equal(to, toEntry.FinanceAccountId);
        Assert.Equal(-100m, fromEntry.Amount);
        Assert.Equal(100m, toEntry.Amount);
        Assert.Equal(0m, fromEntry.Amount + toEntry.Amount);
    }

    [Fact]
    public void ATransferToTheSameAccountIsRejected()
    {
        Guid accountId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => FinanceTransaction.RecordTransfer(
            accountId,
            accountId,
            100m,
            Today,
            "Self transfer",
            null,
            Actor,
            "admin@example.com",
            Now));
    }

    [Fact]
    public void DistributionPayoutPostsOnlyAnOutflow()
    {
        FinancePosting posting = FinanceTransaction.RecordDistributionPayout(
            Guid.NewGuid(),
            Guid.NewGuid(),
            300m,
            Today,
            "Partner payout",
            "Partner A",
            Actor,
            "admin@example.com",
            Now);

        FinanceLedgerEntry entry = Assert.Single(posting.Entries);
        Assert.Equal(-300m, entry.Amount);
        Assert.Equal(FinanceTransactionKind.Distribution, posting.Transaction.Kind);
        Assert.NotNull(posting.Transaction.FinanceDistributionId);
    }

    [Fact]
    public void RewriteBumpsRevisionAndReplacesEveryEntry()
    {
        Guid accountId = Guid.NewGuid();
        FinancePosting original = FinanceTransaction.RecordIncome(
            accountId,
            100m,
            FinanceCategory.Donation,
            Today,
            "Original",
            null,
            null,
            Actor,
            "admin@example.com",
            Now);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Sponsorship,
            Amount = 150m,
            OccurredOn = Today.AddDays(1),
            Description = "Corrected",
            AccountId = accountId,
        };

        IReadOnlyList<FinanceLedgerEntry> newEntries = original.Transaction.Rewrite(
            edit,
            Actor,
            "admin@example.com",
            Now.AddMinutes(5));

        Assert.Equal(2, original.Transaction.RevisionNumber);
        Assert.Equal(FinanceCategory.Sponsorship, original.Transaction.Category);
        Assert.Equal(150m, original.Transaction.Amount);
        Assert.Equal("Corrected", original.Transaction.Description);
        FinanceLedgerEntry entry = Assert.Single(newEntries);
        Assert.Equal(150m, entry.Amount);
    }

    [Fact]
    public void RewriteMayChangeKindAmongIncomeExpenseAndTransfer()
    {
        Guid accountId = Guid.NewGuid();
        Guid otherAccountId = Guid.NewGuid();
        FinancePosting original = FinanceTransaction.RecordIncome(
            accountId,
            100m,
            FinanceCategory.Donation,
            Today,
            "Original",
            null,
            null,
            Actor,
            "admin@example.com",
            Now);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Transfer,
            Amount = 100m,
            OccurredOn = Today,
            Description = "Actually a transfer",
            AccountId = accountId,
            ToAccountId = otherAccountId,
        };

        IReadOnlyList<FinanceLedgerEntry> newEntries = original.Transaction.Rewrite(
            edit,
            Actor,
            "admin@example.com",
            Now.AddMinutes(5));

        Assert.Equal(FinanceTransactionKind.Transfer, original.Transaction.Kind);
        Assert.Null(original.Transaction.Category);
        Assert.Equal(2, newEntries.Count);
    }

    [Fact]
    public void RewritingToOpeningBalanceIsRejected()
    {
        FinancePosting original = FinanceTransaction.RecordIncome(
            Guid.NewGuid(),
            100m,
            FinanceCategory.Donation,
            Today,
            "Original",
            null,
            null,
            Actor,
            "admin@example.com",
            Now);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.OpeningBalance,
            Amount = 100m,
            OccurredOn = Today,
            Description = "nope",
            AccountId = Guid.NewGuid(),
        };

        Assert.Throws<InvalidOperationException>(
            () => original.Transaction.Rewrite(edit, Actor, "admin@example.com", Now));
    }

    [Fact]
    public void RewritingAnOpeningBalanceTransactionIsRejected()
    {
        FinancePosting original = FinanceTransaction.RecordOpeningBalance(
            Guid.NewGuid(),
            100m,
            Today,
            "Opening",
            Actor,
            "admin@example.com",
            Now);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Donation,
            Amount = 100m,
            OccurredOn = Today,
            Description = "nope",
            AccountId = Guid.NewGuid(),
        };

        Assert.Throws<InvalidOperationException>(
            () => original.Transaction.Rewrite(edit, Actor, "admin@example.com", Now));
    }

    [Fact]
    public void RewritingADistributionPayoutIsRejected()
    {
        FinancePosting original = FinanceTransaction.RecordDistributionPayout(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            Today,
            "Payout",
            "Partner A",
            Actor,
            "admin@example.com",
            Now);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Expense,
            Category = FinanceCategory.Operational,
            Amount = 100m,
            OccurredOn = Today,
            Description = "nope",
            AccountId = Guid.NewGuid(),
        };

        Assert.Throws<InvalidOperationException>(
            () => original.Transaction.Rewrite(edit, Actor, "admin@example.com", Now));
    }
}
