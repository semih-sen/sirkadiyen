namespace Sirkadiyen.Application.Announcements;

/// <summary>
/// Answers "who would receive this announcement, and who could not" (ADR-107).
/// </summary>
/// <remarks>
/// Unlike <see cref="GoogleCalendar.ICalendarSyncTargetReadStore"/>, which returns only the users a
/// calendar write may reach, this store must return the ineligible ones too. The exclusion list
/// with its reasons is the point of the screen: an operator who is told "412 recipients" without
/// being told that 38 accounts have a dead Calendar grant does not know what they are confirming
/// (plan §4.3 step 3, §4.4).
/// </remarks>
public interface IAnnouncementAudienceReadStore
{
    /// <summary>
    /// Resolves an audience into included and excluded candidates. Both lists are ordered by
    /// user id so the resolution — and therefore the plan hash derived from it — is deterministic.
    /// </summary>
    Task<AnnouncementAudienceResolution> ResolveAsync(
        AnnouncementAudienceCriteria criteria,
        CancellationToken cancellationToken);
}
