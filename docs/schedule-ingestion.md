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

`GoogleSheetsServiceFactory` supports either an offline OAuth refresh token or a
service-account credential and always uses the least-privilege
`https://www.googleapis.com/auth/spreadsheets.readonly` scope. Worker
configuration binds exactly one of those credential modes. Credentials remain
outside source control.

## Polling and parser orchestration

The worker now loads and seeds the versioned source catalog, lists
polling-enabled sources, and processes them sequentially so one cycle never
overlaps the next. For each supported Google Sheets source it:

```text
acquires normalized snapshot
â†’ stores only changed content
â†’ begins or resumes the deterministic parse run
â†’ calls POST /v1/parse
â†’ persists the response
â†’ creates a candidate revision and canonical records in one transaction
```

An unchanged snapshot is normally already parsed and stops early. If the prior
parser transport attempt failed, the worker parses the stored immutable
snapshot again and increments its attempt count instead of creating duplicate
snapshot or parse-run rows. Parser responses must echo every contract identifier
exactly before they are persisted.

Polling delay is selected in `Europe/Istanbul`: 15 minutes during weekday
daytime, 25 minutes in late afternoon, 45 minutes at night, and 60 minutes on
weekends. Window boundaries and intervals are configurable through the
`SIRKADIYEN_POLLING__*` variables documented in `.env.example`.

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

This hash drives the implemented unchanged-source short circuit. The raw normalized
snapshot must still be persisted immutably whenever the hash is new.

## Remaining integration work

Production ingestion still needs:

1. an unattended source credential in each deployed environment;
2. Google Drive and HTTP acquisition adapters plus DOCX conversion;
3. recovery of parse runs left `Running` by abrupt process termination;
4. validation, manual review, publication, and semantic diff after candidate
   revision creation.

## Local XLSX fixture conversion

`LocalXlsxSnapshotConverter` and `tools/Sirkadiyen.SnapshotTool` are development
tools for turning collected `.xlsx` fixtures into the same normalized contract.
They preserve typed and formatted values, formulas, legacy comments, number
formats, merges, hidden dimensions, and frozen panes.

The converter calculates a semantic used range from cell content, comments, and
merge ranges. This deliberately drops formatting-only worksheet tails while
retaining formatting evidence inside the meaningful boundary. It emits the
`snapshot.local_xlsx_fixture` diagnostic so local fixtures cannot be mistaken
for live API acquisitions. Workbooks using the Excel 1904 date system produce
an error diagnostic until that date basis is explicitly supported.
