# Schedule ingestion

## Implemented acquisition boundary

`ISpreadsheetSnapshotAcquirer` is the application-layer port for acquiring one
immutable normalized spreadsheet snapshot. Its request receives the source ID,
snapshot ID, acquisition time, spreadsheet ID, and optional A1 ranges from the
orchestrating workflow. Passing identity and time into the port keeps conversion
deterministic and avoids generating domain metadata inside infrastructure code.

`GoogleSheetsSnapshotAcquirer` is the production Google Sheets v4 adapter. It:

- requests grid data with cancellation support;
- normalizes and sorts requested ranges;
- delegates the API response to `GoogleSheetsSnapshotMapper`;
- expects an authenticated `SheetsService` to be supplied by composition.

The authentication composition is intentionally not fixed in this stage. When
it is added, use the least-privilege
`https://www.googleapis.com/auth/spreadsheets.readonly` scope and keep all
credentials outside source control.

## Preserved evidence

The mapper preserves:

- user-entered and effective typed values;
- formulas and formatted values;
- notes;
- effective number, alignment, font, and color formatting;
- zero-based grid coordinates and A1 evidence addresses;
- merge ranges;
- hidden rows and columns, including filter-hidden rows;
- worksheet dimensions, order, visibility, and frozen panes;
- the requested range scope attributable to each worksheet.

Sparse cells are emitted only when the API returned value, formatting, note, or
other cell evidence. Repeated cells from overlapping ranges are deduplicated. If
overlapping ranges disagree, the disputed cell is omitted and an error
diagnostic is emitted so arbitrary response order cannot choose a value and the
acquisition cannot look silently healthy.

## Content hashing

Snapshots use a lowercase `sha256:` content hash. The hash covers the normalized
contract version, ordered worksheets, and acquisition diagnostics. It excludes
source ID, snapshot ID, and acquisition time, so acquiring identical source
content twice yields the same hash. Range scope and structural or diagnostic
changes intentionally change the hash.

This hash enables the future unchanged-source short circuit. The raw normalized
snapshot must still be persisted immutably whenever the hash is new.

## Remaining integration work

Before real snapshots can be captured, the worker still needs:

1. source configuration containing spreadsheet IDs and optional ranges;
2. an authenticated `SheetsService` composition using read-only access;
3. immutable snapshot persistence;
4. polling and unchanged-source orchestration.
