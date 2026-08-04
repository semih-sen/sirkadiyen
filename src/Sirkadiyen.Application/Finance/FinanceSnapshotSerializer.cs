using System.Text.Json;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Application.Finance;

/// <summary>A stable, diffable snapshot of one ledger entry, used inside a transaction snapshot.</summary>
public sealed record FinanceLedgerEntrySnapshot
{
    public required Guid FinanceAccountId { get; init; }

    public required FinanceLedgerLeg Leg { get; init; }

    public required decimal Amount { get; init; }
}

/// <summary>
/// A stable, diffable snapshot of a transaction plus its ledger entries — what a
/// <c>finance_audits</c> row's <c>BeforeState</c>/<c>AfterState</c> actually holds, so a deleted
/// transaction is fully reconstructable from its audit row.
/// </summary>
public sealed record FinanceTransactionSnapshot
{
    public required Guid Id { get; init; }

    public required FinanceTransactionKind Kind { get; init; }

    public FinanceCategory? Category { get; init; }

    public required decimal Amount { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string Description { get; init; }

    public string? Reference { get; init; }

    public string? CounterpartyName { get; init; }

    public Guid? FinanceDistributionId { get; init; }

    public required int RevisionNumber { get; init; }

    public required IReadOnlyList<FinanceLedgerEntrySnapshot> Entries { get; init; }
}

/// <summary>
/// Builds the <c>BeforeState</c>/<c>AfterState</c>/<c>ChangedFields</c> payloads that
/// <see cref="Domain.Finance.FinanceAudit"/> rows carry. Pure: no I/O, no clock. Uses
/// <see cref="ContractJson.CreateOptions"/> so the serialized shape is stable and diffable across
/// the codebase.
/// </summary>
public static class FinanceSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = ContractJson.CreateOptions();

    public static FinanceTransactionSnapshot Capture(
        FinanceTransaction transaction,
        IReadOnlyList<FinanceLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entries);

        return new FinanceTransactionSnapshot
        {
            Id = transaction.Id,
            Kind = transaction.Kind,
            Category = transaction.Category,
            Amount = transaction.Amount,
            OccurredOn = transaction.OccurredOn,
            Description = transaction.Description,
            Reference = transaction.Reference,
            CounterpartyName = transaction.CounterpartyName,
            FinanceDistributionId = transaction.FinanceDistributionId,
            RevisionNumber = transaction.RevisionNumber,
            Entries =
            [
                .. entries
                    .OrderBy(entry => entry.Leg)
                    .ThenBy(entry => entry.FinanceAccountId)
                    .Select(entry => new FinanceLedgerEntrySnapshot
                    {
                        FinanceAccountId = entry.FinanceAccountId,
                        Leg = entry.Leg,
                        Amount = entry.Amount,
                    }),
            ],
        };
    }

    public static string Serialize(FinanceTransactionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static FinanceTransactionSnapshot Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<FinanceTransactionSnapshot>(json, Options)
            ?? throw new InvalidOperationException("The finance transaction snapshot could not be deserialized.");
    }

    /// <summary>The names of the top-level fields that differ between two snapshots.</summary>
    public static IReadOnlyList<string> DiffChangedFields(
        FinanceTransactionSnapshot before,
        FinanceTransactionSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        List<string> changed = [];

        if (before.Kind != after.Kind)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.Kind));
        }

        if (before.Category != after.Category)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.Category));
        }

        if (before.Amount != after.Amount)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.Amount));
        }

        if (before.OccurredOn != after.OccurredOn)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.OccurredOn));
        }

        if (before.Description != after.Description)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.Description));
        }

        if (before.Reference != after.Reference)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.Reference));
        }

        if (before.CounterpartyName != after.CounterpartyName)
        {
            changed.Add(nameof(FinanceTransactionSnapshot.CounterpartyName));
        }

        if (!EntriesEqual(before.Entries, after.Entries))
        {
            changed.Add(nameof(FinanceTransactionSnapshot.Entries));
        }

        return changed;
    }

    /// <summary>The net cash effect of moving from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static decimal AmountDelta(FinanceTransactionSnapshot? before, FinanceTransactionSnapshot? after)
    {
        decimal beforeNet = before?.Entries.Sum(entry => entry.Amount) ?? 0m;
        decimal afterNet = after?.Entries.Sum(entry => entry.Amount) ?? 0m;
        return afterNet - beforeNet;
    }

    private static bool EntriesEqual(
        IReadOnlyList<FinanceLedgerEntrySnapshot> left,
        IReadOnlyList<FinanceLedgerEntrySnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index].FinanceAccountId != right[index].FinanceAccountId
                || left[index].Leg != right[index].Leg
                || left[index].Amount != right[index].Amount)
            {
                return false;
            }
        }

        return true;
    }
}
