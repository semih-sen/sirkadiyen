# Real normalized snapshot fixtures

These JSON files are deterministic conversions of the committed public XLSX and
DOCX fixtures. They are real schedule structures, not synthetic examples, but
they were produced by the local development converter rather than Google Sheets
API.

Each snapshot includes a `snapshot.local_xlsx_fixture` or
`snapshot.local_docx_fixture` acquisition diagnostic so it cannot be mistaken for
a production capture — a downloaded document says `snapshot.google_drive_download`
instead, and the diagnostic is part of the hashed content. Regenerate a fixture with
`tools/Sirkadiyen.SnapshotTool` and review the resulting diff whenever the
converter or source workbook changes:

```powershell
dotnet run --project tools/Sirkadiyen.SnapshotTool -- `
  --repository-root . --source-id G1-EN-ANNUAL `
  --output src/parser/tests/fixtures/real/g1-en-annual.snapshot.json `
  --acquired-at-utc 2026-07-21T00:00:00Z
```

| Fixture | Source | Notes |
| --- | --- | --- |
| `g1-tr-annual.snapshot.json` | `G1-TR-ANNUAL` | one worksheet, hidden rows, time cells the source turned into dates |
| `g1-en-annual.snapshot.json` | `G1-EN-ANNUAL` | English headers, a second lookup worksheet, rows shifted by one column |
| `g1-tr-practice.snapshot.json` | `G1-TR-PRACTICE` | rotation matrix, not yet parsed by any profile |
| `g2-tr-annual.snapshot.json` | `G2-TR-ANNUAL` | one worksheet `DÖNEM 2`; 119 bare `UYGULAMA` placeholders that defer to the practice program, 159 dissection rotation rows, one backwards time range |
| `g2-tr-practice.snapshot.json` | `G2-TR-PRACTICE` | slot-column rotation: nine curriculum blocks, 15 slot-header rows, topic lists between them, four slot dates whose year is a year out, a whole-cohort session written into a merged run of cells |
| `g2-en-practice.snapshot.json` | `G2-EN-PRACTICE` | the original workbook filename says 2024-2025, but its 39 dated practice slots run from September 2025 through May 2026; groups are `İ1`/`İ2`, and the lone 2024 value is in an anatomy row this profile defers to the anatomy source |
| `g2-en-annual.snapshot.json` | `G2-EN-ANNUAL` | the 2026-2027 workbook, captured while its Turkish counterpart is still committed at 2025-2026. One worksheet `CLASS 2`, used range `A1:G1278`; **the term column carries no header** — `A1` is empty where the 2025-2026 capture wrote `Dönem`, while every row below still states `Time Table 2` (ADR-128). `lunch break` rows with an empty term and date cell, one backwards time range, a `NEW YEAR BREAK` row with a date and no times, practice groups written inside titles |
| `g3-tr-a-annual.snapshot.json` | `G3-TR-A-ANNUAL` | one worksheet `A GRUBU`, no merges; the term column carries no header and states the curriculum group (`Dönem 3A Grubu`, and `Dönem 3A+3B Grubu` on joint sessions). 64 `Öğretim üyesi Uygulama 1`-`8` rows the faculty source owns, 92 `Hasta Başı` rows this profile keeps, one row with a date and no time |
| `g3-tr-b-annual.snapshot.json` | `G3-TR-B-ANNUAL` | the same layout for the B group; its header row writes `Başlama Saati` where A writes `Başlangıç Saati`, and a stray `25` sits above the unlabelled term column. Also writes the joint group as `Dönem 3B/3A Grubu` |
| `g3-en-annual.snapshot.json` | `G3-EN-ANNUAL` | one worksheet `İNG`; English headers including the misspelled `DEPARTMEND`. Its term cell reads `Time Table 3`, but 49 joint-lecture rows write `Dönem 3A Grubu` and one reads `cc` |
| `g3-tr-a-faculty.snapshot.json` | `G3-TR-A-FACULTY` | eight rotation blocks in one worksheet, each a merged title stating its curriculum block and the 11:10-12:10 practice hour, a department header row, a `TARİH` marker and eight date rows over cohorts `A1`-`A8`. Free-text topic lists sit between the blocks. One date row (2027-03-24, SİNDİRİM 2) writes `A4` twice and omits `A8` |
| `g3-tr-b-faculty.snapshot.json` | `G3-TR-B-FACULTY` | the same eight blocks in a different order over cohorts `B1`-`B8`; every date row states all eight cohorts exactly once |
| `g3-faculty-locations.snapshot.json` | `G3-FACULTY-LOCATIONS` | a small lookup, not a schedule: a merged curriculum-block heading, a `PRATİK ADI` / `PRATİK YERİ` header and one row per department. Its department wording does not match the faculty matrix headers, and five rooms are blank |

## Converted from Word documents

Six sources are published as DOCX and are converted onto the same contract
(ADR-076): a Word table becomes a worksheet, a run of paragraphs between tables
becomes a single-column worksheet, and `Table n` / `Text n` titles are assigned
by the converter because Word names nothing. Every value is text and no cell
declares a number format, so a profile reading one of these resolves dates and
times from text alone.

| Fixture | Source | Notes |
| --- | --- | --- |
| `g2-anatomy-autumn.snapshot.json` | `G2-ANATOMY-AUTUMN` | the dissection rotation `grade2_yearly_v1` defers to: `date / hour / anatomy group` over two tables split by a page break. The first 45 rows write the date in the middle row of each triple and leave the neighbours empty; from row 46 the same thing is a vertical merge. 30 teaching days, every date sound |
| `g2-anatomy-spring.snapshot.json` | `G2-ANATOMY-SPRING` | the same layout, 49 + 21 rows, 23 teaching days. One of them states `9 Nisan 2025` where it means 2026, and its own weekday says so |
| `g2-vertical-autumn.snapshot.json` | `G2-VERTICAL-AUTUMN` | one 60x7 table: a row is a dated slot whose first cell holds a label, a date and a time range on three lines, and a column is a skill practice. This is where the Grade 2 practice sheet's `*` cells are answered. Only twelve of its 53 dated rows name a group so far — the document is filled in over the year |
| `g2-vertical-spring.snapshot.json` | `G2-VERTICAL-SPRING` | the same table split across seven Word tables, one of which leaves the place header empty. Carries subgroups (`B2`), two-cohort runs (`CD`), examinations (`A-B-C-D SINAV`), the English programme's cohorts and the separately published `EK-n` lists |
| `g3-tr-a-bedside.snapshot.json` | `G3-TR-A-BEDSIDE` | nine worksheets: `Text 1` is the topic catalogue (177 paragraphs, sectioned by department), `Table 1`-`Table 4` are one-cell Word artifacts holding a topic that Word happened to wrap in a table, and `Table 5` is the schedule — `Tarih / A Grubu / ⟨spacer⟩ / Tarih / A Grubu`, autumn on the left and spring on the right. Dates are dotted text and one carries leading spaces |
| `g3-tr-b-bedside.snapshot.json` | `G3-TR-B-BEDSIDE` | the same document for the B group in two worksheets: the catalogue and one four-column schedule table with **no spacer column** and upper-case headers, so a reader must pair its date and topic columns by header rather than by position |

All six are in `config/schedule-sources.json`, so all six regenerate through
the ordinary `--source-id` form above. The anatomy pair is catalogued under the
`administrativeUpload` transport (ADR-079): the document has no URL, so the
entry names itself with a URN and an administrator uploads the file each
semester through `POST /api/sources/{sourceId}/document` (ADR-080).

Each anatomy document is catalogued twice, once per program, and one upload
serves both. These fixtures cover only the Turkish source: the English
counterpart converts the same bytes and differs only in the source identity it
carries. Note that a snapshot produced by an upload is deliberately **not**
identical to one of these — its acquisition diagnostic says it was uploaded
rather than converted from a fixture, and that diagnostic is part of the hashed
content.

Use `--document` only for a file the catalog does not describe yet:

```powershell
dotnet run --project tools/Sirkadiyen.SnapshotTool -- `
  --repository-root . --source-id G1-ANATOMY `
  --document "sheets/donem-1-tr/some-new-document.docx" `
  --output src/parser/tests/fixtures/real/g1-anatomy.snapshot.json `
  --acquired-at-utc 2026-07-25T00:00:00Z
```
