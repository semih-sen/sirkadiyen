using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Application.Scheduling.Sources;

/// <summary>
/// Computes what one catalog document would change relative to another, and the hash that binds a
/// confirmation to that computation (ADR-114).
/// </summary>
/// <remarks>
/// This is deliberately a pure function of two documents. A textual diff would tell an operator
/// that a line moved; what they need to know is that <c>classYear</c> went from 3 to 4, which
/// hands one program's published lessons to a different cohort at the next dispatch. Every field
/// the pipeline reads is therefore compared by name and classified, and the ones that only a human
/// reads are classified apart so a rename is not dressed up as a dangerous operation.
/// </remarks>
public static class ScheduleSourceCatalogPlanner
{
    /// <summary>The exact bytes a submitted document turns into before it is hashed or written.</summary>
    /// <remarks>
    /// Line endings are normalized and a single trailing newline is enforced, so a document edited
    /// on Windows and one edited in the browser do not read as different catalogs. Nothing else is
    /// touched: the operator's formatting is theirs, and reserializing from the parsed model would
    /// silently drop anything the model does not know about.
    /// </remarks>
    public static string Normalize(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');
        return normalized + "\n";
    }

    public static string Hash(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    /// <summary>Binds a confirmation to the exact pair of documents the plan was computed from.</summary>
    public static string PlanHash(string baseContentHash, string proposedContentHash) =>
        Hash($"{baseContentHash}:{proposedContentHash}");

    public static ScheduleSourceCatalogPlan Plan(
        ScheduleSourceCatalog? current,
        string baseContentHash,
        ScheduleSourceCatalog proposed,
        string normalizedProposedContent)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentHash);
        ArgumentNullException.ThrowIfNull(normalizedProposedContent);

        Dictionary<string, ScheduleSourceDefinition> before =
            (current?.Sources ?? []).ToDictionary(
                static source => source.SourceId,
                StringComparer.Ordinal);
        Dictionary<string, ScheduleSourceDefinition> after = proposed.Sources.ToDictionary(
            static source => source.SourceId,
            StringComparer.Ordinal);

        List<ScheduleSourceCatalogSourceChange> added = [];
        List<ScheduleSourceCatalogSourceChange> removed = [];
        List<ScheduleSourceCatalogSourceChange> modified = [];
        int unchanged = 0;

        foreach (string sourceId in after.Keys.Order(StringComparer.Ordinal))
        {
            ScheduleSourceDefinition source = after[sourceId];
            if (!before.TryGetValue(sourceId, out ScheduleSourceDefinition? previous))
            {
                added.Add(Change(source, ScheduleSourceCatalogChangeKind.Added, [], isHighRisk: true));
                continue;
            }

            IReadOnlyList<ScheduleSourceCatalogFieldChange> fields = FieldChanges(previous, source);
            if (fields.Count == 0)
            {
                unchanged++;
                continue;
            }

            modified.Add(Change(
                source,
                ScheduleSourceCatalogChangeKind.Modified,
                fields,
                fields.Any(field => field.Risk is ScheduleSourceCatalogChangeRisk.High)));
        }

        foreach (string sourceId in before.Keys.Order(StringComparer.Ordinal))
        {
            if (!after.ContainsKey(sourceId))
            {
                removed.Add(Change(
                    before[sourceId],
                    ScheduleSourceCatalogChangeKind.Removed,
                    [],
                    isHighRisk: true));
            }
        }

