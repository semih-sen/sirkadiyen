namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// Bounds the automatic academic-year reconciler (ADR-117), so a rollover of a whole grade is
/// spread across cycles rather than restamping several hundred profiles and queueing several
/// hundred calendar convergences in one pass.
/// </summary>
public sealed class ProfileAcademicYearDriftOptions
{
    /// <summary>
    /// How many drifted profiles one cycle repairs per program. The convergence each one queues
    /// is itself budgeted per cycle (<see cref="GoogleCalendar.ProfileResyncOptions"/>), so this
    /// bounds how fast work is created rather than how fast calendars are written.
    /// </summary>
    public int ProfilesPerProgramPerCycle { get; init; } = 25;

    public void Validate() =>
        ArgumentOutOfRangeException.ThrowIfLessThan(ProfilesPerProgramPerCycle, 1);
}
