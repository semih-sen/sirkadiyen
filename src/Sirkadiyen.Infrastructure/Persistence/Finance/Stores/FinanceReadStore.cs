using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Infrastructure.Persistence.Finance.Stores;

/// <summary>Read-only listings over finance accounts and transactions, deriving balances on read.</summary>
public sealed class FinanceReadStore(SirkadiyenDbContext dbContext) : IFinanceReadStore
{
    // The ordinary list endpoint validates its own page size against a stricter 200-row cap before
    // ever reaching this store; this higher ceiling only matters for the CSV export path, which
    // calls this store directly with a larger single page rather than paging through it.
    private const int MaximumPageSize = 5000;

    public async Task<IReadOnlyList<FinanceAccountHolderListItem>> ListHoldersAsync(
        CancellationToken cancellationToken) =>
        await dbContext.FinanceAccountHolders
            .AsNoTracking()
            .OrderBy(holder => holder.DisplayName)
            .Select(holder => new FinanceAccountHolderListItem
            {
                HolderId = holder.Id,
                DisplayName = holder.DisplayName,
                UserId = holder.UserId,
                ShareBasisPoints = holder.ShareBasisPoints,
                Status = holder.Status,
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinanceAccountListItem>> ListAccountsAsync(
        DateOnly asOfOn,
        CancellationToken cancellationToken)
    {
        List<FinanceAccount> accounts = await dbContext.FinanceAccounts
            .AsNoTracking()
            .OrderBy(account => account.Name)
            .ToListAsync(cancellationToken);
        if (accounts.Count == 0)
        {
            return [];
        }

        Guid[] accountIds = [.. accounts.Select(account => account.Id)];
        Dictionary<Guid, decimal> balances = await GetBalancesAsync(accountIds, asOfOn, cancellationToken);

        Guid[] holderIds = [.. accounts.Select(account => account.FinanceAccountHolderId).Distinct()];
        Dictionary<Guid, string> holderNames = await dbContext.FinanceAccountHolders
            .AsNoTracking()
            .Where(holder => holderIds.Contains(holder.Id))
            .ToDictionaryAsync(holder => holder.Id, holder => holder.DisplayName, cancellationToken);

        return
        [
            .. accounts.Select(account => Project(
                account,
                holderNames.GetValueOrDefault(account.FinanceAccountHolderId, string.Empty),
                balances.GetValueOrDefault(account.Id),
                asOfOn)),
        ];
    }

    public async Task<FinanceAccountListItem?> FindAccountAsync(
        Guid accountId,
        DateOnly asOfOn,
        CancellationToken cancellationToken)
    {
        FinanceAccount? account = await dbContext.FinanceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        string holderName = await dbContext.FinanceAccountHolders
            .AsNoTracking()
            .Where(holder => holder.Id == account.FinanceAccountHolderId)
            .Select(holder => holder.DisplayName)
            .SingleAsync(cancellationToken);

        decimal? sum = await dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.FinanceAccountId == accountId && entry.OccurredOn <= asOfOn)
            .SumAsync(entry => (decimal?)entry.Amount, cancellationToken);

        return Project(account, holderName, sum ?? 0m, asOfOn);
    }

    public async Task<PagedResult<FinanceTransactionListItem>> ListTransactionsAsync(
        FinanceTransactionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);

        IQueryable<FinanceTransaction> transactions = dbContext.FinanceTransactions.AsNoTracking();

        if (query.FromOn is { } fromOn)
        {
            transactions = transactions.Where(transaction => transaction.OccurredOn >= fromOn);
        }

        if (query.ToOn is { } toOn)
        {
            transactions = transactions.Where(transaction => transaction.OccurredOn <= toOn);
        }

        if (query.Kind is { } kind)
        {
            transactions = transactions.Where(transaction => transaction.Kind == kind);
        }

        if (query.Category is { } category)
        {
            transactions = transactions.Where(transaction => transaction.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            transactions = transactions.Where(transaction =>
                EF.Functions.ILike(transaction.Description, pattern)
                || (transaction.Reference != null && EF.Functions.ILike(transaction.Reference, pattern))
                || (transaction.CounterpartyName != null
                    && EF.Functions.ILike(transaction.CounterpartyName, pattern)));
        }

        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(transaction => dbContext.FinanceLedgerEntries
                .Any(entry => entry.FinanceTransactionId == transaction.Id
                    && entry.FinanceAccountId == accountId));
        }

        if (query.HolderId is { } holderId)
        {
            transactions = transactions.Where(transaction => dbContext.FinanceLedgerEntries
                .Any(entry => entry.FinanceTransactionId == transaction.Id
                    && dbContext.FinanceAccounts.Any(account =>
                        account.Id == entry.FinanceAccountId && account.FinanceAccountHolderId == holderId)));
        }

        int totalCount = await transactions.CountAsync(cancellationToken);

        List<FinanceTransaction> pageItems = await transactions
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        Guid[] transactionIds = [.. pageItems.Select(transaction => transaction.Id)];
        List<FinanceLedgerEntry> entries = transactionIds.Length == 0
            ? []
            : await dbContext.FinanceLedgerEntries
                .AsNoTracking()
                .Where(entry => transactionIds.Contains(entry.FinanceTransactionId))
                .ToListAsync(cancellationToken);

        Guid[] accountIds = [.. entries.Select(entry => entry.FinanceAccountId).Distinct()];
        Dictionary<Guid, string> accountNames = accountIds.Length == 0
            ? []
            : await dbContext.FinanceAccounts
                .AsNoTracking()
                .Where(account => accountIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);

        List<FinanceTransactionListItem> items =
        [
            .. pageItems.Select(transaction => ProjectListItem(
                transaction,
                entries.Where(entry => entry.FinanceTransactionId == transaction.Id),
                accountNames)),
        ];

        return new PagedResult<FinanceTransactionListItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<FinanceTransactionDetail?> FindTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        FinanceTransaction? transaction = await dbContext.FinanceTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == transactionId, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        List<FinanceLedgerEntry> entries = await dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.FinanceTransactionId == transactionId)
            .ToListAsync(cancellationToken);

