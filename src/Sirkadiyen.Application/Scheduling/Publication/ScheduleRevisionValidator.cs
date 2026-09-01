using System.Globalization;
using System.Text.Json;
using Sirkadiyen.Contracts.Parsing;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Publication;

/// <summary>
/// Decides whether a candidate revision may be published, be held for review, or
/// is unusable (ADR-025, ADR-029).
/// </summary>
/// <remarks>
/// This is the safety boundary between a technically successful parse and real
/// student calendars. A parse that succeeded can still describe a broken source,
/// so every revision passes through here before it can become live data.
/// <para>
/// The validator is a pure function of its input. It reads nothing, writes
/// nothing, and takes its clock as an argument, so every rule and every threshold
/// boundary is testable without a database.
/// </para>
/// </remarks>
public sealed class ScheduleRevisionValidator(RevisionValidationOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();

    public RevisionValidationResult Validate(
        RevisionValidationInput input,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<RevisionValidationFinding> findings = [];

        if (input.Records.Count == 0)
        {
            // Nothing an administrator could approve would make an empty revision
            // publishable, and publishing it would delete every event the source
            // owns. This is the one terminal outcome.
            //
            // Which of two very different situations produced it is worth stating, because the
            // rejection reads identically in both and the operator's next step does not: a source
            // that has published before and now publishes nothing is an alarm, while a source that
            // has never published anything may simply be one that never will (a companion, whose
            // whole job is to enrich another source's sessions - ADR-102, ADR-133).
            findings.Add(Finding(
                input,
                RevisionValidationRule.EmptyRevision,
                ValidationSeverity.Error,
                "The revision contains no schedule records, so it cannot be published. "
                + (input.HasPreviousPublishedRevision
                    ? "This source has published records before, so something changed: the "
                        + "document, or the parser profile reading it."
                    : "This source has never published a revision. For a companion source, which "
                        + "only enriches sessions another source states, that is the expected and "
                        + "permanent outcome and no publication is missing. For any other source "
                        + "it means the document or the parser profile is wrong."),
                atUtc));

            return new RevisionValidationResult
            {
                Outcome = RevisionState.Rejected,
                Findings = findings,
                StateReason = input.HasPreviousPublishedRevision
                    ? "Empty revision; this source has published records before."
                    : "Empty revision; this source has never published records.",
            };
        }

        if (DescribeParseCollapse(input) is ParseCollapse collapse)
        {
            // Terminal for the same reason an empty revision is: there is no
            // version of this that an operator could approve. Approving it would
            // delete every event this source has on every student's calendar and
            // replace it with an unrelated set. It is the same fault as an empty
            // revision — the parse read something other than this source's
            // schedule — and it must not sit in the review queue pretending to be
            // a decision, in front of the real ones.
            findings.Add(Finding(
                input,
                RevisionValidationRule.ParseCollapse,
                ValidationSeverity.Error,
                $"Not one of the {collapse.PreviousCount} currently published record(s) appears in "
                + $"this revision's {collapse.CurrentCount} record(s). A schedule that changed "
                + "keeps some of its lessons; a parse that read the wrong document, the wrong "
                + "sheet, or read it under the wrong profile keeps none. The published revision "
                + "stays live and nothing was removed from any calendar.",
                atUtc,
                detail: Detail(input.PreviouslyPublishedIdentities.Take(20)),
                affectedRecordCount: collapse.PreviousCount));

            return new RevisionValidationResult
            {
                Outcome = RevisionState.Rejected,
                Findings = findings,
                StateReason =
                    $"Parse collapse: none of {collapse.PreviousCount} published record(s) "
                    + "survived into this revision.",
            };
        }

        ValidateDates(input, findings, atUtc);
        ValidateDateSequence(input, findings, atUtc);
        ValidateDurations(input, findings, atUtc);
        ValidateConfidence(input, findings, atUtc);
        ValidateStableIdentities(input, findings, atUtc);
        ValidateAudienceSelectors(input, findings, atUtc);
        ValidateOverlaps(input, findings, atUtc);
        ValidateDeletionShare(input, findings, atUtc);

        bool quarantine = findings.Any(
            static finding => finding.Severity is ValidationSeverity.Error);

        return new RevisionValidationResult
        {
            Outcome = quarantine ? RevisionState.ReviewRequired : RevisionState.Validated,
            Findings = findings,
            StateReason = quarantine
                ? DescribeQuarantine(findings)
                : "All validation rules passed.",
        };
    }

    /// <summary>
    /// Whether this revision replaced the published schedule wholesale instead of
    /// changing it, and the sizes involved when it did.
    /// </summary>
    /// <remarks>
    /// The distinction this draws is between a deletion and a collapse. A faculty
    /// really does delete lessons — a rewritten block, a finished term — and those
    /// revisions must stay approvable, which is what <see cref="ValidateDeletionShare"/>
    /// is for. But a real edit keeps *something*: some lesson, somewhere in the
    /// year, still matches. When the overlap is exactly zero, the document that
    /// was read is not the document that was published from, and no human
    /// judgement can turn it into a schedule.
    /// <para>
    /// Production is what made this its own rule. Seventeen mass-deletion holds
    /// covered 12,256 records, ten of them reading "935 of 935 (100.0 %) are
    /// absent" — parses that had collapsed, queued for review beside genuine
    /// schedule edits, which then waited behind them.
    /// </para>
    /// <para>
    /// The size floor is <see cref="RevisionValidationOptions.MinimumDeletionCount"/>,
    /// shared with the deletion rule for the same reason it exists there: on a
    /// source publishing a handful of records, zero overlap is not evidence of
    /// anything. A small source keeps the old behaviour and stays reviewable.
    /// </para>
    /// </remarks>
    private ParseCollapse? DescribeParseCollapse(RevisionValidationInput input)
    {
        if (!input.HasPreviousPublishedRevision
            || input.PreviouslyPublishedIdentities.Count < options.MinimumDeletionCount)
        {
            return null;
        }

        HashSet<string> current = input.Records
            .Select(record => record.StableIdentity)
            .ToHashSet(StringComparer.Ordinal);

        return input.PreviouslyPublishedIdentities.Any(current.Contains)
            ? null
            : new ParseCollapse(input.PreviouslyPublishedIdentities.Count, input.Records.Count);
    }

    private void ValidateDates(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        if (!TryReadAcademicYear(input.Source.AcademicYear, out DateOnly start, out DateOnly end))
        {
            // A source whose academic year cannot be read is a configuration
            // fault, not a schedule fault. Report it rather than silently
            // skipping the rule that depends on it.
            findings.Add(Finding(
                input,
                RevisionValidationRule.RecordDateOutsideAcademicYear,
                ValidationSeverity.Warning,
                $"Academic year '{input.Source.AcademicYear}' is not a readable "
                + "range, so record dates were not checked against it.",
                atUtc));
            return;
        }

        DateOnly earliest = start.AddDays(-options.AcademicYearGraceDays);
        DateOnly latest = end.AddDays(options.AcademicYearGraceDays);

        List<CanonicalScheduleRecord> outside = input.Records
            .Where(record => record.LocalDate < earliest || record.LocalDate > latest)
            .ToList();

        if (outside.Count == 0)
        {
            return;
        }

        findings.Add(Finding(
            input,
            RevisionValidationRule.RecordDateOutsideAcademicYear,
            ValidationSeverity.Error,
            $"{outside.Count} record(s) fall outside academic year "
            + $"'{input.Source.AcademicYear}' ({Invariant(earliest)} to {Invariant(latest)}), "
            + "which usually means a date was misread rather than that the schedule moved.",
            atUtc,
            detail: Detail(outside.Take(20).Select(record => new
            {
                record.CandidateId,
                Date = record.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                record.DisplayTitle,
            })),
            affectedRecordCount: outside.Count));
    }

    private static void ValidateDateSequence(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        // The parser reads each date column chronologically and repairs a mistyped
        // year when the dates around it leave exactly one reading (ADR-139). Two
        // different things reach here from that, and they are deliberately one
        // finding rather than two rules: they are the same anomaly, and what
        // separates them is only whether the parser could resolve it alone.
        //
        // A repair moved a lesson to a day the document does not state, so a
        // reviewer must see it before approving — but it is the corrected reading
        // of a broken cell, and holding the revision for it would leave the
        // schedule exactly where the typo left it. It is a warning.
        //
        // A suggestion is a date the parser refused to correct and published as
        // written. That one *does* hold the revision, because approving it would
        // put a lesson on a day nobody believes, and because the whole point of
        // reporting the candidates is that an operator picks one.
        List<ParserDateAnomaly> repaired = [.. input.DateAnomalies.Where(
            static anomaly => anomaly.Repaired)];
        List<ParserDateAnomaly> unresolved = [.. input.DateAnomalies.Where(
            static anomaly => !anomaly.Repaired)];

        if (repaired.Count > 0)
        {
            findings.Add(Finding(
                input,
                RevisionValidationRule.RecordDateOutOfSequence,
                ValidationSeverity.Warning,
                $"{repaired.Count} date(s) contradicted the order of the column they sit in "
                + "and were read as a mistyped year, which the dates around them left only one "
                + "reading of. The lessons on them were published on the corrected date.",
                atUtc,
                detail: Detail(repaired.Take(20)),
                affectedRecordCount: repaired.Count));
        }

        if (unresolved.Count == 0)
        {
            return;
        }

        findings.Add(Finding(
            input,
            RevisionValidationRule.RecordDateOutOfSequence,
            ValidationSeverity.Error,
            $"{unresolved.Count} date(s) contradict the order of the column they sit in and "
            + "were published as written, because more than one reading fits or the cell "
            + "disagrees with its own weekday. Each one lists the dates it may have meant; "
            + "accepting one corrects the source for every future parse.",
            atUtc,
            detail: Detail(unresolved.Take(20)),
            affectedRecordCount: unresolved.Count));
    }

    private void ValidateDurations(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        // An all-day item has no duration to find implausible (ADR-046). Measuring
        // one against the lesson thresholds would quarantine every revision that
        // publishes a holiday.
        List<CanonicalScheduleRecord> impossible = input.Records
            .Where(record => !record.IsAllDay)
            .Where(record =>
            {
                double minutes = (record.EndLocalTime!.Value - record.StartLocalTime!.Value)
                    .TotalMinutes;
                return minutes < options.MinimumLessonMinutes
                    || minutes > options.MaximumLessonMinutes;
            })
            .ToList();

        if (impossible.Count == 0)
        {
            return;
        }

        findings.Add(Finding(
            input,
            RevisionValidationRule.ImpossibleLessonDuration,
            ValidationSeverity.Error,
            $"{impossible.Count} record(s) last outside the plausible range of "
            + $"{options.MinimumLessonMinutes} to {options.MaximumLessonMinutes} minutes.",
            atUtc,
            detail: Detail(impossible.Take(20).Select(record => new
            {
                record.CandidateId,
                Start = record.StartLocalTime!.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
                End = record.EndLocalTime!.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
            })),
            affectedRecordCount: impossible.Count));
    }

    private void ValidateConfidence(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        List<CanonicalScheduleRecord> low = input.Records
            .Where(record => record.Confidence < options.LowConfidenceThreshold)
            .ToList();

        if (low.Count == 0)
        {
            return;
        }

        findings.Add(Finding(
            input,
            RevisionValidationRule.LowConfidenceRecord,
            ValidationSeverity.Error,
            $"{low.Count} record(s) were resolved below the confidence threshold of "
            + $"{options.LowConfidenceThreshold.ToString("0.00", CultureInfo.InvariantCulture)}.",
            atUtc,
            detail: Detail(low.Take(20).Select(record => new
            {
                record.CandidateId,
                record.Confidence,
                record.DisplayTitle,
            })),
            affectedRecordCount: low.Count));
    }

    private static void ValidateStableIdentities(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        List<string> duplicates = input.Records
            .GroupBy(record => record.StableIdentity, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count == 0)
        {
            return;
        }

        // Two records sharing an identity would make the semantic diff ambiguous
        // and could make synchronization update one event twice.
        //
        // The schedule schema already carries a unique index on
        // (ScheduleRevisionId, StableIdentity), so the normal pipeline cannot
        // persist a duplicate and this rule cannot fire through it. It is kept
        // deliberately: the validator is a pure function that also runs over
        // input assembled in tests and future admin tooling, and a rule that
        // silently disappears when a constraint is relaxed is worse than a rule
        // that never fires.
        findings.Add(Finding(
            input,
            RevisionValidationRule.DuplicateStableIdentity,
            ValidationSeverity.Error,
            $"{duplicates.Count} stable identity value(s) appear on more than one record, "
            + "which would make the semantic diff ambiguous.",
            atUtc,
            detail: Detail(duplicates.Take(20)),
            affectedRecordCount: duplicates.Count));
    }

    private static void ValidateAudienceSelectors(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        if (input.Source.SupportedAudienceSelectors is not { } supported)
        {
            // The source has not declared its cohorts. Treating that silence as
            // "nothing is supported" would quarantine every revision of every
            // source that has not been surveyed yet.
            return;
        }

        HashSet<string> unknown = new(StringComparer.Ordinal);
        foreach (CanonicalScheduleRecord record in input.Records)
        {
            foreach (AudienceSelector selector in ReadSelectors(record))
            {
                bool known = supported.TryGetValue(selector.Dimension, out IReadOnlyList<string>? values)
                    && values.Contains(selector.Value, StringComparer.Ordinal);
                if (!known)
                {
                    unknown.Add($"{selector.Dimension}:{selector.Value}");
                }
            }
        }

        if (unknown.Count == 0)
        {
            return;
        }

        findings.Add(Finding(
            input,
            RevisionValidationRule.UnknownAudienceSelector,
            ValidationSeverity.Error,
            $"{unknown.Count} audience selector value(s) are not declared as supported by "
            + "this source, so no student profile can be matched to them.",
            atUtc,
            detail: Detail(unknown.OrderBy(value => value, StringComparer.Ordinal)),
            affectedRecordCount: unknown.Count));
    }

    private void ValidateOverlaps(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        // Records are compared within an exact audience, because a selector set
        // is the only audience statement available before student profiles exist.
        // A group and one of its own subgroups therefore do not register as
        // overlapping; that needs the ADR-027 profile schema to detect.
        // Only timed records can overlap. An all-day closure states no times, and
        // reading it as booking the whole day would report every lesson published
        // on a half-day holiday as a double booking.
        // Three kinds of same-audience time overlap are distinguished. Two
        // records of the SAME course
        // are a genuine duplication — almost always a parsing fault that would put
        // the same lesson on a calendar twice — and quarantine the revision. Two
        // records of DIFFERENT courses are a legitimate parallel offering the source
        // deliberately schedules for the whole cohort: an elective against another
        // activity, a make-up or retake exam beside the regular one. Those are
        // reported for visibility but never block publication, because treating them
        // as errors would hold every annual revision this faculty publishes.
        // Free-study records are availability rather than teaching. Source-authored
        // intersections between them remain visible but cannot double-book a student
        // into the same lesson, so they do not quarantine (ADR-069).
        List<string> duplicateOverlaps = [];
        List<string> parallelOverlaps = [];
        List<string> freeStudyOverlaps = [];
        IEnumerable<IGrouping<(string Audience, DateOnly Date), CanonicalScheduleRecord>> groups =
            input.Records
                .Where(record => !record.IsAllDay)
                .GroupBy(record => (
                    Audience: $"{record.AudienceScope}|{record.AudienceSelectors}",
                    Date: record.LocalDate));

        foreach (var group in groups)
        {
            List<CanonicalScheduleRecord> ordered = group
                .OrderBy(record => record.StartLocalTime!.Value)
                .ThenBy(record => record.EndLocalTime!.Value)
                .ToList();

            for (int index = 1; index < ordered.Count; index++)
            {
                CanonicalScheduleRecord previous = ordered[index - 1];
                CanonicalScheduleRecord current = ordered[index];
                if (current.StartLocalTime!.Value >= previous.EndLocalTime!.Value)
                {
                    continue;
                }

                string entry =
                    $"{Invariant(group.Key.Date)} {group.Key.Audience} "
                    + $"'{previous.DisplayTitle}' / '{current.DisplayTitle}'";
                if (IsFreeStudy(previous) && IsFreeStudy(current))
                {
                    freeStudyOverlaps.Add(entry);
                }
                else
                {
                    (IsSameCourse(previous, current) ? duplicateOverlaps : parallelOverlaps)
                        .Add(entry);
                }
            }
        }

        if (duplicateOverlaps.Count == 0
            && parallelOverlaps.Count == 0
            && freeStudyOverlaps.Count == 0)
        {
            return;
        }

        // Only same-course duplicates decide quarantine; parallel offerings never do.
        if (duplicateOverlaps.Count > options.MaximumToleratedOverlaps)
        {
            findings.Add(Finding(
                input,
                RevisionValidationRule.AudienceOverlap,
                ValidationSeverity.Error,
                $"{duplicateOverlaps.Count} lesson(s) book the same audience for the same "
                + "course at the same local date and time, which is almost always a parsing "
                + "duplication rather than a real schedule.",
                atUtc,
                detail: Detail(duplicateOverlaps.Take(20)),
                affectedRecordCount: duplicateOverlaps.Count));
            return;
        }

        int total = duplicateOverlaps.Count
            + parallelOverlaps.Count
            + freeStudyOverlaps.Count;
        findings.Add(Finding(
            input,
            RevisionValidationRule.AudienceOverlap,
            ValidationSeverity.Warning,
            $"{total} same-audience time overlap(s): {parallelOverlaps.Count} parallel "
            + "offering(s) of different courses (expected — for example electives or a "
            + $"make-up exam), {freeStudyOverlaps.Count} free-study availability overlap(s), "
            + $"and {duplicateOverlaps.Count} same-course overlap(s). Parallel/free-study "
            + "offerings do not block publication.",
            atUtc,
            detail: Detail(
                duplicateOverlaps
                    .Concat(parallelOverlaps)
                    .Concat(freeStudyOverlaps)
                    .Take(20)),
            affectedRecordCount: total));
    }

    private static bool IsFreeStudy(CanonicalScheduleRecord record) =>
        record.EventType is Sirkadiyen.Domain.Scheduling.Publication.ScheduleEventType.FreeStudy;

    // Same course when the normalized identity matches; the display title is the
    // fallback when a record has no resolved identity, so an unresolved duplicate is
    // still caught rather than silently treated as a parallel offering.
    private static bool IsSameCourse(CanonicalScheduleRecord a, CanonicalScheduleRecord b) =>
        string.Equals(CourseKey(a), CourseKey(b), StringComparison.OrdinalIgnoreCase);

    private static string CourseKey(CanonicalScheduleRecord record) =>
        record.NormalizedCourseIdentity is { Length: > 0 } identity ? identity : record.DisplayTitle;

    private void ValidateDeletionShare(
        RevisionValidationInput input,
        List<RevisionValidationFinding> findings,
        DateTimeOffset atUtc)
    {
        if (!input.HasPreviousPublishedRevision)
        {
            // Nothing has been published for this source, so nothing can vanish.
            return;
        }

        HashSet<string> current = input.Records
            .Select(record => record.StableIdentity)
            .ToHashSet(StringComparer.Ordinal);

        List<string> disappeared = input.PreviouslyPublishedIdentities
            .Where(identity => !current.Contains(identity))
            .ToList();

        if (disappeared.Count == 0)
        {
            return;
        }

        int priorCount = input.PreviouslyPublishedIdentities.Count;
        double share = priorCount == 0 ? 0 : (double)disappeared.Count / priorCount;

        // Both conditions must hold (ADR-029). The share alone is meaningless on
        // a small source, and the count alone is noisy on a large one.
        bool quarantine = share > options.MaximumDeletionShare
            && disappeared.Count >= options.MinimumDeletionCount;

        findings.Add(Finding(
            input,
            RevisionValidationRule.MassDeletion,
            quarantine ? ValidationSeverity.Error : ValidationSeverity.Information,
            $"{disappeared.Count} of {priorCount} previously published record(s) "
            + $"({share.ToString("P1", CultureInfo.InvariantCulture)}) are absent from "
            + "this revision.",
            atUtc,
            detail: Detail(disappeared.Take(20)),
            affectedRecordCount: disappeared.Count));
    }

    private static IEnumerable<AudienceSelector> ReadSelectors(CanonicalScheduleRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.AudienceSelectors))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<AudienceSelector>>(
                record.AudienceSelectors,
                JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Record '{record.CandidateId}' stores unreadable audience selectors.",
                exception);
        }
    }

    /// <summary>
    /// Reads an academic year label such as <c>2025-2026</c> into the date range
    /// a lesson may plausibly fall in.
    /// </summary>
    /// <remarks>
    /// The range runs from 1 August of the first year to 31 July of the second,
    /// which covers a Turkish medical faculty year including resit periods. The
    /// caller widens it by a configured grace period.
    /// </remarks>
    private static bool TryReadAcademicYear(string academicYear, out DateOnly start, out DateOnly end)
    {
        start = default;
        end = default;

        string[] parts = academicYear.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out int first)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int second)
            || second != first + 1)
        {
            return false;
        }

        start = new DateOnly(first, 8, 1);
        end = new DateOnly(second, 7, 31);
        return true;
    }

    private static string DescribeQuarantine(IEnumerable<RevisionValidationFinding> findings) =>
        "Held for review: "
        + string.Join(
            ", ",
            findings
                .Where(finding => finding.Severity is ValidationSeverity.Error)
                .Select(finding => finding.Rule.ToString()));

    private static RevisionValidationFinding Finding(
        RevisionValidationInput input,
        RevisionValidationRule rule,
        ValidationSeverity severity,
        string message,
        DateTimeOffset atUtc,
        string? detail = null,
        int affectedRecordCount = 0) => new(
            input.Revision.Id,
            rule,
            severity,
            message,
            atUtc,
            detail,
            affectedRecordCount);

    /// <summary>
    /// Renders a date the same way on every host.
    /// </summary>
    /// <remarks>
    /// Findings are written to the database and read back by whoever reviews the
    /// revision, possibly on another machine. Stored evidence must not depend on
    /// the culture of the host that produced it, which is also why the thresholds
    /// in these messages are formatted invariantly.
    /// </remarks>
    private static string Invariant(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Detail<T>(IEnumerable<T> values) =>
        JsonSerializer.Serialize(values, JsonOptions);
}

