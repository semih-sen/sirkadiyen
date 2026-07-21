# Source Inventory Notes

## Inspection scope

The 17 `.xlsx` fixtures present on 2026-07-21 were imported without modifying
the source files. CSV exports were retained as secondary inspection aids. DOCX
files were inventoried but were not treated as normalized Google Sheets
snapshots.

## Structural families

### Annual programs

The first-, second-, and third-year annual programs are predominantly dense,
row-oriented schedules. Dates and times may arrive as Excel serial numbers and
fractions rather than display text. The parser must retain raw typed values and
formatted values so date interpretation remains deterministic.

Observed main worksheets:

- Grade 1 Turkish: `DÖNEM 1`, `A1:M942`
- Grade 1 English: `CLASS 1`, `A1:Z985`
- Grade 2 Turkish: `DÖNEM 2`, `A1:G1157`
- Grade 2 English: `CLASS 2`, `A1:I1253`
- Grade 3 Turkish A: `A GRUBU`, `A1:G1283`
- Grade 3 Turkish B: `B GRUBU`, `A1:H1287`

The Grade 1 English workbook also contains a small `UYGULAMA YERLERİ` lookup
worksheet. Parser profiles must select meaningful worksheets explicitly rather
than assuming the first worksheet is the entire source.

### Practice and faculty-practice programs

Practice sources use repeated blocks and rotation matrices. Dates commonly run
down the first column while practice groups rotate across department columns.
Headers, topic descriptions, and lookup data may be interleaved in one used
range. Blank separators and merged headings are structural evidence and must not
be discarded during snapshot acquisition.

Grade 3 faculty-practice locations are stored in a separate two-column workbook.
The schedule and location lookup therefore require explicit, traceable
enrichment rather than filename-based implicit joining.

### Weekly amphitheatre assignments

Weekly amphitheatre sources are room-oriented matrices with time slots on rows
and rooms on columns. The inspected files contain three or four worksheets and
some secondary worksheets have large used ranges with historical or repeated
content. The parser profile must restrict itself to configured worksheet scopes
and the requested week before producing enrichment candidates.

## Known gaps

- No Grade 1 anatomy-practice fixture has been confidently identified.
- Grade 2 anatomy autumn, anatomy spring, and vertical-corridor fixtures remain
  unclassified. Several DOCX files are candidates but require source-owner
  confirmation.
- Grade 3 English annual, bedside, and faculty-practice fixtures are missing.
- Grade 3 bedside fixtures currently exist only as DOCX references; raw Google
  Sheets snapshots are still needed.
- The Grade 2 English practice fixture is from academic year 2024-2025 while the
  other primary fixtures are mostly 2025-2026.

## Parser implications

- Preserve raw values, formulas, formatted values, merges, hidden dimensions,
  and relevant formatting metadata in the normalized snapshot.
- Use zero-based row and column coordinates in transport contracts and preserve
  A1 addresses only as evidence.
- Select worksheets and ranges through source configuration.
- Treat Unicode text as source data; never normalize Turkish characters by
  lossy encoding conversion.
- Do not infer the identity of unclassified DOCX fixtures from filenames alone.
