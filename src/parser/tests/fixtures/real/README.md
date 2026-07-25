# Real normalized snapshot fixtures

These JSON files are deterministic conversions of the committed public XLSX
fixtures. They are real schedule structures, not synthetic examples, but they
were produced by the local development converter rather than Google Sheets API.

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
| `g2-en-annual.snapshot.json` | `G2-EN-ANNUAL` | English headers, `lunch break` rows with an empty term and date cell, a bare `9` in an `hh:mm` start-time cell, practice groups written inside titles (`İ1`-`İ5`) |
