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
        Assert.Equal(25, catalog.Sources.Count);
        Assert.Equal(8, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleSheets));

        // Up from 11: the microbiology/pathology practice document is one Drive DOCX
        // catalogued once per program (ADR-145).
        Assert.Equal(13, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.GoogleDriveFile));
        foreach (string sourceId in new[] { "G3-TR-MICROPATHO-PRACTICE", "G3-EN-MICROPATHO-PRACTICE" })
        {
            ScheduleSourceDefinition microPatho = Assert.Single(
                catalog.Sources,
                source => source.SourceId == sourceId);
            Assert.Equal("grade3_micropathology_practice_v1", microPatho.ParserProfile);
            Assert.Equal("1IOx-_LE8ESpTCT8qH09DI66TPtyQW9es", microPatho.ExternalId);
            Assert.Equal(
                ["A1", "A2", "B1", "B2"],
                microPatho.SupportedAudienceSelectors!["microPathologyGroup"]);
        }

        // No source uses the HTTP transport any more. `SHARED-AMPHI` was the only
        // one, and it was configured against a dated CDN file name that had to be
        // guessed forward every week; the faculty publishes the same document as a
        // Google Sheets workbook in a Drive folder, which the existing transport
        // already reads (ADR-133).
        Assert.DoesNotContain(
            catalog.Sources,
            source => source.Transport == ScheduleSourceTransport.HttpFile);
        Assert.Equal(4, catalog.Sources.Count(
            source => source.Transport == ScheduleSourceTransport.AdministrativeUpload));

        // The faculty publishes the 2026-2027 Grade 1 workbooks as XLSX files in
        // Drive rather than as Google Sheets, so the transport moved with the
        // documents and the sheet gid went away with it (ADR-131).
        ScheduleSourceDefinition annual = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G1-TR-ANNUAL");
        Assert.Equal(ScheduleSourceTransport.GoogleDriveFile, annual.Transport);
        Assert.Equal(ScheduleDocumentFormat.Xlsx, annual.DocumentFormat);
        Assert.Equal("1FcXgJIn7L9oFJKFCGLrSeefXHocrToAe", annual.ExternalId);
        Assert.Null(annual.SheetGid);
        Assert.Equal("grade1_yearly_v1", annual.ParserProfile);
        Assert.Equal("2026-2027", annual.AcademicYear);

        // The three Grade 3 annual workbooks moved the other way from Grade 1: the
        // faculty trashed the XLSX files and republished each as a Google Sheets
        // workbook, so the transport moved with the documents and a gid came back
        // (ADR-137). The old files failed every poll for four days with
        // `DriveDocumentFailure.Trashed`, which is what made the move visible.
        foreach (string sourceId in new[] { "G3-TR-A-ANNUAL", "G3-TR-B-ANNUAL", "G3-EN-ANNUAL" })
        {
            ScheduleSourceDefinition grade3 = Assert.Single(
                catalog.Sources,
                source => source.SourceId == sourceId);
            Assert.Equal(ScheduleSourceTransport.GoogleSheets, grade3.Transport);
            Assert.Equal(ScheduleDocumentFormat.GoogleSheet, grade3.DocumentFormat);
            Assert.NotNull(grade3.SheetGid);
            Assert.Equal("grade3_yearly_v1", grade3.ParserProfile);
        }

        // Each of the three reads its own workbook. Handing one cohort's document to
        // another source is the mistake this catalog can make that nothing downstream
        // would report: the parse would succeed and half the class would receive the
        // other half's schedule.
        Assert.Equal(
            3,
            catalog.Sources
                .Where(source => source.SourceId.StartsWith("G3-", StringComparison.Ordinal)
                    && source.SourceId.EndsWith("-ANNUAL", StringComparison.Ordinal))
                .Select(source => source.ExternalId)
                .Distinct(StringComparer.Ordinal)
                .Count());

        // The weekly amphitheatre program is a companion of every annual source and
        // publishes nothing itself, so its own class year and language are nominal
        // (ADR-133). What matters is that it is readable at all.
        ScheduleSourceDefinition amphitheatre = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "SHARED-AMPHI");
        Assert.Equal(ScheduleSourceTransport.GoogleSheets, amphitheatre.Transport);
        Assert.Equal(ScheduleDocumentFormat.GoogleSheet, amphitheatre.DocumentFormat);
        Assert.Equal("weekly_amphitheatre_v1", amphitheatre.ParserProfile);
        Assert.Equal("2026-2027", amphitheatre.AcademicYear);

        // The folder is the address, so the workbook is resolved per poll and no
        // worksheet gid is pinned: a gid belongs to one workbook and next week's is
        // a different file. It is the only source configured this way.
        Assert.Equal("1ZkB8GD_niGknZLVD_aGN0oxWm5F_F8G1", amphitheatre.DiscoveryFolderId);
        Assert.Null(amphitheatre.SheetGid);
        Assert.False(string.IsNullOrWhiteSpace(amphitheatre.ExternalId));
        Assert.Single(catalog.Sources, source => source.DiscoveryFolderId is not null);

        // Every annual source reads it, which is what makes the room reach a lesson.
        Assert.Equal(7, catalog.Sources.Count(
            source => source.CompanionSourceIds is not null
                && source.CompanionSourceIds.Contains("SHARED-AMPHI")));

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
        Assert.Equal("1.4.0", grade2TurkishPractice.ParserProfileVersion);

        ScheduleSourceDefinition grade2EnglishPractice = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G2-EN-PRACTICE");
        Assert.Equal(
            ["İ1", "İ2"],
            grade2EnglishPractice.SupportedAudienceSelectors!["practiceGroup"]);
        Assert.Equal("1.4.0", grade2EnglishPractice.ParserProfileVersion);

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

        // The Grade 3 class is split into two curriculum groups that do not share a
        // document. The transport is asserted where the move is explained, above.
        ScheduleSourceDefinition grade3TurkishAnnual = Assert.Single(
            catalog.Sources,
            source => source.SourceId == "G3-TR-A-ANNUAL");
        Assert.Equal("grade3_yearly_v1", grade3TurkishAnnual.ParserProfile);
        Assert.Equal("2026-2027", grade3TurkishAnnual.AcademicYear);
        Assert.Equal(3, grade3TurkishAnnual.ClassYear);

        Assert.Equal(10, catalog.Sources.Count(source => source.ClassYear == 3));
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
