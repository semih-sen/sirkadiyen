namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Decides whether one student's cohort selectors satisfy an announcement's audience (ADR-107).
/// </summary>
/// <remarks>
/// This is deliberately the opposite rule to <see cref="GoogleCalendar.CalendarAudienceResolver"/>.
/// A lesson lists the groups it is <em>for</em>, so a student matching <em>any</em> of them attends
/// it. An announcement's selectors are the operator narrowing who they are addressing, so a
/// student must match <em>all</em> of them — "Dönem 2, uygulama grubu C" means students who are
/// both, not students who are either.
/// <para>
/// Kept as a pure function so the rule is unit-tested without a database, and so the audience
/// query only has to optimize for it rather than restate it.
/// </para>
/// </remarks>
public static class AnnouncementAudienceMatcher
{
    public static bool Matches(
        IReadOnlyDictionary<string, string> audienceSelectors,
        IReadOnlyDictionary<string, string> profileSelectors)
    {
        ArgumentNullException.ThrowIfNull(audienceSelectors);
        ArgumentNullException.ThrowIfNull(profileSelectors);

        foreach (KeyValuePair<string, string> required in audienceSelectors)
        {
            // A selector the student's profile does not carry at all is a mismatch, not a pass.
            // Treating an absent dimension as "matches anything" would send a message meant for
            // one anatomy group to every student whose programme has no anatomy group.
            if (!profileSelectors.TryGetValue(required.Key, out string? actual)
                || !string.Equals(actual, required.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
