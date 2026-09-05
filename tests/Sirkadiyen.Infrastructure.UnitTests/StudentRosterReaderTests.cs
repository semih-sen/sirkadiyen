using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Contracts.Spreadsheets;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// Covers the reader against the shapes the four published faculty lists
/// actually have (ADR-085).
/// </summary>
/// <remarks>
/// The snapshots here are written by hand rather than loaded from a workbook,
/// because the real lists hold student names and numbers and must not enter the
/// repository. Every quirk they exercise is one of the real documents': the
/// merged group runs, the student numbers a spreadsheet stored as numbers, and
/// the subgroup column that writes <c>a1</c> where the schema says <c>A1</c>.
/// </remarks>
public sealed class StudentRosterReaderTests
{
    private readonly StudentRosterReader reader = new();

    [Fact]
    public void EveryStudentInAMergedRunTakesTheRunsGroup()
    {
        // The shape of all three grouped lists: the value is written once at the
        // top of the run and the rest of the run is merged into it. Read row by
        // row, a list of 384 students would state one group and 383 blanks.
        StudentRosterReading reading = reader.Read(
            Grade2Turkish(),
            Snapshot(
                headers: ["GRUP", "Alt Grup", "Öğrenci No", "Ad", "Soyad"],
                rows:
                [
                    ["A", "a1", "0101250001", "BİR", "ÖĞRENCİ"],
                    ["", "", "0101250002", "İKİ", "ÖĞRENCİ"],
                    ["", "a2", "0101250003", "ÜÇ", "ÖĞRENCİ"],
                    ["B", "b1", "0101250004", "DÖRT", "ÖĞRENCİ"],
                ],
                merged:
                [
                    Merge(startRow: 1, endRowExclusive: 4, column: 0),
                    Merge(startRow: 1, endRowExclusive: 3, column: 1),
                ]));

        Assert.Empty(reading.RefusedRows);
        Assert.Equal(4, reading.Entries.Count);
        Assert.Equal(
            ["A", "A", "A", "B"],
            reading.Entries.Select(entry => entry.Selectors["practiceGroup"]));
        Assert.Equal(
            ["A1", "A1", "A2", "B1"],
            reading.Entries.Select(entry => entry.Selectors["practiceSubgroup"]));
    }

    [Fact]
    public void TheSharedListReadsOnlyItsOwnProgramsRowsAndGroupsThemByMergedRun()
    {
        // One file holds both programs, keyed only by the microbiology/pathology
        // group in an unheadered column D, so this roster addresses D by letter and
        // claims only the 0101 rows via its prefix (ADR-145). The 0102 row is another
        // program's and is skipped silently, not refused.
        NormalizedSpreadsheetSnapshot snapshot = Snapshot(
            headers: ["Öğrenci No", "Ad", "Soyad", ""],
            rows:
            [
                ["0101250001", "BİR", "ÖĞRENCİ", "A1"],
                ["0102250002", "TWO", "STUDENT", ""],
                ["0101250003", "ÜÇ", "ÖĞRENCİ", ""],
                ["0101250004", "DÖRT", "ÖĞRENCİ", "B2"],
            ],
            merged:
            [
                Merge(startRow: 1, endRowExclusive: 4, column: 3),
            ]);

        StudentRosterReading turkish = reader.Read(MicroPatho(ProgramLanguage.Turkish, "0101"), snapshot);

        Assert.Empty(turkish.RefusedRows);
        Assert.Equal(
            ["0101250001", "0101250003", "0101250004"],
            turkish.Entries.Select(entry => entry.StudentNumber));
        Assert.Equal(
            ["A1", "A1", "B2"],
            turkish.Entries.Select(entry => entry.Selectors["microPathologyGroup"]));

        StudentRosterReading english = reader.Read(MicroPatho(ProgramLanguage.English, "0102"), snapshot);

        StudentRosterEntry only = Assert.Single(english.Entries);
        Assert.Equal("0102250002", only.StudentNumber);
        Assert.Equal("A1", only.Selectors["microPathologyGroup"]);
    }

