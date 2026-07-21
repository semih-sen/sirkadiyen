from sirkadiyen_parser.contracts.parsing import (
    ParserResultStatus,
    ParserWarningSeverity,
    SourceEvidence,
)
from sirkadiyen_parser.diagnostics import (
    METRIC_ROWS_IGNORED,
    WARNING_ROWS_IGNORED,
    ParseDiagnostics,
)


def evidence(cell_range: str) -> SourceEvidence:
    return SourceEvidence(
        sheet_id="sheet-1",
        sheet_title="Test Sheet",
        range=cell_range,
        raw_text=None,
        extraction_rule="testRule",
    )


def test_a_clean_run_completes() -> None:
    assert ParseDiagnostics().status() is ParserResultStatus.COMPLETED


def test_information_alone_still_completes() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.information("noteCode", "A note.")

    assert diagnostics.status() is ParserResultStatus.COMPLETED


def test_a_warning_is_never_silently_swallowed() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.warning("anomalyCode", "Something looks wrong.")

    assert diagnostics.status() is ParserResultStatus.COMPLETED_WITH_WARNINGS
    assert diagnostics.warnings[0].severity is ParserWarningSeverity.WARNING


def test_an_error_rejects_the_parse() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.warning("anomalyCode", "Something looks wrong.")
    diagnostics.error("failureCode", "Structure was not understood.")

    assert diagnostics.status() is ParserResultStatus.REJECTED


def test_warnings_keep_the_order_they_were_recorded_in() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.information("first", "1")
    diagnostics.warning("second", "2")

    assert [warning.code for warning in diagnostics.warnings] == ["first", "second"]


def test_metrics_are_ordered_by_name_so_responses_stay_comparable() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.increment("zeta.count")
    diagnostics.set_metric("alpha.duration", 1.5, unit="ms")

    assert [metric.name for metric in diagnostics.metrics] == ["alpha.duration", "zeta.count"]
    assert diagnostics.metrics[0].unit == "ms"


def test_increment_accumulates() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.increment("rows.read")
    diagnostics.increment("rows.read", 2)

    assert diagnostics.metrics[0].value == 3


def test_every_ignored_row_is_counted_by_reason() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.record_ignored_row("blankRow", evidence("A5"))
    diagnostics.record_ignored_row("blankRow", evidence("A6"))
    diagnostics.record_ignored_row("headingRow", evidence("A1"))

    metrics = {metric.name: metric.value for metric in diagnostics.metrics}

    assert metrics[METRIC_ROWS_IGNORED] == 3
    assert metrics["rows.ignored.blankRow"] == 2
    assert metrics["rows.ignored.headingRow"] == 1


def test_each_ignore_reason_is_explained_once_with_evidence() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.record_ignored_row("blankRow", evidence("A5"))
    diagnostics.record_ignored_row("blankRow", evidence("A6"))

    reported = [warning for warning in diagnostics.warnings if warning.code == WARNING_ROWS_IGNORED]

    assert len(reported) == 1
    assert reported[0].evidence is not None
    assert reported[0].evidence.range == "A5"
    assert diagnostics.status() is ParserResultStatus.COMPLETED


def test_an_anomalous_ignore_reason_reports_every_affected_row() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.record_ignored_row(
        "unresolvedDate",
        evidence("B5"),
        severity=ParserWarningSeverity.WARNING,
        message="Date cell could not be read.",
    )
    diagnostics.record_ignored_row(
        "unresolvedDate",
        evidence("B9"),
        severity=ParserWarningSeverity.WARNING,
        message="Date cell could not be read.",
    )

    reported = [warning for warning in diagnostics.warnings if warning.code == WARNING_ROWS_IGNORED]

    assert [warning.evidence.range for warning in reported if warning.evidence] == ["B5", "B9"]
    assert diagnostics.status() is ParserResultStatus.COMPLETED_WITH_WARNINGS


def test_confidence_indicators_carry_the_field_and_candidate() -> None:
    diagnostics = ParseDiagnostics()
    diagnostics.confidence(
        field_name="localDate",
        score=0.7,
        reason="yearSuppliedByProfile",
        candidate_id="candidate-1",
    )

    indicator = diagnostics.confidence_indicators[0]

    assert indicator.field == "localDate"
    assert indicator.candidate_id == "candidate-1"
    assert indicator.score == 0.7
