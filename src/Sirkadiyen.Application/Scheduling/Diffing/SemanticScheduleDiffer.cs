using System.Globalization;
using System.Text;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;

namespace Sirkadiyen.Application.Scheduling.Diffing;

/// <summary>
/// Calculates a deterministic semantic diff between two revisions (ADR-018,
/// ADR-035).
/// </summary>
/// <remarks>
/// Stable identity is authoritative. Secondary matching is attempted only for
/// records left unmatched by identity, and only when the old and new records
/// explicitly state a lesson name and an instructor.
/// <para>
/// The academic department participates only when both records name exactly one,
/// which is the strong case. When the source names none, or names several for an
/// integrated session, the match is made on title and instructor against a
/// higher composite bar (ADR-035 as amended). Which basis was used is visible on
/// the entry: <c>DepartmentScore</c> is null for a match made without one.
/// </para>
/// A many-to-one or one-to-many candidate set remains ambiguous and is never
/// converted into a destructive delete-and-create pair. Such a set yields one
/// ambiguous entry per record it drew in, not one per candidate pair, because a
/// record may be classified only once within a diff.
/// <para>
/// One kind of contested set is settled rather than held: candidates whose two
/// records kept the very same local slot are offered first, and a candidate that
/// is unique among those is a match (ADR-165). A source-wide rewording is the
/// case this exists for; a lesson that was reworded and moved is still held.
/// </para>
/// </remarks>
public sealed class SemanticScheduleDiffer
{
    private readonly SemanticDiffOptions options;

    public SemanticScheduleDiffer(SemanticDiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
    }

