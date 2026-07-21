# Synthetic parser fixtures

These snapshots are **synthetic**. They were written by hand to exercise the
shared normalization primitives and are not captures of any faculty schedule.

They exist because the .NET ingestion layer does not yet produce normalized
snapshots, so no real snapshot JSON is available to test against. Real fixtures
belong under `sheets/` and are captured, never authored.

`annual-normalization-sample.json` deliberately contains:

- a date merged across three lesson rows, so merge expansion is exercised
- a serial date carrying a `DATE` number format, which resolves by default
- a bare serial without a number format, which stays unresolved until a parser
  profile opts in
- a Turkish text date with a weekday that agrees with the date
- time ranges using `:`, `.` and an en dash separator
- a compact `0900-1050` range, which stays unresolved until a profile opts in
- an all-groups phrase, a group range, a group list, a single group and a word
  that is not a group at all
- multi-line cells mixing a course title with an instructor
- a hidden row, which is captured rather than dropped

Do not edit a case merely to make a parser pass. Add a new case instead.
