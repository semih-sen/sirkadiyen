namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// Turns catalog text into a validated <see cref="ScheduleSourceCatalog"/>, applying exactly the
/// rules the worker applies when it loads the file at startup.
/// </summary>
/// <remarks>
/// The editing surface must refuse a document the worker would refuse. Sharing one implementation
/// is what guarantees that: a rule added for the worker cannot drift out of the admin panel, and an
/// edit accepted here cannot leave the worker unable to start.
/// </remarks>
public interface IScheduleSourceCatalogSerializer
{
    /// <summary>
    /// Parses and fully validates a catalog document.
    /// </summary>
    /// <exception cref="ScheduleSourceCatalogValidationException">
    /// The text is not JSON, declares an unknown property, or breaks a catalog rule.
    /// </exception>
    ScheduleSourceCatalog Parse(string content);
}
