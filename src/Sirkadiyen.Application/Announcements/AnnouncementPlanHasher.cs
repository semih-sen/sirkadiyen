using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Domain.Announcements;

namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Hashes exactly what an operator was shown, so the confirmation is bound to that plan and not
/// to whatever the audience happens to resolve to a minute later (ADR-093's pattern, ADR-107).
/// </summary>
/// <remarks>
/// The recipient identities are part of the material, not merely their count. Two audiences can
/// have the same size and different members — a student activating while another's grant dies —
/// and confirming "412 recipients" must not authorize writing to a different 412 people.
/// </remarks>
public static class AnnouncementPlanHasher
{
    public static string Compute(
        string campaignKey,
        AnnouncementContent content,
        AnnouncementAudienceCriteria criteria,
        AnnouncementAudienceResolution resolution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(resolution);

        StringBuilder material = new();
        material.Append("announcement-plan/v1\n");
        material.Append(campaignKey).Append('\n');

        material.Append(content.Title.Trim()).Append('\n');
        material.Append(content.Body.Trim()).Append('\n');
        material.Append(content.Location?.Trim() ?? string.Empty).Append('\n');
        material.Append(content.IsAllDay ? "all-day" : "timed").Append('\n');
        material.Append(content.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('\n');
        material.Append(Time(content.StartLocalTime)).Append('\n');
        material.Append(Time(content.EndLocalTime)).Append('\n');
        material.Append(content.TimeZoneId.Trim()).Append('\n');
        material.Append(
                content.ReminderMinutesBefore?.ToString(CultureInfo.InvariantCulture) ?? "none")
            .Append('\n');
        material.Append(content.CategoryKey.Trim()).Append('\n');

        material.Append(criteria.AcademicYear.Trim()).Append('\n');
        material.Append(criteria.ClassYear?.ToString(CultureInfo.InvariantCulture) ?? "*")
            .Append('\n');
        material.Append(criteria.ProgramLanguage?.ToString() ?? "*").Append('\n');
        foreach (KeyValuePair<string, string> selector in
            criteria.Selectors.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            material.Append(selector.Key).Append('=').Append(selector.Value).Append(';');
        }

        material.Append('\n');
        material.Append(criteria.TargetUserId?.ToString("N") ?? "*").Append('\n');

        material.Append("included\n");
        foreach (AnnouncementAudienceCandidate candidate in
            resolution.Included.OrderBy(candidate => candidate.UserId))
        {
            material.Append(candidate.UserId.ToString("N")).Append('\n');
        }

        material.Append("excluded\n");
        foreach (AnnouncementAudienceCandidate candidate in
            resolution.Excluded.OrderBy(candidate => candidate.UserId))
        {
            material.Append(candidate.UserId.ToString("N"))
                .Append(':')
                .Append(candidate.ExclusionReason?.ToString() ?? "unknown")
                .Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private static string Time(TimeOnly? value) =>
        value?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "none";
}
