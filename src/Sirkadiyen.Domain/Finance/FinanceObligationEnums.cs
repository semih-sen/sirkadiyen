namespace Sirkadiyen.Domain.Finance;

public enum FinanceObligationDirection
{
    Receivable,
    Payable,
}

public enum FinanceObligationStatus
{
    Open,
    PartiallySettled,
    Settled,
    WrittenOff,
    Cancelled,
}
