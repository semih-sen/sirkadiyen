using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Scheduling.Sources;
using Sirkadiyen.Infrastructure.StudentRosters;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Guards the committed student-list catalog (ADR-085), which is the only
/// statement of where the lists are and how each is laid out.
/// </summary>
public sealed class StudentRosterCatalogTests
{
    [Fact]
    public async Task TheCommittedCatalogDescribesTheSixPublishedListsAsync()
    {
        StudentRosterCatalog catalog = await LoadAsync();

        Assert.Equal("1.0", catalog.CatalogVersion);
        Assert.Equal(
            [
                "G2-EN-ROSTER",
                "G2-TR-ROSTER",
                "G3-EN-MICROPATHO-ROSTER",
                "G3-EN-ROSTER",
                "G3-TR-MICROPATHO-ROSTER",
                "G3-TR-ROSTER",
            ],
            catalog.Rosters.Select(roster => roster.RosterId).Order(StringComparer.Ordinal));
        Assert.All(catalog.Rosters, roster => Assert.Equal("2026-2027", roster.AcademicYear));
    }

    [Fact]
    public async Task TheMicroPathologyListAddressesItsUnheaderedGroupColumnByLetterAsync()
    {
        StudentRosterCatalog catalog = await LoadAsync();
        StudentRosterDefinition roster = Assert.Single(
            catalog.Rosters,
            candidate => candidate.RosterId == "G3-TR-MICROPATHO-ROSTER");

        Assert.Equal("0101", roster.StudentNumberProgramPrefix);
        StudentRosterDimensionColumn group = Assert.Single(roster.Layout.DimensionColumns);
        Assert.Equal("microPathologyGroup", group.Dimension);
        Assert.Equal("D", group.ColumnLetter);
        Assert.Null(group.Header);
        Assert.True(group.StatedOncePerMergedRun);
        Assert.Equal(["A1", "A2", "B1", "B2"], group.ValueMap.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TwoRostersMayShareACohortWhenTheirDimensionsAreDisjoint()
    {
        // The curriculum-group list and the microbiology/pathology group list both
        // hold every Grade 3 student and state different dimensions, so they merge
        // rather than conflict (ADR-145).
        StudentRosterCatalog catalog = new StudentRosterCatalogLoader().Parse(
            TwoCohortRosters(
                firstDimension: "curriculumGroup",
                secondDimension: "microPathologyGroup"));

        Assert.Equal(2, catalog.Rosters.Count);
    }

    [Fact]
    public void TwoRostersOfOneCohortStatingTheSameDimensionAreRefused()
    {
        StudentRosterCatalogValidationException exception =
            Assert.Throws<StudentRosterCatalogValidationException>(
                () => new StudentRosterCatalogLoader().Parse(
                    TwoCohortRosters(
                        firstDimension: "microPathologyGroup",
                        secondDimension: "microPathologyGroup")));

        Assert.Contains("microPathologyGroup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADimensionColumnAddressedByBothHeaderAndLetterIsRefused()
    {
        Assert.Throws<StudentRosterCatalogValidationException>(
            () => new StudentRosterCatalogLoader().Parse(
                Document(
                    """
                    "transport": "googleSheets",
                    "documentFormat": "googleSheet",
                    "sourceUri": "https://docs.google.com/spreadsheets/d/x/edit",
                    "externalId": "x",
                    "sheetGid": 1,
                    """,
                    dimensionColumns: """
                    {
                      "header": "GRUP",
                      "columnLetter": "D",
                      "dimension": "practiceGroup",
                      "valueMap": { "A": "A" }
                    }
                    """)));
    }

    [Fact]
    public async Task TheGradeTwoTurkishListMapsItsLowercaseSubgroupsOntoTheSchemaAsync()
    {
        // The one place the catalog does real work: the list writes 'a1' and the
        // supported-profile schema says 'A1'. The mapping is exhaustive and
        // declared, never derived by case folding, because Turkish upper-casing
        // would turn an English list's 'i1' into a cohort of a different
        // dimension (ADR-085, ADR-130).
        StudentRosterCatalog catalog = await LoadAsync();
        StudentRosterDefinition roster = Assert.Single(
            catalog.Rosters,
            candidate => candidate.RosterId == "G2-TR-ROSTER");

        Assert.Equal(ScheduleSourceTransport.GoogleSheets, roster.Transport);
        Assert.Equal(143571180, roster.SheetGid);
        Assert.Equal(2, roster.ClassYear);
        Assert.Equal(ProgramLanguage.Turkish, roster.ProgramLanguage);

        StudentRosterDimensionColumn subgroup = Assert.Single(
            roster.Layout.DimensionColumns,
            column => column.Dimension == "practiceSubgroup");
        Assert.True(subgroup.StatedOncePerMergedRun);
        Assert.Equal(16, subgroup.ValueMap.Count);
        Assert.Equal("A1", subgroup.ValueMap["a1"]);
        Assert.Equal("H2", subgroup.ValueMap["h2"]);
        Assert.DoesNotContain("i1", subgroup.ValueMap.Keys);
    }

    [Fact]
    public async Task TheGradeThreeTurkishListMapsItsGroupWordingOntoTheSchemaAsync()
    {
        StudentRosterCatalog catalog = await LoadAsync();
        StudentRosterDefinition roster = Assert.Single(
            catalog.Rosters,
            candidate => candidate.RosterId == "G3-TR-ROSTER");

        StudentRosterDimensionColumn group = Assert.Single(roster.Layout.DimensionColumns);
        Assert.Equal("curriculumGroup", group.Dimension);
        Assert.Equal("3-A", group.ValueMap["A GRUBU"]);
        Assert.Equal("3-B", group.ValueMap["B GRUBU"]);
    }

    [Fact]
    public async Task TheGradeThreeEnglishListDeclaresNoDimensionAtAllAsync()
    {
        // A statement, not an omission. That program states no A/B division, which
        // is why it declares no selector and is absent from the supported-profile
        // schema (ADR-098). Adding a column here would invent one.
        StudentRosterCatalog catalog = await LoadAsync();
        StudentRosterDefinition roster = Assert.Single(
            catalog.Rosters,
            candidate => candidate.RosterId == "G3-EN-ROSTER");

        Assert.Empty(roster.Layout.DimensionColumns);
        Assert.Equal("OgrenciNo", roster.Layout.StudentNumberHeader);
    }

    [Fact]
    public async Task TheGradeTwoEnglishListKeepsItsTwoDivisionsApartAsync()
    {
        // The document proves they are independent: its İ1/İ2 boundary and its
        // i1/i2/i3 boundaries cut across each other. Collapsing them because the
        // labels differ mostly by typography is what ADR-085 forbids.
        StudentRosterCatalog catalog = await LoadAsync();
        StudentRosterDefinition roster = Assert.Single(
            catalog.Rosters,
            candidate => candidate.RosterId == "G2-EN-ROSTER");

        Assert.Equal(
            ["generalGroup", "generalSubgroup"],
            roster.Layout.DimensionColumns.Select(column => column.Dimension));
        Assert.Equal(ScheduleSourceTransport.GoogleDriveFile, roster.Transport);
        Assert.Equal(ScheduleDocumentFormat.Xlsx, roster.DocumentFormat);
        Assert.Null(roster.SheetGid);
    }

    [Fact]
    public void ARosterMayNotDeclareTheAdministrativeUploadTransport()
    {
        // Every published list has a location. Accepting the upload transport
        // would create a roster nothing ever reads, which would look configured
        // and return nobody.
        StudentRosterCatalogValidationException exception =
            Assert.Throws<StudentRosterCatalogValidationException>(
                () => new StudentRosterCatalogLoader().Parse(
                    Document("""
                    "transport": "administrativeUpload",
                    "documentFormat": "xlsx",
                    "sourceUri": "urn:sirkadiyen:upload:x",
                    "externalId": "x",
                    """)));

        Assert.Contains("administrative-upload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASheetsRosterWithoutAWorksheetGidIsRefused()
    {
        Assert.Throws<StudentRosterCatalogValidationException>(
            () => new StudentRosterCatalogLoader().Parse(
                Document("""
                "transport": "googleSheets",
                "documentFormat": "googleSheet",
                "sourceUri": "https://docs.google.com/spreadsheets/d/x/edit",
                "externalId": "x",
                """)));
    }

    [Fact]
    public void APropertyTheModelDoesNotKnowIsRefusedRatherThanDropped()
    {
        // A mistyped key would otherwise deserialize to nothing, validate cleanly,
        // and leave a list read by a layout nobody intended.
        Assert.Throws<StudentRosterCatalogValidationException>(
            () => new StudentRosterCatalogLoader().Parse(
                Document("""
                "transport": "googleSheets",
                "documentFormat": "googleSheet",
                "sourceUri": "https://docs.google.com/spreadsheets/d/x/edit",
                "externalId": "x",
                "sheetGid": 1,
                "worksheetTitle": "Sayfa1",
                """)));
    }

    [Fact]
    public void ADimensionColumnThatMapsNoValueIsRefused()
    {
        // An empty map refuses every value the column writes while looking like a
        // configured column. A column that should suggest nothing is removed.
        Assert.Throws<StudentRosterCatalogValidationException>(
            () => new StudentRosterCatalogLoader().Parse(
                Document(
                    """
                    "transport": "googleSheets",
                    "documentFormat": "googleSheet",
                    "sourceUri": "https://docs.google.com/spreadsheets/d/x/edit",
                    "externalId": "x",
                    "sheetGid": 1,
                    """,
                    dimensionColumns: """
                    { "header": "GRUP", "dimension": "practiceGroup", "valueMap": {} }
                    """)));
    }

    private static string TwoCohortRosters(string firstDimension, string secondDimension) =>
        $$"""
        {
          "catalogVersion": "1.0",
          "rosters": [
            {{CohortRoster("ROSTER-ONE", firstDimension, "C")}},
            {{CohortRoster("ROSTER-TWO", secondDimension, "D")}}
          ]
        }
        """;

    private static string CohortRoster(string rosterId, string dimension, string columnLetter) =>
        $$"""
        {
          "rosterId": "{{rosterId}}",
          "displayName": "Test",
          "transport": "googleDriveFile",
          "documentFormat": "xlsx",
          "sourceUri": "https://drive.google.com/file/d/x/view",
          "externalId": "x",
          "academicYear": "2026-2027",
          "classYear": 3,
          "programLanguage": "turkish",
          "layout": {
            "worksheetTitle": "Sayfa1",
            "headerRow": 1,
            "studentNumberHeader": "Öğrenci No",
            "givenNameHeader": "Ad",
            "familyNameHeader": "Soyad",
            "dimensionColumns": [
              {
                "columnLetter": "{{columnLetter}}",
                "dimension": "{{dimension}}",
                "valueMap": { "A1": "A1" }
              }
            ]
          }
        }
        """;

    private static async Task<StudentRosterCatalog> LoadAsync() =>
        await new StudentRosterCatalogLoader().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "student-rosters.json"),
            CancellationToken.None);

    private static string Document(string transportFields, string dimensionColumns = "") =>
        $$"""
        {
          "catalogVersion": "1.0",
          "rosters": [
            {
              "rosterId": "TEST-ROSTER",
              "displayName": "Test",
              {{transportFields}}
              "academicYear": "2026-2027",
              "classYear": 2,
              "programLanguage": "turkish",
              "layout": {
                "worksheetTitle": "Sayfa1",
                "headerRow": 1,
                "studentNumberHeader": "Öğrenci No",
                "givenNameHeader": "Ad",
                "familyNameHeader": "Soyad",
                "dimensionColumns": [{{dimensionColumns}}]
              }
            }
          ]
        }
        """;
}
