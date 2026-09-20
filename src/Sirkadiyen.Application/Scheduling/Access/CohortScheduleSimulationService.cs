using System.Text.Json;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.Scheduling.Access;

/// <summary>
/// Resolves one week of the live published schedule for an arbitrary cohort, with no student
/// and no writes of any kind.
/// </summary>
/// <remarks>
/// It answers "what would a student in this cohort receive?" — a question nothing else could
/// answer, because <see cref="IUserScheduleReadStore"/> projects the event-mapping ledger and
/// therefore needs a real, already-synchronized user.
/// <para>
/// It deliberately reuses the initial-synchronization read path
/// (<see cref="ICanonicalScheduleReadStore.ListCurrentPublishedRecordsAsync"/>), the audience
/// rule (<see cref="CalendarAudienceResolver"/>) and the presentation policy
/// (<see cref="CalendarEventPresentationPolicy"/>) rather than restating any of them. A
/// simulation that resolved lessons its own way would be a second implementation of the thing
/// it exists to audit, and would agree with the calendar only by luck.
/// </para>
/// </remarks>
public sealed class CohortScheduleSimulationService(
    ICanonicalScheduleReadStore readStore,
    DepartmentColorService departmentColors,
    SupportedProfileSchema schema)
{
    /// <summary>The zone every schedule in this system is interpreted in (AI_GUIDELINE §16).</summary>
    public const string ScheduleTimeZoneId = "Europe/Istanbul";

    // The stored selectors are camelCase, as the resolver reads them.
    private static readonly JsonSerializerOptions SelectorJsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The Monday of the week a date falls in. <see cref="DayOfWeek.Sunday"/> is 0, so a naive
    /// subtraction would send Sunday forward into the next week instead of back into its own.
    /// </summary>
    public static DateOnly SnapToMonday(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public async Task<CohortSimulationWeek> SimulateAsync(
        CohortSimulationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The program owns its academic year, and a record reaches a student only when the two
        // match exactly (ADR-103/115). Letting an operator type a year would mostly produce an
        // empty week that looked like missing data, so it is derived and never asked for.
        SupportedProfileProgram? program =
            schema.FindProgram(query.ClassYear, query.ProgramLanguage);

        // A dimension left unstated is allowed here, so an operator can narrow a cohort one
        // choice at a time and watch the week fill in. What is still refused is a selector the
        // program does not define or a value it does not publish — those are mistakes, not
        // partial answers.
        StudentProfileValidationResult validation = StudentProfileValidator.ValidateSelectors(
            schema,
            query.ClassYear,
            query.ProgramLanguage,
            query.Selectors,
            requireEveryDimension: false);
        if (!validation.IsValid || program is null)
        {
            throw new CohortSimulationValidationException(validation.Errors);
        }

        CalendarAudience audience = new()
        {
            AcademicYear = program.AcademicYear,
            ClassYear = query.ClassYear,
            ProgramLanguage = query.ProgramLanguage,
            Selectors = query.Selectors,
        };

        DateOnly weekStart = SnapToMonday(query.AnchorLocalDate);
        DateOnly weekEnd = weekStart.AddDays(6);

        IReadOnlyList<CanonicalScheduleRecord> published =
            await readStore.ListCurrentPublishedRecordsAsync(
                program.AcademicYear,
                query.ClassYear,
                query.ProgramLanguage,
                cancellationToken);

        // The whole program-year is resolved before the week is cut out of it, because the year
        // count and the contributing sources are what tell an operator whether an empty week is
        // a quiet week or an empty cohort.
        List<CanonicalScheduleRecord> cohortYear =
            [.. published.Where(record => CalendarAudienceResolver.Applies(record, audience))];

        List<CanonicalScheduleRecord> week =
        [
            .. cohortYear
                .Where(record => record.LocalDate >= weekStart && record.LocalDate <= weekEnd)
                .OrderBy(record => record.LocalDate)
                .ThenByDescending(record => record.IsAllDay)
                .ThenBy(record => record.StartLocalTime)
                .ThenBy(record => record.SourceId.Value, StringComparer.Ordinal)
                .ThenBy(record => record.StableIdentity, StringComparer.Ordinal),
        ];

        // Two sources publishing one lesson is two distinct identities and therefore two real
        // events on a calendar, so nothing is de-duplicated here. Two records sharing an
        // identity is the dangerous case instead, because the managed event id is derived from
        // the identity alone — those are flagged rather than hidden.
        HashSet<string> collidingIdentities =
        [
            .. week
                .GroupBy(record => record.StableIdentity, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        // The admin palette is exactly "the operator default, else the catalog default" — the
        // right colors for a cohort that belongs to no user.
        IReadOnlyDictionary<string, string> colors =
            (await departmentColors.GetForAdminAsync(cancellationToken))
            .ToDictionary(view => view.Key, view => view.EffectiveColor, StringComparer.Ordinal);

        return new CohortSimulationWeek
        {
            AcademicYear = program.AcademicYear,
            ClassYear = query.ClassYear,
            ProgramLanguage = query.ProgramLanguage,
            Selectors = query.Selectors,
            WeekStartLocalDate = weekStart,
            WeekEndLocalDate = weekEnd,
            TimeZoneId = ScheduleTimeZoneId,
            MissingRequiredSelectors =
                StudentProfileValidator.MissingRequiredSelectors(program, query.Selectors),
            CohortYearEventCount = cohortYear.Count,
            PublishedSourceIds =
            [
                .. cohortYear
                    .Select(record => record.SourceId.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal),
            ],
            Events = [.. week.Select(record => ToEvent(record, colors, collidingIdentities))],
        };
    }

    private static CohortSimulationEvent ToEvent(
        CanonicalScheduleRecord record,
        IReadOnlyDictionary<string, string> colors,
        HashSet<string> collidingIdentities) => new()
        {
            // The policy is called directly rather than through ManagedCalendarEventFactory,
            // which needs a user id only to derive a deterministic event id. A simulation has no
            // user, and inventing a Guid to satisfy a signature would put a fiction in the
            // output for nothing.
            Summary = CalendarEventPresentationPolicy.Summary(record),
            Description = CalendarEventPresentationPolicy.Description(record),
            Location = CalendarEventPresentationPolicy.Location(record),
            Label = CalendarEventPresentationPolicy.EventLabel(record, colors),
            LocalDate = record.LocalDate,
            StartLocalTime = record.StartLocalTime,
            EndLocalTime = record.EndLocalTime,
            IsAllDay = record.IsAllDay,
            TimeZoneId = record.TimeZoneId,
            Raw = new CohortSimulationRawRecord
            {
                CanonicalRecordId = record.Id,
                ScheduleRevisionId = record.ScheduleRevisionId,
                SourceId = record.SourceId.Value,
                CandidateId = record.CandidateId,
                StableIdentity = record.StableIdentity,
                ContentHash = record.ContentHash,
                RecordStatus = record.RecordStatus.ToString(),
                EventType = record.EventType.ToString(),
                AudienceScope = record.AudienceScope.ToString(),
                AudienceSelectors = ParseSelectors(record.AudienceSelectors),
                DisplayTitle = record.DisplayTitle,
                NormalizedCourseIdentity = record.NormalizedCourseIdentity,
                Instructor = record.Instructor,
                RawLocation = record.Location,
                CurriculumBlock = record.CurriculumBlock,
                Departments = record.Departments,
                ComparableDepartment = record.ComparableDepartment,
                Notes = record.Notes,
                Confidence = record.Confidence,
                Evidence = record.Evidence,
                SharesStableIdentity = collidingIdentities.Contains(record.StableIdentity),
            },
        };

    private static IReadOnlyList<AudienceSelectorView> ParseSelectors(string audienceSelectorsJson)
    {
        if (string.IsNullOrWhiteSpace(audienceSelectorsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AudienceSelectorView>>(
                audienceSelectorsJson,
                SelectorJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // The resolver treats malformed selector JSON as targeting nobody rather than
            // failing; a diagnostic view of the same record should not be the one thing that
            // throws. An empty list beside a populated AudienceScope is itself the finding.
            return [];
        }
    }
}
