using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Application.ScheduleSources;

public sealed record ScheduleSourceCatalog
{
    public required string CatalogVersion { get; init; }

    public IReadOnlyList<ScheduleSourceDefinition> Sources { get; init; } = [];
}

public sealed record ScheduleSourceDefinition
{
    public required string SourceId { get; init; }

    public required string DisplayName { get; init; }

    public required ScheduleSourceTransport Transport { get; init; }

    public required ScheduleDocumentFormat DocumentFormat { get; init; }

    public required Uri SourceUri { get; init; }

    public string? ExternalId { get; init; }

    public long? SheetGid { get; init; }

    public required string ParserProfile { get; init; }

    public required string ParserProfileVersion { get; init; }

    /// <summary>
    /// The source context the workbook does not state (ADR-017). It is
    /// configuration, so it lives beside the source definition rather than
    /// being derived from dates, file names or profile names.
    /// </summary>
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    public required string TimeZoneId { get; init; }

    public string? FixturePath { get; init; }

    public string? Notes { get; init; }

    /// <summary>Projects the configured definition onto its persisted form.</summary>
    public ScheduleSource ToScheduleSource() => new(
        Domain.ScheduleSources.SourceId.Parse(SourceId),
        DisplayName,
        Transport,
        DocumentFormat,
        SourceUri.ToString(),
        ParserProfile,
        ParserProfileVersion,
        AcademicYear,
        ClassYear,
        ProgramLanguage,
        TimeZoneId,
        ExternalId,
        SheetGid);
}
