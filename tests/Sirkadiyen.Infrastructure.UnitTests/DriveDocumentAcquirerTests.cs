using System.Text;
using Sirkadiyen.Application.Scheduling.Ingestion;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Ingestion;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class DriveDocumentAcquirerTests
{
    private static readonly byte[] VerticalCorridorDocument = File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "g2-vertical-autumn.docx"));

    // Any real workbook proves the XLSX path; this one is already linked into the
    // test output for the converter's own tests.
    private static readonly byte[] AnnualWorkbook = File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "g1-tr-annual.xlsx"));

    [Fact]
    public async Task TheVerticalCorridorDocumentArrivesOnTheSameContractASheetProduces()
    {
        FakeDriveClient client = new(VerticalCorridorDocument);

        NormalizedSpreadsheetSnapshot snapshot = await Acquirer(client).AcquireAsync(
            ScheduleDocumentFormat.Docx,
            Request("snapshot-1"),
            CancellationToken.None);

        Assert.Equal(SpreadsheetContractVersions.V1, snapshot.ContractVersion);
        Assert.Equal("G2-VERTICAL-AUTUMN", snapshot.SourceId);

        // The Drive file identifier is the snapshot's provenance, so a stored
        // snapshot names the document it was read from.
        Assert.Equal("drive-file-1", snapshot.SpreadsheetId);
        Assert.NotEmpty(snapshot.Worksheets);

        // The autumn calendar is one wide table (ADR-076).
        Assert.Equal(7, snapshot.Worksheets[0].ColumnCount);

        // The download is asked for as the format the catalog declares, so a file
        // someone converted to another format is refused rather than parsed.
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            client.LastRequest!.ExpectedMimeType);

        Assert.Contains(
            snapshot.Diagnostics,
            diagnostic =>
                diagnostic.Code == DocxSnapshotConverter.GoogleDriveDownloadDiagnosticCode
                && diagnostic.Severity == DiagnosticSeverity.Information);
    }

    [Fact]
    public async Task ARedownloadOfAnUnchangedDocumentHashesTheSame()
    {
        FakeDriveClient client = new(VerticalCorridorDocument)
        {
            Name = "Dönem 2 Beceri uygulama takvimi güz.docx",
            ModifiedAtUtc = new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
        };
        DriveDocumentAcquirer acquirer = Acquirer(client);

        NormalizedSpreadsheetSnapshot first = await acquirer.AcquireAsync(
            ScheduleDocumentFormat.Docx,
            Request("snapshot-1"),
            CancellationToken.None);

        // The same bytes, renamed and re-saved in Drive: exactly what happens when
        // Student Affairs opens the calendar and saves it without editing.
        client.Name = "Dönem 2 Beceri uygulama takvimi güz (son).docx";
        client.ModifiedAtUtc = new DateTimeOffset(2026, 7, 24, 6, 0, 0, TimeSpan.Zero);

        NormalizedSpreadsheetSnapshot second = await acquirer.AcquireAsync(
            ScheduleDocumentFormat.Docx,
            Request("snapshot-2"),
            CancellationToken.None);

        // The content hash is what decides whether a poll stored anything, and the
        // diagnostics are part of it. Recording the modification time or the file
        // name as provenance would make every poll look like a change and produce
        // a revision that changes nothing.
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
    }

    [Fact]
    public async Task APageServedInPlaceOfTheDocumentIsRefused()
    {
        FakeDriveClient client = new(
            Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Sign in</body></html>"));

        DriveDocumentException exception = await Assert.ThrowsAsync<DriveDocumentException>(
            () => Acquirer(client).AcquireAsync(
                ScheduleDocumentFormat.Docx,
                Request("snapshot-1"),
                CancellationToken.None));

        // Named here rather than left to the document reader, which would report
        // it as an unreadable package and read like a damaged document.
        Assert.Equal(DriveDocumentFailure.CorruptContent, exception.Failure);
    }

    [Fact]
    public async Task AWorkbookPublishedOnDriveArrivesOnTheSameContractADocumentProduces()
    {
        FakeDriveClient client = new(AnnualWorkbook);
        DriveDocumentAcquirer acquirer = Acquirer(client);

        Assert.True(acquirer.CanAcquire(ScheduleDocumentFormat.Xlsx));

        NormalizedSpreadsheetSnapshot snapshot = await acquirer.AcquireAsync(
            ScheduleDocumentFormat.Xlsx,
            Request("snapshot-1"),
            CancellationToken.None);

        Assert.Equal(SpreadsheetContractVersions.V1, snapshot.ContractVersion);
        Assert.Equal("drive-file-1", snapshot.SpreadsheetId);
        Assert.NotEmpty(snapshot.Worksheets);

        // The workbook is asked for as the format the catalog declares, so a file
        // someone converted to a document is refused rather than parsed.
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            client.LastRequest!.ExpectedMimeType);

        // A downloaded workbook must not claim to be a repository fixture: the
        // snapshot is the only place that records how the file arrived.
        Assert.Contains(
            snapshot.Diagnostics,
            diagnostic =>
                diagnostic.Code == LocalXlsxSnapshotConverter.GoogleDriveDownloadDiagnosticCode
                && diagnostic.Severity == DiagnosticSeverity.Information);
        Assert.DoesNotContain(
            snapshot.Diagnostics,
            diagnostic => diagnostic.Code == LocalXlsxSnapshotConverter.LocalFixtureDiagnosticCode);
    }

    [Fact]
    public async Task APageServedInPlaceOfAWorkbookIsRefused()
    {
        FakeDriveClient client = new(
            Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Sign in</body></html>"));

        DriveDocumentException exception = await Assert.ThrowsAsync<DriveDocumentException>(
            () => Acquirer(client).AcquireAsync(
                ScheduleDocumentFormat.Xlsx,
                Request("snapshot-1"),
                CancellationToken.None));

        Assert.Equal(DriveDocumentFailure.CorruptContent, exception.Failure);
    }

    [Fact]
    public async Task ATransportThisAcquirerDoesNotReadIsRefusedBeforeAnyDownload()
    {
        FakeDriveClient client = new(VerticalCorridorDocument);
        DriveDocumentAcquirer acquirer = Acquirer(client);

        Assert.False(acquirer.CanAcquire(ScheduleDocumentFormat.GoogleSheet));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => acquirer.AcquireAsync(
                ScheduleDocumentFormat.GoogleSheet,
                Request("snapshot-1"),
                CancellationToken.None));

        Assert.Null(client.LastRequest);
    }

    private static DriveDocumentAcquirer Acquirer(FakeDriveClient client) =>
        new(client, new DocxSnapshotConverter(), new LocalXlsxSnapshotConverter());

    private static AcquireSpreadsheetSnapshotRequest Request(string snapshotId) => new()
    {
        SourceId = "G2-VERTICAL-AUTUMN",
        SnapshotId = snapshotId,
        SpreadsheetId = "drive-file-1",
        AcquiredAtUtc = new DateTimeOffset(2026, 7, 26, 5, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDriveClient(byte[] content) : IGoogleDriveFileClient
    {
        public DriveFileRequest? LastRequest { get; private set; }

        public string Name { get; set; } = "document.docx";

        public DateTimeOffset? ModifiedAtUtc { get; set; }

        public Task<DriveFile> FetchAsync(
            DriveFileRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new DriveFile
            {
                FileId = request.FileId,
                Name = Name,
                MimeType = request.ExpectedMimeType,
                Content = content,
                ModifiedAtUtc = ModifiedAtUtc,
            });
        }
    }
}
