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
        long? sheetGid = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? supportedAudienceSelectors = null,
        string? sharedDocumentGroup = null)
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
        SupportedAudienceSelectors = supportedAudienceSelectors;
        SharedDocumentGroup = sharedDocumentGroup;
        IsPollingEnabled = true;
    }

    /// <summary>Maximum length of a shared-document group name.</summary>
    public const int MaximumSharedDocumentGroupLength = 100;

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

    /// <summary>
    /// The audience selector values this source may state, keyed by dimension,
    /// or <see langword="null"/> when the source has not declared them.
    /// </summary>
    /// <remarks>
    /// Null means "not declared", so revision validation skips the
    /// unknown-selector rule rather than treating every selector as unknown. A
    /// declared dimension with an empty list means the dimension may not appear.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? SupportedAudienceSelectors
    {
        get;
        private set;
    }

    /// <summary>
    /// The name shared by every source whose document is literally the same
    /// file, or <see langword="null"/> when this source has its own document.
    /// </summary>
    /// <remarks>
    /// The Grade 2 anatomy group list is handed to the Turkish and the English
    /// program as one document, and each program needs its own revision because a
    /// canonical record matches a student only when its program language matches
    /// theirs. The group is what lets one administrative upload become a snapshot
    /// for every source the document serves, instead of asking an administrator
    /// to upload the identical file once per program (ADR-080).
    /// </remarks>
    public string? SharedDocumentGroup { get; private set; }

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

    /// <summary>
    /// The document is handed out rather than published, so an administrator
    /// uploads it and that upload is the only acquisition path (ADR-079).
    /// </summary>
    /// <remarks>
    /// Such a source is never polled: there is nothing to read until someone
    /// uploads, and the worker must not invent a location to fetch it from.
    /// </remarks>
    AdministrativeUpload,
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
