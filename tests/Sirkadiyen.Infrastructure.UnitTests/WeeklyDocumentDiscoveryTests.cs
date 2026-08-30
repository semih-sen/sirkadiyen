using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Tests for resolving which document a republished source acquires (ADR-133).
/// </summary>
/// <remarks>
/// The property that matters most here is that discovery can never take a source
/// offline. Every way of failing to read the folder has to end with the catalogued
/// document being acquired, because the alternative is a week in which no student
/// receives a room at all.
/// </remarks>
public sealed class WeeklyDocumentDiscoveryTests
{
    private const string CataloguedId = "catalogued-workbook";
    private const string FolderId = "1ZkB8GD_niGknZLVD_aGN0oxWm5F_F8G1";

    [Fact]
    public async Task ASourceWithNoFolderResolvesToItsCataloguedDocument()
    {
        FakeFolderClient folderClient = new([]);
        WeeklyDocumentDiscovery discovery = new(folderClient);

        WeeklyDocumentResolution resolution = await discovery.ResolveAsync(
            Source(discoveryFolderId: null),
            CancellationToken.None);

        Assert.Equal(CataloguedId, resolution.ExternalId);
        Assert.Equal(WeeklyDocumentDiscoveryOutcome.NotConfigured, resolution.Outcome);

        // Every source but one declares no folder, so this must not cost a Drive call.
        Assert.Equal(0, folderClient.CallCount);
    }

    [Fact]
    public async Task TheOnlyDocumentInTheFolderIsTaken()
    {
        WeeklyDocumentDiscovery discovery = new(new FakeFolderClient(
            [Entry("this-week", "31 AĞUSTOS -4 EYLÜL 2026 Amfi programı", "2026-08-30T08:23:00Z")]));

        WeeklyDocumentResolution resolution = await discovery.ResolveAsync(
            Source(),
            CancellationToken.None);

        Assert.Equal("this-week", resolution.ExternalId);
        Assert.Equal(WeeklyDocumentDiscoveryOutcome.ResolvedSingle, resolution.Outcome);
        Assert.Equal("31 AĞUSTOS -4 EYLÜL 2026 Amfi programı", resolution.DocumentName);
    }

    [Fact]
    public async Task TheMostRecentlyChangedDocumentIsTakenWhenTheFolderAccumulates()
    {
        WeeklyDocumentDiscovery discovery = new(new FakeFolderClient(
        [
            Entry("older", "24-28 AĞUSTOS 2026 Amfi programı", "2026-08-23T09:00:00Z"),
            Entry("newest", "31 AĞUSTOS -4 EYLÜL 2026 Amfi programı", "2026-08-30T08:23:00Z"),
            Entry("middle", "a stray export", "2026-08-27T09:00:00Z"),
        ]));

        WeeklyDocumentResolution resolution = await discovery.ResolveAsync(
            Source(),
            CancellationToken.None);

        Assert.Equal("newest", resolution.ExternalId);
        Assert.Equal(WeeklyDocumentDiscoveryOutcome.ResolvedNewest, resolution.Outcome);
        Assert.Equal(3, resolution.CandidateCount);
    }

    [Fact]
    public async Task TwoDocumentsChangedAtTheSameInstantResolveTheSameWayEveryCycle()
    {
        // An unstable choice would re-acquire and re-parse the source for no reason,
        // and a parse run is keyed by snapshot, so the churn would be visible to
        // every student the source feeds.
        DriveFolderEntry[] entries =
        [
            Entry("bbb", "one", "2026-08-30T08:23:00Z"),
            Entry("aaa", "another", "2026-08-30T08:23:00Z"),
        ];

        WeeklyDocumentResolution first = await new WeeklyDocumentDiscovery(
            new FakeFolderClient(entries)).ResolveAsync(Source(), CancellationToken.None);
        WeeklyDocumentResolution second = await new WeeklyDocumentDiscovery(
            new FakeFolderClient([.. entries.Reverse()])).ResolveAsync(
                Source(),
                CancellationToken.None);

        Assert.Equal("aaa", first.ExternalId);
        Assert.Equal(first.ExternalId, second.ExternalId);
    }

