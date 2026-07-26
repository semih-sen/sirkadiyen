using Sirkadiyen.Api.Administration;
using Sirkadiyen.Domain.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

public sealed class UploadableSourceViewTests
{
    [Fact]
    public void OnlyAdministrativelyAcquiredSourcesAreOffered()
    {
        IReadOnlyList<UploadableSourceView> uploadable = UploadableSourceView.SelectUploadable(
        [
            Source("G2-TR-ANNUAL", ScheduleSourceTransport.GoogleSheets, ScheduleDocumentFormat.GoogleSheet),
            Source("G2-VERTICAL-AUTUMN", ScheduleSourceTransport.GoogleDriveFile, ScheduleDocumentFormat.Docx),
            Source("SHARED-AMPHI", ScheduleSourceTransport.HttpFile, ScheduleDocumentFormat.Xlsx),
            Source("G2-ANATOMY-AUTUMN", ScheduleSourceTransport.AdministrativeUpload, ScheduleDocumentFormat.Docx),
        ]);

        // A fetched source is not offered even when its document is a DOCX the
        // same converter reads: uploading over it would replace evidence the next
        // poll silently overwrites (ADR-080).
        UploadableSourceView single = Assert.Single(uploadable);
        Assert.Equal("G2-ANATOMY-AUTUMN", single.SourceId);
        Assert.Equal(ScheduleDocumentFormat.Docx, single.DocumentFormat);
    }

    [Fact]
    public void UploadableSourcesAreOrderedByIdentifierRatherThanCatalogOrder()
    {
        IReadOnlyList<UploadableSourceView> uploadable = UploadableSourceView.SelectUploadable(
        [
            Source(
                "G2-ANATOMY-SPRING",
                ScheduleSourceTransport.AdministrativeUpload,
                ScheduleDocumentFormat.Docx,
                sharedDocumentGroup: "g2-anatomy-spring"),
            Source(
                "G2-ANATOMY-AUTUMN-EN",
                ScheduleSourceTransport.AdministrativeUpload,
                ScheduleDocumentFormat.Docx,
                sharedDocumentGroup: "g2-anatomy-autumn",
                programLanguage: ProgramLanguage.English),
            Source(
                "G2-ANATOMY-AUTUMN",
                ScheduleSourceTransport.AdministrativeUpload,
                ScheduleDocumentFormat.Docx,
                sharedDocumentGroup: "g2-anatomy-autumn"),
        ]);

        Assert.Equal(
            ["G2-ANATOMY-AUTUMN", "G2-ANATOMY-AUTUMN-EN", "G2-ANATOMY-SPRING"],
            uploadable.Select(source => source.SourceId));

        // The group is projected so a caller can say which other sources one
        // upload will serve without restating the catalog itself.
        Assert.Equal(
            ["g2-anatomy-autumn", "g2-anatomy-autumn"],
            uploadable.Take(2).Select(source => source.SharedDocumentGroup));
        Assert.Equal(ProgramLanguage.English, uploadable[1].ProgramLanguage);
    }

    [Fact]
    public void ProjectionCarriesTheContextAWorkbookNeverStates()
    {
        UploadableSourceView view = UploadableSourceView.From(
            Source("G2-ANATOMY-SPRING", ScheduleSourceTransport.AdministrativeUpload, ScheduleDocumentFormat.Docx));

        Assert.Equal("Anatomy dissection groups", view.DisplayName);
        Assert.Equal("2025-2026", view.AcademicYear);
        Assert.Equal(2, view.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, view.ProgramLanguage);
        Assert.Null(view.SharedDocumentGroup);
    }

    private static ScheduleSource Source(
        string sourceId,
        ScheduleSourceTransport transport,
        ScheduleDocumentFormat documentFormat,
        string? sharedDocumentGroup = null,
        ProgramLanguage programLanguage = ProgramLanguage.Turkish) => new(
            SourceId.Parse(sourceId),
            "Anatomy dissection groups",
            transport,
            documentFormat,
            transport is ScheduleSourceTransport.AdministrativeUpload
                ? $"urn:sirkadiyen:upload:{sourceId}"
                : "https://example.invalid/document",
            "grade2_anatomy_autumn_v1",
            "1.0.0",
            "2025-2026",
            classYear: 2,
            programLanguage,
            "Europe/Istanbul",
            sharedDocumentGroup: sharedDocumentGroup);
}
