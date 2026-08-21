using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// Reduces the evidence a parse read besides its own snapshot to one value a
/// uniqueness key can hold (ADR-102, ADR-126).
/// </summary>
/// <remarks>
/// A parse run is keyed by snapshot and parser profile version. A companion
/// document is a third input, so without it in the key an edited companion would
/// be short-circuited as "already parsed" and the correction would never reach a
/// calendar. Storing the companion list itself is not an option — it is unbounded
/// — so the key holds a digest of it instead.
/// <para>
/// The digest covers each companion's identifier as well as its content hash.
/// Two companions that swap places are different evidence: which document
/// supplied a topic is part of what the parse read, not an implementation detail.
/// </para>
/// </remarks>
public static class ParseRunCompanionFingerprint
{
    /// <summary>The value stated by a run that read no companion evidence.</summary>
    /// <remarks>
    /// Empty rather than null: every run states what it read, and a nullable
    /// column would be excluded from the unique index by PostgreSQL's rule that
    /// NULLs are distinct, allowing exactly the duplicate runs the key forbids.
    /// </remarks>
    public const string None = "";

    /// <summary>Longest value <see cref="Compute"/> can produce.</summary>
    public const int MaxLength = 100;

    /// <param name="rotationCoverage">
    /// The dates the sources owning this source's group rotation had published
    /// when the run began (ADR-126). It belongs in the key for the same reason a
    /// companion does: it is an input the parse read, and a run keyed without it
    /// would be short-circuited as "already parsed" after a group list arrived,
    /// leaving the fallback events on every student's calendar for good.
    /// </param>
    public static string Compute(
        IReadOnlyList<CompanionEvidence> companions,
        IReadOnlyList<DateOnly>? rotationCoverage = null)
    {
        ArgumentNullException.ThrowIfNull(companions);

        IReadOnlyList<DateOnly> coverage = rotationCoverage ?? [];
        if (companions.Count == 0 && coverage.Count == 0)
        {
            return None;
        }

        // Newline-separated rather than concatenated: an identifier and a hash
        // are both variable-length, and joining them without a separator would
        // let two different lists produce one string.
        StringBuilder builder = new();
        foreach (CompanionEvidence companion in companions)
        {
            builder.Append(companion.SourceId.Value)
                .Append('\n')
                .Append(companion.ContentHash)
                .Append('\n');
        }

        // Written only when there is coverage, so every run that reads none keeps
        // exactly the value it had before coverage was part of the key and is not
        // reparsed for a change that did not happen. The label separates the two
        // lists: a date must never be readable as a companion hash.
        if (coverage.Count > 0)
        {
            builder.Append("rotationCoverage").Append('\n');
            foreach (DateOnly date in coverage)
            {
                builder.Append(date.ToString("O")).Append('\n');
            }
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"sha256:{Convert.ToHexStringLower(digest)}";
    }
}

/// <summary>One companion document as the parse run recorded reading it.</summary>
public readonly record struct CompanionEvidence(SourceId SourceId, string ContentHash);
