"""Shared, deterministic normalization primitives.

These primitives are profile-independent. They normalize text, expand the grid,
and resolve raw cell content into typed values. They never guess: an
unresolvable value is returned as unresolved with an explicit reason so the
calling parser profile can record a warning instead of silently dropping data.
"""
