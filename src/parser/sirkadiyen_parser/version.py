"""Parser engine version.

The engine version covers the shared normalization primitives and the parse
pipeline. It is independent from the transport contract version and from the
individual parser-profile versions.

Determinism is defined as "the same parser version and the same snapshot produce
the same output". A behavioural change to the shared primitives therefore
requires bumping this value *and* bumping every parser-profile version whose
output can change, because only the profile version travels on the wire.
"""

PARSER_ENGINE_VERSION = "0.1.0"