        Guid[] accountIds = [.. entries.Select(entry => entry.FinanceAccountId).Distinct()];
        Dictionary<Guid, string> accountNames = accountIds.Length == 0
            ? []
            : await dbContext.FinanceAccounts
                .AsNoTracking()
                .Where(account => accountIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);

        return new FinanceTransactionDetail
        {
            Transaction = ProjectListItem(transaction, entries, accountNames),
            RowVersion = transaction.RowVersion,
            CreatedByUserId = transaction.CreatedByUserId,
            CreatedByEmail = transaction.CreatedByEmail,
            CreatedAtUtc = transaction.CreatedAtUtc,
            UpdatedByUserId = transaction.UpdatedByUserId,
            UpdatedByEmail = transaction.UpdatedByEmail,
            UpdatedAtUtc = transaction.UpdatedAtUtc,
        };
    }

    private async Task<Dictionary<Guid, decimal>> GetBalancesAsync(
        Guid[] accountIds,
        DateOnly asOfOn,
        CancellationToken cancellationToken) =>
        await dbContext.FinanceLedgerEntries
            .AsNoTracking()
            .Where(entry => accountIds.Contains(entry.FinanceAccountId) && entry.OccurredOn <= asOfOn)
            .GroupBy(entry => entry.FinanceAccountId)
            .Select(group => new { AccountId = group.Key, Balance = group.Sum(entry => entry.Amount) })
            .ToDictionaryAsync(item => item.AccountId, item => item.Balance, cancellationToken);

    private static FinanceAccountListItem Project(
        FinanceAccount account,
        string holderDisplayName,
        decimal balance,
        DateOnly asOfOn) => new()
        {
            AccountId = account.Id,
            FinanceAccountHolderId = account.FinanceAccountHolderId,
            HolderDisplayName = holderDisplayName,
            Name = account.Name,
            Kind = account.Kind,
            CurrencyCode = account.CurrencyCode,
            Status = account.Status,
            OpenedOn = account.OpenedOn,
            CurrentBalance = balance,
            BalanceAsOfOn = asOfOn,
        };

    private static FinanceTransactionListItem ProjectListItem(
        FinanceTransaction transaction,
        IEnumerable<FinanceLedgerEntry> entries,
        IReadOnlyDictionary<Guid, string> accountNames) => new()
        {
            TransactionId = transaction.Id,
            Kind = transaction.Kind,
            Category = transaction.Category,
            Amount = transaction.Amount,
            OccurredOn = transaction.OccurredOn,
            Description = transaction.Description,
            Reference = transaction.Reference,
            CounterpartyName = transaction.CounterpartyName,
            RevisionNumber = transaction.RevisionNumber,
            Entries =
            [
                .. entries
                    .OrderBy(entry => entry.Leg)
                    .Select(entry => new FinanceTransactionListItemEntry
                    {
                        FinanceAccountId = entry.FinanceAccountId,
                        AccountName = accountNames.GetValueOrDefault(entry.FinanceAccountId, string.Empty),
                        Leg = entry.Leg,
                        Amount = entry.Amount,
                    }),
            ],
        };
}
