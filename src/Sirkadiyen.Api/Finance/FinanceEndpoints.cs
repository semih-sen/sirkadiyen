using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Sirkadiyen.Api.Identity;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Application.Finance;
using Sirkadiyen.Domain.Auditing;
using Sirkadiyen.Domain.Finance;

namespace Sirkadiyen.Api.Finance;

/// <summary>
/// SuperAdmin-only finance ledger administration: holders, accounts, and transactions including
/// edit, hard delete and the per-transaction audit history (ADR-093).
/// </summary>
public static class FinanceEndpoints
{
    private const int MaximumPageSize = 200;

    private const int MaximumExportRows = 5000;

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder finance = builder
            .MapGroup("/api/admin/finance")
            .RequireAuthorization(AuthorizationPolicies.SuperAdmin)
            .WithTags("Finance");

        finance.MapGet("/holders", ListHoldersAsync)
            .WithSummary("Lists finance account holders.");
        finance.MapPost("/holders", CreateHolderAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Creates a finance account holder.");
        finance.MapPost("/holders/{holderId:guid}/share", SetHolderShareAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Sets one holder's profit-distribution share in basis points.");
        finance.MapPost("/holders/{holderId:guid}/deactivate", DeactivateHolderAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Deactivates a holder, zeroing its distribution share.");

        finance.MapGet("/accounts", ListAccountsAsync)
            .WithSummary("Lists finance accounts with their derived balance as of a given date.");
        finance.MapPost("/accounts", OpenAccountAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Opens a cash or bank account for a holder.");
        finance.MapGet("/accounts/{accountId:guid}", FindAccountAsync)
            .WithSummary("Returns one account with its derived balance.");
        finance.MapPost("/accounts/{accountId:guid}/close", CloseAccountAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Closes an account.");

        finance.MapGet("/transactions", ListTransactionsAsync)
            .WithSummary("Lists transactions, newest first, with period/kind/category/account filters.");
        finance.MapGet("/transactions/{transactionId:guid}", FindTransactionAsync)
            .WithSummary("Returns one transaction with its ledger entries.");
        finance.MapGet("/transactions/{transactionId:guid}/history", GetTransactionHistoryAsync)
            .WithSummary("Returns the full finance-audit chain for one transaction.");
        finance.MapPost("/transactions/opening-balance", RecordOpeningBalanceAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Records an account's opening balance.");
        finance.MapPost("/transactions/income", RecordIncomeAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Records income into one account.");
        finance.MapPost("/transactions/expense", RecordExpenseAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Records an expense out of one account.");
        finance.MapPost("/transactions/transfer", RecordTransferAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Transfers money between two accounts.");
        finance.MapPut("/transactions/{transactionId:guid}", UpdateTransactionAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Edits a transaction, rewriting its ledger entries. Requires a reason.");

        // POST rather than DELETE with a body, matching the /{id}/unmask and /{id}/deactivate
        // style already used elsewhere: a hard delete requires a reason, which a bodyless DELETE
        // cannot carry cleanly across every client.
        finance.MapPost("/transactions/{transactionId:guid}/delete", DeleteTransactionAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(required: true))
            .WithSummary("Hard-deletes a transaction. Requires a reason.");

        finance.MapGet("/audit", QueryAuditAsync)
            .WithSummary("Queries the finance module's own append-only audit log.");

        finance.MapGet("/summary", GetSummaryAsync)
            .WithSummary("Returns the ten period figures for a period selector, with category totals.");
        finance.MapGet("/trend", GetTrendAsync)
            .WithSummary("Returns monthly income/expenses/net for the trailing N months.");

        finance.MapGet("/transactions/export", ExportTransactionsAsync)
            .WithSummary("Downloads transactions matching the same filters as the list endpoint, as CSV.");

        return builder;
    }

    private static async Task<IResult> ListHoldersAsync(
        IFinanceReadStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListHoldersAsync(cancellationToken));

    private static async Task<IResult> CreateHolderAsync(
        CreateFinanceAccountHolderRequest request,
        FinanceLedgerService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            FinanceAccountHolderMutationResult result = await service.CreateHolderAsync(
                request.DisplayName ?? string.Empty,
                request.UserId,
                request.ShareBasisPoints,
                cancellationToken);
            return MapHolderOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> SetHolderShareAsync(
        Guid holderId,
        SetFinanceAccountHolderShareRequest request,
        FinanceLedgerService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return MapHolderOutcome(
                await service.SetHolderShareAsync(holderId, request.ShareBasisPoints, cancellationToken));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> DeactivateHolderAsync(
        Guid holderId,
        FinanceLedgerService service,
        CancellationToken cancellationToken) =>
        MapHolderOutcome(await service.DeactivateHolderAsync(holderId, cancellationToken));

    private static async Task<IResult> ListAccountsAsync(
        IFinanceReadStore store,
        FinancePeriodResolver periodResolver,
        CancellationToken cancellationToken,
        DateOnly? asOfOn = null) =>
        Results.Ok(await store.ListAccountsAsync(asOfOn ?? periodResolver.Today(), cancellationToken));

    private static async Task<IResult> FindAccountAsync(
        Guid accountId,
        IFinanceReadStore store,
        FinancePeriodResolver periodResolver,
        CancellationToken cancellationToken,
        DateOnly? asOfOn = null) =>
        await store.FindAccountAsync(accountId, asOfOn ?? periodResolver.Today(), cancellationToken) is { } account
            ? Results.Ok(account)
            : Results.Problem(
                title: "Account not found",
                detail: $"No finance account with ID '{accountId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> OpenAccountAsync(
        OpenFinanceAccountRequest request,
        FinanceLedgerService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            FinanceAccountMutationResult result = await service.OpenAccountAsync(
                request.FinanceAccountHolderId,
                request.Name ?? string.Empty,
                request.Kind,
                request.OpenedOn,
                cancellationToken);
            return MapAccountOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> CloseAccountAsync(
        Guid accountId,
        CloseFinanceAccountRequest request,
        FinanceLedgerService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return MapAccountOutcome(
                await service.CloseAccountAsync(accountId, request.Reason ?? string.Empty, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> ListTransactionsAsync(
        IFinanceReadStore store,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        FinanceTransactionKind? kind = null,
        FinanceCategory? category = null,
        Guid? accountId = null,
        Guid? holderId = null,
        string? search = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return InvalidPageSize();
        }

        PagedResult<FinanceTransactionListItem> result = await store.ListTransactionsAsync(
            new FinanceTransactionQuery
            {
                Page = page,
                PageSize = pageSize,
                FromOn = from,
                ToOn = to,
                Kind = kind,
                Category = category,
                AccountId = accountId,
                HolderId = holderId,
                Search = search,
            },
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExportTransactionsAsync(
        IFinanceReadStore store,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        FinanceTransactionKind? kind = null,
        FinanceCategory? category = null,
        Guid? accountId = null,
        Guid? holderId = null,
        string? search = null)
    {
        PagedResult<FinanceTransactionListItem> result = await store.ListTransactionsAsync(
            new FinanceTransactionQuery
            {
                Page = 1,
                PageSize = MaximumExportRows,
                FromOn = from,
                ToOn = to,
                Kind = kind,
                Category = category,
                AccountId = accountId,
                HolderId = holderId,
                Search = search,
            },
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine(
            "TransactionId,Kind,Category,Amount,OccurredOn,Description,Reference,CounterpartyName,Accounts,RevisionNumber");
        foreach (FinanceTransactionListItem transaction in result.Items)
        {
            string accounts = string.Join(
                ' ',
                transaction.Entries.Select(entry => $"{entry.AccountName}({entry.Leg}:{entry.Amount:0.00})"));
            csv.AppendLine(string.Join(
                ',',
                transaction.TransactionId,
                transaction.Kind,
                transaction.Category?.ToString() ?? string.Empty,
                transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                transaction.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CsvField(transaction.Description),
                CsvField(transaction.Reference),
                CsvField(transaction.CounterpartyName),
                CsvField(accounts),
                transaction.RevisionNumber));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return Results.File(bytes, "text/csv", "finance-transactions.csv");
    }

    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuoting = value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal);
        if (!needsQuoting)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static async Task<IResult> FindTransactionAsync(
        Guid transactionId,
        IFinanceReadStore store,
        CancellationToken cancellationToken) =>
        await store.FindTransactionAsync(transactionId, cancellationToken) is { } detail
            ? Results.Ok(detail)
            : TransactionNotFound(transactionId);

    private static async Task<IResult> GetTransactionHistoryAsync(
        Guid transactionId,
        IFinanceAuditStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.GetHistoryAsync("FinanceTransaction", transactionId, cancellationToken));

    private static async Task<IResult> RecordOpeningBalanceAsync(
        RecordOpeningBalanceRequest request,
        ClaimsPrincipal principal,
        FinanceLedgerService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            FinanceTransactionMutationResult result = await service.RecordOpeningBalanceAsync(
                request.AccountId,
                request.SignedAmount,
                request.OccurredOn,
                request.Description ?? string.Empty,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                context.CorrelationId(),
                cancellationToken);
            return MapTransactionOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> RecordIncomeAsync(
        RecordFinanceTransactionRequest request,
        ClaimsPrincipal principal,
        FinanceLedgerService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Category is not { } category)
        {
            return ValidationProblem("'category' is required.");
        }

        try
        {
            FinanceTransactionMutationResult result = await service.RecordIncomeAsync(
                request.AccountId,
                request.Amount,
                category,
                request.OccurredOn,
                request.Description ?? string.Empty,
                request.Reference,
                request.CounterpartyName,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                context.CorrelationId(),
                cancellationToken);
            return MapTransactionOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> RecordExpenseAsync(
        RecordFinanceTransactionRequest request,
        ClaimsPrincipal principal,
        FinanceLedgerService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Category is not { } category)
        {
            return ValidationProblem("'category' is required.");
        }

        try
        {
            FinanceTransactionMutationResult result = await service.RecordExpenseAsync(
                request.AccountId,
                request.Amount,
                category,
                request.OccurredOn,
                request.Description ?? string.Empty,
                request.Reference,
                request.CounterpartyName,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                context.CorrelationId(),
                cancellationToken);
            return MapTransactionOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> RecordTransferAsync(
        RecordFinanceTransferRequest request,
        ClaimsPrincipal principal,
        FinanceLedgerService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            FinanceTransactionMutationResult result = await service.RecordTransferAsync(
                request.FromAccountId,
                request.ToAccountId,
                request.Amount,
                request.OccurredOn,
                request.Description ?? string.Empty,
                request.Reference,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                context.CorrelationId(),
                cancellationToken);
            return MapTransactionOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> UpdateTransactionAsync(
        Guid transactionId,
        UpdateFinanceTransactionRequest request,
        ClaimsPrincipal principal,
        FinanceLedgerService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to edit a transaction.");
        }

        var edit = new FinanceTransactionEdit
        {
            Kind = request.Kind,
            Category = request.Category,
            Amount = request.Amount,
            OccurredOn = request.OccurredOn,
            Description = request.Description ?? string.Empty,
            Reference = request.Reference,
            CounterpartyName = request.CounterpartyName,
            AccountId = request.AccountId,
            ToAccountId = request.ToAccountId,
        };

        try
        {
            FinanceTransactionMutationResult result = await service.UpdateTransactionAsync(
                transactionId,
                edit,
                request.RowVersion,
                request.Reason,
                UserClaimsPrincipalFactory.GetRequiredUserId(principal),
                UserClaimsPrincipalFactory.GetRequiredEmail(principal),
                context.CorrelationId(),
                cancellationToken);
            return MapTransactionOutcome(result);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    private static async Task<IResult> DeleteTransactionAsync(
        Guid transactionId,
        DeleteFinanceTransactionRequest request,
        ClaimsPrincipal principal,
        FinanceLedgerService service,
        AuditEventRecorder audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ValidationProblem("A reason is required to delete a transaction.");
        }

        Guid actorUserId = UserClaimsPrincipalFactory.GetRequiredUserId(principal);
        string actorEmail = UserClaimsPrincipalFactory.GetRequiredEmail(principal);

        FinanceTransactionMutationResult result = await service.DeleteTransactionAsync(
            transactionId,
            request.RowVersion,
            request.Reason,
            actorUserId,
            actorEmail,
            context.CorrelationId(),
            cancellationToken);

        if (result.Outcome == FinanceTransactionOutcome.Deleted)
        {
            // A hard delete of money data is high-risk enough to also land in the cross-cutting
            // access/activity log, distinct from the module's own finance_audits row that already
            // committed with the transaction (ADR-093). A crash between the two loses only this
            // convenience index entry, never the money audit.
            await audit.RecordAsync(
                new AuditEventDraft
                {
                    Category = AuditEventCategory.FinanceTransactionDeleted,
                    ActorUserId = actorUserId,
                    ActorEmail = actorEmail,
                    SubjectType = "FinanceTransaction",
                    SubjectId = transactionId.ToString(),
                    CorrelationId = context.CorrelationId(),
                    ClientIp = context.ClientIp(),
                    UserAgent = context.ClientUserAgent(),
                    Reason = request.Reason,
                },
                cancellationToken);
        }

        return MapTransactionOutcome(result);
    }

    private static async Task<IResult> QueryAuditAsync(
        IFinanceAuditStore store,
        CancellationToken cancellationToken,
        string? subjectType = null,
        Guid? subjectId = null,
        FinanceAuditAction? action = null,
        Guid? actorUserId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return InvalidPageSize();
        }

        PagedResult<FinanceAuditListItem> result = await store.ListAsync(
            new FinanceAuditQuery
            {
                Page = page,
                PageSize = pageSize,
                SubjectType = subjectType,
                SubjectId = subjectId,
                Action = action,
                ActorUserId = actorUserId,
                FromUtc = from,
                ToUtc = to,
            },
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSummaryAsync(
        FinanceSummaryService service,
        CancellationToken cancellationToken,
        FinancePeriodSelector period = FinancePeriodSelector.CurrentMonth,
        DateOnly? startOn = null,
        DateOnly? endOn = null,
        Guid? accountId = null)
    {
        FinanceSummaryResult result = await service.GetSummaryAsync(
            period,
            startOn,
            endOn,
            accountId,
            cancellationToken);

        return result.Outcome switch
        {
            FinanceSummaryOutcome.Resolved => Results.Ok(result.Summary),
            FinanceSummaryOutcome.CustomRangeRequiresBothDates => ValidationProblem(
                "A custom period requires both 'startOn' and 'endOn'."),
            FinanceSummaryOutcome.EndBeforeStart => ValidationProblem(
                "'endOn' cannot be before 'startOn'."),
            FinanceSummaryOutcome.RangeTooLong => ValidationProblem(
                $"The period cannot span more than {FinancePeriodResolver.MaximumPeriodDays} days."),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> GetTrendAsync(
        FinanceSummaryService service,
        CancellationToken cancellationToken,
        int months = 12)
    {
        FinanceTrendResult result = await service.GetTrendAsync(months, cancellationToken);
        return result.Outcome == FinanceTrendOutcome.Resolved
            ? Results.Ok(result.Points)
            : ValidationProblem("'months' must be between 1 and 36.");
    }

    private static IResult MapHolderOutcome(FinanceAccountHolderMutationResult result) => result.Outcome switch
    {
        FinanceAccountHolderOutcome.Created =>
            Results.Created($"/api/admin/finance/holders/{result.HolderId}", result),
        FinanceAccountHolderOutcome.ShareSet or FinanceAccountHolderOutcome.Deactivated => Results.Ok(result),
        FinanceAccountHolderOutcome.NotFound => Results.Problem(
            title: "Holder not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceAccountHolderOutcome.DuplicateDisplayName => Results.Problem(
            title: "Display name already in use",
            statusCode: StatusCodes.Status409Conflict),
        FinanceAccountHolderOutcome.AlreadyInactive => Results.Problem(
            title: "Holder is inactive",
            detail: "An inactive holder cannot be given a share, or deactivated again.",
            statusCode: StatusCodes.Status409Conflict),
        FinanceAccountHolderOutcome.ConcurrentUpdate => Results.Problem(
            title: "Concurrent update",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
    };

    private static IResult MapAccountOutcome(FinanceAccountMutationResult result) => result.Outcome switch
    {
        FinanceAccountOutcome.Opened => Results.Created($"/api/admin/finance/accounts/{result.AccountId}", result),
        FinanceAccountOutcome.Closed => Results.Ok(result),
        FinanceAccountOutcome.NotFound => Results.Problem(
            title: "Account not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceAccountOutcome.HolderNotFound => Results.Problem(
            title: "Holder not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceAccountOutcome.AlreadyClosed => Results.Problem(
            title: "Account already closed",
            statusCode: StatusCodes.Status409Conflict),
        FinanceAccountOutcome.DuplicateName => Results.Problem(
            title: "Account name already in use for this holder",
            statusCode: StatusCodes.Status409Conflict),
        FinanceAccountOutcome.ConcurrentUpdate => Results.Problem(
            title: "Concurrent update",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
    };

    private static IResult MapTransactionOutcome(FinanceTransactionMutationResult result) => result.Outcome switch
    {
        FinanceTransactionOutcome.Recorded =>
            Results.Created($"/api/admin/finance/transactions/{result.TransactionId}", result),
        FinanceTransactionOutcome.Updated or FinanceTransactionOutcome.Deleted => Results.Ok(result),
        FinanceTransactionOutcome.NotFound => Results.Problem(
            title: "Transaction not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceTransactionOutcome.AccountNotFound => Results.Problem(
            title: "Account not found",
            statusCode: StatusCodes.Status404NotFound),
        FinanceTransactionOutcome.AccountClosed => Results.Problem(
            title: "Account is closed",
            statusCode: StatusCodes.Status409Conflict),
        FinanceTransactionOutcome.CategoryMismatch => Results.Problem(
            title: "Category does not match the transaction kind",
            statusCode: StatusCodes.Status400BadRequest),
        FinanceTransactionOutcome.SameAccount => Results.Problem(
            title: "A transfer cannot move money to the same account",
            statusCode: StatusCodes.Status400BadRequest),
        FinanceTransactionOutcome.InsufficientBalance => Results.Problem(
            title: "Insufficient balance",
            statusCode: StatusCodes.Status409Conflict),
        FinanceTransactionOutcome.TransactionSettlesAnObligation => Results.Problem(
            title: "This transaction settles an obligation",
            detail: "Cancel the settlement before editing or deleting this transaction.",
            statusCode: StatusCodes.Status409Conflict),
        FinanceTransactionOutcome.TransactionIsADistributionPayout => Results.Problem(
            title: "This transaction is a distribution payout",
            detail: "Reverse the distribution before editing or deleting this transaction.",
            statusCode: StatusCodes.Status409Conflict),
        FinanceTransactionOutcome.KindChangeNotAllowed => Results.Problem(
            title: "This kind change is not allowed",
            detail: "An opening balance cannot be edited, and no edit may produce an opening " +
                "balance or a distribution payout.",
            statusCode: StatusCodes.Status409Conflict),
        FinanceTransactionOutcome.ConcurrentUpdate => Results.Problem(
            title: "Concurrent update",
            detail: "This transaction was changed by someone else. Reload and try again.",
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
    };

    private static IResult TransactionNotFound(Guid transactionId) => Results.Problem(
        title: "Transaction not found",
        detail: $"No transaction with ID '{transactionId}' exists.",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult InvalidPageSize() => Results.Problem(
        title: "Invalid page size",
        detail: $"'pageSize' must be between 1 and {MaximumPageSize}.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult ValidationProblem(string detail) => Results.Problem(
        title: "Invalid request",
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);
}