    [Fact]
    public void AGapNoMergedRangeCoversIsNotFilledFromTheRowAbove()
    {
        // Carrying the value down would look identical for a merged run and read a
        // group the document never stated for that student. The document's own
        // merge is the evidence; without it there is none.
        StudentRosterReading reading = reader.Read(
            Grade2Turkish(),
            Snapshot(
                headers: ["GRUP", "Alt Grup", "Öğrenci No", "Ad", "Soyad"],
                rows:
                [
                    ["A", "a1", "0101250001", "BİR", "ÖĞRENCİ"],
                    ["", "", "0101250002", "İKİ", "ÖĞRENCİ"],
                ],
                merged: []));

        StudentRosterEntry orphan = reading.Entries[1];
        Assert.Empty(orphan.Selectors);
        Assert.Equal("0101250002", orphan.StudentNumber);
        Assert.Equal(
            2,
            reading.Warnings.Count(warning =>
                warning.Code == StudentRosterWarningCode.DimensionValueUnstated));
    }

    [Fact]
    public void AStudentNumberTheSpreadsheetStoredAsANumberRegainsItsLeadingZero()
    {
        // Five cells across the four real lists are like this. A nine-digit numeric
        // value is missing exactly one zero, because every valid number is ten
        // digits, so the recovery is a reading rather than a guess.
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad", "Grubu"],
                rows: [[101240215m, "BİR", "ÖĞRENCİ", "A GRUBU"]],
                merged: []));

        StudentRosterEntry entry = Assert.Single(reading.Entries);
        Assert.Equal("0101240215", entry.StudentNumber);
        StudentRosterWarning warning = Assert.Single(reading.Warnings);
        Assert.Equal(StudentRosterWarningCode.StudentNumberLeadingZeroRestored, warning.Code);
        Assert.Equal("A2", warning.A1Address);
    }

    [Fact]
    public void ARowWhoseNumberCannotBeReadIsRefusedRatherThanDropped()
    {
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad", "Grubu"],
                rows:
                [
                    ["0101240001", "BİR", "ÖĞRENCİ", "A GRUBU"],
                    ["not a number", "İKİ", "ÖĞRENCİ", ""],
                    ["", "ÜÇ", "ÖĞRENCİ", ""],
                ],
                merged: [Merge(startRow: 1, endRowExclusive: 4, column: 3)]));

        Assert.Single(reading.Entries);
        Assert.Equal(
            [StudentRosterWarningCode.StudentNumberMalformed, StudentRosterWarningCode.StudentNumberMissing],
            reading.RefusedRows.Select(row => row.Code));
        Assert.Equal([3, 4], reading.RefusedRows.Select(row => row.RowNumber));
    }

    [Fact]
    public void ARowTheDocumentLeftBlankIsNotARefusal()
    {
        // Every grouped list ends in blank rows, because the merged group cell
        // stretches past the last name. They state no student, so there is nothing
        // to refuse and nothing for an operator to investigate.
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad", "Grubu"],
                rows:
                [
                    ["0101240001", "BİR", "ÖĞRENCİ", "A GRUBU"],
                    ["", "", "", ""],
                ],
                merged: []));

        Assert.Single(reading.Entries);
        Assert.Empty(reading.RefusedRows);
    }

    [Fact]
    public void AValueTheColumnIsNotDeclaredToWriteIsRefusedRatherThanTransformed()
    {
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad", "Grubu"],
                rows: [["0101240001", "BİR", "ÖĞRENCİ", "C GRUBU"]],
                merged: []));

        StudentRosterEntry entry = Assert.Single(reading.Entries);
        Assert.Empty(entry.Selectors);
        StudentRosterWarning warning = Assert.Single(reading.Warnings);
        Assert.Equal(StudentRosterWarningCode.UnmappedDimensionValue, warning.Code);
        Assert.Contains("C GRUBU", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mapping is declared value by value, and this is why.
    /// </summary>
    /// <remarks>
    /// Upper-casing would read the Grade 2 Turkish <c>a1</c> correctly and, in
    /// Turkish, turn the Grade 2 English <c>i1</c> into <c>İ1</c> — a value that
    /// belongs to a different dimension of a different program. ADR-130 had to
    /// bound the same fold in the parser; here it is simply never applied.
    /// </remarks>
    [Fact]
    public void ATurkishDottedICohortIsNotInventedByCaseFolding()
    {
        StudentRosterDefinition grade2English = new()
        {
            RosterId = "G2-EN-ROSTER",
            DisplayName = "Grade 2 English",
            Transport = ScheduleSourceTransport.GoogleDriveFile,
            DocumentFormat = ScheduleDocumentFormat.Xlsx,
            SourceUri = new Uri("https://drive.google.com/file/d/x/view"),
            ExternalId = "x",
            AcademicYear = "2026-2027",
            ClassYear = 2,
            ProgramLanguage = ProgramLanguage.English,
            Layout = new StudentRosterLayout
            {
                WorksheetTitle = "İNG",
                HeaderRow = 1,
                StudentNumberHeader = "Öğrenci No",
                GivenNameHeader = "Ad",
                FamilyNameHeader = "Soyad",
                DimensionColumns =
                [
                    new StudentRosterDimensionColumn
                    {
                        Header = "Genel Alt Grup",
                        Dimension = "generalSubgroup",
                        StatedOncePerMergedRun = true,
                        ValueMap = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["i1"] = "i1",
                        },
                    },
                ],
            },
        };

        StudentRosterReading reading = reader.Read(
            grade2English,
            Snapshot(
                headers: ["Genel Alt Grup", "Öğrenci No", "Ad", "Soyad"],
                rows: [["i1", "0102250001", "BİR", "ÖĞRENCİ"]],
                merged: [],
                worksheetTitle: "İNG"));

        StudentRosterEntry entry = Assert.Single(reading.Entries);
        Assert.Equal("i1", entry.Selectors["generalSubgroup"]);
    }

    [Fact]
    public void AHeaderIsMatchedThroughTheWhitespaceTheDocumentWrites()
    {
        // Two of the four lists write 'Ad ' with a trailing space and one writes
        // 'Genel  Alt Grup' with two.
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci  No", "Ad ", " Soyad", "Grubu"],
                rows: [["0101240001", "BİR", "ÖĞRENCİ", "A GRUBU"]],
                merged: []));

        Assert.Empty(reading.Warnings);
        Assert.Single(reading.Entries);
    }

    [Fact]
    public void ANumberTwoRowsShareIsReportedRatherThanResolved()
    {
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad", "Grubu"],
                rows:
                [
                    ["0101240001", "BİR", "ÖĞRENCİ", "A GRUBU"],
                    ["0101240001", "İKİ", "ÖĞRENCİ", "A GRUBU"],
                ],
                merged: []));

        Assert.Equal(2, reading.Entries.Count);
        StudentRosterWarning warning = Assert.Single(reading.Warnings);
        Assert.Equal(StudentRosterWarningCode.DuplicateStudentNumber, warning.Code);
    }

    [Fact]
    public void AListMissingItsWorksheetYieldsNoStudentsAndSaysWhy()
    {
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad", "Grubu"],
                rows: [["0101240001", "BİR", "ÖĞRENCİ", "A GRUBU"]],
                merged: [],
                worksheetTitle: "Renamed"));

        Assert.Empty(reading.Entries);
        StudentRosterWarning warning = Assert.Single(reading.Warnings);
        Assert.Equal(StudentRosterWarningCode.WorksheetMissing, warning.Code);
    }

    [Fact]
    public void AListMissingAColumnYieldsNoStudentsAndNamesTheColumn()
    {
        // A renamed header would otherwise read every student with no group, which
        // is a silently emptied list rather than a broken one.
        StudentRosterReading reading = reader.Read(
            Grade3Turkish(),
            Snapshot(
                headers: ["Öğrenci No", "Ad", "Soyad"],
                rows: [["0101240001", "BİR", "ÖĞRENCİ"]],
                merged: []));

        Assert.Empty(reading.Entries);
        StudentRosterWarning warning = Assert.Single(reading.Warnings);
        Assert.Equal(StudentRosterWarningCode.ColumnMissing, warning.Code);
        Assert.Contains("Grubu", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListThatStatesNoGroupSuggestsIdentityOnly()
    {
        // The Grade 3 English list. Declaring no dimension columns is a statement,
        // not an omission: that program states no A/B division at all (ADR-098).
        StudentRosterDefinition grade3English = Grade3Turkish() with
        {
            RosterId = "G3-EN-ROSTER",
            ProgramLanguage = ProgramLanguage.English,
            Layout = Grade3Turkish().Layout with
            {
                StudentNumberHeader = "OgrenciNo",
                DimensionColumns = [],
            },
        };

        StudentRosterReading reading = reader.Read(
            grade3English,
            Snapshot(
                headers: ["OgrenciNo", "Ad", "Soyad"],
                rows: [["0102240001", "BİR", "ÖĞRENCİ"]],
                merged: []));

        StudentRosterEntry entry = Assert.Single(reading.Entries);
        Assert.Empty(entry.Selectors);
        Assert.Empty(reading.Warnings);
    }

    private static StudentRosterDefinition Grade2Turkish() => new()
    {
        RosterId = "G2-TR-ROSTER",
        DisplayName = "Grade 2 Turkish",
        Transport = ScheduleSourceTransport.GoogleSheets,
        DocumentFormat = ScheduleDocumentFormat.GoogleSheet,
        SourceUri = new Uri("https://docs.google.com/spreadsheets/d/x/edit"),
        ExternalId = "x",
        SheetGid = 1,
        AcademicYear = "2026-2027",
        ClassYear = 2,
        ProgramLanguage = ProgramLanguage.Turkish,
        Layout = new StudentRosterLayout
        {
            WorksheetTitle = "Sayfa1",
            HeaderRow = 1,
            StudentNumberHeader = "Öğrenci No",
            GivenNameHeader = "Ad",
            FamilyNameHeader = "Soyad",
            DimensionColumns =
            [
                new StudentRosterDimensionColumn
                {
                    Header = "GRUP",
                    Dimension = "practiceGroup",
                    StatedOncePerMergedRun = true,
                    ValueMap = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["A"] = "A",
                        ["B"] = "B",
                    },
                },
                new StudentRosterDimensionColumn
                {
                    Header = "Alt Grup",
                    Dimension = "practiceSubgroup",
                    StatedOncePerMergedRun = true,
                    ValueMap = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["a1"] = "A1",
                        ["a2"] = "A2",
                        ["b1"] = "B1",
                    },
                },
            ],
        },
    };

    private static StudentRosterDefinition Grade3Turkish() => new()
    {
        RosterId = "G3-TR-ROSTER",
        DisplayName = "Grade 3 Turkish",
        Transport = ScheduleSourceTransport.GoogleSheets,
        DocumentFormat = ScheduleDocumentFormat.GoogleSheet,
        SourceUri = new Uri("https://docs.google.com/spreadsheets/d/y/edit"),
        ExternalId = "y",
        SheetGid = 2,
        AcademicYear = "2026-2027",
        ClassYear = 3,
        ProgramLanguage = ProgramLanguage.Turkish,
        Layout = new StudentRosterLayout
        {
            WorksheetTitle = "Sayfa1",
            HeaderRow = 1,
            StudentNumberHeader = "Öğrenci No",
            GivenNameHeader = "Ad",
            FamilyNameHeader = "Soyad",
            DimensionColumns =
            [
                new StudentRosterDimensionColumn
                {
                    Header = "Grubu",
                    Dimension = "curriculumGroup",
                    StatedOncePerMergedRun = true,
                    ValueMap = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["A GRUBU"] = "3-A",
                        ["B GRUBU"] = "3-B",
                    },
                },
            ],
        },
    };

    private static StudentRosterDefinition MicroPatho(ProgramLanguage language, string prefix) => new()
    {
        RosterId = $"G3-{(language == ProgramLanguage.Turkish ? "TR" : "EN")}-MICROPATHO-ROSTER",
        DisplayName = "Grade 3 microbiology/pathology",
        Transport = ScheduleSourceTransport.GoogleDriveFile,
        DocumentFormat = ScheduleDocumentFormat.Xlsx,
        SourceUri = new Uri("https://drive.google.com/file/d/z/view"),
        ExternalId = "z",
        AcademicYear = "2026-2027",
        ClassYear = 3,
        ProgramLanguage = language,
        StudentNumberProgramPrefix = prefix,
        Layout = new StudentRosterLayout
        {
            WorksheetTitle = "Sayfa1",
            HeaderRow = 1,
            StudentNumberHeader = "Öğrenci No",
            GivenNameHeader = "Ad",
            FamilyNameHeader = "Soyad",
            DimensionColumns =
            [
                new StudentRosterDimensionColumn
                {
                    ColumnLetter = "D",
                    Dimension = "microPathologyGroup",
                    StatedOncePerMergedRun = true,
                    ValueMap = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["A1"] = "A1",
                        ["A2"] = "A2",
                        ["B1"] = "B1",
                        ["B2"] = "B2",
                    },
                },
            ],
        },
    };

    private static GridRange Merge(int startRow, int endRowExclusive, int column) => new()
    {
        StartRowIndex = startRow,
        EndRowIndexExclusive = endRowExclusive,
        StartColumnIndex = column,
        EndColumnIndexExclusive = column + 1,
    };

    /// <summary>
    /// Builds the snapshot the acquisition layer would hand the reader. A
    /// <see cref="decimal"/> cell becomes a numeric cell, which is how a
    /// spreadsheet that dropped a leading zero arrives.
    /// </summary>
    private static NormalizedSpreadsheetSnapshot Snapshot(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<object>> rows,
        IReadOnlyList<GridRange> merged,
        string worksheetTitle = "Sayfa1")
    {
        List<NormalizedCell> cells = [];
        AddRow(cells, 0, headers.Cast<object>().ToArray());
        for (int index = 0; index < rows.Count; index++)
        {
            AddRow(cells, index + 1, rows[index]);
        }

        return new NormalizedSpreadsheetSnapshot
        {
            ContractVersion = SpreadsheetContractVersions.V1,
            SourceId = "TEST",
            SnapshotId = "snapshot",
            SpreadsheetId = "spreadsheet",
            AcquiredAtUtc = DateTimeOffset.UnixEpoch,
            ContentHash = "hash",
            ContentHashAlgorithm = "sha256",
            Worksheets =
            [
                new NormalizedWorksheet
                {
                    SheetId = "0",
                    Title = worksheetTitle,
                    Index = 0,
                    RowCount = rows.Count + 1,
                    ColumnCount = headers.Count,
                    MergedRanges = merged,
                    Cells = cells,
                },
            ],
        };
    }

    private static void AddRow(List<NormalizedCell> cells, int rowIndex, IReadOnlyList<object> values)
    {
        for (int column = 0; column < values.Count; column++)
        {
            object value = values[column];
            if (value is string text && text.Length == 0)
            {
                continue;
            }

            cells.Add(new NormalizedCell
            {
                RowIndex = rowIndex,
                ColumnIndex = column,
                A1Address = $"{(char)('A' + column)}{rowIndex + 1}",
                EffectiveValue = value is decimal number
                    ? new CellScalar { Kind = CellScalarKind.Number, NumberValue = number }
                    : new CellScalar { Kind = CellScalarKind.Text, TextValue = (string)value },
            });
        }
    }
}
