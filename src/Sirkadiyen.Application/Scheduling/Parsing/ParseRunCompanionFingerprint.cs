using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Parsing;

/// <summary>
/// Reduces the companion evidence a parse read to one value a uniqueness key can
/// hold (ADR-102).
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

    public static string Compute(IReadOnlyList<CompanionEvidence> companions)
    {
        ArgumentNullException.ThrowIfNull(companions);

        if (companions.Count == 0)
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

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"sha256:{Convert.ToHexStringLower(digest)}";
    }
}

/// <summary>One companion document as the parse run recorded reading it.</summary>
public readonly record struct CompanionEvidence(SourceId SourceId, string ContentHash);
