# Source notes — Dönem 2 (Türkçe)

## Source family

Two Dönem 2 Turkish sources reach the parser from this folder:

- **Ders programı / uygulama tablosu** — the annual program and its practice table
  (`grade2_yearly_v1`, `grade2_practice_v1`). See the workbooks under `xlsx/`.
- **Beceri uygulama takvimi (dikey koridor)** — the skill-practice calendar the annual and
  practice profiles both defer to (`grade2_vertical_corridor_v1`). This note documents that
  source; see ADR-071/074/077/147.

## Vertical-corridor calendar — format change (2026-2027)

Through 2025-2026 this calendar was published as a **Word document**:
`Dönem 2 Beceri uygulama takvimi güz.docx` and `Dönem 2 Beceri Uyg BAHAR Takvim 26.01.2025.docx`
(also `2. SINIF SALON GRUP SAATLERİ …GÜZ/BAHAR.docx`). The committed snapshot fixtures
`g2-vertical-autumn` / `g2-vertical-spring` are those documents.

For **2026-2027** it is published as a **workbook**:
`xlsx/2026-2027 Dönem 2 Beceri Uygulama Takvimi.xlsx`
(Drive id `1-EigEZue7FVRoRxx0J_FXcZxypVbozVD`). Snapshot fixture: `real/g2-vertical.snapshot.json`.

## Parser profile

`grade2_vertical_corridor_v1` **1.3.0** (reads the workbook layout; ADR-147).

## Meaningful sheets

- `TR` — the whole-year calendar. One header row, then dated slot rows.
- `İNG` — present but **empty** (the English track is not published yet).

## Header detection

Header row is recognized by its first cell: `Uygulama yeri` (the Word document wrote
`Uygulama adı`; both are accepted). The five skill practices begin in the **next** column (B):
there is no separate room column any more. Each practice header cell holds three lines:

```
<title>
<instructor, starting with an academic title: Prof. Dr. / Doç. Dr. / Dr. Öğr. Üyesi>
<room>
```

## Date rules

Slot cell (column A) holds an optional label (`1/1`, `*`, or none), then the date, then the time
range — each on its own line; the date may itself wrap onto a weekday line. Dates are **text**
(Turkish month names, e.g. `7 Eylül 2026 Pazartesi`), not spreadsheet serials. Numeric dates
(`7.10.2026`) are read only when unambiguous — the profile declares no numeric order (ADR-051).
A run of dates repairs a mistyped year from its neighbours (ADR-139); a weekday that contradicts
its own date refuses the row (correcting either half would be a guess).

## Time rules

`08:30-10:20`, `8:30-10:20`, `10.30-12.20`, `10.30-12:20` all resolve (colon or dot separators).

## Group rules

Group cells (columns B onward) name the lettered cohorts A–H that attend each practice:
a single letter (`A`), a run (`AB` → A and B), a subgroup (`B2`), an exam with hyphens
(`A-B-C-D SINAV`), or `Telafi (Tüm Gruplar)` (a whole-class makeup). English cohorts (`İ1`) and
the separately published `EK-n` lists are counted, never published under this Turkish source.

## Merge behavior

Only one merge: a `TÜM GRUPLAR` divider merged across the whole row. A group cell is read from the
value **stored at the cell**, not merge-expanded, so the banner is not read as an audience for
every practice.

## Known anomalies (2026-2027 capture)

- `23 Eylü 2026` — month typo (missing `l`), unreadable → warning.
- `1 Aralık 2065`, `5 Şubat 2026` (means 2027) — years out; repaired from the run (ADR-139).
- `18 Aralık 20256` — five-digit year, unreadable → warning.
- `23 Mart 2027 Pazartesi`, `29 Mart 2027 Çarşamba`/`Salı`, `5 Nisan 2027 Perşembe` — weekday
  contradicts the date → refused. `29 Mart 2027 Salı` carries group H, so a real session is
  refused loudly rather than published on the wrong day.
- `TÜM GRUPLAR 7.10.2026` divider — numeric date, no audience → refused (not guessed).

## Expected ignored regions

Most dated rows state **no groups yet** — Student Affairs fills the grid in over the year. They
are counted (`rows.slot`) and raise nothing. The empty `İNG` sheet is reported once, at
information level.

## Open questions

- When the `İNG` (English) track is filled in, it will need its own catalogue entry and its
  cohorts declared under an English source context, not read under the Turkish one (ADR-048).
