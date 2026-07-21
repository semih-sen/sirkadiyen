"""Golden-file comparison harness.

A golden file records what a parser run produced for a fixture. It is the
regression net for deterministic parsing: if a change to a shared primitive
alters any candidate, warning or metric, the comparison fails and the diff shows
exactly what moved.

Regenerate deliberately, never to make a failing test pass:

```powershell
$env:SIRKADIYEN_UPDATE_GOLDEN = "1"; .venv\\Scripts\\python -m pytest
```

Then read the diff before committing it.
"""

import json
import os
from collections.abc import Callable
from pathlib import Path
from typing import Any

from sirkadiyen_parser.version import PARSER_ENGINE_VERSION

TESTS_ROOT = Path(__file__).resolve().parents[1]
FIXTURES_ROOT = TESTS_ROOT / "fixtures"
GOLDEN_ROOT = TESTS_ROOT / "golden"

UPDATE_ENVIRONMENT_VARIABLE = "SIRKADIYEN_UPDATE_GOLDEN"


def load_fixture_json(relative_path: str) -> Any:
    """Load a fixture document from ``tests/fixtures``."""
    path = FIXTURES_ROOT / relative_path
    if not path.exists():
        raise FileNotFoundError(f"Fixture '{relative_path}' does not exist at {path}.")
    return json.loads(path.read_text(encoding="utf-8"))


def build_golden_document(
    *,
    fixture: str,
    subject: str,
    payload: Any,
) -> dict[str, Any]:
    """Wrap a payload with the provenance a golden file must record.

    The engine version is part of the document because the shared primitives can
    change output without any parser-profile version changing.
    """
    return {
        "fixture": fixture,
        "subject": subject,
        "parserEngineVersion": PARSER_ENGINE_VERSION,
        "payload": payload,
    }


def assert_matches_golden(relative_path: str, document: Any) -> None:
    """Compare a document against its golden file.

    Keys are sorted and Unicode is preserved, so golden files stay reviewable
    and diffs stay minimal.
    """
    serialized = json.dumps(document, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    path = GOLDEN_ROOT / relative_path

    if os.environ.get(UPDATE_ENVIRONMENT_VARIABLE) == "1":
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(serialized, encoding="utf-8")
        return

    if not path.exists():
        raise AssertionError(
            f"Golden file '{relative_path}' is missing. Generate it with "
            f"{UPDATE_ENVIRONMENT_VARIABLE}=1 and review the result before committing."
        )

    expected = path.read_text(encoding="utf-8")
    if serialized != expected:
        raise AssertionError(
            f"Output does not match golden file '{relative_path}'.\n"
            f"--- expected ---\n{expected}\n--- actual ---\n{serialized}"
        )


def assert_deterministic(produce: Callable[[], Any]) -> Any:
    """Run a producer twice and require identical serialized output.

    Determinism is a contract requirement, not an implementation detail, so it
    is asserted directly rather than assumed.
    """
    first = json.dumps(produce(), ensure_ascii=False, sort_keys=True)
    second = json.dumps(produce(), ensure_ascii=False, sort_keys=True)
    if first != second:
        raise AssertionError(
            "Producer is not deterministic across runs.\n"
            f"--- first ---\n{first}\n--- second ---\n{second}"
        )
    return json.loads(first)
