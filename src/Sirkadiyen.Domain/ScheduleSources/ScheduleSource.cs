namespace Sirkadiyen.Domain.ScheduleSources;

/// <summary>
/// A configured schedule source: where a program is published, how it is
/// acquired, which parser profile reads it, and what the spreadsheet itself does
/// not state.
/// </summary>
/// <remarks>
/// The source context fields exist because a workbook never states its academic
/// year, class year, program language or interpretation timezone, and one parser
/// profile serves several sources (ADR-017). They are configuration, and the
/// parser must never infer them.
/// </remarks>
public sealed class ScheduleSource
{
    private ScheduleSource()
    {
        // Materialization constructor.
        DisplayName = string.Empty;
        SourceUri = string.Empty;
        ParserProfile = string.Empty;
        ParserProfileVersion = string.Empty;
        AcademicYear = string.Empty;
        TimeZoneId = string.Empty;
    }

    public ScheduleSource(
        SourceId sourceId,
        string displayName,
        ScheduleSourceTransport transport,
        ScheduleDocumentFormat documentFormat,
        string sourceUri,
        string parserProfile,
        string parserProfileVersion,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string timeZoneId,
        string? externalId = null,
        long? sheetGid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(classYear, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(classYear, 6);

        Id = Guid.CreateVersion7();
        SourceId = sourceId;
        DisplayName = displayName;
        Transport = transport;
        DocumentFormat = documentFormat;
        SourceUri = sourceUri;
        ParserProfile = parserProfile;
        ParserProfileVersion = parserProfileVersion;
        AcademicYear = academicYear;
        ClassYear = classYear;
        ProgramLanguage = programLanguage;
        TimeZoneId = timeZoneId;
        ExternalId = externalId;
        SheetGid = sheetGid;
        IsPollingEnabled = true;
    }

    public Guid Id { get; private set; }

    public SourceId SourceId { get; private set; }

    public string DisplayName { get; private set; }

    public ScheduleSourceTransport Transport { get; private set; }

    public ScheduleDocumentFormat DocumentFormat { get; private set; }

    public string SourceUri { get; private set; }

    public string? ExternalId { get; private set; }

    public long? SheetGid { get; private set; }

    public string ParserProfile { get; private set; }

    public string ParserProfileVersion { get; private set; }

    public string AcademicYear { get; private set; }

    public int ClassYear { get; private set; }

    public ProgramLanguage ProgramLanguage { get; private set; }

    public string TimeZoneId { get; private set; }

    public bool IsPollingEnabled { get; private set; }

    /// <summary>When the source was last successfully polled, changed or not.</summary>
    public DateTimeOffset? LastPolledAtUtc { get; private set; }

    /// <summary>When the source last produced content that differed from the previous poll.</summary>
    public DateTimeOffset? LastChangedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public void RecordPolled(DateTimeOffset polledAtUtc, bool changed)
    {
        LastPolledAtUtc = polledAtUtc;
        if (changed)
        {
            LastChangedAtUtc = polledAtUtc;
        }
    }

    public void SetPollingEnabled(bool enabled) => IsPollingEnabled = enabled;
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

public enum ProgramLanguage
{
    Turkish,
    English,
}
