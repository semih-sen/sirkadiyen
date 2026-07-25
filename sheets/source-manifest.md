# Source Manifest

The deployable source IDs, URLs, transport types, document formats, parser
profiles, and fixture mappings are maintained in
`config/schedule-sources.json`. This manifest remains the human-readable
inventory and records missing source families and structural observations.

Complete this file when adding real source fixtures.

## Status values

```text
Missing
Collected
Documented
ParserPlanned
ParserImplemented
Validated
```

## Inventory

| ID | Class | Language | Source type | Curriculum group | Semester | Fixture path | Parser profile | Status | Notes |
|---|---:|---|---|---|---|---|---|---|---|
| G1-TR-ANNUAL | 1 | TR | Annual | All | Annual | `donem-1-tr/xlsx/2025-2026 D1 Türkçe Tıp Ders Programı.xlsx` | grade1_yearly_v1 | Collected | Worksheet `DÖNEM 1`, used range `A1:M942` |
| G1-TR-PRACTICE | 1 | TR | Practice | Dynamic | Annual | `donem-1-tr/xlsx/2025-2026 Dönem 1 Türkçe Uygulama Tablosu (1).xlsx` | grade1_practice_v1 | Collected | Worksheet `Sayfa1`, used range `A1:N335` |
| G1-EN-ANNUAL | 1 | EN | Annual | All | Annual | `donem-1-ing/xlsx/2025-2026 Term 1 Medicine Program in English Course Program.xlsx` | grade1_yearly_v1 | Collected | Main worksheet `CLASS 1`; separate location lookup worksheet exists |
| G1-EN-PRACTICE | 1 | EN | Practice | İ1, İ2, İ3 | Annual | `donem-1-ing/xlsx/2025-2026 Term 1 Medicine Program in English-Med. Skill Practices.xlsx` | grade1_practice_v1 | Collected | Worksheet `Sayfa1`, used range `A1:O278`; selector matrix confirmed from the 2025-2026 fixture |
| G1-ANATOMY | 1 | TR+EN | Anatomy practice | 1, 2, 3 | Annual |  | grade1_anatomy_v1 | Missing | Same structural family as Grade 2 anatomy; anatomy group is independent from the normal practice group |
| G2-TR-ANNUAL | 2 | TR | Annual | All | Annual | `donem-2-tr/xlsx/2025-2026 Dönem 2 Türkçe Tıp Programı Ders Programı.xlsx` | grade2_yearly_v1 | ParserImplemented | Worksheet `DÖNEM 2`, used range `A1:G1157`; 119 bare `UYGULAMA` placeholders defer to the practice program and 159 `DİSEKSİYON` rows are the anatomy group rotation, both excluded and counted (ADR-071, ADR-073) |
| G2-TR-PRACTICE | 2 | TR | Practice | A-H | Annual | `donem-2-tr/xlsx/2025-2026 Dönem 2 Türkçe Tıp Programı Uygulama Tablosu (1).xlsx` | grade2_practice_v1 | ParserImplemented | Worksheet `Sayfa1`, used range `A1:S265`; no subgroup values in the fixture. Slot-column layout: a column is a dated slot and a row is a practice subject (ADR-074). Four slot dates state a year that is a year out and are refused |
| G2-EN-ANNUAL | 2 | EN | Annual | All | Annual | `donem-2-ing/xlsx/2025-2026 Term 2 Medicine Program in English Course Program.....xlsx` | grade2_yearly_v1 | ParserImplemented | Worksheet `CLASS 2`, used range `A1:I1253`; 159 `DISSECTION` rotation rows excluded (ADR-073); practice groups `İ1`-`İ5` are written inside titles and no audience is inferred from them |
| G2-EN-PRACTICE | 2 | EN | Practice | İ1, İ2 (provisional) | Annual | `donem-2-ing/xlsx/2024-2025 Term 2 Medicine Program in English PRACTICUM TABLE.xlsx` | grade2_practice_v1 | Collected | Fixture belongs to the prior academic year; capture the current source before adding these values to validation |
| G2-ANATOMY-AUTUMN | 2 | TR+EN | Anatomy practice | 1, 2, 3 | Autumn | `donem-2-tr/2. SINIF SALON GRUP SAATLERİ 2025-2026 GÜZ.docx` | grade2_anatomy_autumn_v1 | ParserImplemented | Shared by Turkish and English programs; lessons appear as `Diseksiyon` in the annual program. Converted to `g2-anatomy-autumn.snapshot.json` (ADR-076) and parsed by ADR-078: two tables of 49 and 42 rows, each row `date / hour / anatomy group`, 30 teaching days, 90 sessions. Catalogued as `G2-ANATOMY-AUTUMN` under the `administrativeUpload` transport (ADR-079): the document is handed out once a semester and has no published URL, so it names itself `urn:sirkadiyen:upload:G2-ANATOMY-AUTUMN` and an administrator uploads it. Declares `anatomyGroup` `1`, `2`, `3`, which is what admits Grade 2 Turkish to the supported-profile schema |
| G2-ANATOMY-SPRING | 2 | TR+EN | Anatomy practice | 1, 2, 3 | Spring | `donem-2-tr/2. SINIF SALON GRUP SAATLERİ 2025-2026  BAHAR.docx` | grade2_anatomy_spring_v1 | ParserImplemented | As the autumn document. Converted to `g2-anatomy-spring.snapshot.json` and parsed by ADR-078: two tables of 49 and 21 rows, 23 teaching days, 66 sessions; one day states a year that is a year out and is refused whole. Catalogued as `G2-ANATOMY-SPRING` on the same terms |
| G2-VERTICAL | 2 | TR+EN | Vertical corridor | Dynamic | Annual | `donem-2-tr/Dönem 2 Beceri uygulama takvimi güz.docx`<br>`donem-2-tr/Dönem 2 Beceri Uyg BAHAR Takvim 26.01.2025.docx` | grade2_vertical_corridor_v1 | ParserImplemented | Shared by Turkish and English programs; lessons appear as `Uygulama` in the annual program. Both catalogued as `G2-VERTICAL-AUTUMN` and `G2-VERTICAL-SPRING` and converted to `g2-vertical-{autumn,spring}.snapshot.json` (ADR-076). Autumn is one 60x7 table, spring is seven; a row is a dated slot and a column is a skill practice, and this is the table the Grade 2 practice sheet's `*` cells defer to. Parsed by ADR-077: 12 candidates from autumn and 30 from spring, using the same `A`-`H` practice groups and their subgroups. Student Affairs edits these documents during the year, so they must be re-acquired rather than converted once; nine dated rows contradict their own weekday and are refused |
| G3-TR-A-ANNUAL | 3 | TR | Annual | A | Annual | `donem-3-tr-A/2025-2026 Dönem 3 A Türkçe Tıp Ders Programı.xlsx` | grade3_yearly_v1 | Collected | Worksheet `A GRUBU`, used range `A1:G1283` |
| G3-TR-A-BEDSIDE | 3 | TR | Bedside practice | A | Annual | `donem-3-tr-A/2025-2026 Dönem 3 HASTA BAŞI A GRUBU UYGULAMA KONULARI VE TABLOSU.docx` | grade3_bedside_v1 | Collected | DOCX reference only; normalized Google Sheets snapshot still required |
| G3-TR-A-FACULTY | 3 | TR | Faculty practice | A | Annual | `donem-3-tr-A/Dönem 3 ÖĞRETİM ÜYESİ A GRUBU UYGULAMA TABLOSU VE KONULARI 2025-2026 (1).xlsx` | grade3_faculty_practice_v1 | Collected | Requires the separate practical-location lookup workbook in the same directory |
| G3-TR-B-ANNUAL | 3 | TR | Annual | B | Annual | `donem-3-tr-B/2025-2026 Dönem 3 B Türkçe Tıp Ders Programı.xlsx` | grade3_yearly_v1 | Collected | Worksheet `B GRUBU`, used range `A1:H1287` |
| G3-TR-B-BEDSIDE | 3 | TR | Bedside practice | B | Annual | `donem-3-tr-B/2025-2026 Dönem 3 HASTA BAŞI B GRUBU UYGULAMA KONULARI VE TABLOSU.docx` | grade3_bedside_v1 | Collected | DOCX reference only; normalized Google Sheets snapshot still required |
| G3-TR-B-FACULTY | 3 | TR | Faculty practice | B | Annual | `donem-3-tr-B/Dönem 3 ÖĞRETİM ÜYESİ B GRUBU UYGULAMA TABLOSU VE KONULARI 2025-2026 (2).xlsx` | grade3_faculty_practice_v1 | Collected | Requires the separate practical-location lookup workbook in the same directory |
| G3-EN-ANNUAL | 3 | EN | Annual | TBD | Annual |  | grade3_yearly_v1 | Missing | Confirm exact group layout |
| G3-EN-BEDSIDE | 3 | EN | Bedside practice | TBD | Annual |  | grade3_bedside_v1 | Missing | Confirm exact group layout |
| G3-EN-FACULTY | 3 | EN | Faculty practice | TBD | Annual |  | grade3_faculty_practice_v1 | Missing | Confirm exact group layout |
| SHARED-AMPHI | Shared | Dynamic | Amphitheatre assignment | Dynamic | Weekly | `amfi/` | weekly_amphitheatre_v1 | Collected | Three weekly versions; room-oriented multi-worksheet layout; dated CDN URL returned HTTP 200 to a browser-like GET on 2026-07-23 |

## Questions to answer per fixture

- Which worksheet tabs matter?
- Are dates stored as values, formulas, or display text?
- Are merged cells semantically meaningful?
- Does background color carry meaning?
- Are hidden rows or columns relevant?
- How are student groups represented?
- How are cancelled or rescheduled lessons marked?
- Can one cell contain multiple lessons?
- Is the room present in this source or an enrichment source?
- Which fields are authoritative when sources conflict?
