using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.StudentRosters;

/// <summary>
/// Loads <c>config/student-rosters.json</c>, the catalog of published faculty
/// student lists ADR-085 looks a student number up in.
/// </summary>
/// <remarks>
/// The catalog holds locations, not people. The lists themselves are published
/// openly by the faculty, so the link is safe to keep in source control; their
/// contents are read at runtime and never committed.
/// </remarks>
public sealed class StudentRosterCatalogLoader : IStudentRosterCatalogSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

            // A property the model does not know is refused rather than dropped, for
            // the reason the schedule source catalog gives: a mistyped key would
            // deserialize to nothing, validate cleanly, and leave a list read by a
            // layout nobody intended.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    public async Task<StudentRosterCatalog> LoadAsync(
        string catalogPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        string content = await File.ReadAllTextAsync(catalogPath, cancellationToken);
        return Parse(content);
    }

    public StudentRosterCatalog Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        StudentRosterCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<StudentRosterCatalog>(content, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new StudentRosterCatalogValidationException(exception.Message);
        }

        if (catalog is null)
        {
            throw new StudentRosterCatalogValidationException("The student roster catalog is empty.");
        }

        try
        {
            Validate(catalog);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or FormatException)
        {
            throw new StudentRosterCatalogValidationException(exception.Message);
        }

        return catalog;
    }

    private static void Validate(StudentRosterCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(catalog.CatalogVersion))
        {
            throw new InvalidDataException("The student roster catalog states no version.");
        }

        HashSet<string> rosterIds = new(StringComparer.Ordinal);
        foreach (StudentRosterDefinition roster in catalog.Rosters)
        {
            Require(roster.RosterId, nameof(roster.RosterId));
            Require(roster.DisplayName, nameof(roster.DisplayName));
            Require(roster.AcademicYear, nameof(roster.AcademicYear));

            if (!rosterIds.Add(roster.RosterId))
            {
                throw new InvalidDataException($"Duplicate roster ID '{roster.RosterId}'.");
            }

            if (roster.ClassYear is < 1 or > 6)
            {
                throw new InvalidDataException(
                    $"Roster '{roster.RosterId}' states class year {roster.ClassYear}.");
            }

            ValidateTransport(roster);
            ValidateLayout(roster);
        }

        ValidateOneRosterPerCohort(catalog);
    }

    /// <summary>
    /// A roster is fetched, never uploaded. Every one of the four published lists
    /// has a location, so the administrative-upload transport has no meaning here
    /// and is refused rather than silently treated as unpollable.
    /// </summary>
    private static void ValidateTransport(StudentRosterDefinition roster)
    {
        if (roster.Transport == ScheduleSourceTransport.AdministrativeUpload)
        {
            throw new InvalidDataException(
                $"Roster '{roster.RosterId}' declares the administrative-upload transport, which a "
                + "student list does not use: every published list has a location to read it from.");
        }

        if (!roster.SourceUri.IsAbsoluteUri)
        {
            throw new InvalidDataException(
                $"Roster '{roster.RosterId}' states a relative location.");
        }

        if (roster.Transport == ScheduleSourceTransport.GoogleSheets)
        {
            if (roster.DocumentFormat != ScheduleDocumentFormat.GoogleSheet)
            {
                throw new InvalidDataException(
                    $"Roster '{roster.RosterId}' is read through the Sheets API but does not "
                    + "declare the googleSheet format.");
            }

            if (roster.SheetGid is null)
            {
                throw new InvalidDataException(
                    $"Roster '{roster.RosterId}' is read through the Sheets API but names no "
                    + "worksheet gid.");
            }
        }
        else if (roster.SheetGid is not null)
        {
            throw new InvalidDataException(
                $"Roster '{roster.RosterId}' names a worksheet gid, which only a Sheets-API roster "
                + "has.");
        }

        if (string.IsNullOrWhiteSpace(roster.ExternalId))
        {
            throw new InvalidDataException(
                $"Roster '{roster.RosterId}' names no external document ID.");
        }
    }

    private static void ValidateLayout(StudentRosterDefinition roster)
    {
        StudentRosterLayout layout = roster.Layout;
        Require(layout.WorksheetTitle, nameof(layout.WorksheetTitle));
        Require(layout.StudentNumberHeader, nameof(layout.StudentNumberHeader));
        Require(layout.GivenNameHeader, nameof(layout.GivenNameHeader));
        Require(layout.FamilyNameHeader, nameof(layout.FamilyNameHeader));

        if (layout.HeaderRow < 1)
        {
            throw new InvalidDataException(
                $"Roster '{roster.RosterId}' states header row {layout.HeaderRow}.");
        }

        HashSet<string> dimensions = new(StringComparer.Ordinal);
        foreach (StudentRosterDimensionColumn column in layout.DimensionColumns)
        {
            Require(column.Header, nameof(column.Header));
            Require(column.Dimension, nameof(column.Dimension));

            if (!dimensions.Add(column.Dimension))
            {
                throw new InvalidDataException(
                    $"Roster '{roster.RosterId}' states dimension '{column.Dimension}' twice.");
            }

            if (column.ValueMap.Count == 0)
            {
                // An empty map would refuse every value the column writes while
                // looking like a configured column, so it is a mistake rather than
                // a way to disable one. A column that should suggest nothing is
                // removed from the layout instead.
                throw new InvalidDataException(
                    $"Roster '{roster.RosterId}' maps no values for dimension "
                    + $"'{column.Dimension}'.");
            }

            foreach ((string stated, string mapped) in column.ValueMap)
            {
                if (string.IsNullOrWhiteSpace(stated) || string.IsNullOrWhiteSpace(mapped))
                {
                    throw new InvalidDataException(
                        $"Roster '{roster.RosterId}' maps a blank value for dimension "
                        + $"'{column.Dimension}'.");
                }
            }
        }
    }

    /// <summary>
    /// Two lists for one cohort and year would make every lookup in it ambiguous,
    /// which is a configuration mistake rather than the data conflict ADR-085
    /// asks the lookup to report.
    /// </summary>
    private static void ValidateOneRosterPerCohort(StudentRosterCatalog catalog)
    {
        IEnumerable<IGrouping<(string, int, ProgramLanguage), StudentRosterDefinition>> cohorts =
            catalog.Rosters.GroupBy(static roster =>
                (roster.AcademicYear, roster.ClassYear, roster.ProgramLanguage));

        foreach (var cohort in cohorts.Where(static group => group.Count() > 1))
        {
            string ids = string.Join(", ", cohort.Select(static roster => roster.RosterId));
            throw new InvalidDataException(
                $"Rosters {ids} all state class year {cohort.Key.Item2} "
                + $"{cohort.Key.Item3} for {cohort.Key.Item1}.");
        }
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"A roster states no '{field}'.");
        }
    }
}
