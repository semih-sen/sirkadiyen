using System.Globalization;
using System.Text;
using Sirkadiyen.Domain.ScheduleDiffing;
using Sirkadiyen.Domain.SchedulePublication;

namespace Sirkadiyen.Application.ScheduleDiffing;

/// <summary>
/// Calculates a deterministic semantic diff between two revisions (ADR-018,
/// ADR-035).
/// </summary>
/// <remarks>
/// Stable identity is authoritative. Secondary matching is attempted only for
/// records left unmatched by identity and only when the old and new records
/// explicitly state lesson name, instructor and academic department. A
/// many-to-one or one-to-many candidate set remains ambiguous and is never
/// converted into a destructive delete-and-create pair.
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

    private static void AddSecondaryMatches(
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

        foreach (SecondaryCandidate candidate in candidates
                     .OrderBy(candidate => candidate.Previous.Id)
                     .ThenBy(candidate => candidate.Current.Id))
        {
            bool unique = previousCandidateCounts[candidate.Previous.Id] == 1
                && currentCandidateCounts[candidate.Current.Id] == 1;

            entries.Add(candidate.ToEntry(
                unique ? ScheduleDiffChange.Updated : ScheduleDiffChange.Ambiguous));
            matchedPrevious.Add(candidate.Previous.Id);
            matchedCurrent.Add(candidate.Current.Id);
        }
    }

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
            || string.IsNullOrWhiteSpace(current.Instructor)
            || string.IsNullOrWhiteSpace(previous.Department)
            || string.IsNullOrWhiteSpace(current.Department))
        {
            candidate = null!;
            return false;
        }

        decimal title = Similarity(previousTitle, currentTitle);
        decimal instructor = Similarity(previous.Instructor, current.Instructor);
        decimal department = Similarity(previous.Department, current.Department);
        decimal composite = (title * options.TitleWeight)
            + (instructor * options.InstructorWeight)
            + (department * options.DepartmentWeight);

        if (title < options.MinimumTitleSimilarity
            || instructor < options.MinimumInstructorSimilarity
            || department < options.MinimumDepartmentSimilarity
            || composite < options.MinimumCompositeSimilarity)
        {
            candidate = null!;
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
        decimal DepartmentScore)
    {
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
    }
}
