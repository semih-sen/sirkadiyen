using System.Reflection;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Persistence.Scheduling.Stores;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Pins that every catalog-owned field of a source is actually copied onto an existing row
/// (ADR-136).
/// </summary>
/// <remarks>
/// A source's configuration reaches a running database through one explicit field list. Omitting a
/// field from it is the quietest bug this codebase has: the row keeps its old value, the source
/// keeps polling, the parse succeeds, the schedule publishes in full, and only the behaviour the
/// new field was added for never happens. It has now happened twice — the companion identifiers
/// (ADR-112) and the discovery folder (ADR-133) — and in both cases a fresh database was correct
/// while the running one was not, which is the hardest version to notice.
/// <para>
/// The persistence tests that cover the upsert need a configured PostgreSQL and are skipped in CI
/// as well as locally, so this reflects over the entity instead: every property that is not
/// explicitly owned by the row must appear in the list. Adding a property to
/// <see cref="ScheduleSource"/> therefore forces a decision about which of the two owns it.
/// </para>
/// </remarks>
public sealed class ScheduleSourceConfigurationCoverageTests
{
    /// <summary>
    /// What the row owns rather than the catalog, each for a stated reason.
    /// </summary>
    private static readonly Dictionary<string, string> RowOwned = new(StringComparer.Ordinal)
    {
        ["Id"] = "The database's own key; a redeploy must not renumber a source.",
        ["SourceId"] = "The identity the row is matched by, so it cannot be a field that is copied.",
        ["IsPollingEnabled"] =
            "Turned off when a source leaves the catalog and by operators; a redeploy must not "
            + "silently re-enable polling for a source someone stopped.",
        ["LastPolledAtUtc"] = "What the worker observed, not what the catalog states.",
        ["LastChangedAtUtc"] = "What the worker observed, not what the catalog states.",
        ["LastPollFailureAtUtc"] =
            "What the worker observed. Copying it from the catalog would either invent a failure "
            + "or clear a real one that is still happening (ADR-137).",
        ["LastPollFailureReason"] = "The acquirer's own message about this row, not configuration.",
        ["RowVersion"] = "The concurrency token, owned by the database.",
    };

    [Fact]
    public void EveryCatalogOwnedFieldIsCopiedOntoAnExistingRow()
    {
        Dictionary<string, object?> copied = ScheduleSourceUpsert.ConfigurationOf(Source());

        IReadOnlyList<string> missing =
        [
            .. typeof(ScheduleSource)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Where(name => !RowOwned.ContainsKey(name) && !copied.ContainsKey(name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            missing.Count == 0,
            $"These properties of ScheduleSource are neither copied from the catalog nor listed as "
            + $"owned by the row: {string.Join(", ", missing)}. A field that is only set when the "
            + "row is first inserted reaches a fresh database and never reaches a running one. "
            + "Either add it to ConfigurationOf, or add it to RowOwned with the reason it belongs "
            + "to the row.");
    }

    [Fact]
    public void TheDiscoveryFolderIsCopiedOntoAnExistingRow()
    {
        // The specific regression: production held a null discovery folder while the catalog
        // declared one, so every poll acquired the catalogued workbook instead of the newest one —
        // and the fallback warning could not fire, because the source appeared to declare no
        // folder at all (ADR-133, ADR-136).
        Dictionary<string, object?> copied = ScheduleSourceUpsert.ConfigurationOf(
            Source(discoveryFolderId: "1ZkB8GD"));

        Assert.Equal("1ZkB8GD", copied[nameof(ScheduleSource.DiscoveryFolderId)]);
    }

    [Fact]
    public void NothingTheRowOwnsIsCopiedOverIt()
    {
        // The other direction, and the reason the list is explicit rather than reflected: copying
        // LastPolledAtUtc or IsPollingEnabled would let a redeploy reset what the worker observed
        // or re-enable a source an operator had stopped.
        Dictionary<string, object?> copied = ScheduleSourceUpsert.ConfigurationOf(Source());

        foreach (string name in RowOwned.Keys)
        {
            Assert.False(copied.ContainsKey(name), $"'{name}' is owned by the row: {RowOwned[name]}");
        }
    }

    private static ScheduleSource Source(string? discoveryFolderId = null) => new(
        SourceId.Parse("SHARED-AMPHI"),
        "Haftalık amfi programı",
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        "https://docs.google.com/spreadsheets/d/1uCfBw8/edit",
        "weekly_amphitheatre_v1",
        "1.0.0",
        "2026-2027",
        1,
        ProgramLanguage.Turkish,
        "Europe/Istanbul",
        externalId: "1uCfBw8",
        discoveryFolderId: discoveryFolderId);
}
