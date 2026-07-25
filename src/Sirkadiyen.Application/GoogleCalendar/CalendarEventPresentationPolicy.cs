using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Owns the visible, provider-specific presentation of canonical schedule records.
/// Canonical truth stays source-faithful; Calendar labels, concise theory titles and
/// human-readable description labels are derived here for every class year.
/// </summary>
public static partial class CalendarEventPresentationPolicy
{
    private const string AnatomyKey = "department:anatomi";
    private const string PhysiologyKey = "department:fizyoloji";
    private const string BiochemistryKey = "department:tibbi-biyokimya";
    private const string MedicalBiologyKey = "department:tibbi-biyoloji";
    private const string HistologyKey = "department:histoloji-ve-embriyoloji";
    private const string BiophysicsKey = "department:biyofizik";
    private const string MedicalMicrobiologyKey = "department:tibbi-mikrobiyoloji";

    public static string Summary(CanonicalScheduleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.DisplayTitle;
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

    public static ManagedCalendarEventLabel EventLabel(CanonicalScheduleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        PresentationCategory category = ResolveCategory(record);
        return new ManagedCalendarEventLabel
        {
            Id = DeterministicLabelId(category.Key),
            Name = Truncate(category.Name, 50),
            BackgroundColor = category.BackgroundColor ?? DerivedColor(category.Key),
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

        if (record.Departments.Count == 1)
        {
            return DepartmentCategory(record.Departments[0]);
        }

        if (record.Departments.Count > 1)
        {
            string joined = string.Join("|", record.Departments.Select(Normalize));
            return new($"departments:{joined}", "Entegre oturum", null);
        }

        string normalizedTitle = Normalize(record.DisplayTitle);
        if (TryKnownDepartment(normalizedTitle, out PresentationCategory inferred))
        {
            return inferred;
        }

        if (record.EventType is ScheduleEventType.AnatomyPractice)
        {
            return KnownDepartment(AnatomyKey);
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
        if (TryKnownDepartment(normalized, out PresentationCategory known))
        {
            return known;
        }

        return new($"department:{normalized}", name, null);
    }

    private static bool TryKnownDepartment(
        string normalized,
        out PresentationCategory category)
    {
        string? key = normalized switch
        {
            _ when normalized.Contains("histoloji", StringComparison.Ordinal)
                || normalized.Contains("embriyoloji", StringComparison.Ordinal)
                || normalized.Contains("embiriyoloji", StringComparison.Ordinal)
                || normalized.Contains("histology", StringComparison.Ordinal)
                || normalized.Contains("embryology", StringComparison.Ordinal) => HistologyKey,
            _ when normalized.Contains("tibbi biyokimya", StringComparison.Ordinal)
                || normalized.Contains("medical biochemistry", StringComparison.Ordinal)
                || normalized.Contains("biyokimya", StringComparison.Ordinal) => BiochemistryKey,
            _ when normalized.Contains("tibbi biyoloji", StringComparison.Ordinal)
                || normalized.Contains("medical biology", StringComparison.Ordinal) => MedicalBiologyKey,
            _ when normalized.Contains("biyofizik", StringComparison.Ordinal)
                || normalized.Contains("biophysics", StringComparison.Ordinal) => BiophysicsKey,
            _ when normalized.Contains("mikrobiyoloji", StringComparison.Ordinal)
                || normalized.Contains("microbiology", StringComparison.Ordinal) =>
                MedicalMicrobiologyKey,
            _ when normalized.Contains("fizyoloji", StringComparison.Ordinal)
                || normalized.Contains("physiology", StringComparison.Ordinal) => PhysiologyKey,
            _ when normalized.Contains("anatomi", StringComparison.Ordinal)
                || normalized.Contains("anatomy", StringComparison.Ordinal)
                || normalized.Contains("diseksiyon", StringComparison.Ordinal)
                || normalized.Contains("dissection", StringComparison.Ordinal) => AnatomyKey,
            _ => null,
        };

        if (key is null)
        {
            category = null!;
            return false;
        }

        category = KnownDepartment(key);
        return true;
    }

    private static PresentationCategory KnownDepartment(string key) => key switch
    {
        AnatomyKey => new(key, "Anatomi AD", "#D50000"),
        PhysiologyKey => new(key, "Fizyoloji AD", "#1A237E"),
        BiochemistryKey => new(key, "Tıbbi Biyokimya AD", "#F4511E"),
        MedicalBiologyKey => new(key, "Tıbbi Biyoloji AD", "#0B8043"),
        HistologyKey => new(key, "Histoloji ve Embriyoloji AD", "#8E24AA"),
        BiophysicsKey => new(key, "Biyofizik AD", "#F6BF26"),
        MedicalMicrobiologyKey => new(key, "Tıbbi Mikrobiyoloji AD", "#E67C73"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown department key."),
    };

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
