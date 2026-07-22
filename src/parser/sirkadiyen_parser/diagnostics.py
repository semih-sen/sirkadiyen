"""Warning, metric and confidence collection for a single parse run.

A parser run must be explainable. Every ignored row is accounted for by a
metric, every anomaly is a warning, and the result status is derived from what
was recorded rather than chosen by the profile. A warning is never converted
into a silent success.
"""

from dataclasses import dataclass, field

from sirkadiyen_parser.contracts.parsing import (
    ConfidenceIndicator,
    ParserMetric,
    ParserResultStatus,
    ParserWarning,
    ParserWarningSeverity,
    SourceEvidence,
)

#: Total rows the profile refused to turn into candidates.
METRIC_ROWS_IGNORED = "rows.ignored"

#: Prefix for the per-reason breakdown of ignored rows.
METRIC_ROWS_IGNORED_PREFIX = "rows.ignored."

#: Matrix sources carry one lesson per cell rather than per row, so ignored
#: cells are accounted for separately from ignored rows.
METRIC_CELLS_IGNORED = "cells.ignored"

METRIC_CELLS_IGNORED_PREFIX = "cells.ignored."

WARNING_ROWS_IGNORED = "rowsIgnored"

WARNING_CELLS_IGNORED = "cellsIgnored"


