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

        if (matches.Count > 1)
        {
            // Two rows claim one student. Choosing between them is exactly what
            // ADR-085 forbids, and the published lists already contain a case:
            // one number is on both the Grade 2 and the Grade 3 Turkish list.
            return new StudentRosterLookupResult
            {
                Outcome = StudentRosterLookupOutcome.Ambiguous,
                StudentNumber = number,
                ConflictingRosterIds =
                    [.. matches.Select(match => match.Reading.RosterId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                UnreadableRosterIds = unreadable,
            };
        }

        (StudentRosterReading reading, StudentRosterEntry entry) = matches[0];
        return Suggest(number, reading, entry, unreadable);
    }

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
