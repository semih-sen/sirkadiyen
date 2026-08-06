namespace Sirkadiyen.Domain.Finance;

public enum FinanceTransactionKind
{
    OpeningBalance,
    Income,
    Expense,
    Transfer,
    Distribution,
}

public enum FinanceLedgerLeg
{
    Single,
    From,
    To,
}
