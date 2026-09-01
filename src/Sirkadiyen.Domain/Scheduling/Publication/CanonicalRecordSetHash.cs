using System.Security.Cryptography;
using System.Text;

namespace Sirkadiyen.Domain.Scheduling.Publication;

/// <summary>
/// The identity of what a parse produced, rather than of the document it read.
/// </summary>
/// <remarks>
/// A snapshot hash answers "did the document change". This answers the question
/// the pipeline actually acts on: "does the schedule say anything different".
/// The two come apart constantly — a recoloured cell, a Drive re-export, or a
/// companion document edited beside an untouched program all change the bytes
/// and none of them change a single lesson.
/// <para>
/// It is built from exactly what the semantic differ compares: each record's
/// stable identity and its content hash. Two record sets with the same hash
/// therefore produce a diff whose every entry is <c>Unchanged</c>. That is the
/// direction which must hold; the converse is not claimed, and does not need to
/// be. A set that hashes differently is simply diffed as before, so this can
/// suppress work but never suppress a change.
/// </para>
/// <para>
/// Order is not part of the identity: records are sorted before hashing, because
/// the same schedule read twice may be emitted in a different order and that is
/// not a schedule change. The content hash is included beside the identity
/// rather than trusted alone, so that a record whose identity moved and a record
/// whose content moved are both visible here.
/// </para>
/// </remarks>
public static class CanonicalRecordSetHash
{
    /// <summary>Length of the hex digest this produces.</summary>
    public const int Length = 64;

    private const char FieldSeparator = '\u001F';

    private const char RecordSeparator = '\u001E';

    public static string Compute(IEnumerable<CanonicalScheduleRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return Compute(records.Select(static record =>
            (record.StableIdentity, record.ContentHash)));
    }

    public static string Compute(
        IEnumerable<(string StableIdentity, string ContentHash)> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        StringBuilder builder = new();
        foreach ((string identity, string contentHash) in records
                     .OrderBy(static record => record.StableIdentity, StringComparer.Ordinal)
                     .ThenBy(static record => record.ContentHash, StringComparer.Ordinal))
        {
            builder
                .Append(identity)
                .Append(FieldSeparator)
                .Append(contentHash)
                .Append(RecordSeparator);
        }

        // An empty set hashes to the digest of an empty string rather than to a
        // sentinel: a source that parsed to nothing twice really did produce the
        // same result twice, and the emptiness itself is judged by validation.
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