/// <summary>A revision that kept nothing of what is published, and by how much.</summary>
internal readonly record struct ParseCollapse(int PreviousCount, int CurrentCount);

/// <summary>Everything revision validation needs, gathered by the caller.</summary>
public sealed record RevisionValidationInput
{
    public required ScheduleRevision Revision { get; init; }

    public required ScheduleSource Source { get; init; }

    public required IReadOnlyList<CanonicalScheduleRecord> Records { get; init; }

    /// <summary>
    /// Distinguishes "no previous revision" from "a previous revision that was
    /// empty", which the deletion rule must not confuse.
    /// </summary>
    public required bool HasPreviousPublishedRevision { get; init; }

    public IReadOnlyCollection<string> PreviouslyPublishedIdentities { get; init; } = [];

    /// <summary>
    /// The dates the parser reported as contradicting the order of the column
    /// they sit in (ADR-139), read out of the parse run's own response.
    /// </summary>
    /// <remarks>
    /// This is the one input the validator takes from the parser's account of
    /// itself rather than from the records. It has to be: a date the parser
    /// repaired leaves a record that looks perfectly ordinary, and a date it
    /// refused to repair leaves one that only looks wrong next to its neighbours
    /// — which the flat record list no longer holds.
    /// <para>
    /// Empty is the ordinary case and means the parse reported none, not that
    /// none were looked for.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ParserDateAnomaly> DateAnomalies { get; init; } = [];
}

