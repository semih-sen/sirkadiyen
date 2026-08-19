using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.StudentProfiles;

/// <summary>
/// Answers whether the deployed supported-profile schema and the running source catalog still
/// agree about which academic year each cohort is on (ADR-115).
/// </summary>
/// <remarks>
/// This is the check whose absence cost a cohort a year of calendars. A student's profile is
/// stamped with their <em>program's</em> academic year, and
/// <c>CalendarAudienceResolver.Applies</c> matches a canonical record to them only when the
/// record's year equals it. The catalog is editable at runtime from <c>/admin/sources</c>
/// (ADR-114) while the schema is compiled in, so an operator moving a cohort's sources to a new
/// year makes the two disagree in a way that no downstream check notices: the revision publishes,
/// the diff dispatches, the ledger-driven deletions fire, and the cohort-driven insertions resolve
/// to nobody, because the cohort query filters profiles on the record's year.
/// <para>
/// It is deliberately a pure comparison rather than a guard that blocks anything. Refusing to
/// publish on a mismatch would let one mistyped catalog field stop every program; refusing to
/// start would do worse. The divergence is a thing to report loudly and fix with a rollover, not
/// a reason to take the pipeline down.
/// </para>
/// </remarks>
public static class SupportedProfileSchemaCatalogCheck
{
    /// <summary>
    /// Every cohort whose sources state a year the schema does not stamp on its students.
    /// </summary>
    /// <remarks>
    /// A cohort the schema declares no program for is not a divergence: Grade 2 English and
    /// Grade 3 English have catalog sources and deliberately no onboarding, for reasons their own
    /// ADRs record (ADR-084, ADR-098). Silence about them is correct; nobody is stamped with
    /// anything, so nobody can be stamped with the wrong thing.
    /// </remarks>
    public static IReadOnlyList<AcademicYearDivergence> FindDivergences(
        SupportedProfileSchema schema,
        IEnumerable<CohortPublishedYear> catalogCohorts)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(catalogCohorts);

        List<AcademicYearDivergence> divergences = [];

        foreach (SupportedProfileProgram program in schema.Programs)
        {
            HashSet<string> published =
            [
                .. catalogCohorts
                    .Where(cohort => cohort.ClassYear == program.ClassYear
                        && cohort.ProgramLanguage == program.ProgramLanguage)
                    .Select(cohort => cohort.AcademicYear),
            ];

            // A cohort with no sources at all is a different problem — nothing publishes for it,
            // which the source-status surface already shows — and reporting it here would bury
            // the one signal this exists for.
            if (published.Count == 0 || published.Contains(program.AcademicYear))
            {
                continue;
            }

            divergences.Add(new AcademicYearDivergence
            {
                ClassYear = program.ClassYear,
                ProgramLanguage = program.ProgramLanguage,
                SchemaAcademicYear = program.AcademicYear,
                PublishedAcademicYears = [.. published.OrderBy(year => year, StringComparer.Ordinal)],
            });
        }

        return divergences;
    }
}

/// <summary>One cohort's academic year as the running catalog states it.</summary>
public sealed record CohortPublishedYear
{
    public required string AcademicYear { get; init; }

    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }
}

/// <summary>
/// A cohort whose students are stamped with one academic year while every source publishing to
/// them states another. Every calendar in it is receiving nothing.
/// </summary>
public sealed record AcademicYearDivergence
{
    public required int ClassYear { get; init; }

    public required ProgramLanguage ProgramLanguage { get; init; }

    /// <summary>The year new and existing profiles in this program carry.</summary>
    public required string SchemaAcademicYear { get; init; }

    /// <summary>The years the catalog's sources for this cohort actually state.</summary>
    public required IReadOnlyList<string> PublishedAcademicYears { get; init; }

    public override string ToString() =>
        $"class {ClassYear} {ProgramLanguage}: profiles are stamped {SchemaAcademicYear} but "
        + $"sources publish {string.Join(", ", PublishedAcademicYears)}";
}
