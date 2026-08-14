using Sirkadiyen.Application.Scheduling.Sources;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class ScheduleSourceCatalogTests
{
    [Fact]
    public async Task CommittedCatalogContainsAllConfirmedSources()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "schedule-sources.json");

        ScheduleSourceCatalog catalog = await new ScheduleSourceCatalogLoader()
            .LoadAsync(path, CancellationToken.None);

        Assert.Equal("1.0", catalog.CatalogVersion);
        Assert.Equal(23, catalog.Sources.Count);
        Assert.Equal(7, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleSheets));
        Assert.Equal(11, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleDriveFile));
        Assert.Single(catalog.Sources, source => source.Transport == ScheduleSourceTransport.HttpFile);
        Assert.Equal(4, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.AdministrativeUpload));

        ScheduleSourceDefinition annual = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G1-TR-ANNUAL");
        Assert.Equal("1Xwqz2bXHvH2oQ_utv_WIVzPFvLZyJEXHvwey-bVDt7A", annual.ExternalId);
        Assert.Equal(1054469518, annual.SheetGid);
        Assert.Equal("grade1_yearly_v1", annual.ParserProfile);

        ScheduleSourceDefinition grade1EnglishPractice = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G1-EN-PRACTICE");
        Assert.Equal(
            ["İ1", "İ2", "İ3"],
            grade1EnglishPractice.SupportedAudienceSelectors!["practiceSubgroup"]);

        ScheduleSourceDefinition grade2TurkishPractice = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-TR-PRACTICE");
        Assert.Equal(
            ["A", "B", "C", "D", "E", "F", "G", "H"],
            grade2TurkishPractice.SupportedAudienceSelectors!["practiceGroup"]);
        Assert.Equal("1.2.0", grade2TurkishPractice.ParserProfileVersion);

        ScheduleSourceDefinition grade2EnglishPractice = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-EN-PRACTICE");
        Assert.Equal(
            ["İ1", "İ2"],
            grade2EnglishPractice.SupportedAudienceSelectors!["practiceGroup"]);
        Assert.Equal("1.2.0", grade2EnglishPractice.ParserProfileVersion);

        ScheduleSourceDefinition vertical = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-VERTICAL-SPRING");
        Assert.Equal(ScheduleDocumentFormat.Docx, vertical.DocumentFormat);

        // The anatomy documents are handed out rather than published, so they
        // name themselves instead of claiming a location they do not have.
        ScheduleSourceDefinition anatomy = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-ANATOMY-AUTUMN");
        Assert.Equal(ScheduleSourceTransport.AdministrativeUpload, anatomy.Transport);
        Assert.Equal(
            "urn:sirkadiyen:upload:G2-ANATOMY-AUTUMN",
            anatomy.SourceUri.OriginalString);
        Assert.Null(anatomy.ExternalId);
        Assert.Equal(["1", "2", "3"], anatomy.SupportedAudienceSelectors!["anatomyGroup"]);

        // The document is handed to both programs, and each needs its own
        // revision, so the pair is declared as one shared document (ADR-080).
        ScheduleSourceDefinition anatomyEnglish = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-ANATOMY-AUTUMN-EN");
        Assert.Equal(ProgramLanguage.English, anatomyEnglish.ProgramLanguage);
        Assert.Equal("g2-anatomy-autumn", anatomy.SharedDocumentGroup);
        Assert.Equal("g2-anatomy-autumn", anatomyEnglish.SharedDocumentGroup);
        Assert.Equal(anatomy.FixturePath, anatomyEnglish.FixturePath);

        // The Grade 3 programs are workbooks on the Drive transport, and the class
        // is split into two curriculum groups that do not share a document.
        ScheduleSourceDefinition grade3TurkishAnnual = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G3-TR-A-ANNUAL");
        Assert.Equal(ScheduleSourceTransport.GoogleDriveFile, grade3TurkishAnnual.Transport);
        Assert.Equal(ScheduleDocumentFormat.Xlsx, grade3TurkishAnnual.DocumentFormat);
        Assert.Equal("grade3_yearly_v1", grade3TurkishAnnual.ParserProfile);
        Assert.Equal("2026-2027", grade3TurkishAnnual.AcademicYear);
        Assert.Equal(3, grade3TurkishAnnual.ClassYear);

        Assert.Equal(8, catalog.Sources.Count(source => source.ClassYear == 3));
        Assert.All(
            catalog.Sources.Where(source => source.ClassYear == 3),
            source => Assert.Equal("2026-2027", source.AcademicYear));

        Assert.All(catalog.Sources, source =>
        {
            Assert.True(source.SourceUri.IsAbsoluteUri);
            Assert.Equal(
                source.Transport == ScheduleSourceTransport.AdministrativeUpload
                    ? "urn"
                    : Uri.UriSchemeHttps,
                source.SourceUri.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(source.ParserProfile));
        });
    }
}
