namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// A single enum with disjoint income and expense members, so the database check constraint that
/// mirrors <see cref="FinanceCategories"/> stays unambiguous.
/// </summary>
public enum FinanceCategory
{
    LicenseSales,
    Sponsorship,
    Donation,
    OtherIncome,
    Servers,
    Domains,
    ExternalServices,
    SoftwareLicenses,
    Marketing,
    Operational,
    Charitable,
    OtherExpense,
}

/// <summary>
/// The income/expense partition of <see cref="FinanceCategory"/>, shared by the domain factory and
/// mirrored in <c>ck_finance_transactions_category</c> — the same "constant shared by domain and EF
/// config" convention as <c>License.MaximumActorEmailLength</c>.
/// </summary>
public static class FinanceCategories
{
    public static readonly IReadOnlyCollection<FinanceCategory> IncomeCategories =
    [
        FinanceCategory.LicenseSales,
        FinanceCategory.Sponsorship,
        FinanceCategory.Donation,
        FinanceCategory.OtherIncome,
    ];

    public static readonly IReadOnlyCollection<FinanceCategory> ExpenseCategories =
    [
        FinanceCategory.Servers,
        FinanceCategory.Domains,
        FinanceCategory.ExternalServices,
        FinanceCategory.SoftwareLicenses,
        FinanceCategory.Marketing,
        FinanceCategory.Operational,
        FinanceCategory.Charitable,
        FinanceCategory.OtherExpense,
    ];

    public static bool IsIncome(FinanceCategory category) => IncomeCategories.Contains(category);

    public static bool IsExpense(FinanceCategory category) => ExpenseCategories.Contains(category);
}
