using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Sources;

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

    /// <summary>
    /// The audience selector values this source is allowed to state, keyed by
    /// selector dimension.
    /// </summary>
    /// <remarks>
    /// A source with no entry here has not declared its cohorts yet, and the
    /// unknown-selector rule is not enforced for it. That is deliberately
    /// different from an entry declaring an empty value list, which asserts the
    /// dimension may not appear at all. Until the ADR-027 supported-profile
    /// schema exists this is the only statement of which cohorts are real, so
    /// silence must not be read as "nothing is permitted".
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? SupportedAudienceSelectors
    {
        get;
        init;
    }

    /// <summary>
    /// The audience values this source is the authority for, keyed by selector
    /// dimension (ADR-110).
    /// </summary>
    /// <remarks>
    /// The Grade 3 A and B workbooks both state the sessions both halves of the class
    /// attend, each in its own wording, so neither copy can be recognized as the
    /// other's and a student receives the session twice. Naming the half each workbook
    /// owns makes exactly one of them publish it.
    /// <para>
    /// An absent dimension is not narrowed, so every source that declares nothing here
    /// keeps publishing exactly what it published before. This is narrower than
    /// <see cref="SupportedAudienceSelectors"/>, which says what the source may state
    /// at all.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? AuthoritativeAudienceSelectors
    {
        get;
        init;
    }

    /// <summary>
    /// Names the set of sources whose document is literally the same file, so one
    /// administrative upload becomes evidence for all of them (ADR-080).
    /// </summary>
    public string? SharedDocumentGroup { get; init; }

    /// <summary>
    /// Other source IDs whose latest snapshot is handed to this source's parser
    /// as supporting evidence (ADR-102).
    /// </summary>
    public IReadOnlyList<string>? CompanionSourceIds { get; init; }

    public string? FixturePath { get; init; }

    public string? Notes { get; init; }

    /// <summary>Projects the configured definition onto its persisted form.</summary>
    public ScheduleSource ToScheduleSource() => new(
        Domain.Scheduling.Sources.SourceId.Parse(SourceId),
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
        SheetGid,
        SupportedAudienceSelectors,
        SharedDocumentGroup,
        CompanionSourceIds
            ?.Select(Domain.Scheduling.Sources.SourceId.Parse)
            .ToArray(),
        AuthoritativeAudienceSelectors);
}
