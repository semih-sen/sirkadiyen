# Source Inventory Notes

## Inspection scope

The 17 `.xlsx` fixtures present on 2026-07-21 were imported without modifying
the source files. CSV exports were retained as secondary inspection aids. DOCX
files were inventoried but were not treated as normalized Google Sheets
snapshots.

Since 2026-07-25 the four Grade 2 DOCX sources are converted onto the same
normalized snapshot contract as the workbooks (ADR-076); see the Grade 2 DOCX
reading rules below. The Grade 3 bedside documents are still only inventoried.

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

## Grade 2 Turkish practice reading rules (confirmed 2026-07-25)

Read from the committed `Uygulama Tablosu` workbook while implementing
`grade2_practice_v1` (ADR-074). It is **not** the Grade 1 practice layout.

- The table is transposed relative to Grade 1: a **column** is a dated slot, whose
  header holds a `1/3`-style slot label, a date and a time range on separate lines,
  and a **row** is a practice subject with its room in the second column.
- Nine curriculum blocks open with a wide merged heading (`KAN LENFOİD 1`,
  `DOLAŞIM-1`, …). Each holds one to three slot-header rows (`Uygulama adı` /
  `Uygulama yeri`, abbreviated to `Uygulama` in the `ENDOKRİN-1` block), and each is
  followed by topic lists that are not schedule data.
- Cell values are: a single cohort letter `A`-`H`; several (`F + B`, `D+H`);
  concatenated with a session number (`ABCD 1/1`, `GH 1/3`); `*`, which the source's
  own note says means the groups and rooms are announced in a separate table; `-`
  for no session; a make-up or examination marker with no group (`UYGULAMA TELAFİ`,
  `TELAFİ`, `SINAV`); and dissection date serials in the `Anatomi (n)` rows.
- Three whole-cohort sessions are written into the body of the table as `TÜM GRUPLAR`
  with their **own** date and time, merged across a run of columns. Two of them state
  a date that differs from the one their column header states, so the cell is the
  authority.
- Four slot dates state a year that is a year out (`3 Şubat 2025`, `24 Aralık 2024`,
  `26 Şubat 2025`, `27 Şubat 2025`); every one of them also contradicts the weekday
  typed beside it. One more is an unreadable month (`24 Eylü 2025`).
- One cell writes a numeric date, `TÜM GRUPLAR 8.10.2025`. It is the first numeric
  date any committed fixture states. Profile version 1.0.0 refused it under ADR-051;
  1.1.0 declares `dayFirst` and publishes it as 8 October 2025, because the Turkish
  annual program schedules the same session — `FİZYOLOJİ 1. UYGULAMASI (TÜM GRUPLAR
  Amfide yapılacak)`, 08:30-10:20 — on that day as a spreadsheet serial, and the
  month-first reading falls outside both the academic year and the `DOLAŞIM-1`
  block's own 3-16 October range (ADR-075).
- The room-and-telephone lookup tables at the end of the worksheet
  (`Dikey Koridor II Laboratuarı`) are not schedule data.

## Grade 2 DOCX reading rules (confirmed 2026-07-25)

Read from the four committed Grade 2 Word documents while implementing the DOCX
conversion (ADR-076). They are now converted to normalized snapshots, so these are
observations about the documents rather than about the conversion.

- **Anatomy (`SALON GRUP SAATLERİ`, autumn and spring).** Three columns: a date, one
  of the three dissection hours (13:30-14:20, 14:30-15:20, 15:30-16:20), and the
  anatomy group `1`, `2` or `3` attending it. Each date occupies three consecutive
  rows and the groups rotate between them, which is the direct confirmation of
  ADR-073: the annual program's three identical `DİSEKSİYON (n/13)` rows are one
  session per student, not three.
- The same document writes that structure **two different ways**. Up to autumn row 45
  the date sits in the middle row of its triple and the rows above and below are
  empty; from row 46 onward the three rows are a vertical merge. A profile must read
  both, and the empty-neighbour form cannot be told from a genuinely undated row by
  shape alone. ADR-078 resolves it by reading a day as a run of consecutive rows whose
  hours advance and that state exactly one date between them.
