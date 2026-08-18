using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// The code-owned identity catalog for Istanbul Faculty of Medicine departments.
/// Colors are configurable; department identity and aliases are deliberately reviewed code.
/// </summary>
public static partial class DepartmentCatalog
{
    public static IReadOnlyList<DepartmentDefinition> Departments { get; } =
    [
        // Temel Tıp Bilimleri
        D("anatomi", "Anatomi", DepartmentDivision.Basic, "#D50000", "anatomy", "diseksiyon", "dissection"),
        D("biyofizik", "Biyofizik", DepartmentDivision.Basic, "#F6BF26", "biophysics"),
        D("biyoistatistik", "Biyoistatistik", DepartmentDivision.Basic, null, "biostatistics", "biostatistic"),
        D("fizyoloji", "Fizyoloji", DepartmentDivision.Basic, "#1A237E", "physiology"),
        D("histoloji-ve-embriyoloji", "Histoloji ve Embriyoloji", DepartmentDivision.Basic, "#8E24AA", "histology and embryology", "histology & embryology", "histoloji embriyoloji", "histoloji", "embriyoloji", "embiriyoloji", "histology", "embryology"),
        D("tibbi-biyokimya", "Tıbbi Biyokimya", DepartmentDivision.Basic, "#F4511E", "medical biochemistry", "biyokimya", "biochemistry"),
        D("tibbi-biyoloji", "Tıbbi Biyoloji", DepartmentDivision.Basic, "#0B8043", "medical biology"),
        D("tibbi-mikrobiyoloji", "Tıbbi Mikrobiyoloji", DepartmentDivision.Basic, "#E67C73", "medical microbiology", "mikrobiyoloji", "microbiology"),
        D("tip-egitimi", "Tıp Eğitimi", DepartmentDivision.Basic, null, "medical education"),
        D("tip-tarihi-ve-etik", "Tıp Tarihi ve Etik", DepartmentDivision.Basic, null, "history of medicine and ethics", "medical history and ethics", "tip tarihi", "tibbi etik", "medical ethics"),

        // Dahili Tıp Bilimleri
        D("adli-tip", "Adli Tıp", DepartmentDivision.Internal, null, "forensic medicine"),
        D("aile-hekimligi", "Aile Hekimliği", DepartmentDivision.Internal, null, "family medicine"),
        D("cocuk-sagligi-ve-hastaliklari", "Çocuk Sağlığı ve Hastalıkları", DepartmentDivision.Internal, null, "pediatrics", "paediatrics", "child health and diseases", "cocuk hastaliklari", "ÇSvH", "ÇSH"),
        D("cocuk-ve-ergen-ruh-sagligi", "Çocuk ve Ergen Ruh Sağlığı ve Hastalıkları", DepartmentDivision.Internal, null, "child and adolescent psychiatry", "cocuk psikiyatrisi"),
        D("deri-ve-zuhrevi-hastaliklari", "Deri ve Zührevi Hastalıkları", DepartmentDivision.Internal, null, "dermatology and venereology", "dermatoloji", "dermatology"),
        D("fiziksel-tip-ve-rehabilitasyon", "Fiziksel Tıp ve Rehabilitasyon", DepartmentDivision.Internal, null, "physical medicine and rehabilitation", "ftr"),
        D("gogus-hastaliklari", "Göğüs Hastalıkları", DepartmentDivision.Internal, null, "chest diseases", "pulmonology", "respiratory medicine"),
        D("halk-sagligi", "Halk Sağlığı", DepartmentDivision.Internal, null, "public health"),
        D("ic-hastaliklari", "İç Hastalıkları", DepartmentDivision.Internal, null, "internal medicine", "dahiliye", "İç H."),
        D("infeksiyon-hastaliklari-ve-klinik-mikrobiyoloji", "İnfeksiyon Hastalıkları ve Klinik Mikrobiyoloji", DepartmentDivision.Internal, null, "infectious diseases and clinical microbiology", "enfeksiyon hastaliklari ve klinik mikrobiyoloji", "infectious diseases"),
        D("kardiyoloji", "Kardiyoloji", DepartmentDivision.Internal, null, "cardiology"),
        D("noroloji", "Nöroloji", DepartmentDivision.Internal, null, "neurology"),
        D("nukleer-tip", "Nükleer Tıp", DepartmentDivision.Internal, null, "nuclear medicine"),
        D("radyasyon-onkolojisi", "Radyasyon Onkolojisi", DepartmentDivision.Internal, null, "radiation oncology"),
        D("radyoloji", "Radyoloji", DepartmentDivision.Internal, null, "radiology"),
        D("ruh-sagligi-ve-hastaliklari", "Ruh Sağlığı ve Hastalıkları", DepartmentDivision.Internal, null, "psychiatry", "psikiyatri"),
        D("spor-hekimligi", "Spor Hekimliği", DepartmentDivision.Internal, null, "sports medicine"),
        D("sualti-hekimligi-ve-hiperbarik-tip", "Sualtı Hekimliği ve Hiperbarik Tıp", DepartmentDivision.Internal, null, "underwater and hyperbaric medicine", "sualti hekimligi", "hiperbarik tip", "hyperbaric medicine"),
        D("tibbi-ekoloji-ve-hidroklimatoloji", "Tıbbi Ekoloji ve Hidroklimatoloji", DepartmentDivision.Internal, null, "medical ecology and hydroclimatology", "hidroklimatoloji"),
        D("tibbi-farmakoloji", "Tıbbi Farmakoloji", DepartmentDivision.Internal, null, "medical pharmacology", "farmakoloji", "pharmacology"),
        D("tibbi-genetik", "Tıbbi Genetik", DepartmentDivision.Internal, null, "medical genetics", "genetik", "genetics"),

        // Cerrahi Tıp Bilimleri
        D("agiz-yuz-ve-cene-cerrahisi", "Ağız, Yüz ve Çene Cerrahisi", DepartmentDivision.Surgical, null, "oral and maxillofacial surgery", "agiz yuz cene cerrahisi"),
        D("anesteziyoloji-ve-reanimasyon", "Anesteziyoloji ve Reanimasyon", DepartmentDivision.Surgical, null, "anesthesiology and reanimation", "anaesthesiology and reanimation", "anestezi"),
        D("beyin-ve-sinir-cerrahisi", "Beyin ve Sinir Cerrahisi", DepartmentDivision.Surgical, null, "neurosurgery"),
        D("cocuk-cerrahisi", "Çocuk Cerrahisi", DepartmentDivision.Surgical, null, "pediatric surgery", "paediatric surgery"),
        D("genel-cerrahi", "Genel Cerrahi", DepartmentDivision.Surgical, null, "general surgery"),
        D("gogus-cerrahisi", "Göğüs Cerrahisi", DepartmentDivision.Surgical, null, "thoracic surgery", "chest surgery"),
        D("goz-hastaliklari", "Göz Hastalıkları", DepartmentDivision.Surgical, null, "ophthalmology", "goz"),
        D("kadin-hastaliklari-ve-dogum", "Kadın Hastalıkları ve Doğum", DepartmentDivision.Surgical, null, "obstetrics and gynecology", "obstetrics and gynaecology", "kadin dogum"),
        D("kalp-ve-damar-cerrahisi", "Kalp ve Damar Cerrahisi", DepartmentDivision.Surgical, null, "cardiovascular surgery", "cardiac and vascular surgery", "kvc"),
        D("kulak-burun-bogaz-hastaliklari", "Kulak Burun Boğaz Hastalıkları", DepartmentDivision.Surgical, null, "otorhinolaryngology", "ear nose and throat", "kulak burun bogaz", "kbb", "ent"),
        D("ortopedi-ve-travmatoloji", "Ortopedi ve Travmatoloji", DepartmentDivision.Surgical, null, "orthopedics and traumatology", "orthopaedics and traumatology", "ortopedi"),
        D("plastik-rekonstruktif-ve-estetik-cerrahi", "Plastik, Rekonstrüktif ve Estetik Cerrahi", DepartmentDivision.Surgical, null, "plastic reconstructive and aesthetic surgery", "plastik cerrahi"),
        D("tibbi-patoloji", "Tıbbi Patoloji", DepartmentDivision.Surgical, null, "medical pathology", "patoloji", "pathology"),
        D("uroloji", "Üroloji", DepartmentDivision.Surgical, null, "urology"),
    ];

