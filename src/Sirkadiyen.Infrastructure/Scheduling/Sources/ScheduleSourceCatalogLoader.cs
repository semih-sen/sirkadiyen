using System.Text.Json;
using System.Text.Json.Serialization;
using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Infrastructure.Scheduling.Sources;

public sealed class ScheduleSourceCatalogLoader : IScheduleSourceCatalogSerializer
{
    /// <summary>
    /// How an administratively uploaded source names itself, since it has no
    /// location to be named by (ADR-079).
    /// </summary>
    public const string AdministrativeUploadUriPrefix = "urn:sirkadiyen:upload:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // A property the model does not know is refused rather than dropped. Silent tolerance was
        // survivable while only a deployment could edit this file and a reviewer read the diff;
        // once it is editable from the admin panel (ADR-114), a mistyped "parserProfileVersion"
        // would deserialize to nothing, validate cleanly, and leave the source parsed by the old
        // profile with no sign that the edit did not take (AI_GUIDELINE §9).
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<ScheduleSourceCatalog> LoadAsync(
        string catalogPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        string content = await File.ReadAllTextAsync(catalogPath, cancellationToken);
        return Parse(content);
    }

    /// <summary>
    /// Parses and validates a catalog document held in memory, applying exactly the rules
    /// <see cref="LoadAsync"/> applies to the deployed file.
    /// </summary>
    /// <remarks>
    /// The admin panel validates a proposed catalog through this method, so a document it accepts
    /// is a document the worker can start on. Every failure is translated into
    /// <see cref="ScheduleSourceCatalogValidationException"/>, because the caller here is an
    /// administrator typing JSON, not a misconfigured deployment.
    /// </remarks>
    public ScheduleSourceCatalog Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        ScheduleSourceCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<ScheduleSourceCatalog>(content, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new ScheduleSourceCatalogValidationException(exception.Message);
        }

        if (catalog is null)
        {
            throw new ScheduleSourceCatalogValidationException(
                "The schedule source catalog is empty.");
        }

        try
        {
            Validate(catalog);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or FormatException)
        {
            throw new ScheduleSourceCatalogValidationException(exception.Message);
        }

