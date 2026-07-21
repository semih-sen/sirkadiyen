# Real normalized snapshot fixtures

These JSON files are deterministic conversions of the committed public XLSX
fixtures. They are real schedule structures, not synthetic examples, but they
were produced by the local development converter rather than Google Sheets API.

Each snapshot includes the `snapshot.local_xlsx_fixture` acquisition diagnostic
so it cannot be mistaken for a production API capture. Regenerate a fixture with
`tools/Sirkadiyen.SnapshotTool` and review the resulting diff whenever the
converter or source workbook changes.
