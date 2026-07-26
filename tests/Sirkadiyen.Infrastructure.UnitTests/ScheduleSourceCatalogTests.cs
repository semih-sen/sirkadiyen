using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.ScheduleSources;
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
        Assert.Equal(22, catalog.Sources.Count);
        Assert.Equal(7, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleSheets));
        Assert.Equal(10, catalog.Sources.Count(
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
