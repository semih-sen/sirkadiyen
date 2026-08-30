namespace Sirkadiyen.Domain.Scheduling.Sources;

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
    /// <summary>How much of a failure message is kept. Enough for the acquirer's own sentence.</summary>
    public const int MaximumPollFailureReasonLength = 1000;

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
        string? sharedDocumentGroup = null,
        IReadOnlyList<SourceId>? companionSourceIds = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? authoritativeAudienceSelectors = null,
        IReadOnlyList<SourceId>? groupRotationSourceIds = null,
        string? discoveryFolderId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserProfileVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(classYear, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(classYear, 6);

        IReadOnlyList<SourceId> companions = companionSourceIds is null ? [] : [.. companionSourceIds];
        if (companions.Contains(sourceId))
        {
            throw new ArgumentException(
                $"Source '{sourceId}' names itself as its own companion.",
                nameof(companionSourceIds));
        }

        if (companions.Distinct().Count() != companions.Count)
        {
            // A repeated companion would be attached to the parse request twice
            // and counted twice in the companion fingerprint, so the same
            // document would look like two pieces of evidence (ADR-102).
            throw new ArgumentException(
                $"Source '{sourceId}' names the same companion more than once.",
                nameof(companionSourceIds));
        }

        IReadOnlyList<SourceId> rotationSources = groupRotationSourceIds is null
            ? []
            : [.. groupRotationSourceIds];
        if (rotationSources.Contains(sourceId))
        {
            throw new ArgumentException(
                $"Source '{sourceId}' names itself as the owner of its own group rotation.",
                nameof(groupRotationSourceIds));
        }

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
        DiscoveryFolderId = discoveryFolderId;
        SupportedAudienceSelectors = supportedAudienceSelectors;
        AuthoritativeAudienceSelectors = authoritativeAudienceSelectors;
        SharedDocumentGroup = sharedDocumentGroup;
        CompanionSourceIds = companions;
        GroupRotationSourceIds = rotationSources;
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

    /// <summary>
    /// The Drive folder this source's document is republished into, when the
    /// faculty replaces the document itself rather than editing one in place
    /// (ADR-133).
    /// </summary>
    /// <remarks>
    /// Only the weekly amphitheatre program works this way: a new workbook appears
    /// in one folder every week, and the folder is the address the faculty
    /// publishes, not the file. <see cref="ExternalId"/> stays the document the
    /// catalog was written against and is what a cycle falls back to when the
    /// folder cannot be read, so a source configured this way is never left with
    /// no document at all.
    /// <para>
    /// The resolved document is deliberately not stored back here. Which file is
    /// current is a fact about this week rather than configuration, and writing it
    /// into the catalogued source would put the poller and the catalog planner in
    /// permanent disagreement about what the source is.
    /// </para>
    /// </remarks>
    public string? DiscoveryFolderId { get; private set; }

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
    /// The audience values this source is the authority for, keyed by selector
    /// dimension (ADR-110).
    /// </summary>
    /// <remarks>
    /// Two documents may state the same session — the Grade 3 A and B workbooks both
    /// carry the sessions both halves of the class attend, each in its own wording, so
    /// neither copy can be recognized as the other's. Naming the half each document owns
    /// is what makes exactly one of them publish it.
    /// <para>
    /// Null or an absent dimension means "not narrowed", so a source that declares
    /// nothing publishes exactly what it published before ownership existed. This is
    /// distinct from <see cref="SupportedAudienceSelectors"/>, which says what a source
    /// may legally *state*; this says which of those statements it may *publish*.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? AuthoritativeAudienceSelectors
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

    /// <summary>
    /// Other sources whose latest snapshot this source's parser reads alongside
    /// its own, in catalog order (ADR-102).
    /// </summary>
    /// <remarks>
    /// A companion is supporting evidence, never a second schedule: the Grade 3
    /// annual program states the date and time of every bedside session, and the
    /// bedside document is the only source of what each session is about. The
    /// companion publishes nothing of its own here — its own source and profile
    /// still parse it separately if it has anything to publish.
    /// <para>
    /// Empty is the normal case and means the parser sees only this source's
    /// document. A companion that has never been acquired is simply absent from
    /// the parse request: the parser must degrade rather than wait, because a
    /// missing topic is far cheaper than a schedule that never reaches a student.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SourceId> CompanionSourceIds { get; private set; } = [];

    /// <summary>
    /// The sources that own the group rotation this source's rows defer to, whose
    /// published dates decide where the deferral still applies (ADR-126).
    /// </summary>
    /// <remarks>
    /// The Grade 2 annual workbooks state all three dissection hours of a session
    /// and the anatomy group lists assign each student one of them, so the annual
    /// rows are excluded — but only for the dates a group list has actually
    /// published. Naming those sources here is what lets the poller tell "the
    /// rotation is owned elsewhere and published" from "owned elsewhere and
    /// missing", which the workbook cannot say and the parser cannot see.
    /// <para>
    /// Empty means this source defers unconditionally, which is what every source
    /// but the Grade 2 annual workbooks does. It is separate from
    /// <see cref="CompanionSourceIds"/>: a companion's document is read by this
    /// parse, while a rotation owner's *published result* is only consulted.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SourceId> GroupRotationSourceIds { get; private set; } = [];

    public bool IsPollingEnabled { get; private set; }

    /// <summary>When the source was last successfully polled, changed or not.</summary>
    public DateTimeOffset? LastPolledAtUtc { get; private set; }

    /// <summary>When the source last produced content that differed from the previous poll.</summary>
    public DateTimeOffset? LastChangedAtUtc { get; private set; }

    /// <summary>
    /// When the last attempt to acquire this source's document failed, if the attempt after it has
    /// not yet succeeded (ADR-137).
    /// </summary>
    /// <remarks>
    /// A source whose document cannot be acquired produces no snapshot, no parse run and no
    /// revision, so before this existed the only trace of the failure was a line in the host's
    /// journal and a <see cref="LastPolledAtUtc"/> that quietly stopped advancing. The three Grade
    /// 3 annual workbooks failed that way for four days — their files had been moved to the Drive
    /// trash — and the first anyone noticed was a student's calendar missing its rooms.
    /// </remarks>
    public DateTimeOffset? LastPollFailureAtUtc { get; private set; }

    /// <summary>Why that attempt failed, in the words the acquirer used.</summary>
    public string? LastPollFailureReason { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public void RecordPolled(DateTimeOffset polledAtUtc, bool changed)
    {
        LastPolledAtUtc = polledAtUtc;
        if (changed)
        {
            LastChangedAtUtc = polledAtUtc;
        }

        // A success clears the failure rather than leaving it beside a newer poll time. Two
        // timestamps that both look current, one of them stale, is how a screen ends up saying
        // both that the source is healthy and that it is broken.
        LastPollFailureAtUtc = null;
        LastPollFailureReason = null;
    }

    /// <summary>Records that the document could not be acquired, leaving the last success intact.</summary>
    /// <remarks>
    /// The previous successful poll is deliberately not cleared: "last read four days ago, failing
    /// since" is the sentence an operator needs, and it takes both facts to say it.
    /// </remarks>
    public void RecordPollFailure(DateTimeOffset failedAtUtc, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        LastPollFailureAtUtc = failedAtUtc;
        LastPollFailureReason = reason.Length > MaximumPollFailureReasonLength
            ? reason[..MaximumPollFailureReasonLength]
            : reason;
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
