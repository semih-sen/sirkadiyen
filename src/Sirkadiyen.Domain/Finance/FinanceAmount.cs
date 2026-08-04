namespace Sirkadiyen.Domain.Finance;

/// <summary>
/// Validation for monetary values in the finance module. There is no <c>Money</c> value object
/// (ADR-092): a bare <see cref="decimal"/> mapped <c>numeric(18,2)</c> is enough for a
/// single-currency ledger, but the scale must be enforced at the boundary. A value with more than
/// two decimal places is rejected outright rather than silently rounded, because silent rounding is
/// how a ledger loses a kuruş.
/// </summary>
public static class FinanceAmount
{
    public const int Scale = 2;

    public const decimal MaximumAmount = 1_000_000_000m;

    /// <summary>Validates scale and magnitude only. The value may be negative or zero.</summary>
    public static decimal Require(decimal value, string parameterName)
    {
        if (decimal.Round(value, Scale) != value)
        {
            throw new ArgumentException(
                $"'{parameterName}' must not carry more than {Scale} decimal places.",
                parameterName);
        }

        if (Math.Abs(value) > MaximumAmount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"'{parameterName}' must not exceed {MaximumAmount:0.00} in magnitude.");
        }

        return value;
    }

    /// <summary>Validates scale, magnitude, and that the value is strictly greater than zero.</summary>
    public static decimal RequirePositive(decimal value, string parameterName)
    {
        decimal validated = Require(value, parameterName);
        if (validated <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"'{parameterName}' must be greater than zero.");
        }

        return validated;
    }

    /// <summary>Validates scale, magnitude, and that the value is not zero. Sign is unconstrained.</summary>
    public static decimal RequireNonZero(decimal value, string parameterName)
    {
        decimal validated = Require(value, parameterName);
        if (validated == 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"'{parameterName}' must not be zero.");
        }

        return validated;
    }
}
