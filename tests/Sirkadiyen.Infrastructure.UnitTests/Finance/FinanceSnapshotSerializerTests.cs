using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Finance;

public sealed class FinanceSnapshotSerializerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void ASerializedSnapshotRoundTripsToTheSameValues()
    {
        FinancePosting posting = FinanceTransaction.RecordIncome(
            Guid.NewGuid(),
            100m,
            FinanceCategory.Donation,
            Today,
            "Donation",
            "REF-1",
            "Jane Doe",
            Actor,
            "admin@example.com",
            Now);

        FinanceTransactionSnapshot before = FinanceSnapshotSerializer.Capture(
            posting.Transaction,
            posting.Entries);
        string json = FinanceSnapshotSerializer.Serialize(before);
        FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Deserialize(json);

        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Kind, after.Kind);
        Assert.Equal(before.Category, after.Category);
        Assert.Equal(before.Amount, after.Amount);
        Assert.Equal(before.OccurredOn, after.OccurredOn);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.Reference, after.Reference);
        Assert.Equal(before.CounterpartyName, after.CounterpartyName);
        Assert.Equal(before.RevisionNumber, after.RevisionNumber);
        Assert.Equal(before.Entries.Count, after.Entries.Count);
        Assert.Equal(before.Entries[0].Amount, after.Entries[0].Amount);
        Assert.Equal(before.Entries[0].Leg, after.Entries[0].Leg);
        Assert.Equal(before.Entries[0].FinanceAccountId, after.Entries[0].FinanceAccountId);
    }

    [Fact]
    public void ChangedFieldsIsEmptyForANoOpEdit()
    {
        Guid accountId = Guid.NewGuid();
        FinancePosting posting = FinanceTransaction.RecordIncome(
            accountId,
            100m,
            FinanceCategory.Donation,
            Today,
            "Donation",
            null,
            null,
            Actor,
            "admin@example.com",
            Now);
        FinanceTransactionSnapshot before = FinanceSnapshotSerializer.Capture(
            posting.Transaction,
            posting.Entries);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Donation,
            Amount = 100m,
            OccurredOn = Today,
            Description = "Donation",
            AccountId = accountId,
        };
        IReadOnlyList<FinanceLedgerEntry> newEntries = posting.Transaction.Rewrite(
            edit,
            Actor,
            "admin@example.com",
            Now.AddMinutes(1));
        FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Capture(posting.Transaction, newEntries);

        IReadOnlyList<string> changedFields = FinanceSnapshotSerializer.DiffChangedFields(before, after);

        Assert.Empty(changedFields);
    }

    [Fact]
    public void ChangedFieldsNamesEveryFieldThatMoved()
    {
        Guid accountId = Guid.NewGuid();
        FinancePosting posting = FinanceTransaction.RecordIncome(
            accountId,
            100m,
            FinanceCategory.Donation,
            Today,
            "Donation",
            null,
            null,
            Actor,
            "admin@example.com",
            Now);
        FinanceTransactionSnapshot before = FinanceSnapshotSerializer.Capture(
            posting.Transaction,
            posting.Entries);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Sponsorship,
            Amount = 150m,
            OccurredOn = Today,
            Description = "Donation",
            AccountId = accountId,
        };
        IReadOnlyList<FinanceLedgerEntry> newEntries = posting.Transaction.Rewrite(
            edit,
            Actor,
            "admin@example.com",
            Now.AddMinutes(1));
        FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Capture(posting.Transaction, newEntries);

        IReadOnlyList<string> changedFields = FinanceSnapshotSerializer.DiffChangedFields(before, after);

        Assert.Contains(nameof(FinanceTransactionSnapshot.Category), changedFields);
        Assert.Contains(nameof(FinanceTransactionSnapshot.Amount), changedFields);
        Assert.Contains(nameof(FinanceTransactionSnapshot.Entries), changedFields);
        Assert.DoesNotContain(nameof(FinanceTransactionSnapshot.Description), changedFields);
    }

    [Fact]
    public void AmountDeltaIsTheDifferenceInNetLedgerEffect()
    {
        Guid accountId = Guid.NewGuid();
        FinancePosting posting = FinanceTransaction.RecordIncome(
            accountId,
            100m,
            FinanceCategory.Donation,
            Today,
            "Donation",
            null,
            null,
            Actor,
            "admin@example.com",
            Now);
        FinanceTransactionSnapshot before = FinanceSnapshotSerializer.Capture(
            posting.Transaction,
            posting.Entries);

        var edit = new FinanceTransactionEdit
        {
            Kind = FinanceTransactionKind.Income,
            Category = FinanceCategory.Donation,
            Amount = 150m,
            OccurredOn = Today,
            Description = "Donation",
            AccountId = accountId,
        };
        IReadOnlyList<FinanceLedgerEntry> newEntries = posting.Transaction.Rewrite(
            edit,
            Actor,
            "admin@example.com",
            Now.AddMinutes(1));
        FinanceTransactionSnapshot after = FinanceSnapshotSerializer.Capture(posting.Transaction, newEntries);

        Assert.Equal(50m, FinanceSnapshotSerializer.AmountDelta(before, after));
        Assert.Equal(100m, FinanceSnapshotSerializer.AmountDelta(null, before));
        Assert.Equal(-100m, FinanceSnapshotSerializer.AmountDelta(before, null));
    }
}
