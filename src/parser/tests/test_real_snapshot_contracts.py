import json
from pathlib import Path

import pytest

from sirkadiyen_parser.contracts.snapshot import NormalizedSpreadsheetSnapshot

FIXTURE_ROOT = Path(__file__).parent / "fixtures" / "real"


@pytest.mark.parametrize(
    ("filename", "source_id", "title", "rows", "columns", "worksheets"),
    (
        ("g1-tr-annual.snapshot.json", "G1-TR-ANNUAL", "DÖNEM 1", 942, 13, 1),
        ("g1-tr-practice.snapshot.json", "G1-TR-PRACTICE", "Sayfa1", 335, 14, 1),
        ("g1-en-annual.snapshot.json", "G1-EN-ANNUAL", "CLASS 1", 985, 26, 2),
        ("g3-tr-a-annual.snapshot.json", "G3-TR-A-ANNUAL", "A GRUBU", 1259, 7, 1),
        ("g3-tr-b-annual.snapshot.json", "G3-TR-B-ANNUAL", "B GRUBU", 1244, 7, 1),
        ("g3-en-annual.snapshot.json", "G3-EN-ANNUAL", "İNG", 1247, 7, 1),
        # The faculty workbooks name their sheet differently, which is why no
        # profile may find a block by worksheet title.
        ("g3-tr-a-faculty.snapshot.json", "G3-TR-A-FACULTY", "Sayfa1", 382, 9, 1),
        ("g3-tr-b-faculty.snapshot.json", "G3-TR-B-FACULTY", "Sayfa2", 397, 9, 1),
        ("g3-faculty-locations.snapshot.json", "G3-FACULTY-LOCATIONS", "Sayfa1", 59, 2, 1),
    ),
)
def test_real_xlsx_snapshot_matches_inbound_contract(
    filename: str,
    source_id: str,
    title: str,
    rows: int,
    columns: int,
    worksheets: int,
) -> None:
    payload = json.loads((FIXTURE_ROOT / filename).read_text(encoding="utf-8"))

    snapshot = NormalizedSpreadsheetSnapshot.model_validate(payload)

    assert snapshot.source_id == source_id
    assert len(snapshot.worksheets) == worksheets
    worksheet = snapshot.worksheets[0]
    assert worksheet.title == title
    assert worksheet.row_count == rows
    assert worksheet.column_count == columns
    assert all(diagnostic.severity != "error" for diagnostic in snapshot.diagnostics)


@pytest.mark.parametrize(
    ("filename", "source_id", "title", "rows", "columns", "worksheets"),
    (
        ("g2-anatomy-autumn.snapshot.json", "G2-ANATOMY-AUTUMN", "Table 1", 49, 3, 2),
        ("g2-anatomy-spring.snapshot.json", "G2-ANATOMY-SPRING", "Table 1", 49, 3, 2),
        ("g2-vertical-autumn.snapshot.json", "G2-VERTICAL-AUTUMN", "Table 1", 60, 7, 1),
        ("g2-vertical-spring.snapshot.json", "G2-VERTICAL-SPRING", "Table 1", 10, 7, 7),
        # The bedside documents lead with their topic catalogue, so worksheet zero
        # is prose rather than a table, and the A document wraps four of its topics
        # in one-cell Word tables that become worksheets of their own.
        ("g3-tr-a-bedside.snapshot.json", "G3-TR-A-BEDSIDE", "Text 1", 274, 1, 9),
        ("g3-tr-b-bedside.snapshot.json", "G3-TR-B-BEDSIDE", "Text 1", 313, 1, 2),
    ),
)
def test_real_docx_snapshot_matches_inbound_contract(
    filename: str,
    source_id: str,
    title: str,
    rows: int,
    columns: int,
    worksheets: int,
) -> None:
    """A Word document arrives on the same contract as a workbook (ADR-076)."""
    payload = json.loads((FIXTURE_ROOT / filename).read_text(encoding="utf-8"))

    snapshot = NormalizedSpreadsheetSnapshot.model_validate(payload)

    assert snapshot.source_id == source_id
    assert len(snapshot.worksheets) == worksheets
    worksheet = snapshot.worksheets[0]
    assert worksheet.title == title
    assert worksheet.row_count == rows
    assert worksheet.column_count == columns
    assert all(diagnostic.severity != "error" for diagnostic in snapshot.diagnostics)


@pytest.mark.parametrize(
    "filename",
    (
        "g2-anatomy-autumn.snapshot.json",
        "g2-vertical-autumn.snapshot.json",
    ),
)
def test_a_converted_word_document_states_only_text(filename: str) -> None:
    """Every value from a Word document is text, and none declares a format.

    A profile reading one of these sources therefore resolves dates and times
    from text alone: there is no serial to fall back on and no number format to
    corroborate a reading. That is a property of the source, so it is pinned
    here rather than discovered by a profile that assumed otherwise.
    """
    payload = json.loads((FIXTURE_ROOT / filename).read_text(encoding="utf-8"))

    snapshot = NormalizedSpreadsheetSnapshot.model_validate(payload)

    cells = [cell for worksheet in snapshot.worksheets for cell in worksheet.cells]
    assert cells
    assert all(cell.effective_value is not None for cell in cells)
    assert all(cell.effective_value.kind == "text" for cell in cells if cell.effective_value)
    assert all(cell.effective_format is None for cell in cells)
    assert all(cell.formula is None for cell in cells)
