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
- Grade 3 English annual, bedside, and faculty-practice fixtures are missing.
- Grade 3 bedside fixtures currently exist only as DOCX references; raw Google
  Sheets snapshots are still needed.
- The Grade 2 English practice fixture is from academic year 2024-2025 while the
  other primary fixtures are mostly 2025-2026.

## Confirmed Grade 1 and Grade 2 special-program rules

- Grade 2 autumn and spring `SALON GRUP SAATLERİ` documents are the anatomy
  group lists.
- Anatomy groups are `1`, `2`, and `3` and are independent from a student's
  normal practice group.
- Grade 1 anatomy uses the same or a very similar structural model and the same
  independent `1`/`2`/`3` grouping scheme.
- Anatomy lessons are represented as `Diseksiyon` in the annual program.
- Grade 2 `Beceri uygulama takvimi` documents are the vertical-corridor program.
- Vertical-corridor and other practice lessons are represented as `Uygulama` in
  the annual program.
- The Grade 2 anatomy and vertical-corridor sources apply to both Turkish and
  English programs.
- The special-program documents enrich or disambiguate annual-program entries;
  they must not create duplicate logical lessons when joined.

## Grade 2 annual reading rules (confirmed 2026-07-25)

Read from the committed `DÖNEM 2` and `CLASS 2` workbooks while implementing
`grade2_yearly_v1` (ADR-073).

- Both workbooks are the Grade 1 row layout. The header wording differs
  (`TARİH`/`Start Date`, `KONU`/`Subject`, `DİLİM ADI / ANABİLİM DALI`/`Description`)
  and so does the term cell: `Dönem 2` against `Time Table 2`.
- Every date is a spreadsheet serial and every time a day fraction, except one
  English start-time cell holding the whole number `9`, which the workbook itself
  renders `00:00`.
- `DİSEKSİYON (n/13)` / `DISSECTION (n/13)` appears three times per day at
  13:30-14:20, 14:30-15:20 and 15:30-16:20 with the **same** session number. The
  autumn and spring `SALON GRUP SAATLERİ` documents assign anatomy groups 1, 2 and 3
  to those three hours in rotation, so each student attends exactly one. The annual
  row states no group; 159 such rows exist in each workbook.
- The Turkish workbook writes 119 rows whose whole title is `UYGULAMA`, each with the
  location `FAKÜLTEMİZ WEB SİTESİ ÖĞRENCİ AĞI DÖNEM 2 UYGULAMA PROGRAMINA BAKINIZ` —
  the source deferring to the companion practice program.
- The English workbook writes practice group labels inside lesson titles
  (`LABORATORY SKILLS (HISTOLOGY AND EMBRYOLOGY) İ2`, `Team Work İ1` to `İ5`), which
  is a *fifth* group value beyond the `İ1`-`İ3` the Grade 1 English practice fixture
  states. Capture the current Grade 2 English practice source before declaring any of
  them as supported selectors.
- English `lunch break` rows carry an empty term and an empty date cell.

## Parser implications

- Preserve raw values, formulas, formatted values, merges, hidden dimensions,
  and relevant formatting metadata in the normalized snapshot.
- Use zero-based row and column coordinates in transport contracts and preserve
  A1 addresses only as evidence.
- Select worksheets and ranges through source configuration.
- Treat Unicode text as source data; never normalize Turkish characters by
  lossy encoding conversion.
- Do not infer the identity of unclassified DOCX fixtures from filenames alone.
