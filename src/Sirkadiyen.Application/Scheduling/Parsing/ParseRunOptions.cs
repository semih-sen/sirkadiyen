namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// How long a parse run may stay open before it is treated as abandoned.
/// </summary>
/// <remarks>
/// A run is keyed by snapshot and parser profile version, so a run left running
/// by a killed worker blocks that snapshot from ever being parsed again. The
/// source then recovers only when its content next changes, which can silently
/// delay a real schedule change by days.
/// <para>
/// The timeout must comfortably exceed the parser transport timeout. If it did
/// not, a slow but healthy parse would be recovered while it was still running:
/// the recovery would change the run's correlation ID, and the original worker's
/// response would then be rejected as belonging to a different attempt. That is
/// safe — no duplicate revision can be created — but it wastes a parse and looks
/// like a transport fault, so the default leaves a wide margin.
/// </para>
/// </remarks>
public sealed record ParseRunOptions
{
    public TimeSpan StaleRunTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public void Validate()
    {
        if (StaleRunTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("The stale parse run timeout must be positive.");
        }
    }
}
