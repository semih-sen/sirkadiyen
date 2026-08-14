using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Owns the visible, provider-specific presentation of canonical schedule records.
/// Canonical truth stays source-faithful; Calendar labels, concise theory titles and
/// human-readable description labels are derived here for every class year.
/// </summary>
public static partial class CalendarEventPresentationPolicy
{
    public static string Summary(CanonicalScheduleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.EventType is ScheduleEventType.AnatomyPractice)
        {
            return "DİSEKSİYON";
        }

        return IsPractice(record.EventType)
            ? $"UYGULAMA - {record.DisplayTitle.Trim().ToUpper(new CultureInfo("tr-TR"))}"
            : record.DisplayTitle;
    }

    public static string? Description(CanonicalScheduleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(record.Instructor))
        {
            lines.Add($"Öğretim üyesi: {record.Instructor}");
        }

        if (!string.IsNullOrWhiteSpace(record.CurriculumBlock))
        {
            lines.Add($"Dilim: {record.CurriculumBlock}");
        }

        if (record.Departments.Count > 0)
        {
            string label = record.Departments.Count == 1
                ? "Anabilim dalı"
                : "Anabilim dalları";
            lines.Add($"{label}: {string.Join(", ", record.Departments)}");
        }

        // The topic goes last and on its own paragraph: it is a sentence or two
        // of prose, while everything above it is a short labelled value, and a
        // student reading the event wants to know what the session is about
        // after they know whose it is (ADR-088).
        if (!string.IsNullOrWhiteSpace(record.Notes))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"Konu: {record.Notes}");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    public static string? Location(CanonicalScheduleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.Location))
        {
            return null;
        }

        string normalized = Normalize(record.Location);
        return normalized.Contains("amfi programina bakiniz", StringComparison.Ordinal)
            || normalized.Contains("amphitheatre program", StringComparison.Ordinal)
            || normalized.Contains("amphitheater program", StringComparison.Ordinal)
                ? null
                : record.Location.Trim();
    }

    public static ManagedCalendarEventLabel EventLabel(
        CanonicalScheduleRecord record,
        IReadOnlyDictionary<string, string>? departmentColors = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        PresentationCategory category = ResolveCategory(record);
        return new ManagedCalendarEventLabel
        {
            Id = DeterministicLabelId(category.Key),
            Name = Truncate(category.Name, 50),
            BackgroundColor = ResolveColor(category, departmentColors),
        };
    }

    private static PresentationCategory ResolveCategory(CanonicalScheduleRecord record)
    {
        if (record.EventType is ScheduleEventType.Exam)
        {
            return new("event:exam", "Sınavlar", "#616161");
        }

        if (record.EventType is ScheduleEventType.FreeStudy)
        {
            return new("event:free-study", "Serbest çalışma", "#039BE5");
        }

        if (IsPractice(record.EventType))
        {
            return new("event:practice", "Uygulamalar", "#FF6D00");
        }

        if (record.EventType is ScheduleEventType.IntegratedSession
            || record.Departments.Count > 1)
        {
            return new("event:integrated-session", "Entegre oturum", "#5E35B1");
        }

        if (record.Departments.Count == 1)
        {
            return DepartmentCategory(record.Departments[0]);
        }

        if (TryKnownDepartmentFromTitle(record.DisplayTitle, out PresentationCategory inferred))
        {
            return inferred;
        }

        string eventType = record.EventType.ToString();
        return new(
            $"event-type:{Normalize(eventType)}",
            EventTypeName(record.EventType),
            null);
    }

    private static PresentationCategory DepartmentCategory(string department)
    {
        string name = CleanDepartmentName(department);
        string normalized = Normalize(name);
        if (DepartmentCatalog.TryResolve(name, out DepartmentDefinition resolved))
        {
            return KnownDepartment(resolved.Key);
        }

        return new($"department:{normalized}", name, null);
    }

    private static bool TryKnownDepartmentFromTitle(
        string title,
        out PresentationCategory category)
    {
        if (!DepartmentCatalog.TryResolveFromTitle(title, out DepartmentDefinition department))
        {
            category = null!;
            return false;
        }

        category = KnownDepartment(department.Key);
        return true;
    }

    private static PresentationCategory KnownDepartment(string departmentKey)
    {
        if (!DepartmentCatalog.TryGet(departmentKey, out DepartmentDefinition department))
        {
            throw new ArgumentOutOfRangeException(
                nameof(departmentKey),
                departmentKey,
                "Unknown department key.");
        }

        return new(
            $"department:{department.Key}",
            $"{department.Name} AD",
            DepartmentCatalog.DefaultColor(department.Key));
    }

    private static string ResolveColor(
        PresentationCategory category,
        IReadOnlyDictionary<string, string>? departmentColors)
    {
        string? categoryColorKey = category.Key switch
        {
            "event:integrated-session" => CalendarPresentationColorCatalog.IntegratedSessionKey,
            "event:practice" => CalendarPresentationColorCatalog.PracticeKey,
            _ => null,
        };
        if (categoryColorKey is not null
            && departmentColors is not null
            && departmentColors.TryGetValue(categoryColorKey, out string? categoryColor))
        {
            return categoryColor;
        }

        const string prefix = "department:";
        if (category.Key.StartsWith(prefix, StringComparison.Ordinal)
            && departmentColors is not null
            && departmentColors.TryGetValue(category.Key[prefix.Length..], out string? configured))
        {
            return configured;
        }

        return category.BackgroundColor ?? DerivedColor(category.Key);
    }

    private static bool IsPractice(ScheduleEventType eventType) => eventType is
        ScheduleEventType.Practice
        or ScheduleEventType.AnatomyPractice
        or ScheduleEventType.BedsidePractice
        or ScheduleEventType.FacultyPractice
        or ScheduleEventType.VerticalCorridor;

    private static string EventTypeName(ScheduleEventType eventType) => eventType switch
    {
        ScheduleEventType.Practice => "Uygulama",
        ScheduleEventType.AnatomyPractice => "Anatomi uygulaması",
        ScheduleEventType.BedsidePractice => "Hasta başı uygulama",
        ScheduleEventType.FacultyPractice => "Fakülte uygulaması",
        ScheduleEventType.VerticalCorridor => "Dikey koridor",
        ScheduleEventType.IntegratedSession => "Entegre oturum",
        ScheduleEventType.Other => "Diğer",
        _ => "Teorik ders",
    };

    private static string CleanDepartmentName(string value)
    {
        string cleaned = value.Trim().TrimEnd('.');
        cleaned = DepartmentSuffix().Replace(cleaned, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? value.Trim() : cleaned;
    }

    private static string DeterministicLabelId(string key)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"sirkadiyen-label\n{key}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }

    private static string DerivedColor(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"sirkadiyen-color\n{key}"));
        double hue = ((hash[0] << 8) | hash[1]) / 65535d * 360d;
        return HslToHex(hue, 0.68d, 0.43d);
    }

    private static string HslToHex(double hue, double saturation, double lightness)
    {
        double chroma = (1d - Math.Abs((2d * lightness) - 1d)) * saturation;
        double segment = hue / 60d;
        double secondary = chroma * (1d - Math.Abs((segment % 2d) - 1d));
        (double red, double green, double blue) = segment switch
        {
            < 1d => (chroma, secondary, 0d),
            < 2d => (secondary, chroma, 0d),
            < 3d => (0d, chroma, secondary),
            < 4d => (0d, secondary, chroma),
            < 5d => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary),
        };

        double match = lightness - (chroma / 2d);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{ToByte(red + match):X2}{ToByte(green + match):X2}{ToByte(blue + match):X2}");
    }

    private static int ToByte(double component) =>
        (int)Math.Round(component * 255d, MidpointRounding.AwayFromZero);

    private static string Normalize(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char normalized = char.ToLowerInvariant(character) switch
            {
                'ı' => 'i',
                _ => char.ToLowerInvariant(character),
            };
            builder.Append(char.IsLetterOrDigit(normalized) ? normalized : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString().Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength].TrimEnd();

    [GeneratedRegex(@"\s+(?:A\.?\s*D\.?|ANABİLİM DALI)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DepartmentSuffix();

    private sealed record PresentationCategory(
        string Key,
        string Name,
        string? BackgroundColor);
}