@dataclass(slots=True)
class ParseDiagnostics:
    """Accumulates everything a parse run must report back to .NET."""

    _warnings: list[ParserWarning] = field(default_factory=list)
    _confidence_indicators: list[ConfidenceIndicator] = field(default_factory=list)
    _counters: dict[str, float] = field(default_factory=dict)
    _units: dict[str, str] = field(default_factory=dict)
    _reported_ignore_reasons: set[str] = field(default_factory=set)

    def warn(
        self,
        *,
        severity: ParserWarningSeverity,
        code: str,
        message: str,
        candidate_id: str | None = None,
        evidence: SourceEvidence | None = None,
    ) -> None:
        """Record a warning at an explicit severity."""
        self._warnings.append(
            ParserWarning(
                severity=severity,
                code=code,
                message=message,
                candidate_id=candidate_id,
                evidence=evidence,
            )
        )

    def information(
        self,
        code: str,
        message: str,
        *,
        candidate_id: str | None = None,
        evidence: SourceEvidence | None = None,
    ) -> None:
        """Record an explanatory note that does not weaken the result."""
        self.warn(
            severity=ParserWarningSeverity.INFORMATION,
            code=code,
            message=message,
            candidate_id=candidate_id,
            evidence=evidence,
        )

    def warning(
        self,
        code: str,
        message: str,
        *,
        candidate_id: str | None = None,
        evidence: SourceEvidence | None = None,
    ) -> None:
        """Record an anomaly that must reach revision validation."""
        self.warn(
            severity=ParserWarningSeverity.WARNING,
            code=code,
            message=message,
            candidate_id=candidate_id,
            evidence=evidence,
        )

    def error(
        self,
        code: str,
        message: str,
        *,
        candidate_id: str | None = None,
        evidence: SourceEvidence | None = None,
    ) -> None:
        """Record a failure that makes the whole parse unpublishable."""
        self.warn(
            severity=ParserWarningSeverity.ERROR,
            code=code,
            message=message,
            candidate_id=candidate_id,
            evidence=evidence,
        )

    def confidence(
        self,
        *,
        field_name: str,
        score: float,
        reason: str,
        candidate_id: str | None = None,
    ) -> None:
        """Record how well one field of one candidate was understood."""
        self._confidence_indicators.append(
            ConfidenceIndicator(
                field=field_name,
                score=score,
                reason=reason,
                candidate_id=candidate_id,
            )
        )

    def increment(self, name: str, amount: float = 1.0, *, unit: str | None = None) -> None:
        """Add to a counter metric, creating it when first seen."""
        self._counters[name] = self._counters.get(name, 0.0) + amount
        if unit is not None:
            self._units[name] = unit

    def set_metric(self, name: str, value: float, *, unit: str | None = None) -> None:
        """Set a metric to an absolute value."""
        self._counters[name] = value
        if unit is not None:
            self._units[name] = unit

    def record_ignored_row(
        self,
        reason: str,
        evidence: SourceEvidence,
        *,
        severity: ParserWarningSeverity = ParserWarningSeverity.INFORMATION,
        message: str | None = None,
    ) -> None:
        """Account for a source row that produced no candidate.

        Rows are never dropped silently. Every ignored row increments both the
        total and its per-reason counter.

        The severity separates two different kinds of ignored row. A structural
        rule such as "this row belongs to another class year" is expected, so
        only the first occurrence records an informational note and the metric
        carries the total. An anomaly such as an unreadable date is not
        expected, so *every* occurrence records its own warning with its own
        evidence: a reviewer has to be able to open each affected row.
        """
        self._record_ignored(
            total_metric=METRIC_ROWS_IGNORED,
            reason_prefix=METRIC_ROWS_IGNORED_PREFIX,
            code=WARNING_ROWS_IGNORED,
            unit="Rows",
            reason=reason,
            evidence=evidence,
            severity=severity,
            message=message,
        )

    def record_ignored_cell(
        self,
        reason: str,
        evidence: SourceEvidence,
        *,
        severity: ParserWarningSeverity = ParserWarningSeverity.INFORMATION,
        message: str | None = None,
    ) -> None:
        """Account for a matrix cell that produced no candidate.

        A rotation matrix states one lesson per cell, so the cell is the unit
        that must be accounted for. The severity rule matches
        :meth:`record_ignored_row`.
        """
        self._record_ignored(
            total_metric=METRIC_CELLS_IGNORED,
            reason_prefix=METRIC_CELLS_IGNORED_PREFIX,
            code=WARNING_CELLS_IGNORED,
            unit="Cells",
            reason=reason,
            evidence=evidence,
            severity=severity,
            message=message,
        )

    def _record_ignored(
        self,
        *,
        total_metric: str,
        reason_prefix: str,
        code: str,
        unit: str,
        reason: str,
        evidence: SourceEvidence,
        severity: ParserWarningSeverity,
        message: str | None,
    ) -> None:
        self.increment(total_metric)
        self.increment(f"{reason_prefix}{reason}")

        detail = message or (
            f"{unit} were ignored because of rule '{reason}'. "
            f"See metric '{reason_prefix}{reason}' for the total."
        )

        if severity is not ParserWarningSeverity.INFORMATION:
            self.warn(severity=severity, code=code, message=detail, evidence=evidence)
            return

        reported_key = f"{code}:{reason}"
        if reported_key not in self._reported_ignore_reasons:
            self._reported_ignore_reasons.add(reported_key)
            self.information(code, detail, evidence=evidence)

    @property
    def warnings(self) -> tuple[ParserWarning, ...]:
        """Warnings in the order they were recorded."""
        return tuple(self._warnings)

    @property
    def confidence_indicators(self) -> tuple[ConfidenceIndicator, ...]:
        """Confidence indicators in the order they were recorded."""
        return tuple(self._confidence_indicators)

    @property
    def metrics(self) -> tuple[ParserMetric, ...]:
        """Metrics ordered by name so responses stay byte-comparable."""
        return tuple(
            ParserMetric(name=name, value=self._counters[name], unit=self._units.get(name))
            for name in sorted(self._counters)
        )

    @property
    def has_error(self) -> bool:
        """Whether any recorded warning was an error."""
        return any(warning.severity is ParserWarningSeverity.ERROR for warning in self._warnings)

    @property
    def has_warning(self) -> bool:
        """Whether any recorded warning was a warning or worse."""
        return any(
            warning.severity in (ParserWarningSeverity.WARNING, ParserWarningSeverity.ERROR)
            for warning in self._warnings
        )

    def status(self) -> ParserResultStatus:
        """Derive the parser result status from what was recorded.

        An error rejects the parse. A warning downgrades it to
        ``completedWithWarnings`` so the revision pipeline can decide whether
        review is required. Informational notes alone leave it complete.
        """
        if self.has_error:
            return ParserResultStatus.REJECTED
        if self.has_warning:
            return ParserResultStatus.COMPLETED_WITH_WARNINGS
        return ParserResultStatus.COMPLETED
