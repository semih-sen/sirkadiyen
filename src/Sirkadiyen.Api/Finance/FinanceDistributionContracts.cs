namespace Sirkadiyen.Api.Administration;

public sealed record PreviewFinanceDistributionRequest
{
    public required DateOnly PeriodStartOn { get; init; }

    public required DateOnly PeriodEndOn { get; init; }

    public required Guid SourceAccountId { get; init; }
}

public sealed record ExecuteFinanceDistributionRequest
{
    public required DateOnly PeriodStartOn { get; init; }

    public required DateOnly PeriodEndOn { get; init; }

    public required Guid SourceAccountId { get; init; }

    public required Guid ConfirmationToken { get; init; }

    public required string? PlanHash { get; init; }

    /// <summary>The distributable amount as shown in the preview, formatted "0.00".</summary>
    public required string? ExpectedConfirmationPhrase { get; init; }

    /// <example>Q1 2026 profit distribution, approved by both partners.</example>
    public required string? Reason { get; init; }
}

public sealed record ReverseFinanceDistributionRequest
{
    /// <example>Distributable amount was miscalculated; reversing to redo.</example>
    public required string? Reason { get; init; }
}
