using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Domain.Announcements;

/// <summary>
/// Derives the deterministic key that makes an announcement idempotent (ADR-107).
/// </summary>
/// <remarks>
/// The key is the announcement's natural identity, and a unique index on it is what turns a
/// repeated confirmation into a replay rather than a second copy of the same message on every
/// student's calendar. It is derived rather than typed so two operators composing the same
/// announcement independently collide instead of duplicating, and so the screen can show the key
/// during preview — before anything is written.
/// <para>
/// A bulk key covers the audience, the date and the normalized title, because those are what make
/// two drafts "the same announcement". Body text, location and colour are deliberately outside it:
/// correcting the wording of an announcement already delivered must patch the existing events, not
/// produce a second one (plan §4.4).
/// </para>
/// </remarks>
public static class AnnouncementCampaignKey
{
    public const int MaximumLength = 200;

    public const int MaximumTemplateKeyLength = 64;

    /// <summary>
    /// The key for a cohort-addressed announcement. Every audience dimension participates, so
    /// widening or narrowing the audience is a different announcement rather than an edit of
    /// this one — its recipient set was already frozen at confirmation.
    /// </summary>
    public static string ForBulk(
        string academicYear,
        int? classYear,
        string? programLanguage,
        IReadOnlyDictionary<string, string> selectors,
        DateOnly localDate,
        string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(academicYear);
        ArgumentNullException.ThrowIfNull(selectors);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        StringBuilder material = new();
        material.Append(academicYear.Trim()).Append('\n');
        material.Append(classYear?.ToString(CultureInfo.InvariantCulture) ?? "*").Append('\n');
        material.Append(string.IsNullOrWhiteSpace(programLanguage) ? "*" : programLanguage.Trim())
            .Append('\n');

        // Ordinal ordering makes the key independent of the order the operator filled the form in.
        foreach (KeyValuePair<string, string> selector in
            selectors.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            material.Append(selector.Key).Append('=').Append(selector.Value).Append(';');
        }

        material.Append('\n').Append(Iso(localDate)).Append('\n').Append(NormalizeTitle(title));

        return $"bulk:{Iso(localDate)}:{ShortHash(material.ToString())}";
    }

    /// <summary>
    /// The key for a single-user warning: the user, the template and the local date, exactly as
    /// the design plan specifies (plan §4.5). Sending the same template to the same person on the
    /// same day is therefore a replay, not a second event.
    /// </summary>
    public static string ForUserWarning(Guid userId, string templateKey, DateOnly localDate)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A warning needs a recipient.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        templateKey = templateKey.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            templateKey.Length,
            MaximumTemplateKeyLength,
            nameof(templateKey));

        return $"warning:{userId:N}:{templateKey}:{Iso(localDate)}";
    }

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Collapses case and whitespace so a re-typed title is recognized as the same announcement.
    /// It never changes the title that is written to a calendar; only the identity derived from it.
    /// </summary>
    private static string NormalizeTitle(string title) =>
        string.Join(
            ' ',
            title.Trim().ToLowerInvariant().Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ShortHash(string material) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
}
