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

WARNING_ROWS_IGNORED = "rowsIgnored"


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

    def record_ignored_row(self, reason: str, evidence: SourceEvidence) -> None:
        """Account for a source row that produced no candidate.

        Rows are never dropped silently. Every ignored row increments both the
        total and its per-reason counter, and the first occurrence of each
        reason also records an informational warning carrying evidence, so a
        reviewer can open the row that triggered the rule.
        """
        self.increment(METRIC_ROWS_IGNORED)
        self.increment(f"{METRIC_ROWS_IGNORED_PREFIX}{reason}")

        if reason not in self._reported_ignore_reasons:
            self._reported_ignore_reasons.add(reason)
            self.information(
                WARNING_ROWS_IGNORED,
                f"Rows were ignored because of rule '{reason}'. "
                f"See metric '{METRIC_ROWS_IGNORED_PREFIX}{reason}' for the total.",
                evidence=evidence,
            )

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