/// <summary>
/// One date the parser reported as out of sequence, and what it did about it
/// (ADR-139).
/// </summary>
/// <remarks>
/// The shape mirrors the parser warning's own detail, because it is what the
/// review screen renders and what the "accept this date" action reads: accepting
/// a candidate writes a source date correction from <see cref="Original"/> to
/// that candidate.
/// </remarks>
public sealed record ParserDateAnomaly
{
    public required DateOnly Original { get; init; }

    /// <summary>The date the parser published instead, or null when it published the original.</summary>
    public DateOnly? Applied { get; init; }

    public DateOnly? LowerAnchor { get; init; }

    public DateOnly? UpperAnchor { get; init; }

    /// <summary>Which rule decided the outcome, in the parser's own vocabulary.</summary>
    public required string Reason { get; init; }

    /// <summary>Where in the document the date is, so a reviewer can open the cell.</summary>
    public string? Cell { get; init; }

    public IReadOnlyList<ParserDateCandidate> Candidates { get; init; } = [];

    public bool Repaired => Applied is not null;
}

/// <summary>One date an out-of-sequence cell may have meant.</summary>
public sealed record ParserDateCandidate
{
    public required DateOnly Value { get; init; }

    public required string Rule { get; init; }

    /// <summary>Whether the cell's own weekday agrees; null when it states none.</summary>
    public bool? WeekdayMatches { get; init; }
}

public sealed record RevisionValidationResult
{
    /// <summary>The state the revision should move to: never <c>Published</c>.</summary>
    public required RevisionState Outcome { get; init; }

    public required IReadOnlyList<RevisionValidationFinding> Findings { get; init; }

    public required string StateReason { get; init; }
}
