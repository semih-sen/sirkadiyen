using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// Computes what one roster catalog document would change relative to another, and the hash that
/// binds a confirmation to that computation (ADR-134).
/// </summary>
/// <remarks>
/// A pure function of two documents, for the reason
/// <see cref="Scheduling.Sources.ScheduleSourceCatalogPlanner"/> gives: a textual diff would say
/// that a line moved, when what the operator needs to know is that <c>studentNumberHeader</c> went
/// from <c>Öğrenci No</c> to <c>No</c> — after which every lookup in that cohort misses — or that a
/// value map now writes <c>B1</c> where it used to write <c>A1</c>, which is worse, because
/// nothing fails at all and the students it enrols simply receive another group's practicals.
/// </remarks>
public static class StudentRosterCatalogPlanner
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

    public static StudentRosterCatalogPlan Plan(
        StudentRosterCatalog? current,
        string baseContentHash,
        StudentRosterCatalog proposed,
        string normalizedProposedContent)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseContentHash);
        ArgumentNullException.ThrowIfNull(normalizedProposedContent);

        Dictionary<string, StudentRosterDefinition> before =
            (current?.Rosters ?? []).ToDictionary(
                static roster => roster.RosterId,
                StringComparer.Ordinal);
        Dictionary<string, StudentRosterDefinition> after = proposed.Rosters.ToDictionary(
            static roster => roster.RosterId,
            StringComparer.Ordinal);

        List<StudentRosterCatalogRosterChange> added = [];
        List<StudentRosterCatalogRosterChange> removed = [];
        List<StudentRosterCatalogRosterChange> modified = [];
        int unchanged = 0;

        foreach (string rosterId in after.Keys.Order(StringComparer.Ordinal))
        {
            StudentRosterDefinition roster = after[rosterId];
            if (!before.TryGetValue(rosterId, out StudentRosterDefinition? previous))
            {
                added.Add(Change(roster, StudentRosterCatalogChangeKind.Added, [], isHighRisk: true));
                continue;
            }

            IReadOnlyList<StudentRosterCatalogFieldChange> fields = FieldChanges(previous, roster);
            if (fields.Count == 0)
            {
                unchanged++;
                continue;
            }

            modified.Add(Change(
                roster,
                StudentRosterCatalogChangeKind.Modified,
                fields,
                fields.Any(field => field.Risk is StudentRosterCatalogChangeRisk.High)));
        }

        foreach (string rosterId in before.Keys.Order(StringComparer.Ordinal))
        {
            if (!after.ContainsKey(rosterId))
            {
                removed.Add(Change(
                    before[rosterId],
                    StudentRosterCatalogChangeKind.Removed,
                    [],
                    isHighRisk: true));
            }
        }

        string proposedHash = Hash(normalizedProposedContent);
        return new StudentRosterCatalogPlan
        {
            PlanHash = PlanHash(baseContentHash, proposedHash),
            BaseContentHash = baseContentHash,
            ProposedContentHash = proposedHash,
            NormalizedContent = normalizedProposedContent,
            RosterCount = proposed.Rosters.Count,
            Added = added,
            Removed = removed,
            Modified = modified,
            UnchangedCount = unchanged,
            Warnings = Warnings(added, removed, modified, current, proposed),
        };
    }

    private static StudentRosterCatalogRosterChange Change(
        StudentRosterDefinition roster,
        StudentRosterCatalogChangeKind kind,
        IReadOnlyList<StudentRosterCatalogFieldChange> fields,
        bool isHighRisk) => new()
        {
            RosterId = roster.RosterId,
            DisplayName = roster.DisplayName,
            Cohort = $"Dönem {roster.ClassYear} · {roster.ProgramLanguage} · {roster.AcademicYear}",
            Kind = kind,
            Fields = fields,
            IsHighRisk = isHighRisk,
        };

    /// <summary>
    /// Every field of a roster definition, compared by name.
    /// </summary>
    /// <remarks>
    /// Listed explicitly rather than reflected over, because the classification is the point: a
    /// new field added to the definition should force whoever adds it to decide whether changing
    /// it can put a student in the wrong cohort. Reflection would default that decision to "low".
    /// </remarks>
    private static IReadOnlyList<StudentRosterCatalogFieldChange> FieldChanges(
        StudentRosterDefinition before,
        StudentRosterDefinition after)
    {
        List<StudentRosterCatalogFieldChange> changes = [];

        Compare("displayName", before.DisplayName, after.DisplayName, StudentRosterCatalogChangeRisk.Low);
        Compare("notes", before.Notes, after.Notes, StudentRosterCatalogChangeRisk.Low);

        Compare("transport", before.Transport.ToString(), after.Transport.ToString());
        Compare("documentFormat", before.DocumentFormat.ToString(), after.DocumentFormat.ToString());
        Compare("sourceUri", before.SourceUri.ToString(), after.SourceUri.ToString());
        Compare("externalId", before.ExternalId, after.ExternalId);
        Compare("sheetGid", Text(before.SheetGid), Text(after.SheetGid));
        Compare("academicYear", before.AcademicYear, after.AcademicYear);
        Compare("classYear", Text(before.ClassYear), Text(after.ClassYear));
        Compare("programLanguage", before.ProgramLanguage.ToString(), after.ProgramLanguage.ToString());

        // The layout is compared field by field rather than as one blob, because "the layout
        // changed" is not a reviewable statement: a moved header row and a rewritten value map
        // have entirely different consequences, and only one of them fails loudly.
        Compare("layout.worksheetTitle", before.Layout.WorksheetTitle, after.Layout.WorksheetTitle);
        Compare("layout.headerRow", Text(before.Layout.HeaderRow), Text(after.Layout.HeaderRow));
        Compare(
            "layout.studentNumberHeader",
            before.Layout.StudentNumberHeader,
            after.Layout.StudentNumberHeader);
        Compare("layout.givenNameHeader", before.Layout.GivenNameHeader, after.Layout.GivenNameHeader);
        Compare(
            "layout.familyNameHeader",
            before.Layout.FamilyNameHeader,
            after.Layout.FamilyNameHeader);

        foreach (string dimension in Dimensions(before).Union(Dimensions(after), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            Compare(
                $"layout.dimensionColumns[{dimension}]",
                Text(Column(before, dimension)),
                Text(Column(after, dimension)));
        }

        return changes;

        void Compare(
            string field,
            string? previous,
            string? current,
            StudentRosterCatalogChangeRisk risk = StudentRosterCatalogChangeRisk.High)
        {
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                return;
            }

            changes.Add(new StudentRosterCatalogFieldChange
            {
                Field = field,
                Before = previous,
                After = current,
                Risk = risk,
            });
        }
    }

    private static IEnumerable<string> Dimensions(StudentRosterDefinition roster) =>
        roster.Layout.DimensionColumns.Select(static column => column.Dimension);

    private static StudentRosterDimensionColumn? Column(
        StudentRosterDefinition roster,
        string dimension) =>
        roster.Layout.DimensionColumns.FirstOrDefault(
            column => string.Equals(column.Dimension, dimension, StringComparison.Ordinal));

    /// <summary>
    /// Renders one dimension column so two of them can be compared and read as text.
    /// </summary>
    /// <remarks>
    /// The value map is written out in full and in key order. It is the part of a roster that can
    /// be wrong without anything failing, so an operator confirming a change to it has to be shown
    /// which stated value now means which profile value, not that "the map changed".
    /// </remarks>
    private static string? Text(StudentRosterDimensionColumn? column)
    {
        if (column is null)
        {
            return null;
        }

        string values = string.Join(
            ", ",
            column.ValueMap
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => $"{entry.Key}→{entry.Value}"));

        return $"sütun \"{column.Header}\""
            + (column.StatedOncePerMergedRun ? " (birleştirilmiş)" : string.Empty)
            + $": {values}";
    }

    private static string? Text(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The consequences an operator must have read before confirming.
    /// </summary>
    /// <remarks>
    /// These are not validation failures — every one of them describes a legitimate change someone
    /// may need to make. They exist because the consequence is not visible in the field diff: that
    /// a removed list leaves its cohort typing their own profile, that a re-targeted list answers
    /// for a different cohort, and above all that none of this revisits the profiles students have
    /// already saved.
    /// </remarks>
    private static IReadOnlyList<StudentRosterCatalogWarning> Warnings(
        IReadOnlyList<StudentRosterCatalogRosterChange> added,
        IReadOnlyList<StudentRosterCatalogRosterChange> removed,
        IReadOnlyList<StudentRosterCatalogRosterChange> modified,
        StudentRosterCatalog? current,
        StudentRosterCatalog proposed)
    {
        List<StudentRosterCatalogWarning> warnings = [];

        if (current is null)
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "baseline-unreadable",
                Message = "Diskteki katalog okunamadığı veya geçersiz olduğu için alan bazlı "
                    + "karşılaştırma yapılamadı. Gönderilen belge bütünüyle yeni katalog olarak "
                    + "yazılacak.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        if (removed.Count > 0)
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "rosters-removed",
                Message = $"{string.Join(", ", removed.Select(roster => roster.RosterId))} artık "
                    + "katalogda yok. Bu listenin kohortundaki öğrenciler kayıt sırasında "
                    + "numaralarıyla bulunamaz ve profillerini kendileri doldurur. Hâlihazırda "
                    + "kaydedilmiş profiller değişmez.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        if (added.Count > 0)
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "rosters-added",
                Message = $"{string.Join(", ", added.Select(roster => roster.RosterId))} yeni liste "
                    + "olarak eklenecek ve bir sonraki okumada öğrenci aramalarına dahil olacak.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        IReadOnlyList<string> retargeted =
        [
            .. modified
                .Where(roster => roster.Fields.Any(field =>
                    field.Field is "academicYear" or "classYear" or "programLanguage"))
                .Select(static roster => roster.RosterId),
        ];
        if (retargeted.Count > 0)
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "cohort-retargeted",
                Message = $"{string.Join(", ", retargeted)} listesinin kohortu değişiyor. Liste "
                    + "bundan sonra başka bir dönem, dil veya akademik yıl için cevap verir; "
                    + "listede bulunan öğrenciye önerilen dönem ve program bilgisi de bu yeni "
                    + "kohort olur. Daha önce bu listeden doldurulmuş profiller geri alınmaz.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        IReadOnlyList<string> relaid =
        [
            .. modified
                .Where(roster => roster.Fields.Any(field =>
                    field.Field.StartsWith("layout.", StringComparison.Ordinal)))
                .Select(static roster => roster.RosterId),
        ];
        if (relaid.Count > 0)
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "layout-changed",
                Message = $"{string.Join(", ", relaid)} listesi bundan sonra farklı bir yerleşimle "
                    + "okunacak. Yanlış başlık listeyi tümüyle okunamaz yapar ve bu hemen görülür; "
                    + "yanlış bir değer eşlemesi ise hiçbir hata vermeden öğrencileri başka gruba "
                    + "yazar. Değişen eşlemeleri satır satır okuyun.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        IReadOnlyList<string> redocumented =
        [
            .. modified
                .Where(roster => roster.Fields.Any(field =>
                    field.Field is "transport" or "documentFormat" or "sourceUri" or "externalId"
                        or "sheetGid"))
                .Select(static roster => roster.RosterId),
        ];
        if (redocumented.Count > 0)
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "document-changed",
                Message = $"{string.Join(", ", redocumented)} listesi artık başka bir belgeden "
                    + "okunacak. Yeni belgenin Sirkadiyen'in Google kimliğiyle okunabilir olması "
                    + "gerekir; okunamazsa arama, o kohort için 'liste okunamadı' der ve son "
                    + "başarılı okuma bir süre daha kullanılır.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        if (current is not null
            && !string.Equals(current.CatalogVersion, proposed.CatalogVersion, StringComparison.Ordinal))
        {
            warnings.Add(new StudentRosterCatalogWarning
            {
                Code = "catalog-version-changed",
                Message = $"Katalog sürümü {current.CatalogVersion} → {proposed.CatalogVersion} "
                    + "olarak değişiyor.",
                Risk = StudentRosterCatalogChangeRisk.High,
            });
        }

        return warnings;
    }
}
