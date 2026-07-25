using Sirkadiyen.Application.ScheduleSources;
using Sirkadiyen.Domain.ScheduleSources;
using Sirkadiyen.Infrastructure.ScheduleSources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Guards the catalog rules that decide whether a source's stated origin can be
/// believed. A source whose URI contradicts its transport would either be fetched
/// from somewhere it is not published, or claim a provenance it does not have
/// (ADR-079).
/// </summary>
public sealed class ScheduleSourceCatalogLoaderTests : IDisposable
{
    private readonly List<string> temporaryFiles = [];

    [Fact]
    public async Task AnAdministrativelyUploadedSourceIdentifiesItselfByUrn()
    {
        ScheduleSourceCatalog catalog = await LoadAsync(SourceJson(
            "administrativeUpload",
            "urn:sirkadiyen:upload:G2-UPLOAD"));

        ScheduleSourceDefinition source = Assert.Single(catalog.Sources);
        Assert.Equal(ScheduleSourceTransport.AdministrativeUpload, source.Transport);
        Assert.Equal("urn:sirkadiyen:upload:G2-UPLOAD", source.SourceUri.OriginalString);
    }

    [Fact]
    public async Task AnAdministrativelyUploadedSourceMayNotClaimAFetchableLocation()
    {
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => LoadAsync(SourceJson(
                "administrativeUpload",
                "https://drive.google.com/file/d/1abc/view")));

        Assert.Contains("must identify itself", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUploadUrnNamingAnotherSourceIsRejected()
    {
        // A copied entry that kept the other source's URN would attach one
        // document's evidence to the other, which nothing downstream could detect.
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => LoadAsync(SourceJson(
                "administrativeUpload",
                "urn:sirkadiyen:upload:G2-SOME-OTHER-SOURCE")));

        Assert.Contains(
            "urn:sirkadiyen:upload:G2-UPLOAD",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFetchedSourceStillRequiresAnAbsoluteHttpsUri()
    {
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => LoadAsync(SourceJson("googleDriveFile", "urn:sirkadiyen:upload:G2-UPLOAD")));

        Assert.Contains("absolute HTTPS URI", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (string path in temporaryFiles)
        {
            File.Delete(path);
        }
    }

    /// <summary>A one-source catalog whose transport and URI are under test.</summary>
    private static string SourceJson(string transport, string sourceUri) =>
        $$"""
        {
          "catalogVersion": "1.0",
          "sources": [
            {
              "sourceId": "G2-UPLOAD",
              "displayName": "Uploaded document",
              "transport": "{{transport}}",
              "documentFormat": "docx",
              "sourceUri": "{{sourceUri}}",
              "externalId": "1abc",
              "parserProfile": "grade2_anatomy_autumn_v1",
              "parserProfileVersion": "1.0.0",
              "academicYear": "2025-2026",
              "classYear": 2,
              "programLanguage": "turkish",
              "timeZoneId": "Europe/Istanbul"
            }
          ]
        }
        """;

    private async Task<ScheduleSourceCatalog> LoadAsync(string catalogJson)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        temporaryFiles.Add(path);
        await File.WriteAllTextAsync(path, catalogJson, CancellationToken.None);

        return await new ScheduleSourceCatalogLoader().LoadAsync(path, CancellationToken.None);
    }
}
