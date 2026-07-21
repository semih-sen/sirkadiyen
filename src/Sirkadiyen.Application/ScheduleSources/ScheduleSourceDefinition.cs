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

    public string? FixturePath { get; init; }

    public string? Notes { get; init; }
}

public enum ScheduleSourceTransport
{
    GoogleSheets,
    GoogleDriveFile,
    HttpFile,
}

public enum ScheduleDocumentFormat
{
    GoogleSheet,
    Xlsx,
    Docx,
}