    [Fact]
    public async Task AnEmptyFolderFallsBackToTheCataloguedDocument()
    {
        WeeklyDocumentDiscovery discovery = new(new FakeFolderClient([]));

        WeeklyDocumentResolution resolution = await discovery.ResolveAsync(
            Source(),
            CancellationToken.None);

        Assert.Equal(CataloguedId, resolution.ExternalId);
        Assert.Equal(WeeklyDocumentDiscoveryOutcome.FellBackToCatalog, resolution.Outcome);
        Assert.Null(resolution.Failure);
    }

    [Theory]
    [InlineData(DriveDocumentFailure.NotFound)]
    [InlineData(DriveDocumentFailure.AccessDenied)]
    public async Task AFolderThatCannotBeListedFallsBackInsteadOfFailingTheCycle(
        DriveDocumentFailure failure)
    {
        WeeklyDocumentDiscovery discovery = new(new FakeFolderClient(
            new DriveDocumentException(FolderId, failure, "refused")));

        WeeklyDocumentResolution resolution = await discovery.ResolveAsync(
            Source(),
            CancellationToken.None);

        Assert.Equal(CataloguedId, resolution.ExternalId);
        Assert.Equal(WeeklyDocumentDiscoveryOutcome.FellBackToCatalog, resolution.Outcome);

        // The reason travels on the result, because the caller is what reports it.
        Assert.Equal(failure, resolution.Failure);
    }

    [Fact]
    public async Task TheFolderIsAskedOnlyForDocumentsTheSourceCouldBe()
    {
        FakeFolderClient folderClient = new([]);

        await new WeeklyDocumentDiscovery(folderClient).ResolveAsync(
            Source(),
            CancellationToken.None);

        Assert.NotNull(folderClient.LastRequest);
        Assert.Equal(FolderId, folderClient.LastRequest!.FolderId);
        Assert.Equal(
            WeeklyDocumentDiscovery.GoogleSheetMimeType,
            folderClient.LastRequest.ExpectedMimeType);
    }

    private static ScheduleSource Source(string? discoveryFolderId = FolderId) => new(
        SourceId.Parse("SHARED-AMPHI"),
        "Haftalık amfi programı",
        ScheduleSourceTransport.GoogleSheets,
        ScheduleDocumentFormat.GoogleSheet,
        "https://docs.google.com/spreadsheets/d/catalogued-workbook",
        "weekly_amphitheatre_v1",
        "1.0.0",
        "2026-2027",
        1,
        ProgramLanguage.Turkish,
        "Europe/Istanbul",
        externalId: CataloguedId,
        sheetGid: 917709856,
        discoveryFolderId: discoveryFolderId);

    private static DriveFolderEntry Entry(string id, string name, string modifiedAtUtc) => new()
    {
        FileId = id,
        Name = name,
        MimeType = WeeklyDocumentDiscovery.GoogleSheetMimeType,
        ModifiedAtUtc = DateTimeOffset.Parse(
            modifiedAtUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal),
    };

    private sealed class FakeFolderClient : IGoogleDriveFolderClient
    {
        private readonly IReadOnlyList<DriveFolderEntry> entries;
        private readonly DriveDocumentException? exception;

        public FakeFolderClient(IReadOnlyList<DriveFolderEntry> entries) => this.entries = entries;

        public FakeFolderClient(DriveDocumentException exception)
        {
            this.entries = [];
            this.exception = exception;
        }

        public int CallCount { get; private set; }

        public DriveFolderListRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<DriveFolderEntry>> ListAsync(
            DriveFolderListRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return exception is null
                ? Task.FromResult(entries)
                : Task.FromException<IReadOnlyList<DriveFolderEntry>>(exception);
        }
    }
}
