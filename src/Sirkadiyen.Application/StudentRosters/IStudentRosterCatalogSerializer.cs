namespace Sirkadiyen.Application.StudentRosters;

/// <summary>Reads and validates the configured student-list catalog.</summary>
public interface IStudentRosterCatalogSerializer
{
    /// <exception cref="StudentRosterCatalogValidationException">
    /// The document is not a catalog this deployment can act on.
    /// </exception>
    Task<StudentRosterCatalog> LoadAsync(string catalogPath, CancellationToken cancellationToken);

    /// <exception cref="StudentRosterCatalogValidationException">
    /// The document is not a catalog this deployment can act on.
    /// </exception>
    StudentRosterCatalog Parse(string content);
}

public sealed class StudentRosterCatalogValidationException(string message)
    : Exception(message);
