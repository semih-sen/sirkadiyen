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
    /// name, in every audience dimension it states, a group the student belongs to (ADR-109).
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
                TargetsProfile(record.AudienceSelectors, profile.Selectors),
            _ => false,
        };
    }

    private static bool TargetsProfile(
        string audienceSelectorsJson,
        IReadOnlyDictionary<string, string> profileSelectors)
    {
        IReadOnlyList<AudienceSelectorEntry> selectors = Parse(audienceSelectorsJson);

        // A cohort-scoped lesson with no named group targets nobody we can confirm, so it is
        // deliberately excluded rather than sent to everyone.
        if (selectors.Count == 0)
        {
            return false;
        }

        // Selectors of one dimension enumerate alternatives ("curriculum group 3-A or 3-B");
        // selectors of different dimensions each narrow the audience further ("curriculum
        // group 3-A *and* faculty-practice cohort A3"). Reading every selector as an
        // alternative is what put all eight faculty-practice cohorts of a curriculum group on
        // one Grade 3 student's calendar (ADR-109).
        foreach (IGrouping<string, AudienceSelectorEntry> dimension in
            selectors.GroupBy(selector => selector.Dimension, StringComparer.Ordinal))
        {
            // A dimension the student has not declared cannot be confirmed, so the lesson is
            // withheld rather than widened to everyone who happens to match another one.
            if (!profileSelectors.TryGetValue(dimension.Key, out string? declared)
                || !dimension.Any(selector =>
                    string.Equals(selector.Value, declared, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
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
