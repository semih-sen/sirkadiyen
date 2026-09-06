namespace Sirkadiyen.Application.Common;

/// <summary>
/// A one-line, human-readable summary of an exception together with the inner exceptions it wraps.
/// </summary>
/// <remarks>
/// A wrapping exception's own message is often generic while the cause lives one level down: Entity
/// Framework's <c>DbUpdateException</c> always reads "An error occurred while saving the entity
/// changes. See the inner exception for details.", and the actual reason is in its inner exception —
/// for Npgsql a <c>PostgresException</c> carrying the SQLSTATE and the violated column or constraint.
/// A failure string built from the outer message alone therefore tells an operator nothing, which is
/// why a failed parse showed up as "0 / 0" with no cause and a validation alert repeated the same
/// unhelpful sentence every cycle. This walks the chain and joins each distinct
/// "<c>TypeName: message</c>" with an arrow, capped so it fits a phone alert and a database column.
/// It reports the exceptions' own text only; it never includes a stack trace.
/// </remarks>
public static class ExceptionSummary
{
    /// <summary>How far down the inner-exception chain to read before stopping.</summary>
    private const int MaxLevels = 4;

    public static string Describe(Exception exception, int maxLength = 1900)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        List<string> levels = [];
        Exception? current = exception;
        string? previousMessage = null;
        for (int depth = 0; current is not null && depth < MaxLevels; depth++)
        {
            string message = current.Message.Trim();

            // A wrapper often repeats the inner exception's message verbatim, and EF's generic
            // sentence adds nothing once its inner cause is shown. Either way, a level that says the
            // same thing as the one before it is noise, so it is skipped rather than duplicated.
            if (message.Length > 0 && !string.Equals(message, previousMessage, StringComparison.Ordinal))
            {
                levels.Add($"{current.GetType().Name}: {message}");
                previousMessage = message;
            }

            current = current.InnerException;
        }

        // The type name is always meaningful even when every message was empty or repeated, so it is
        // the floor rather than an empty string that would read as "no reason recorded".
        string summary = levels.Count > 0 ? string.Join(" -> ", levels) : exception.GetType().Name;
        return summary.Length <= maxLength ? summary : summary[..maxLength];
    }
}
