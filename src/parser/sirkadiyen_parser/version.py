"""Parser engine version.

The engine version covers the shared normalization primitives and the parse
pipeline. It is independent from the transport contract version and from the
individual parser-profile versions.

Determinism is defined as "the same parser version and the same snapshot produce
the same output". A behavioural change to the shared primitives therefore
requires bumping this value *and* bumping every parser-profile version whose
output can change, because only the profile version travels on the wire.
"""

#: 0.4.0 reads a date column chronologically and repairs a mistyped year from the
#: dates around it (ADR-139). Every profile that reads a date is bumped with it,
#: because a stored snapshot cannot be proved free of such a cell the way the
#: fixtures can — and six committed fixtures are not: `G1-TR-ANNUAL` dates a
#: lunch break 2027-11-30 among 2026-11-30 rows, `G1-TR-PRACTICE` dates a session
#: 2020-11-20 between two 2026-11-20 rows, and `G2-TR-PRACTICE`,
#: `G2-VERTICAL-AUTUMN`, `G2-VERTICAL-SPRING` and `G2-ANATOMY-SPRING` each carry
#: a date whose year is a year out and whose own weekday says so. The last four
#: used to be refused whole; they now publish. `grade3_faculty_locations_v1` is
#: pinned rather than bumped: its document states no date at all.
#:
#: 0.3.0 reads a one-letter cohort label written with a Turkish dotted or dotless
#: `i` as the ASCII letter `comparison_key` already folds it to, so `İ1` and `i1`
#: are one cohort rather than one cohort and one refusal (ADR-130). Longer tokens
#: are deliberately not folded, so no ordinary word became readable as a cohort
#: run. No committed fixture's output moved. Every profile that reads a group
#: expression is bumped with it — `grade1_practice_v1`, `grade2_practice_v1`,
#: `grade2_vertical_corridor_v1` and the two `grade2_anatomy_*` profiles — for
#: the reason 0.2.0 gave: a stored snapshot cannot be proved free of such a cell
#: the way the fixtures can. Only `grade1_practice_v1` has a known document whose
#: output actually moves; for the others the difference a stored cell could make
#: is which refusal reason it is counted under. `grade3_faculty_practice_v1`
#: reads cohorts with its own pattern and is untouched.
#:
#: 0.2.0 refuses a numeric time cell that is not a day fraction instead of
#: reducing it modulo one day, which used to publish a lesson at midnight
#: (ADR-073). No committed fixture's output moved, but the rule is a behavioural
#: change to a shared primitive, so the one affected profile is bumped with it:
#: `grade1_yearly_v1` 1.5.0 reparses its stored snapshots, which cannot be proved
#: free of such a cell the way the fixtures can. `grade1_practice_v1` reads only
#: time ranges written as text and is untouched.
PARSER_ENGINE_VERSION = "0.4.0"