        return catalog;
    }

    private static void Validate(ScheduleSourceCatalog catalog)
    {
        if (!string.Equals(catalog.CatalogVersion, "1.0", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported schedule source catalog version '{catalog.CatalogVersion}'.");
        }

        HashSet<string> sourceIds = new(StringComparer.Ordinal);
        foreach (ScheduleSourceDefinition source in catalog.Sources)
        {
            ValidateRequiredText(source.SourceId, nameof(source.SourceId));
            ValidateRequiredText(source.DisplayName, nameof(source.DisplayName));
            ValidateRequiredText(source.ParserProfile, nameof(source.ParserProfile));
            ValidateRequiredText(source.ParserProfileVersion, nameof(source.ParserProfileVersion));
            ValidateRequiredText(source.AcademicYear, nameof(source.AcademicYear));
            ValidateRequiredText(source.TimeZoneId, nameof(source.TimeZoneId));

            if (source.ClassYear is < 1 or > 6)
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' states an unsupported class year "
                    + $"{source.ClassYear}.");
            }

            if (!sourceIds.Add(source.SourceId))
            {
                throw new InvalidDataException($"Duplicate source ID '{source.SourceId}'.");
            }

            ValidateSourceUri(source);

            if (source.Transport is ScheduleSourceTransport.GoogleSheets)
            {
                ValidateGoogleSheet(source);
            }
            else if (string.IsNullOrWhiteSpace(source.ExternalId)
                && source.Transport is ScheduleSourceTransport.GoogleDriveFile)
            {
                throw new InvalidDataException(
                    $"Google Drive source '{source.SourceId}' requires an external file ID.");
            }

            if (source.FixturePath is { } fixturePath && Path.IsPathRooted(fixturePath))
            {
                throw new InvalidDataException(
                    $"Fixture path for source '{source.SourceId}' must be repository-relative.");
            }

            ValidateSupportedAudienceSelectors(source);
        }

        ValidateSharedDocumentGroups(catalog);
        ValidateCompanionSourceIds(catalog, sourceIds);
        ValidateGroupRotationSourceIds(catalog, sourceIds);
        ValidateAudienceOwnership(catalog);
    }

    /// <summary>
    /// Requires the sources that split one program's audience between them to own exactly one
    /// share each, covering the whole of it (ADR-110).
    /// </summary>
    /// <remarks>
    /// Ownership is what stops two workbooks publishing the same joint session twice, and it is
    /// declared per source. Nothing at runtime can notice that the declarations do not fit
    /// together: a value owned twice reinstates the duplicate this rule exists to prevent, a value
    /// owned by nobody silently unpublishes every row that names it, and a sibling that forgot to
    /// declare authority at all keeps publishing the whole audience beside a sibling that narrowed.
    /// All three look like ordinary output. Catalog load is the last point at which they are
    /// visible, so it fails here rather than letting a student's calendar be the detector.
    /// <para>
    /// The rule engages per program and parser profile, because that is what a shared audience is:
    /// the two Grade 3 Turkish annual workbooks divide one class between them, while the Grade 3
    /// English annual and the faculty-practice sources are not dividing anything and declare no
    /// authority. A group of sources where none claims authority is left alone entirely.
    /// </para>
    /// </remarks>
    private static void ValidateAudienceOwnership(ScheduleSourceCatalog catalog)
    {
        IEnumerable<IGrouping<(string, int, ProgramLanguage, string), ScheduleSourceDefinition>>
            peerGroups = catalog.Sources.GroupBy(source => (
                source.AcademicYear,
                source.ClassYear,
                source.ProgramLanguage,
                source.ParserProfile));

        foreach (IGrouping<(string, int, ProgramLanguage, string), ScheduleSourceDefinition> peers
            in peerGroups)
        {
            foreach (string dimension in ClaimedDimensions(peers))
            {
                ValidateDimensionOwnership(peers, dimension);
            }
        }
    }

    /// <summary>Every dimension at least one peer claims authority over.</summary>
    private static IReadOnlyList<string> ClaimedDimensions(
        IEnumerable<ScheduleSourceDefinition> peers) =>
        [.. peers
            .SelectMany(source =>
                source.AuthoritativeAudienceSelectors?.Keys ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static void ValidateDimensionOwnership(
        IGrouping<(string AcademicYear, int ClassYear, ProgramLanguage Language, string Profile),
            ScheduleSourceDefinition> peers,
        string dimension)
    {
        string program =
            $"{peers.Key.AcademicYear} year {peers.Key.ClassYear} "
            + $"{peers.Key.Language} '{peers.Key.Profile}'";

        // Every value any peer says it may state is a value some peer has to publish.
        HashSet<string> supported = new(StringComparer.Ordinal);
        foreach (ScheduleSourceDefinition source in peers)
        {
            if (source.SupportedAudienceSelectors?.TryGetValue(
                    dimension,
                    out IReadOnlyList<string>? values) is true)
            {
                supported.UnionWith(values);
            }
        }

        Dictionary<string, string> ownerBySelector = new(StringComparer.Ordinal);
        foreach (ScheduleSourceDefinition source in peers)
        {
            if (source.AuthoritativeAudienceSelectors?.TryGetValue(
                    dimension,
                    out IReadOnlyList<string>? owned) is not true)
            {
                // A peer that narrows nothing while its sibling narrows publishes the sibling's
                // share as well, which is the duplicate all of this exists to remove.
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' claims no authority over selector dimension "
                    + $"'{dimension}', but a source it shares {program} with does. Every source "
                    + "dividing one audience must declare its own share.");
            }

            foreach (string value in owned)
            {
                if (ownerBySelector.TryGetValue(value, out string? existing))
                {
                    throw new InvalidDataException(
                        $"Sources '{existing}' and '{source.SourceId}' both claim authority over "
                        + $"selector value '{value}' in dimension '{dimension}' for {program}. "
                        + "Exactly one source may publish each share of an audience.");
                }

                ownerBySelector[value] = source.SourceId;
            }
        }

        foreach (string value in supported.Order(StringComparer.Ordinal))
        {
            if (!ownerBySelector.ContainsKey(value))
            {
                throw new InvalidDataException(
                    $"No source claims authority over selector value '{value}' in dimension "
                    + $"'{dimension}' for {program}, so every row addressing it would be "
                    + "published by nobody.");
            }
        }
    }

    /// <summary>
    /// Rejects a companion declaration that would silently read nothing, or read
    /// something twice (ADR-102).
    /// </summary>
    /// <remarks>
    /// A companion is optional evidence, and the pipeline degrades when it has
    /// never been acquired — which is exactly why a mistyped identifier here can
    /// never be noticed at runtime. It would look identical to a companion that
    /// simply has not been polled yet, and the bedside topics would be missing
    /// from every event forever without a single warning. Catalog load is the
    /// only place the mistake is still visible, so it fails here.
    /// <para>
    /// A companion that itself declares companions is refused too. Evidence is
    /// one level deep by construction: resolving a chain would raise questions
    /// about cycles and about whose fingerprint covers what, and no source needs
    /// it.
    /// </para>
    /// </remarks>
    private static void ValidateCompanionSourceIds(
        ScheduleSourceCatalog catalog,
        HashSet<string> sourceIds)
    {
        Dictionary<string, ScheduleSourceDefinition> byId = catalog.Sources.ToDictionary(
            static source => source.SourceId,
            StringComparer.Ordinal);

        foreach (ScheduleSourceDefinition source in catalog.Sources)
        {
            if (source.CompanionSourceIds is not { } companions)
            {
                continue;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string companionId in companions)
            {
                if (string.Equals(companionId, source.SourceId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names itself as its own companion.");
                }

                if (!sourceIds.Contains(companionId))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names companion '{companionId}', which is "
                        + "not a source in this catalog.");
                }

                if (!seen.Add(companionId))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names companion '{companionId}' twice.");
                }

                if (byId[companionId].CompanionSourceIds is { Count: > 0 })
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names companion '{companionId}', which "
                        + "declares companions of its own; companion evidence is one level deep.");
                }
            }
        }
    }

    /// <summary>
    /// Rejects a group-rotation owner that could not answer for the source that
    /// names it (ADR-126).
    /// </summary>
    /// <remarks>
    /// A named owner decides which of this source's rotation rows stay deferred,
    /// so naming the wrong one is not a missing enrichment but a wrong calendar:
    /// an owner that never publishes for this program would leave every hour
    /// published forever, and an owner for another class year or language would
    /// silence dissection for students it says nothing about. Both are visible
    /// here and nowhere later, so both fail here.
    /// <para>
    /// The academic year is deliberately not compared. An owner still pointing at
    /// last year's document is the ordinary state after a rollover — the Grade 2
    /// annual workbooks moved to 2026-2027 while the anatomy group lists had not
    /// (ADR-115) — and it needs no catalog change: coverage is read per program,
    /// so an owner publishing another year simply covers nothing and the fallback
    /// stays on until this year's list is uploaded.
    /// </para>
    /// </remarks>
    private static void ValidateGroupRotationSourceIds(
        ScheduleSourceCatalog catalog,
        HashSet<string> sourceIds)
    {
        Dictionary<string, ScheduleSourceDefinition> byId = catalog.Sources.ToDictionary(
            static source => source.SourceId,
            StringComparer.Ordinal);

        foreach (ScheduleSourceDefinition source in catalog.Sources)
        {
            if (source.GroupRotationSourceIds is not { } owners)
            {
                continue;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string ownerId in owners)
            {
                if (string.Equals(ownerId, source.SourceId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names itself as the owner of its own "
                        + "group rotation.");
                }

                if (!sourceIds.Contains(ownerId))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names group-rotation owner '{ownerId}', "
                        + "which is not a source in this catalog.");
                }

                if (!seen.Add(ownerId))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names group-rotation owner '{ownerId}' "
                        + "twice.");
                }

                ScheduleSourceDefinition owner = byId[ownerId];
                if (owner.ClassYear != source.ClassYear
                    || owner.ProgramLanguage != source.ProgramLanguage)
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' names group-rotation owner '{ownerId}', "
                        + "which publishes for another class year or program language and so "
                        + "cannot answer for this source's rotation.");
                }
            }
        }
    }

    /// <summary>
    /// Rejects a shared-document group that would make one upload do the wrong
    /// thing (ADR-080).
    /// </summary>
    /// <remarks>
    /// A group exists so a single administrative upload becomes evidence for every
    /// source the document serves. Three ways of getting that wrong are refused
    /// here rather than discovered in production:
    /// <list type="bullet">
    /// <item>A group on a fetched source. Each fetched source acquires its own
    /// copy from its own URL, so a group there would claim a sharing the pipeline
    /// does not perform.</item>
    /// <item>A group of one. That is what a mistyped group name looks like, and it
    /// silently reduces the fan-out to the source the administrator happened to
    /// upload to.</item>
    /// <item>Two members serving the same class year and program language. One
    /// upload would then produce two revisions for one audience, and every lesson
    /// in the document would reach those students twice.</item>
    /// </list>
    /// Members must also agree on document format, since one file is converted by
    /// one reader.
    /// </remarks>
    private static void ValidateSharedDocumentGroups(ScheduleSourceCatalog catalog)
    {
        Dictionary<string, List<ScheduleSourceDefinition>> groups = new(StringComparer.Ordinal);
        foreach (ScheduleSourceDefinition source in catalog.Sources)
        {
            if (source.SharedDocumentGroup is not { } group)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(group))
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' declares a blank shared document group.");
            }

            if (source.Transport is not ScheduleSourceTransport.AdministrativeUpload)
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' declares shared document group '{group}' but is "
                    + "not administratively uploaded; a fetched source acquires its own copy.");
            }

            if (!groups.TryGetValue(group, out List<ScheduleSourceDefinition>? members))
            {
                members = [];
                groups[group] = members;
            }

            members.Add(source);
        }

        foreach ((string group, List<ScheduleSourceDefinition> members) in groups)
        {
            if (members.Count < 2)
            {
                throw new InvalidDataException(
                    $"Shared document group '{group}' has one member, '{members[0].SourceId}'. A "
                    + "group of one is a mistyped group name, not a shared document.");
            }

            ScheduleDocumentFormat format = members[0].DocumentFormat;
            HashSet<(int ClassYear, ProgramLanguage Language)> audiences = [];
            foreach (ScheduleSourceDefinition member in members)
            {
                if (member.DocumentFormat != format)
                {
                    throw new InvalidDataException(
                        $"Shared document group '{group}' mixes document formats; one file cannot "
                        + $"be both {format} and {member.DocumentFormat}.");
                }

                if (!audiences.Add((member.ClassYear, member.ProgramLanguage)))
                {
                    throw new InvalidDataException(
                        $"Shared document group '{group}' has two sources for class year "
                        + $"{member.ClassYear} {member.ProgramLanguage}, which would publish the "
                        + "same document to that audience twice.");
                }
            }
        }
    }

    /// <summary>
    /// Enforces that the source URI states what the transport can actually do.
    /// </summary>
    /// <remarks>
    /// A fetched source must name an absolute HTTPS location, because the worker
    /// reads it from there. An administratively uploaded document has no location
    /// at all, and giving it a plausible-looking URL would be a false provenance
    /// claim, so it names itself with a URN instead. The URN must spell out its
    /// own source ID: it is pure identity, and a copied entry that kept another
    /// source's URN would attach one document's evidence to the other (ADR-079).
    /// </remarks>
    private static void ValidateSourceUri(ScheduleSourceDefinition source)
    {
        if (source.Transport is ScheduleSourceTransport.AdministrativeUpload)
        {
            string expected = AdministrativeUploadUriPrefix + source.SourceId;
            if (!string.Equals(
                source.SourceUri.OriginalString,
                expected,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Administratively uploaded source '{source.SourceId}' must identify itself "
                    + $"as '{expected}'.");
            }

            return;
        }

        if (!source.SourceUri.IsAbsoluteUri || source.SourceUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                $"Source '{source.SourceId}' must use an absolute HTTPS URI.");
        }
    }

    /// <summary>
    /// Rejects a declared selector list that cannot mean what it appears to say.
    /// </summary>
    /// <remarks>
    /// A blank dimension or value, or the same value twice, would make the
    /// unknown-selector rule behave unpredictably rather than fail loudly, so the
    /// catalog refuses to load instead.
    /// </remarks>
    private static void ValidateSupportedAudienceSelectors(ScheduleSourceDefinition source)
    {
        ValidateSelectorMap(source, source.SupportedAudienceSelectors, "supported");
        ValidateSelectorMap(source, source.AuthoritativeAudienceSelectors, "authoritative");
        ValidateAuthorityWithinSupported(source);
    }

    private static void ValidateSelectorMap(
        ScheduleSourceDefinition source,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? declared,
        string kind)
    {
        if (declared is null)
        {
            return;
        }

        foreach ((string dimension, IReadOnlyList<string> values) in declared)
        {
            if (string.IsNullOrWhiteSpace(dimension))
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' declares a {kind} selector dimension "
                    + "with no name.");
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' declares a blank {kind} selector "
                        + $"value for dimension '{dimension}'.");
                }

                if (!seen.Add(value))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' declares {kind} selector value "
                        + $"'{value}' twice for dimension '{dimension}'.");
                }
            }
        }
    }

    /// <summary>
    /// Refuses a source claiming authority over a value it may not even state (ADR-110).
    /// </summary>
    /// <remarks>
    /// Authority narrows what a source publishes out of what it states, so a value outside
    /// its supported list would narrow to nothing and silently unpublish every row that
    /// names it. A source that declares no supported list has not declared its cohorts at
    /// all, and nothing can be checked against it.
    /// </remarks>
    private static void ValidateAuthorityWithinSupported(ScheduleSourceDefinition source)
    {
        if (source.AuthoritativeAudienceSelectors is not { } authoritative
            || source.SupportedAudienceSelectors is not { } supported)
        {
            return;
        }

        foreach ((string dimension, IReadOnlyList<string> values) in authoritative)
        {
            if (!supported.TryGetValue(dimension, out IReadOnlyList<string>? permitted))
            {
                throw new InvalidDataException(
                    $"Source '{source.SourceId}' claims authority over selector dimension "
                    + $"'{dimension}', which it does not declare as supported.");
            }

            foreach (string value in values)
            {
                if (!permitted.Contains(value, StringComparer.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Source '{source.SourceId}' claims authority over selector value "
                        + $"'{value}' in dimension '{dimension}', which it does not declare "
                        + "as supported.");
                }
            }
        }
    }

    private static void ValidateGoogleSheet(ScheduleSourceDefinition source)
    {
        if (source.DocumentFormat is not ScheduleDocumentFormat.GoogleSheet
            || string.IsNullOrWhiteSpace(source.ExternalId)
            || source.SheetGid is null or < 0)
        {
            throw new InvalidDataException(
                $"Google Sheets source '{source.SourceId}' requires a spreadsheet ID, gid, "
                + "and googleSheet document format.");
        }
    }

    private static void ValidateRequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Schedule source field '{fieldName}' is required.");
        }
    }
}
