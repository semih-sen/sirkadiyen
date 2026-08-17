namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// The server-owned starting points for a single-user warning (ADR-107, plan §5.12).
/// </summary>
/// <remarks>
/// A template is a draft, not a locked message: the operator edits the wording before confirming,
/// and only the template key travels into the warning key. Every template names a state the
/// system can actually be in and tells the student the one thing they can do about it.
/// <para>
/// The design plan lists a "deneme bitiyor" (trial ending) template. There is no such state:
/// Sirkadiyen access does not lapse after activation and <c>GET /api/licenses/status</c> reports
/// no time remaining (ADR-089). Shipping that template would have an operator send students a
/// deadline the product does not have, so it is deliberately absent rather than approximated.
/// </para>
/// </remarks>
public static class AnnouncementTemplateCatalog
{
    private static readonly IReadOnlyList<AnnouncementTemplate> All =
    [
        new(
            "calendar-authorization-required",
            "Takvim yetkilendirmesi gerekiyor",
            "Sirkadiyen · Takvim erişimin yenilenmeli",
            "Google Takvim erişimin sona ermiş veya geri alınmış görünüyor, bu yüzden ders "
            + "programındaki değişiklikler takvimine yazılamıyor. Takvimindeki mevcut dersler "
            + "silinmedi; yalnızca yeni değişiklikler bekliyor.\n\n"
            + "Sirkadiyen'e giriş yapıp takvim erişimini yeniden onayladığında, bekleyen "
            + "değişiklikler otomatik olarak uygulanır.",
            "announcement:warning"),
        new(
            "profile-missing",
            "Akademik profil eksik",
            "Sirkadiyen · Akademik profilini tamamla",
            "Hesabın etkin, ancak akademik profilin tamamlanmadığı için hangi derslerin sana ait "
            + "olduğunu belirleyemiyoruz.\n\n"
            + "Sirkadiyen'de profil adımını tamamladığında takvimin oluşturulur ve programın "
            + "yazılmaya başlanır.",
            "announcement:warning"),
        new(
            "license-suspended",
            "Lisans askıya alındı",
            "Sirkadiyen · Senkronizasyon durduruldu",
            "Lisansın şu anda etkin görünmüyor, bu yüzden takvimine yeni ders değişiklikleri "
            + "yazılmıyor. Takvimin ve içindeki mevcut dersler olduğu gibi duruyor; hiçbir şey "
            + "silinmedi.\n\n"
            + "Durumun hatalı olduğunu düşünüyorsan destek ekibiyle iletişime geç.",
            "announcement:warning"),
        new(
            "calendar-repair-required",
            "Yönetilen takvim bulunamıyor",
            "Sirkadiyen · Takvimine ulaşılamıyor",
            "Sirkadiyen'in senin için oluşturduğu takvim silinmiş veya erişilemez durumda, bu "
            + "yüzden ders programın yazılamıyor.\n\n"
            + "Sirkadiyen panelindeki onarım talebini başlatabilir veya destek ekibiyle "
            + "iletişime geçebilirsin.",
            "announcement:warning"),
        new(
            "general-notice",
            "Serbest duyuru",
            "Sirkadiyen · Bilgilendirme",
            string.Empty,
            "announcement:notice"),
    ];

    public static IReadOnlyList<AnnouncementTemplate> List() => All;

    public static AnnouncementTemplate? Find(string? templateKey) =>
        string.IsNullOrWhiteSpace(templateKey)
            ? null
            : All.FirstOrDefault(template =>
                string.Equals(template.Key, templateKey.Trim(), StringComparison.Ordinal));
}

/// <summary>
/// One warning draft: what the operator picks it by, and what it proposes to say.
/// </summary>
public sealed record AnnouncementTemplate(
    string Key,
    string Name,
    string SuggestedTitle,
    string SuggestedBody,
    string CategoryKey);
