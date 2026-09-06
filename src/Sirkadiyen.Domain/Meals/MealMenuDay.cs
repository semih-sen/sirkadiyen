namespace Sirkadiyen.Domain.Meals;

/// <summary>
/// The faculty cafeteria menu acquired for one date and meal (ADR-150).
/// </summary>
/// <remarks>
/// This is the meal counterpart of a published schedule revision, but deliberately far lighter: the
/// menu is not a claim about the academic schedule, so it produces no canonical lesson, no revision
/// and no academic diff (ADR-107 drew this boundary for announcements). What it keeps from the
/// schedule pipeline is the part that makes calendar writes safe — a content hash for change
/// detection and a content version that a stale delivery is patched up to.
/// <para>
/// The API cannot distinguish "no menu today" (a weekend or holiday) from "not published yet" (a
/// future month) from a transient failure: all three return the same empty answer. So a date that
/// once had a menu is only <see cref="MealMenuDayStatus.Withdrawn"/> after several consecutive
/// confirmed misses, never on the first — the same conservatism the schedule applies to a source it
/// briefly cannot read (AI_GUIDELINE §13).
/// </para>
/// </remarks>
public sealed class MealMenuDay
{
    /// <summary>The longest menu text kept. Generous; a day lists a handful of dishes.</summary>
    public const int MaximumMealTextLength = 2000;

    public const int MaximumContentHashLength = 128;

    private MealMenuDay()
    {
        // Materialization constructor.
        MealText = string.Empty;
        ContentHash = string.Empty;
    }

    public Guid Id { get; private init; }

    public DateOnly LocalDate { get; private init; }

    public MealCategory Category { get; private init; }

    /// <summary>The dishes, newline-separated exactly as the source normalized them.</summary>
    public string MealText { get; private set; }

    /// <summary>The hash of the normalized menu text; change detection compares this.</summary>
    public string ContentHash { get; private set; }

    /// <summary>
    /// Increments on every content change. A delivery whose applied version is lower is patched,
    /// which is how a corrected menu reaches calendars without creating a second event.
    /// </summary>
    public int ContentVersion { get; private set; }

    public MealMenuDayStatus Status { get; private set; }

    /// <summary>
    /// Consecutive polls that found no menu since the last one that did. Reset by any confirmed
    /// content; the withdrawal threshold is applied by the caller so it stays configuration.
    /// </summary>
    public int ConsecutiveMissCount { get; private set; }

    public DateTimeOffset FirstSeenAtUtc { get; private init; }

    /// <summary>When a poll last returned a menu for this date, changed or not.</summary>
    public DateTimeOffset LastConfirmedAtUtc { get; private set; }

    /// <summary>When the menu text last actually changed.</summary>
    public DateTimeOffset LastChangedAtUtc { get; private set; }

    public DateTimeOffset? WithdrawnAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token, backed by the PostgreSQL system column.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>The first time a menu is seen for a date.</summary>
    public static MealMenuDay CreatePublished(
        DateOnly localDate,
        MealCategory category,
        string mealText,
        string contentHash,
        DateTimeOffset atUtc)
    {
        RequireUtc(atUtc);

        return new MealMenuDay
        {
            Id = Guid.CreateVersion7(),
            LocalDate = localDate,
            Category = category,
            MealText = RequiredBounded(mealText, MaximumMealTextLength, nameof(mealText)),
            ContentHash = RequiredBounded(contentHash, MaximumContentHashLength, nameof(contentHash)),
            ContentVersion = 1,
            Status = MealMenuDayStatus.Published,
            ConsecutiveMissCount = 0,
            FirstSeenAtUtc = atUtc,
            LastConfirmedAtUtc = atUtc,
            LastChangedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
        };
    }

    /// <summary>
    /// Records a poll that returned a menu. Any confirmed content clears an accumulating miss and,
    /// if the day had been withdrawn, republishes it. Returns whether the menu text changed, so the
    /// caller only reopens deliveries when there is something new to patch.
    /// </summary>
    public bool ApplyObservedContent(string mealText, string contentHash, DateTimeOffset atUtc)
    {
        RequireUtc(atUtc);
        string text = RequiredBounded(mealText, MaximumMealTextLength, nameof(mealText));
        string hash = RequiredBounded(contentHash, MaximumContentHashLength, nameof(contentHash));

        ConsecutiveMissCount = 0;
        LastConfirmedAtUtc = atUtc;
        UpdatedAtUtc = atUtc;

        bool changed = !string.Equals(ContentHash, hash, StringComparison.Ordinal);
        bool republished = Status is MealMenuDayStatus.Withdrawn;

        if (!changed && !republished)
        {
            return false;
        }

        MealText = text;
        ContentHash = hash;
        Status = MealMenuDayStatus.Published;
        WithdrawnAtUtc = null;

        if (changed)
        {
            // A withdrawn day coming back with the same text is not a content change — its version
            // must not move, or every delivery would be needlessly patched. It republishes above
            // and its existing version still matches what was written.
            ContentVersion++;
            LastChangedAtUtc = atUtc;
        }

        return changed || republished;
    }

    /// <summary>
    /// Records a poll that found no menu for this date. Withdraws the day once the misses reach the
    /// threshold, and only then — a single empty answer is far more likely a transient failure or a
    /// closed day than a genuine cancellation. Returns whether this call withdrew it.
    /// </summary>
    public bool RecordMiss(int withdrawalThreshold, DateTimeOffset atUtc)
    {
        RequireUtc(atUtc);
        ArgumentOutOfRangeException.ThrowIfLessThan(withdrawalThreshold, 1);

        UpdatedAtUtc = atUtc;

        if (Status is MealMenuDayStatus.Withdrawn)
        {
            return false;
        }

        ConsecutiveMissCount++;
        if (ConsecutiveMissCount < withdrawalThreshold)
        {
            return false;
        }

        Status = MealMenuDayStatus.Withdrawn;
        WithdrawnAtUtc = atUtc;
        return true;
    }

    private static void RequireUtc(DateTimeOffset atUtc)
    {
        if (atUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A menu-day timestamp must be expressed in UTC.", nameof(atUtc));
        }
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength, parameterName);
        return value;
    }
}