    public IReadOnlyList<ScheduleDiffEntry> Diff(
        IReadOnlyCollection<CanonicalScheduleRecord> previous,
        IReadOnlyCollection<CanonicalScheduleRecord> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        Dictionary<string, CanonicalScheduleRecord> previousByIdentity = IndexByIdentity(
            previous,
            nameof(previous));
        Dictionary<string, CanonicalScheduleRecord> currentByIdentity = IndexByIdentity(
            current,
            nameof(current));

        List<ScheduleDiffEntry> entries = [];
        HashSet<Guid> matchedPrevious = [];
        HashSet<Guid> matchedCurrent = [];

        foreach (string identity in previousByIdentity.Keys
                     .Intersect(currentByIdentity.Keys, StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            CanonicalScheduleRecord oldRecord = previousByIdentity[identity];
            CanonicalScheduleRecord newRecord = currentByIdentity[identity];
            matchedPrevious.Add(oldRecord.Id);
            matchedCurrent.Add(newRecord.Id);

            entries.Add(new ScheduleDiffEntry
            {
                Change = string.Equals(
                    oldRecord.ContentHash,
                    newRecord.ContentHash,
                    StringComparison.Ordinal)
                    ? ScheduleDiffChange.Unchanged
                    : ScheduleDiffChange.Updated,
                Match = ScheduleDiffMatch.ExactStableIdentity,
                PreviousRecordId = oldRecord.Id,
                CurrentRecordId = newRecord.Id,
                MatchScore = 1m,
            });
        }

        List<CanonicalScheduleRecord> unmatchedPrevious = previous
            .Where(record => !matchedPrevious.Contains(record.Id))
            .OrderBy(record => record.Id)
            .ToList();
        List<CanonicalScheduleRecord> unmatchedCurrent = current
            .Where(record => !matchedCurrent.Contains(record.Id))
            .OrderBy(record => record.Id)
            .ToList();

        List<SecondaryCandidate> candidates = BuildSecondaryCandidates(
            unmatchedPrevious,
            unmatchedCurrent);
        AddSecondaryMatches(
            candidates,
            entries,
            matchedPrevious,
            matchedCurrent);

        foreach (CanonicalScheduleRecord oldRecord in unmatchedPrevious
                     .Where(record => !matchedPrevious.Contains(record.Id)))
        {
            entries.Add(new ScheduleDiffEntry
            {
                Change = ScheduleDiffChange.Deleted,
                Match = ScheduleDiffMatch.None,
                PreviousRecordId = oldRecord.Id,
            });
        }

        foreach (CanonicalScheduleRecord newRecord in unmatchedCurrent
                     .Where(record => !matchedCurrent.Contains(record.Id)))
        {
            entries.Add(new ScheduleDiffEntry
            {
                Change = ScheduleDiffChange.Created,
                Match = ScheduleDiffMatch.None,
                CurrentRecordId = newRecord.Id,
            });
        }

        return entries
            .OrderBy(entry => entry.Change)
            .ThenBy(entry => entry.PreviousRecordId)
            .ThenBy(entry => entry.CurrentRecordId)
            .ToList();
    }

    private List<SecondaryCandidate> BuildSecondaryCandidates(
        IReadOnlyCollection<CanonicalScheduleRecord> previous,
        IReadOnlyCollection<CanonicalScheduleRecord> current)
    {
        List<SecondaryCandidate> candidates = [];

        foreach (CanonicalScheduleRecord oldRecord in previous)
        {
            foreach (CanonicalScheduleRecord newRecord in current)
            {
                if (!HasSameStructuralContext(oldRecord, newRecord)
                    || !TryScore(oldRecord, newRecord, out SecondaryCandidate candidate))
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Turns the candidate set into entries: unique pairs become updates, and what stays contested
    /// becomes ambiguity. Candidates that keep their slot are offered first (ADR-165).
    /// </summary>
    /// <remarks>
    /// A contested set is not the same thing as an undecidable one. When a source rewords a word
    /// across a whole course — "drog" became "ilaç" in the pharmacology tables — every lesson of
    /// that hour scores against every other, because secondary matching deliberately ignores the
    /// start time so that a moved lesson can still be recognized. Lecture I at 11:00 then has two
    /// plausible successors, and so does lecture II at 11:50, and the whole revision is held.
    /// <para>
    /// But those two pairs are not actually in doubt: each candidate's records occupy the very same
    /// slot, on a date and an audience that <see cref="HasSameStructuralContext"/> has already
    /// pinned. So a first pass considers only candidates that kept their slot, and accepts the ones
    /// that are unique among <em>those</em>. It settles a rename that did not move anything, and
    /// says nothing about a lesson that moved: a reworded <em>and</em> moved lesson has no anchored
    /// candidate, stays in the second pass, and is held exactly as before.
    /// </para>
    /// The second pass then reconsiders what is left over the records the first pass did not take,
    /// because a record that is now matched is no longer anybody's candidate.
    /// </remarks>
    private static void AddSecondaryMatches(
        IReadOnlyCollection<SecondaryCandidate> candidates,
        List<ScheduleDiffEntry> entries,
        HashSet<Guid> matchedPrevious,
        HashSet<Guid> matchedCurrent)
    {
        AcceptUncontestedCandidates(
            [.. candidates.Where(candidate => candidate.SharesExactSlot)],
            entries,
            matchedPrevious,
            matchedCurrent);

        List<SecondaryCandidate> remaining =
        [
            .. candidates.Where(candidate =>
                !matchedPrevious.Contains(candidate.Previous.Id)
                && !matchedCurrent.Contains(candidate.Current.Id)),
        ];

        List<SecondaryCandidate> contested = AcceptUncontestedCandidates(
            remaining,
            entries,
            matchedPrevious,
            matchedCurrent);

        AddAmbiguousSides(contested, entries, matchedPrevious, matchedCurrent);
    }

    /// <summary>
    /// Accepts every candidate that is the only one its two records take part in, and returns the
    /// candidates that were contested within this set.
    /// </summary>
    private static List<SecondaryCandidate> AcceptUncontestedCandidates(
        IReadOnlyCollection<SecondaryCandidate> candidates,
        List<ScheduleDiffEntry> entries,
        HashSet<Guid> matchedPrevious,
        HashSet<Guid> matchedCurrent)
    {
        IReadOnlyDictionary<Guid, int> previousCandidateCounts = candidates
            .GroupBy(candidate => candidate.Previous.Id)
            .ToDictionary(group => group.Key, group => group.Count());
        IReadOnlyDictionary<Guid, int> currentCandidateCounts = candidates
            .GroupBy(candidate => candidate.Current.Id)
            .ToDictionary(group => group.Key, group => group.Count());

        List<SecondaryCandidate> contested = [];

        foreach (SecondaryCandidate candidate in candidates
                     .OrderBy(candidate => candidate.Previous.Id)
                     .ThenBy(candidate => candidate.Current.Id))
        {
            if (previousCandidateCounts[candidate.Previous.Id] != 1
                || currentCandidateCounts[candidate.Current.Id] != 1)
            {
                contested.Add(candidate);
                continue;
            }

            entries.Add(candidate.ToEntry(ScheduleDiffChange.Updated));
            matchedPrevious.Add(candidate.Previous.Id);
            matchedCurrent.Add(candidate.Current.Id);
        }

        return contested;
    }

    /// <summary>
    /// Records one <see cref="ScheduleDiffChange.Ambiguous"/> entry per record
    /// drawn into a contested candidate set, rather than one per candidate pair.
    /// </summary>
    /// <remarks>
    /// A contested set is many-to-one or one-to-many by nature, but a record may
    /// be classified only once within a diff — the entry table enforces that on
    /// both sides. One entry per pair therefore names the same record twice and
    /// cannot be stored at all, which leaves the revision undiffed rather than
    /// held. The pairing itself is not what the ambiguity means: it means these
    /// records could not be told apart, and every one of them must be visible to
    /// the operator reviewing the hold. Each entry carries its record's
    /// best-scoring candidate as evidence, and the opposite side stays null
    /// because no single counterpart was chosen.
    /// </remarks>
    private static void AddAmbiguousSides(
        IReadOnlyCollection<SecondaryCandidate> contested,
        List<ScheduleDiffEntry> entries,
        HashSet<Guid> matchedPrevious,
        HashSet<Guid> matchedCurrent)
    {
        foreach (IGrouping<Guid, SecondaryCandidate> group in contested
                     .GroupBy(candidate => candidate.Previous.Id)
                     .OrderBy(group => group.Key))
        {
            entries.Add(BestOf(group).ToPreviousSideEntry(ScheduleDiffChange.Ambiguous));
            matchedPrevious.Add(group.Key);
        }

        foreach (IGrouping<Guid, SecondaryCandidate> group in contested
                     .GroupBy(candidate => candidate.Current.Id)
                     .OrderBy(group => group.Key))
        {
            entries.Add(BestOf(group).ToCurrentSideEntry(ScheduleDiffChange.Ambiguous));
            matchedCurrent.Add(group.Key);
        }
    }

    /// <summary>
    /// The strongest candidate a record took part in, resolving equal scores by
    /// the counterpart identifiers so the evidence is deterministic.
    /// </summary>
    private static SecondaryCandidate BestOf(IEnumerable<SecondaryCandidate> candidates) =>
        candidates
            .OrderByDescending(candidate => candidate.CompositeScore)
            .ThenBy(candidate => candidate.Previous.Id)
            .ThenBy(candidate => candidate.Current.Id)
            .First();

    private bool TryScore(
        CanonicalScheduleRecord previous,
        CanonicalScheduleRecord current,
        out SecondaryCandidate candidate)
    {
        string previousTitle = previous.NormalizedCourseIdentity ?? previous.DisplayTitle;
        string currentTitle = current.NormalizedCourseIdentity ?? current.DisplayTitle;

        if (string.IsNullOrWhiteSpace(previousTitle)
            || string.IsNullOrWhiteSpace(currentTitle)
            || string.IsNullOrWhiteSpace(previous.Instructor)
            || string.IsNullOrWhiteSpace(current.Instructor))
        {
            candidate = null!;
            return false;
        }

        decimal title = Similarity(previousTitle, currentTitle);
        decimal instructor = Similarity(previous.Instructor, current.Instructor);

        if (title < options.MinimumTitleSimilarity
            || instructor < options.MinimumInstructorSimilarity)
        {
            candidate = null!;
            return false;
        }

        return TryScoreWithDepartment(previous, current, title, instructor, out candidate)
            || TryScoreWithoutDepartment(previous, current, title, instructor, out candidate);
    }

    private bool TryScoreWithDepartment(
        CanonicalScheduleRecord previous,
        CanonicalScheduleRecord current,
        decimal title,
        decimal instructor,
        out SecondaryCandidate candidate)
    {
        candidate = null!;

        // Only one named department on each side is comparable. None and several
        // both fall through to the two-attribute rule rather than being refused,
        // because refusing would mean deleting and recreating the lesson.
        if (previous.ComparableDepartment is not { } previousDepartment
            || current.ComparableDepartment is not { } currentDepartment)
        {
            return false;
        }

        decimal department = Similarity(previousDepartment, currentDepartment);
        decimal composite = (title * options.TitleWeight)
            + (instructor * options.InstructorWeight)
            + (department * options.DepartmentWeight);

        if (department < options.MinimumDepartmentSimilarity
            || composite < options.MinimumCompositeSimilarity)
        {
            return false;
        }

        candidate = new SecondaryCandidate(
            previous,
            current,
            decimal.Round(composite, 4, MidpointRounding.ToEven),
            decimal.Round(title, 4, MidpointRounding.ToEven),
            decimal.Round(instructor, 4, MidpointRounding.ToEven),
            decimal.Round(department, 4, MidpointRounding.ToEven));
        return true;
    }

    private bool TryScoreWithoutDepartment(
        CanonicalScheduleRecord previous,
        CanonicalScheduleRecord current,
        decimal title,
        decimal instructor,
        out SecondaryCandidate candidate)
    {
        candidate = null!;

        // A record that names one department is not matched against one that
        // names a different single department: that pair was already offered to
        // the stronger rule and refused, and re-scoring it without the attribute
        // that disagreed would turn a rejection into a match.
        if (previous.ComparableDepartment is not null && current.ComparableDepartment is not null)
        {
            return false;
        }

        decimal weight = options.TitleWeight + options.InstructorWeight;
        decimal composite = ((title * options.TitleWeight) + (instructor * options.InstructorWeight))
            / weight;

        if (composite < options.MinimumCompositeSimilarityWithoutDepartment)
        {
            return false;
        }

        candidate = new SecondaryCandidate(
            previous,
            current,
            decimal.Round(composite, 4, MidpointRounding.ToEven),
            decimal.Round(title, 4, MidpointRounding.ToEven),
            decimal.Round(instructor, 4, MidpointRounding.ToEven),
            DepartmentScore: null);
        return true;
    }

    private static bool HasSameStructuralContext(
        CanonicalScheduleRecord previous,
        CanonicalScheduleRecord current) =>
        previous.SourceId == current.SourceId
        && string.Equals(previous.AcademicYear, current.AcademicYear, StringComparison.Ordinal)
        && previous.ClassYear == current.ClassYear
        && previous.ProgramLanguage == current.ProgramLanguage
        && previous.EventType == current.EventType
        && previous.RecordStatus == current.RecordStatus
        && previous.AudienceScope == current.AudienceScope
        && string.Equals(
            previous.AudienceSelectors,
            current.AudienceSelectors,
            StringComparison.Ordinal)
        && previous.LocalDate == current.LocalDate
        // An all-day closure and a timed lesson are never the same logical entry,
        // whatever their titles score. Secondary matching also demands an
        // instructor, which no closure states, so this is the second of two locks.
        && previous.IsAllDay == current.IsAllDay
        && string.Equals(previous.TimeZoneId, current.TimeZoneId, StringComparison.Ordinal);

    private static Dictionary<string, CanonicalScheduleRecord> IndexByIdentity(
        IEnumerable<CanonicalScheduleRecord> records,
        string parameterName)
    {
        Dictionary<string, CanonicalScheduleRecord> result = new(StringComparer.Ordinal);
        foreach (CanonicalScheduleRecord record in records)
        {
            if (!result.TryAdd(record.StableIdentity, record))
            {
                throw new ArgumentException(
                    $"Revision input contains duplicate stable identity '{record.StableIdentity}'.",
                    parameterName);
            }
        }

        return result;
    }

    internal static decimal Similarity(string left, string right)
    {
        string normalizedLeft = Normalize(left);
        string normalizedRight = Normalize(right);

        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return 0m;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 1m;
        }

        int distance = LevenshteinDistance(normalizedLeft, normalizedRight);
        int maximumLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
        return 1m - ((decimal)distance / maximumLength);
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        bool previousWasSeparator = true;

        foreach (char rawCharacter in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(rawCharacter)
                is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char character = char.ToLowerInvariant(rawCharacter) switch
            {
                'ı' => 'i',
                'ğ' => 'g',
                'ş' => 's',
                'ç' => 'c',
                'ö' => 'o',
                'ü' => 'u',
                char candidate => candidate,
            };

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static int LevenshteinDistance(string left, string right)
    {
        int[] previousRow = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] currentRow = new int[right.Length + 1];

        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            currentRow[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                int substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                currentRow[rightIndex] = Math.Min(
                    Math.Min(
                        currentRow[rightIndex - 1] + 1,
                        previousRow[rightIndex] + 1),
                    previousRow[rightIndex - 1] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[right.Length];
    }

    private sealed record SecondaryCandidate(
        CanonicalScheduleRecord Previous,
        CanonicalScheduleRecord Current,
        decimal CompositeScore,
        decimal TitleScore,
        decimal InstructorScore,
        decimal? DepartmentScore)
    {
        /// <summary>
        /// Whether the two records occupy the very same local slot (ADR-165).
        /// </summary>
        /// <remarks>
        /// The date, the audience and the all-day shape are already equal — <see
        /// cref="HasSameStructuralContext"/> demanded them — so this is the last thing that can
        /// tell one of an hour's lessons from the next. Two all-day records of one date share a
        /// slot by this definition, which is correct: neither states a time, so neither can be
        /// told apart by one, and the uniqueness rule is what protects them.
        /// </remarks>
        public bool SharesExactSlot =>
            Previous.StartLocalTime == Current.StartLocalTime
            && Previous.EndLocalTime == Current.EndLocalTime;

        public ScheduleDiffEntry ToEntry(ScheduleDiffChange change) => new()
        {
            Change = change,
            Match = ScheduleDiffMatch.SecondaryAttributes,
            PreviousRecordId = Previous.Id,
            CurrentRecordId = Current.Id,
            MatchScore = CompositeScore,
            TitleScore = TitleScore,
            InstructorScore = InstructorScore,
            DepartmentScore = DepartmentScore,
        };

        /// <summary>The previous record alone, with this candidate as evidence.</summary>
        public ScheduleDiffEntry ToPreviousSideEntry(ScheduleDiffChange change) =>
            ToEntry(change) with { CurrentRecordId = null };

        /// <summary>The current record alone, with this candidate as evidence.</summary>
        public ScheduleDiffEntry ToCurrentSideEntry(ScheduleDiffChange change) =>
            ToEntry(change) with { PreviousRecordId = null };
    }
}
