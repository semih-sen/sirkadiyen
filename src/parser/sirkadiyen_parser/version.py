"""Parser engine version.

The engine version covers the shared normalization primitives and the parse
pipeline. It is independent from the transport contract version and from the
individual parser-profile versions.

Determinism is defined as "the same parser version and the same snapshot produce
the same output". A behavioural change to the shared primitives therefore
requires bumping this value *and* bumping every parser-profile version whose
output can change, because only the profile version travels on the wire.
"""

#: 0.2.0 refuses a numeric time cell that is not a day fraction instead of
#: reducing it modulo one day, which used to publish a lesson at midnight
#: (ADR-073). No committed fixture's output moved, but the rule is a behavioural
#: change to a shared primitive, so the one affected profile is bumped with it:
#: `grade1_yearly_v1` 1.5.0 reparses its stored snapshots, which cannot be proved
#: free of such a cell the way the fixtures can. `grade1_practice_v1` reads only
#: time ranges written as text and is untouched.
PARSER_ENGINE_VERSION = "0.2.0"
