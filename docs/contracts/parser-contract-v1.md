# Parser Contract v1

## Purpose

This contract separates Google Sheets acquisition from deterministic schedule
interpretation. The .NET ingestion layer produces a normalized snapshot. The
Python parser accepts that snapshot and returns canonical schedule candidates,
evidence, warnings, metrics, and confidence indicators.

The C# wire models are located under:

```text
src/Sirkadiyen.Contracts/Spreadsheets/
src/Sirkadiyen.Contracts/Parsing/
```

## Transport

The intended parser endpoint is:

```text
POST /v1/parse
Content-Type: application/json
```

Request model: `ParseSnapshotRequest`

Successful response model: `ParseSnapshotResponse`

Malformed JSON, unsupported contract versions, and parser-profile validation
errors must return a non-success HTTP response. They must not be represented as
a successful parse with an empty candidate list.

## JSON conventions

- JSON properties use camel case.
- Enums use camel-case strings.
- Contract versions are explicit and independent from parser-profile versions.
- Unknown or unsupported contract versions must be rejected.
- Timestamps representing instants use UTC offsets.
- Schedule dates and times remain local values and carry an explicit timezone ID.

## Snapshot conventions

- Worksheet, row, and column identities come from the source snapshot, not from
  parser-generated ordering.
- Row and column indexes are zero-based.
- Range end indexes are exclusive.
- `a1Address` is retained as human-readable evidence, not permanent identity.
- Cells retain user-entered values, effective typed values, formulas, formatted
  values, notes, and relevant effective formatting separately.
- Excel or Google Sheets date serials remain numeric effective values. A parser
  may interpret them only through an explicit profile rule.
- Worksheet merges, hidden dimensions, frozen panes, requested ranges, and
  acquisition diagnostics remain available to the parser.
- The cells collection may be sparse, but blank cells carrying structural
  metadata must be retained when relevant to a parser profile.
- Snapshot hashes must be deterministic and record their algorithm. Exact hash
  canonicalization will be fixed alongside the ingestion implementation.

## Parser response invariants

Every response echoes:

- contract version
- correlation ID
- source ID
- snapshot ID
- parser profile name and version

Every candidate contains source evidence, a stable identity, a content hash, and
a confidence score. A parser warning cannot be converted into silent success;
warnings and confidence indicators remain explicit response collections.

`Rejected` means the parser deliberately refused to produce publishable output.
It does not authorize publication or calendar mutation. The .NET revision
pipeline remains responsible for validation, review, and publication.

## Compatibility

Additive fields may be introduced only when both consumers tolerate them.
Breaking property, enum, coordinate, or semantic changes require a new contract
version. Parser-profile versions may change without changing the transport
version when the wire schema remains compatible.
