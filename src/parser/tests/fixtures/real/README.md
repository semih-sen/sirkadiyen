# Real normalized snapshot fixtures

These JSON files are deterministic conversions of the committed public XLSX and
DOCX fixtures. They are real schedule structures, not synthetic examples, but
they were produced by the local development converter rather than Google Sheets
API.

Each snapshot includes the `snapshot.local_xlsx_fixture` acquisition diagnostic
so it cannot be mistaken for a production API capture. Regenerate a fixture with
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
| `g2-en-annual.snapshot.json` | `G2-EN-ANNUAL` | English headers, `lunch break` rows with an empty term and date cell, a bare `9` in an `hh:mm` start-time cell, practice groups written inside titles (`İ1`-`İ5`) |

## Converted from Word documents

Four Grade 2 sources are published as DOCX and are converted onto the same
contract (ADR-076): a Word table becomes a worksheet, a run of paragraphs
between tables becomes a single-column worksheet, and `Table n` / `Text n`
titles are assigned by the converter because Word names nothing. Every value is
text and no cell declares a number format, so a profile reading one of these
resolves dates and times from text alone.

| Fixture | Source | Notes |
| --- | --- | --- |
| `g2-anatomy-autumn.snapshot.json` | `G2-ANATOMY-AUTUMN` | the dissection rotation `grade2_yearly_v1` defers to: `date / hour / anatomy group` over two tables split by a page break. The first 45 rows write the date in the middle row of each triple and leave the neighbours empty; from row 46 the same thing is a vertical merge. 30 teaching days, every date sound |
| `g2-anatomy-spring.snapshot.json` | `G2-ANATOMY-SPRING` | the same layout, 49 + 21 rows, 23 teaching days. One of them states `9 Nisan 2025` where it means 2026, and its own weekday says so |
| `g2-vertical-autumn.snapshot.json` | `G2-VERTICAL-AUTUMN` | one 60x7 table: a row is a dated slot whose first cell holds a label, a date and a time range on three lines, and a column is a skill practice. This is where the Grade 2 practice sheet's `*` cells are answered. Only twelve of its 53 dated rows name a group so far — the document is filled in over the year |
| `g2-vertical-spring.snapshot.json` | `G2-VERTICAL-SPRING` | the same table split across seven Word tables, one of which leaves the place header empty. Carries subgroups (`B2`), two-cohort runs (`CD`), examinations (`A-B-C-D SINAV`), the English programme's cohorts and the separately published `EK-n` lists |

All four are in `config/schedule-sources.json`, so all four regenerate through
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