        string proposedHash = Hash(normalizedProposedContent);
        return new ScheduleSourceCatalogPlan
        {
            PlanHash = PlanHash(baseContentHash, proposedHash),
            BaseContentHash = baseContentHash,
            ProposedContentHash = proposedHash,
            NormalizedContent = normalizedProposedContent,
            SourceCount = proposed.Sources.Count,
            Added = added,
            Removed = removed,
            Modified = modified,
            UnchangedCount = unchanged,
            Warnings = Warnings(added, removed, modified, current, proposed),
        };
    }

    private static ScheduleSourceCatalogSourceChange Change(
        ScheduleSourceDefinition source,
        ScheduleSourceCatalogChangeKind kind,
        IReadOnlyList<ScheduleSourceCatalogFieldChange> fields,
        bool isHighRisk) => new()
        {
            SourceId = source.SourceId,
            DisplayName = source.DisplayName,
            Program = $"Dönem {source.ClassYear} · {source.ProgramLanguage} · {source.AcademicYear}",
            Kind = kind,
            Fields = fields,
            IsHighRisk = isHighRisk,
        };

    /// <summary>
    /// Every field of a source definition, compared by name.
    /// </summary>
    /// <remarks>
    /// Listed explicitly rather than reflected over, because the classification is the point: a
    /// new field added to the definition should force whoever adds it to decide whether changing
    /// it can move a lesson between students. Reflection would default that decision to "low".
    /// </remarks>
    private static IReadOnlyList<ScheduleSourceCatalogFieldChange> FieldChanges(
        ScheduleSourceDefinition before,
        ScheduleSourceDefinition after)
    {
        List<ScheduleSourceCatalogFieldChange> changes = [];

        Compare("displayName", before.DisplayName, after.DisplayName, ScheduleSourceCatalogChangeRisk.Low);
        Compare("notes", before.Notes, after.Notes, ScheduleSourceCatalogChangeRisk.Low);
        Compare("fixturePath", before.FixturePath, after.FixturePath, ScheduleSourceCatalogChangeRisk.Low);

        Compare("transport", before.Transport.ToString(), after.Transport.ToString());
        Compare("documentFormat", before.DocumentFormat.ToString(), after.DocumentFormat.ToString());
        Compare("sourceUri", before.SourceUri.ToString(), after.SourceUri.ToString());
        Compare("externalId", before.ExternalId, after.ExternalId);
        Compare("sheetGid", Text(before.SheetGid), Text(after.SheetGid));
        Compare("discoveryFolderId", before.DiscoveryFolderId, after.DiscoveryFolderId);
        Compare("parserProfile", before.ParserProfile, after.ParserProfile);
        Compare("parserProfileVersion", before.ParserProfileVersion, after.ParserProfileVersion);
        Compare("academicYear", before.AcademicYear, after.AcademicYear);
        Compare("classYear", Text(before.ClassYear), Text(after.ClassYear));
        Compare("programLanguage", before.ProgramLanguage.ToString(), after.ProgramLanguage.ToString());
        Compare("timeZoneId", before.TimeZoneId, after.TimeZoneId);
        Compare("sharedDocumentGroup", before.SharedDocumentGroup, after.SharedDocumentGroup);
        Compare(
            "companionSourceIds",
            Text(before.CompanionSourceIds),
            Text(after.CompanionSourceIds));
        Compare(
            "supportedAudienceSelectors",
            Text(before.SupportedAudienceSelectors),
            Text(after.SupportedAudienceSelectors));
        Compare(
            "authoritativeAudienceSelectors",
            Text(before.AuthoritativeAudienceSelectors),
            Text(after.AuthoritativeAudienceSelectors));
        Compare(
            "groupRotationSourceIds",
            Text(before.GroupRotationSourceIds),
            Text(after.GroupRotationSourceIds));

        return changes;

        void Compare(
            string field,
            string? previous,
            string? current,
            ScheduleSourceCatalogChangeRisk risk = ScheduleSourceCatalogChangeRisk.High)
        {
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                return;
            }

            changes.Add(new ScheduleSourceCatalogFieldChange
            {
                Field = field,
                Before = previous,
                After = current,
                Risk = risk,
            });
        }
    }

    private static string? Text(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Text(IReadOnlyList<string>? values) =>
        values is null ? null : string.Join(", ", values);

    /// <summary>Renders a selector map so two of them can be compared and read as text.</summary>
    private static string? Text(IReadOnlyDictionary<string, IReadOnlyList<string>>? selectors)
    {
        if (selectors is null)
        {
            return null;
        }

        return string.Join(
            "; ",
            selectors
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => $"{entry.Key}: [{string.Join(", ", entry.Value)}]"));
    }

    /// <summary>
    /// The consequences an operator must have read before confirming.
    /// </summary>
    /// <remarks>
    /// These are not validation failures — every one of them describes a legitimate change someone
    /// may need to make. They exist because the consequence is not visible in the field diff: that
    /// a removed source stops being polled but keeps everything it published, that a re-targeted
    /// source hands its lessons to a different cohort at the next dispatch, and that a parser
    /// profile change reinterprets the same document.
    /// </remarks>
    private static IReadOnlyList<ScheduleSourceCatalogWarning> Warnings(
        IReadOnlyList<ScheduleSourceCatalogSourceChange> added,
        IReadOnlyList<ScheduleSourceCatalogSourceChange> removed,
        IReadOnlyList<ScheduleSourceCatalogSourceChange> modified,
        ScheduleSourceCatalog? current,
        ScheduleSourceCatalog proposed)
    {
        List<ScheduleSourceCatalogWarning> warnings = [];

        if (current is null)
        {
            warnings.Add(new ScheduleSourceCatalogWarning
            {
                Code = "baseline-unreadable",
                Message = "Diskteki katalog okunamadığı veya geçersiz olduğu için alan bazlı "
                    + "karşılaştırma yapılamadı. Gönderilen belge bütünüyle yeni katalog olarak "
                    + "yazılacak.",
                Risk = ScheduleSourceCatalogChangeRisk.High,
            });
        }

        if (removed.Count > 0)
        {
            warnings.Add(new ScheduleSourceCatalogWarning
            {
                Code = "sources-removed",
                Message = $"{string.Join(", ", removed.Select(source => source.SourceId))} artık "
                    + "katalogda yok. Bu kaynakların pollingi kapatılır; veritabanı satırları, "
                    + "yayınlanmış dersleri ve takvim kayıtları silinmez.",
                Risk = ScheduleSourceCatalogChangeRisk.High,
            });
        }

        if (added.Count > 0)
        {
            warnings.Add(new ScheduleSourceCatalogWarning
            {
                Code = "sources-added",
                Message = $"{string.Join(", ", added.Select(source => source.SourceId))} yeni "
                    + "kaynak olarak eklenecek ve bir sonraki döngüde poll edilmeye başlanacak.",
                Risk = ScheduleSourceCatalogChangeRisk.High,
            });
        }

        IReadOnlyList<string> retargeted =
        [
            .. modified
                .Where(source => source.Fields.Any(field =>
                    field.Field is "academicYear" or "classYear" or "programLanguage"))
                .Select(static source => source.SourceId),
        ];
        if (retargeted.Count > 0)
        {
            warnings.Add(new ScheduleSourceCatalogWarning
            {
                Code = "audience-retargeted",
                Message = $"{string.Join(", ", retargeted)} kaynağının hedef kitlesi değişiyor. "
                    + "Bu kaynağın hâlihazırda yayınlanmış dersleri bir sonraki dağıtımda başka "
                    + "bir öğrenci grubuna gider; eski gruptaki takvim etkinlikleri kendiliğinden "
                    + "silinmez, bunun için takvim düzeltmesi çalıştırılmalıdır.",
                Risk = ScheduleSourceCatalogChangeRisk.High,
            });
        }

        IReadOnlyList<string> reparsed =
        [
            .. modified
                .Where(source => source.Fields.Any(field =>
                    field.Field is "parserProfile" or "parserProfileVersion"))
                .Select(static source => source.SourceId),
        ];
        if (reparsed.Count > 0)
        {
            warnings.Add(new ScheduleSourceCatalogWarning
            {
                Code = "parser-changed",
                Message = $"{string.Join(", ", reparsed)} kaynağı bundan sonra farklı bir parser "
                    + "profiliyle okunacak. Aynı belge farklı yorumlanabilir; ortaya çıkan "
                    + "revizyon her zamanki gibi doğrulamadan ve incelemeden geçer.",
                Risk = ScheduleSourceCatalogChangeRisk.High,
            });
        }

        if (current is not null
            && !string.Equals(current.CatalogVersion, proposed.CatalogVersion, StringComparison.Ordinal))
        {
            warnings.Add(new ScheduleSourceCatalogWarning
            {
                Code = "catalog-version-changed",
                Message = $"Katalog sürümü {current.CatalogVersion} → {proposed.CatalogVersion} "
                    + "olarak değişiyor.",
                Risk = ScheduleSourceCatalogChangeRisk.High,
            });
        }

        return warnings;
    }
}