    private static readonly IReadOnlyDictionary<string, DepartmentDefinition> ByKey =
        Departments.ToDictionary(item => item.Key, StringComparer.Ordinal);

    public static bool TryGet(string key, out DepartmentDefinition department) =>
        ByKey.TryGetValue(key, out department!);

    public static bool TryResolve(string value, out DepartmentDefinition department)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = Normalize(RemoveDepartmentSuffix(value));

        department = Departments.FirstOrDefault(item =>
            item.NormalizedAliases.Contains(normalized, StringComparer.Ordinal))!;
        return department is not null;
    }

    public static bool TryResolveFromTitle(string value, out DepartmentDefinition department)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = Normalize(value);
        department = Departments
            .SelectMany(item => item.NormalizedAliases.Select(alias => (Department: item, Alias: alias)))
            .Where(candidate => candidate.Alias.Length >= 4
                && ContainsPhrase(normalized, candidate.Alias))
            .OrderByDescending(candidate => candidate.Alias.Length)
            .Select(candidate => candidate.Department)
            .FirstOrDefault()!;
        return department is not null;
    }

    public static string DefaultColor(string key)
    {
        if (!TryGet(key, out DepartmentDefinition department))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown department key.");
        }

        return department.RequestedColor ?? DerivedColor($"department:{key}");
    }

    public static string Normalize(string value)
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

        return string.Join(' ', builder.ToString().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static DepartmentDefinition D(
        string key,
        string name,
        DepartmentDivision division,
        string? requestedColor,
        params string[] aliases) =>
        new(key, name, division, requestedColor, [name, .. aliases]);

    private static string RemoveDepartmentSuffix(string value) =>
        DepartmentSuffix().Replace(value.Trim().TrimEnd('.'), string.Empty).Trim();

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static string DerivedColor(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"sirkadiyen-color\n{key}"));
        double hue = ((hash[0] << 8) | hash[1]) / 65535d * 360d;
        double saturation = 0.68d;
        double lightness = 0.43d;
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
        return FormattableString.Invariant(
            $"#{ToByte(red + match):X2}{ToByte(green + match):X2}{ToByte(blue + match):X2}");
    }

    private static int ToByte(double component) =>
        (int)Math.Round(component * 255d, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"\s+(?:A\.?\s*D\.?|ANAB[İI]L[İI]M DALI|DEPARTMENT)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DepartmentSuffix();
}

public sealed record DepartmentDefinition(
    string Key,
    string Name,
    DepartmentDivision Division,
    string? RequestedColor,
    IReadOnlyList<string> Aliases)
{
    public IReadOnlyList<string> NormalizedAliases { get; } =
        Aliases.Select(DepartmentCatalog.Normalize).Distinct(StringComparer.Ordinal).ToArray();
}

public enum DepartmentDivision
{
    Basic,
    Internal,
    Surgical,
}
