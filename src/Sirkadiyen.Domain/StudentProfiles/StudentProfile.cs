using Sirkadiyen.Domain.ScheduleSources;

namespace Sirkadiyen.Domain.StudentProfiles;

/// <summary>
/// A student's academic profile: the class year, program language, and cohort
/// selectors that resolve which canonical lessons belong on their calendar.
/// </summary>
/// <remarks>
/// Academic year, class year and program language are relational because every
/// audience query filters on them. The variable cohort dimensions
/// (practice group, subgroup, anatomy group, and future rotation or elective
/// selectors) are a schema-versioned selector document, so a new class-year
/// requirement does not need a schema migration (ADR-055, systemPatterns §22).
/// <para>
/// This aggregate enforces only structural invariants. Whether a selector key or
/// value is <em>supported</em> is validated in the application layer against the
/// server-owned supported-profile schema; the domain never trusts a client claim
/// but also never carries the allowlist, which changes at academic-year rollover.
/// </para>
/// </remarks>
public sealed class StudentProfile
{
    public const int MaximumAcademicYearLength = 20;

    public const int MaximumSchemaVersionLength = 20;

    public const int MaximumSelectorKeyLength = 100;

    public const int MaximumSelectorValueLength = 100;

    public const int MaximumSelectorCount = 20;

    public const int MinimumClassYear = 1;

    public const int MaximumClassYear = 6;

    private StudentProfile()
    {
        // Materialization constructor.
        AcademicYear = string.Empty;
        SelectorSchemaVersion = string.Empty;
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private init; }

    public string AcademicYear { get; private set; }

    public int ClassYear { get; private set; }

    public ProgramLanguage ProgramLanguage { get; private set; }

    /// <summary>The version of the supported-profile schema the selectors satisfy.</summary>
    public string SelectorSchemaVersion { get; private set; }

    /// <summary>
    /// The cohort selectors, keyed by dimension, each holding the single value the
    /// student belongs to.
    /// </summary>
    public IReadOnlyDictionary<string, string> Selectors { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    public static StudentProfile Create(
        Guid userId,
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string selectorSchemaVersion,
        IReadOnlyDictionary<string, string> selectors,
        DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A profile owner is required.", nameof(userId));
        }

        return new StudentProfile
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            AcademicYear = RequiredBounded(
                academicYear,
                MaximumAcademicYearLength,
                nameof(academicYear)),
            ClassYear = ValidClassYear(classYear),
            ProgramLanguage = programLanguage,
            SelectorSchemaVersion = RequiredBounded(
                selectorSchemaVersion,
                MaximumSchemaVersionLength,
                nameof(selectorSchemaVersion)),
            Selectors = NormalizeSelectors(selectors),
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };
    }

    /// <summary>Replaces the mutable academic fields with a newly validated set.</summary>
    public void Update(
        string academicYear,
        int classYear,
        ProgramLanguage programLanguage,
        string selectorSchemaVersion,
        IReadOnlyDictionary<string, string> selectors,
        DateTimeOffset atUtc)
    {
        AcademicYear = RequiredBounded(
            academicYear,
            MaximumAcademicYearLength,
            nameof(academicYear));
        ClassYear = ValidClassYear(classYear);
        ProgramLanguage = programLanguage;
        SelectorSchemaVersion = RequiredBounded(
            selectorSchemaVersion,
            MaximumSchemaVersionLength,
            nameof(selectorSchemaVersion));
        Selectors = NormalizeSelectors(selectors);
        UpdatedAtUtc = atUtc;
    }

    private static int ValidClassYear(int classYear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(classYear, MinimumClassYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(classYear, MaximumClassYear);
        return classYear;
    }

    private static IReadOnlyDictionary<string, string> NormalizeSelectors(
        IReadOnlyDictionary<string, string> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            selectors.Count,
            MaximumSelectorCount,
            nameof(selectors));

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach ((string key, string value) in selectors)
        {
            string normalizedKey = RequiredBounded(
                key,
                MaximumSelectorKeyLength,
                nameof(selectors));
            string normalizedValue = RequiredBounded(
                value,
                MaximumSelectorValueLength,
                nameof(selectors));

            if (!normalized.TryAdd(normalizedKey, normalizedValue))
            {
                // A dictionary cannot hold a duplicate key, but two keys can trim
                // to the same value; that is an ambiguous selector, not a merge.
                throw new ArgumentException(
                    $"Selector dimension '{normalizedKey}' is specified more than once.",
                    nameof(selectors));
            }
        }

        return normalized;
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}
