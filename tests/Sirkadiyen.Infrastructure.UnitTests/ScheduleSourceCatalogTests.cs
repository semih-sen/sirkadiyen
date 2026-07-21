using Sirkadiyen.Application.ScheduleSources;
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
        Assert.Equal(18, catalog.Sources.Count);
        Assert.Equal(7, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleSheets));
        Assert.Equal(10, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleDriveFile));
        Assert.Single(catalog.Sources, source => source.Transport == ScheduleSourceTransport.HttpFile);

        ScheduleSourceDefinition annual = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G1-TR-ANNUAL");
        Assert.Equal("1Xwqz2bXHvH2oQ_utv_WIVzPFvLZyJEXHvwey-bVDt7A", annual.ExternalId);
        Assert.Equal(1054469518, annual.SheetGid);
        Assert.Equal("grade1_yearly_v1", annual.ParserProfile);

        ScheduleSourceDefinition vertical = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-VERTICAL-SPRING");
        Assert.Equal(ScheduleDocumentFormat.Docx, vertical.DocumentFormat);

        Assert.All(catalog.Sources, source =>
        {
            Assert.True(source.SourceUri.IsAbsoluteUri);
            Assert.Equal(Uri.UriSchemeHttps, source.SourceUri.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(source.ParserProfile));
        });
    }
}
