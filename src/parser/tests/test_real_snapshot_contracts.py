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