- Both documents use exactly three time ranges (`13:30-14:20`, `14:30-15:20`,
  `15:30-16:20`) and exactly three group values (`1`, `2`, `3`). Autumn states 30
  teaching days, spring 23.
- The spring document writes `9 Nisan 2025 Perşembe` where it means 2026, and the
  weekday contradicts the year it typed. It is the fourth Grade 2 document to carry a
  date whose year is a year out.
- Neither document's rows name a lesson: they are a date, an hour and a group. The
  title comes from the annual program's word for the same lesson, `Diseksiyon`, which
  the profile declares.
- Both anatomy documents are split into two Word tables by a page break, and the
  second table has no header row. They are two worksheets in the snapshot, because
  joining them would state a table the document does not contain.
- **Vertical corridor (`Beceri uygulama takvimi`).** A row is a dated slot whose first
  cell holds a slot label, a date and a time range on three lines — the same cell
  shape the Grade 2 practice sheet uses — and a column is one of five skill
  practices (`AYDINLATILMIŞ ONAM`, `OKSİJEN`, `HASTANE ENF. ÖNLENMESİ`, `EKİP OLMA`,
  `SH ÖYKÜ ALMA`). A cell holds the cohort letter attending. **This is the table the
  practice sheet's 95 `*` cells defer to.**
- The autumn file is one 60-row table; the spring file is the same content split
  across seven tables, and its file name says `26.01.2025`, a year before the
  academic year it belongs to.
- Most of the vertical-corridor grid is a non-breaking space rather than an empty
  cell, and its second column repeats `Web Sitesinde Yayınlanacak` — the same
  deferred room the practice sheet writes.
- Neither anatomy document states a URL anywhere, and the spring/autumn pair is
  handed out once per semester, so both are catalogued under the
  `administrativeUpload` transport and are acquired by an administrator uploading
  the file rather than by polling (ADR-079). The vertical-corridor documents are
  edited by Student Affairs during the year and keep their Drive URLs.
- **The vertical-corridor grid is mostly unassigned.** Autumn states groups on 12 of
  its 53 dated rows and spring on 30 of 52, because the faculty schedules them as the
  year goes on. A dated row with no groups is the document's normal state, not a defect.
- Its cells state: a cohort letter `A`-`H`; a subgroup `A1`-`H2` (only the `EKİP OLMA`
  column); a two-letter run `AB`, `CD`, `EF`, `GH`; an examination naming its cohorts
  with hyphens, `A-B-C-D SINAV`; the English programme's `İ1 grubu`, `i1+i2` — sometimes
  with a time range of their own that differs from the row's; `EK-1` to `EK-3`, the
  separately published lists the document's own note points at; and `UYGULAMA TELAFİ`,
  `Telafi` or a stray `T` that state no audience.
- Nine of its dated rows contradict the weekday typed beside them. Four name a year that
  is a year out (`3 Şubat 2025`, `24 Aralık 2024`, `26`/`27 Şubat 2025`) — **the same four
  the practice table gets wrong**, which is evidence the two documents are maintained
  together. The rest are ordinary weekday typos (`08`/`15 Mayıs 2026`, `1 Haziran 2026`,
  `11 Kasım 2025`, `23 Aralık 2025`).
- One spring row writes its whole slot on a single line (`20 Nisan 2026 Pazartesi
  8.30-10.20`), one writes the weekday and the time range on the same line
  (`Çarşamba 14:20-16:20`), and one spring table leaves the place header empty.
- Four of the seven spring tables write `OKSİJEN (Doç. Dr. Bengüsu MİRASOĞLU` without
  closing the bracket; the first table closes it.

## Parser implications

- Preserve raw values, formulas, formatted values, merges, hidden dimensions,
  and relevant formatting metadata in the normalized snapshot.
- Use zero-based row and column coordinates in transport contracts and preserve
  A1 addresses only as evidence.
- Select worksheets and ranges through source configuration.
- Treat Unicode text as source data; never normalize Turkish characters by
  lossy encoding conversion.
- Do not infer the identity of unclassified DOCX fixtures from filenames alone.
