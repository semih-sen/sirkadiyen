using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.StudentProfiles;

namespace Sirkadiyen.Application.StudentRosters;

/// <summary>
/// Looks a student number up in the published faculty lists and turns what they
/// state into profile suggestions (ADR-085).
/// </summary>
/// <remarks>
/// Everything this returns is a suggestion the student confirms or changes. The
/// service never persists anything, never decides that a profile is complete,
/// and never resolves a number two lists both claim.
/// <para>
/// The student is not asked which program they are in before the lookup. The
/// number identifies them, and the list that holds it states the cohort, so a
/// student who would have mis-selected their own class year is corrected by the
/// faculty's own document rather than by a guess.
/// </para>
/// </remarks>
public sealed class StudentRosterLookupService(
    IStudentRosterIndex index,
    SupportedProfileSchema schema)
{
    public async Task<StudentRosterLookupResult> LookUpAsync(
        string studentNumber,
        CancellationToken cancellationToken)
    {
        string number = (studentNumber ?? string.Empty).Trim();

        if (number.Length != StudentProfile.StudentNumberLength || !number.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                $"A student number must be exactly {StudentProfile.StudentNumberLength} digits.",
                nameof(studentNumber));
        }

        StudentRosterIndexSnapshot snapshot = await index.GetAsync(cancellationToken);

        List<(StudentRosterReading Reading, StudentRosterEntry Entry)> matches = [.. snapshot.Readings
            .SelectMany(reading => reading.Entries
                .Where(entry => string.Equals(entry.StudentNumber, number, StringComparison.Ordinal))
                .Select(entry => (Reading: reading, Entry: entry)))];

        IReadOnlyList<string> unreadable = [.. snapshot.Failures.Keys.Order(StringComparer.Ordinal)];

        if (matches.Count == 0)
        {
            return new StudentRosterLookupResult
            {
                Outcome = StudentRosterLookupOutcome.NotFound,
                StudentNumber = number,
                UnreadableRosterIds = unreadable,
            };
        }

        if (matches.Count == 1)
        {
            return Suggest(number, matches[0].Reading, matches[0].Entry, unreadable);
        }

        // More than one row states this number. Two cases hide here and only one is
        // ambiguous. A number on two rows of one list, or on lists for different
        // cohorts, cannot be resolved and stays ambiguous (ADR-085): the Grade 2 and
        // Grade 3 Turkish lists share such a number. But a number on two complementary
        // lists of one cohort — the curriculum-group list and the microbiology/pathology
        // group list both hold every Grade 3 student — is not a conflict; the two are
        // merged into one suggestion (ADR-145).
        bool sameListTwice = matches
            .GroupBy(match => match.Reading.RosterId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

        bool multipleCohorts = matches
            .Select(match =>
                (match.Reading.AcademicYear, match.Reading.ClassYear, match.Reading.ProgramLanguage))
            .Distinct()
            .Count() > 1;

        if (sameListTwice || multipleCohorts || !TryMerge(matches, out StudentRosterEntry merged))
        {
            return new StudentRosterLookupResult
            {
                Outcome = StudentRosterLookupOutcome.Ambiguous,
                StudentNumber = number,
                ConflictingRosterIds =
                    [.. matches.Select(match => match.Reading.RosterId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                UnreadableRosterIds = unreadable,
            };
        }

        // The representative list is the one that states the most, so the reported
        // roster id names the list that carried the cohort's selectors.
        StudentRosterReading representative = matches
            .OrderByDescending(match => match.Entry.Selectors.Count)
            .ThenBy(match => match.Reading.RosterId, StringComparer.Ordinal)
            .First()
            .Reading;

        return Suggest(number, representative, merged, unreadable);
    }

    /// <summary>
    /// Unions what several complementary lists of one cohort state about a student,
    /// or reports that two of them disagree on a value (ADR-145).
    /// </summary>
    private static bool TryMerge(
        IReadOnlyList<(StudentRosterReading Reading, StudentRosterEntry Entry)> matches,
        out StudentRosterEntry merged)
    {
        merged = default!;
        Dictionary<string, string> selectors = new(StringComparer.Ordinal);
        foreach ((_, StudentRosterEntry entry) in matches)
        {
            foreach ((string dimension, string value) in entry.Selectors)
            {
                if (selectors.TryGetValue(dimension, out string? existing)
                    && !string.Equals(existing, value, StringComparison.Ordinal))
                {
                    // The catalog forbids two lists of one cohort stating the same
                    // dimension, so this is a defence against a value that slipped
                    // through rather than an expected case.
                    return false;
                }

                selectors[dimension] = value;
            }
        }

        (_, StudentRosterEntry first) = matches[0];
        merged = first with
        {
            GivenName = FirstNonEmpty(matches, static entry => entry.GivenName),
            FamilyName = FirstNonEmpty(matches, static entry => entry.FamilyName),
            Selectors = selectors,
        };
        return true;
    }

    private static string FirstNonEmpty(
        IReadOnlyList<(StudentRosterReading Reading, StudentRosterEntry Entry)> matches,
        Func<StudentRosterEntry, string> select) =>
        matches
            .Select(match => select(match.Entry))
            .FirstOrDefault(static value => !string.IsNullOrEmpty(value))
        ?? string.Empty;

    private StudentRosterLookupResult Suggest(
        string number,
        StudentRosterReading reading,
        StudentRosterEntry entry,
        IReadOnlyList<string> unreadable)
    {
        List<StudentRosterLookupNotice> notices = [];
        Dictionary<string, string> suggested = new(StringComparer.Ordinal);
        List<string> requiringInput = [];

        SupportedProfileProgram? program = schema.FindProgram(
            reading.ClassYear,
            reading.ProgramLanguage);

        if (program is null)
        {
            notices.Add(new StudentRosterLookupNotice
            {
                Code = StudentRosterLookupNoticeCode.ProgramNotOnboardable,
                Message = $"Class year {reading.ClassYear} {reading.ProgramLanguage} is not a "
                    + "program this year's supported-profile schema declares, so the list confirms "
                    + "who you are and suggests nothing else.",
            });
        }
        else if (!string.Equals(program.AcademicYear, reading.AcademicYear, StringComparison.Ordinal))
        {
            notices.Add(new StudentRosterLookupNotice
            {
                Code = StudentRosterLookupNoticeCode.RosterYearDiffersFromProgram,
                Message = $"The list is catalogued for {reading.AcademicYear} while the program is "
                    + $"on {program.AcademicYear}, so nothing it states is suggested.",
            });
        }
        else
        {
            Fill(program, entry, suggested, requiringInput, notices);
        }

        return new StudentRosterLookupResult
        {
            Outcome = StudentRosterLookupOutcome.Matched,
            StudentNumber = number,
            RosterId = reading.RosterId,
            GivenName = entry.GivenName,
            FamilyName = entry.FamilyName,
            AcademicYear = reading.AcademicYear,
            ClassYear = reading.ClassYear,
            ProgramLanguage = reading.ProgramLanguage,
            SuggestedSelectors = suggested,
            DimensionsRequiringInput = requiringInput,
            Notices = notices,
            UnreadableRosterIds = unreadable,
        };
    }

    /// <summary>
    /// Accepts the values the program declares and explains every one it does not.
    /// </summary>
    /// <remarks>
    /// Dimensions are walked in declaration order so a dependent one is checked
    /// against the parent value that was actually accepted, not against the raw
    /// value the list wrote. A subgroup whose group was rejected is rejected with
    /// it, because <c>A1</c> means nothing without <c>A</c>.
    /// </remarks>
    private static void Fill(
        SupportedProfileProgram program,
        StudentRosterEntry entry,
        Dictionary<string, string> suggested,
        List<string> requiringInput,
        List<StudentRosterLookupNotice> notices)
    {
        foreach (SupportedProfileDimension dimension in program.Dimensions)
        {
            if (!entry.Selectors.TryGetValue(dimension.Key, out string? stated))
            {
                if (dimension.Required)
                {
                    requiringInput.Add(dimension.Key);
                    notices.Add(new StudentRosterLookupNotice
                    {
                        Code = StudentRosterLookupNoticeCode.DimensionNotStatedByRoster,
                        Dimension = dimension.Key,
                        Message = $"The list does not state '{dimension.Key}', so you choose it "
                            + "yourself.",
                    });
                }

                continue;
            }

            string? parent = dimension.DependsOn is { } parentKey
                ? suggested.GetValueOrDefault(parentKey)
                : null;

            if (dimension.AllowedValuesFor(parent).Contains(stated, StringComparer.Ordinal))
            {
                suggested[dimension.Key] = stated;
                continue;
            }

            if (dimension.Required)
            {
                requiringInput.Add(dimension.Key);
            }

            notices.Add(new StudentRosterLookupNotice
            {
                Code = StudentRosterLookupNoticeCode.ValueNotSupportedByProgram,
                Dimension = dimension.Key,
                Message = $"The list states '{stated}' for '{dimension.Key}', which this program "
                    + "does not allow, so it is not filled in for you.",
            });
        }

        // A dimension the list states that the program never heard of. It is
        // reported rather than ignored: a list still shaped for last year's
        // structure looks exactly like this.
        foreach (string key in entry.Selectors.Keys
            .Where(key => program.FindDimension(key) is null)
            .Order(StringComparer.Ordinal))
        {
            notices.Add(new StudentRosterLookupNotice
            {
                Code = StudentRosterLookupNoticeCode.DimensionNotDeclaredByProgram,
                Dimension = key,
                Message = $"The list states '{key}', which this program does not declare.",
            });
        }
    }
}
