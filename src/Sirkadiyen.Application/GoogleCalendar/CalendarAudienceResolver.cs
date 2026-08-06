using System.Text.Json;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.GoogleCalendar;

/// <summary>
/// Decides whether one currently-published canonical record belongs on a given student's
/// calendar (ADR-058). It is a pure function so the affected-events rule can be reasoned
/// about and unit-tested without a database.
/// </summary>
public static class CalendarAudienceResolver
{
    // The stored selectors are camelCase (ContractJson uses Web defaults); case-insensitive
    // reading keeps the rule robust to that detail.
    private static readonly JsonSerializerOptions SelectorJsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Whether the student should have an active event for this record. A cancelled lesson
    /// yields no event; the program dimensions must match; and a cohort-scoped lesson must
    /// name a group the student belongs to.
    /// </summary>
    public static bool Applies(CanonicalScheduleRecord record, StudentProfileView profile)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(profile);

        // A cancelled record represents a lesson that is not happening, so it never becomes an
        // event during initial sync. (Turning a live lesson into a cancelled one is a delete,
        // which belongs to incremental sync.)
        if (record.RecordStatus != CanonicalRecordStatus.Scheduled)
        {
            return false;
        }

        // The program dimensions gate everything before any cohort question is asked. The read
        // store already filters on these, but this rule is the single authority for "does this
        // belong to this student", so it re-checks rather than trusting the caller.
        if (record.ClassYear != profile.ClassYear
            || record.ProgramLanguage != profile.ProgramLanguage
            || !string.Equals(record.AcademicYear, profile.AcademicYear, StringComparison.Ordinal))
        {
            return false;
        }

        return record.AudienceScope switch
        {
            AudienceScope.AllStudentsInProgram => true,
            AudienceScope.SelectedGroups =>
                TargetsAnyOf(record.AudienceSelectors, profile.Selectors),
            _ => false,
        };
    }

    private static bool TargetsAnyOf(
        string audienceSelectorsJson,
        IReadOnlyDictionary<string, string> profileSelectors)
    {
        // A cohort-scoped lesson with no named group targets nobody we can confirm, so it is
        // deliberately excluded rather than sent to everyone.
        foreach (AudienceSelectorEntry selector in Parse(audienceSelectorsJson))
        {
            if (profileSelectors.TryGetValue(selector.Dimension, out string? value)
                && string.Equals(value, selector.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<AudienceSelectorEntry> Parse(string audienceSelectorsJson)
    {
        if (string.IsNullOrWhiteSpace(audienceSelectorsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AudienceSelectorEntry>>(
                audienceSelectorsJson,
                SelectorJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // The parser store writes these from typed contracts, so malformed JSON is not
            // expected; treat it as targeting nobody rather than stalling a whole user's sync.
            return [];
        }
    }

    private sealed record AudienceSelectorEntry(string Dimension, string Value);
}
