using Microsoft.EntityFrameworkCore;
using Sirkadiyen.Application.ScheduleIngestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.ScheduleIngestion;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.Persistence;
using Xunit;

namespace Sirkadiyen.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SourceSnapshotStoreTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AnAcquiredSnapshotIsStoredOnce()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = await GivenSourceAsync("G9-STORE-ONCE");

        StoreSnapshotResult first = await StoreAsync(sourceId, "sha256:aaa", Hour(9));
        StoreSnapshotResult second = await StoreAsync(sourceId, "sha256:aaa", Hour(10));

        Assert.Equal(StoreSnapshotOutcome.Stored, first.Outcome);
        Assert.Equal(StoreSnapshotOutcome.Unchanged, second.Outcome);
        Assert.Equal(first.Snapshot.Id, second.Snapshot.Id);

        await using SirkadiyenDbContext context = fixture.CreateContext();
        Assert.Equal(
            1,
            await context.SourceSnapshots.CountAsync(snapshot => snapshot.SourceId == sourceId, Token));
    }

    [Fact]
    public async Task ChangedContentIsStoredBesideTheSnapshotItReplaces()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = await GivenSourceAsync("G9-STORE-CHANGED");

        await StoreAsync(sourceId, "sha256:first", Hour(9));
        StoreSnapshotResult changed = await StoreAsync(sourceId, "sha256:second", Hour(10));

        Assert.True(changed.Changed);

        await using SirkadiyenDbContext context = fixture.CreateContext();

        // The earlier snapshot has to survive: it is the evidence for the parse
        // run that already read it.
        Assert.Equal(
            2,
            await context.SourceSnapshots.CountAsync(snapshot => snapshot.SourceId == sourceId, Token));
    }

    [Fact]
    public async Task ContentThatRevertsToAnEarlierVersionIsStoredAgain()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = await GivenSourceAsync("G9-STORE-REVERTED");

        await StoreAsync(sourceId, "sha256:original", Hour(9));
        await StoreAsync(sourceId, "sha256:edited", Hour(10));
        StoreSnapshotResult reverted = await StoreAsync(sourceId, "sha256:original", Hour(11));

        // A source can be edited and then edited back. Only the latest snapshot
        // decides whether anything changed, so the revert is a change.
        Assert.True(reverted.Changed);
    }

    [Fact]
    public async Task PollingHistoryRecordsBothPollsAndChanges()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = await GivenSourceAsync("G9-STORE-HISTORY");

        await StoreAsync(sourceId, "sha256:only", Hour(9));
        await StoreAsync(sourceId, "sha256:only", Hour(12));

        await using SirkadiyenDbContext context = fixture.CreateContext();
        ScheduleSource source = await context.ScheduleSources
            .SingleAsync(candidate => candidate.SourceId == sourceId, Token);

        Assert.Equal(Hour(12), source.LastPolledAtUtc);
        Assert.Equal(Hour(9), source.LastChangedAtUtc);
    }

    [Fact]
    public async Task AnUnconfiguredSourceCannotStoreEvidence()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => StoreAsync(SourceId.Parse("G9-NEVER-CONFIGURED"), "sha256:x", Hour(9)));
    }

    [Fact]
    public async Task TheStoredPayloadCanBeReadBackAsTheSnapshotContract()
    {
        Assert.SkipUnless(fixture.IsAvailable, PostgresFixture.SkipReason);
        SourceId sourceId = await GivenSourceAsync("G9-STORE-PAYLOAD");

        await StoreAsync(sourceId, "sha256:payload", Hour(9));

        await using SirkadiyenDbContext context = fixture.CreateContext();
        SourceSnapshot stored = await context.SourceSnapshots
            .SingleAsync(snapshot => snapshot.SourceId == sourceId, Token);

        NormalizedSpreadsheetSnapshot? roundTripped =
            System.Text.Json.JsonSerializer.Deserialize<NormalizedSpreadsheetSnapshot>(
                stored.Payload,
                Contracts.Serialization.ContractJson.CreateOptions());

        Assert.NotNull(roundTripped);
        Assert.Equal("sha256:payload", roundTripped.ContentHash);
        Assert.Equal("DÖNEM 1", Assert.Single(roundTripped.Worksheets).Title);
        Assert.Equal(1, stored.WorksheetCount);
        Assert.Equal(1, stored.CellCount);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTimeOffset Hour(int hour) =>
        new(2026, 7, 21, hour, 0, 0, TimeSpan.Zero);

    private async Task<SourceId> GivenSourceAsync(string value)
    {
        SourceId sourceId = SourceId.Parse(value);
        await using SirkadiyenDbContext context = fixture.CreateContext();

        if (await context.ScheduleSources.AnyAsync(source => source.SourceId == sourceId, Token))
        {
            return sourceId;
        }

        context.ScheduleSources.Add(new ScheduleSource(
            sourceId,
            $"Test source {value}",
            ScheduleSourceTransport.GoogleSheets,
            ScheduleDocumentFormat.GoogleSheet,
            "https://example.invalid/sheet",
            "grade1_yearly_v1",
            "1.0.0",
            "2025-2026",
            1,
            ProgramLanguage.Turkish,
            "Europe/Istanbul"));

        await context.SaveChangesAsync(Token);
        return sourceId;
    }

    private async Task<StoreSnapshotResult> StoreAsync(
        SourceId sourceId,
        string contentHash,
        DateTimeOffset acquiredAtUtc)
    {
        await using SirkadiyenDbContext context = fixture.CreateContext();
        SourceSnapshotStore store = new(context);

        return await store.StoreIfChangedAsync(
            sourceId,
            SnapshotFor(sourceId, contentHash, acquiredAtUtc),
            Token);
    }

    private static NormalizedSpreadsheetSnapshot SnapshotFor(
        SourceId sourceId,
        string contentHash,
        DateTimeOffset acquiredAtUtc) => new()
        {
            ContractVersion = SpreadsheetContractVersions.V1,
            SourceId = sourceId.Value,
            SnapshotId = $"snapshot:{contentHash}",
            SpreadsheetId = "spreadsheet-001",
            AcquiredAtUtc = acquiredAtUtc,
            ContentHash = contentHash,
            ContentHashAlgorithm = "SHA-256",
            Worksheets =
            [
                new NormalizedWorksheet
                {
                    SheetId = "1",
                    Title = "DÖNEM 1",
                    Index = 0,
                    RowCount = 1,
                    ColumnCount = 1,
                    Cells =
                    [
                        new NormalizedCell
                        {
                            RowIndex = 0,
                            ColumnIndex = 0,
                            A1Address = "A1",
                            FormattedValue = "Dönem 1",
                        },
                    ],
                },
            ],
        };
}
