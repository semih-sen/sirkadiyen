using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Application.Scheduling.Ingestion;

/// <summary>
/// Resolves which document a source should acquire this cycle, for sources whose
/// document is republished into a folder rather than edited in place (ADR-133).
/// </summary>
public interface IWeeklyDocumentDiscovery
{
    Task<WeeklyDocumentResolution> ResolveAsync(
        ScheduleSource source,
        CancellationToken cancellationToken);
}

/// <summary>Which document a cycle acquires, and how that was decided.</summary>
public sealed record WeeklyDocumentResolution
{
    /// <summary>The document ID to acquire. Never empty for a resolvable source.</summary>
    public required string ExternalId { get; init; }

    public required WeeklyDocumentDiscoveryOutcome Outcome { get; init; }

    /// <summary>The Drive name of the chosen document, for logging and audit.</summary>
    public string? DocumentName { get; init; }

    /// <summary>How many candidates the folder held, for logging and audit.</summary>
    public int CandidateCount { get; init; }

    /// <summary>
    /// Why the folder could not be read, when that is what caused the fallback.
    /// Null when the folder was read and simply held nothing.
    /// </summary>
    public DriveDocumentFailure? Failure { get; init; }
}

public enum WeeklyDocumentDiscoveryOutcome
{
    /// <summary>The source declares no folder, so the catalogued document stands.</summary>
    NotConfigured,

    /// <summary>The folder held exactly one candidate and it was taken.</summary>
    ResolvedSingle,

    /// <summary>The folder held several and the most recently changed was taken.</summary>
    ResolvedNewest,

    /// <summary>The folder could not be listed or held nothing, so the catalogued document stands.</summary>
    FellBackToCatalog,
}

/// <summary>
/// Picks the current document out of the folder the faculty publishes into.
/// </summary>
/// <remarks>
/// <para>
/// The choice is "the most recently changed candidate", which is deliberately not
/// a reading of the file name. The faculty names these documents after the week
/// they cover — `31 AĞUSTOS -4 EYLÜL 2026 Amfi programı` — but that name is not
/// reliable enough to select on: the workbook behind that very name titles its own
/// first worksheet `31 AĞUSTOS-1 EYLÜL 2026-`, three days short of the week it
/// actually holds. A rule that parsed Turkish month names out of file names would
/// be a second, weaker date parser sitting in front of the real one.
/// </para>
/// <para>
/// Picking by modification time is safe because it cannot be wrong in a way that
/// misplaces a lesson. The parser dates every room assignment from the day title
/// rows inside the document, so acquiring last week's workbook by mistake yields
/// assignments for last week's dates, which match no lesson of the current week
/// and change nothing. The failure mode is a missing room, never a wrong one.
/// </para>
/// <para>
/// A folder that cannot be read is not an error that stops the cycle. The
/// catalogued document is acquired instead, so a revoked folder permission
/// degrades this source to the fixed document it was configured with rather than
/// taking it offline.
/// </para>
/// </remarks>
public sealed class WeeklyDocumentDiscovery(IGoogleDriveFolderClient folderClient)
    : IWeeklyDocumentDiscovery
{
    /// <summary>The most candidates one folder listing reads.</summary>
    public const int MaximumEntries = 100;

    /// <summary>What a Google Sheets document is called in Drive.</summary>
    public const string GoogleSheetMimeType = "application/vnd.google-apps.spreadsheet";

    /// <summary>What an XLSX workbook is called in Drive.</summary>
    public const string XlsxMimeType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<WeeklyDocumentResolution> ResolveAsync(
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.DiscoveryFolderId))
        {
            return new WeeklyDocumentResolution
            {
                ExternalId = source.ExternalId ?? string.Empty,
                Outcome = WeeklyDocumentDiscoveryOutcome.NotConfigured,
            };
        }

        IReadOnlyList<DriveFolderEntry> entries;
        try
        {
            entries = await folderClient.ListAsync(
                new DriveFolderListRequest
                {
                    FolderId = source.DiscoveryFolderId,
                    ExpectedMimeType = MimeTypeFor(source.DocumentFormat),
                    MaximumEntries = MaximumEntries,
                },
                cancellationToken);
        }
        catch (DriveDocumentException exception)
        {
            // Deliberately not rethrown. The source still has a document, and a
            // folder that cannot be listed today must not stop this week's rooms
            // from being read out of the document the catalog already names. The
            // reason travels on the result so the caller can report it.
            return FallBack(source, exception.Failure);
        }

        if (entries.Count == 0)
        {
            return FallBack(source, failure: null);
        }

        // Ordered by file ID after the timestamp so that two documents Drive
        // reports with the same modification time still resolve the same way on
        // every cycle. An unstable choice here would re-acquire and re-parse a
        // source for no reason.
        DriveFolderEntry chosen = entries
            .OrderByDescending(entry => entry.ModifiedAtUtc)
            .ThenBy(entry => entry.FileId, StringComparer.Ordinal)
            .First();

        return new WeeklyDocumentResolution
        {
            ExternalId = chosen.FileId,
            Outcome = entries.Count == 1
                ? WeeklyDocumentDiscoveryOutcome.ResolvedSingle
                : WeeklyDocumentDiscoveryOutcome.ResolvedNewest,
            DocumentName = chosen.Name,
            CandidateCount = entries.Count,
        };
    }

    private static WeeklyDocumentResolution FallBack(
        ScheduleSource source,
        DriveDocumentFailure? failure) =>
        new()
        {
            ExternalId = source.ExternalId ?? string.Empty,
            Outcome = WeeklyDocumentDiscoveryOutcome.FellBackToCatalog,
            Failure = failure,
        };

    private static string MimeTypeFor(ScheduleDocumentFormat format) => format switch
    {
        ScheduleDocumentFormat.GoogleSheet => GoogleSheetMimeType,
        ScheduleDocumentFormat.Xlsx => XlsxMimeType,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            "Discovery is only defined for the formats Drive can be asked to list."),
    };
}
