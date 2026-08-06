using System.Diagnostics.CodeAnalysis;

namespace Sirkadiyen.Domain.ScheduleSources;

/// <summary>
/// The stable identifier of a schedule source, such as <c>G1-TR-ANNUAL</c>.
/// </summary>
/// <remarks>
/// A source identifier travels through polling, snapshots, parse runs, revisions
/// and audit records, so it is an explicit domain type rather than a bare string:
/// an empty or accidentally swapped identifier would silently attach evidence to
/// the wrong source.
/// </remarks>
public readonly record struct SourceId
{
    public const int MaxLength = 64;

    private SourceId(string value) => Value = value;

    public string Value { get; }

    public static SourceId Parse(string value)
    {
        if (!TryParse(value, out SourceId sourceId))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid source identifier.",
                nameof(value));
        }

        return sourceId;
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out SourceId sourceId)
    {
        sourceId = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        if (!value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return false;
        }

        sourceId = new SourceId(value.ToUpperInvariant());
        return true;
    }

    public override string ToString() => Value;
}
