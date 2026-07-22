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
→ stores only changed content
→ begins or resumes the deterministic parse run
→ calls POST /v1/parse
→ persists the response
→ creates a candidate revision and canonical records in one transaction
→ validates the revision in a separate transaction
→ publishes it in a third, superseding the revision it replaces
```

Every store that opens its own transaction runs through `RetriableTransaction`.
The hosts configure the context with `EnableRetryOnFailure`, and saving inside a
hand-rolled transaction under a retrying execution strategy throws; the failure
appears only under the host configuration, never under a plain test context.

An unchanged snapshot is normally already parsed and stops early. If the prior
parser transport attempt failed, the worker parses the stored immutable
snapshot again and increments its attempt count instead of creating duplicate
snapshot or parse-run rows. Parser responses must echo every contract identifier
exactly before they are persisted.

## Validation

Validation is a separate transaction from parse persistence, so a revision that
survives parsing but not validation stays in `Parsed` and is retried by the next
pass rather than being lost. It moves a revision through
`Parsed → Validating → Validated | ReviewRequired | Rejected` and records why
(ADR-029).

Any error-severity finding holds the revision for review. Only an empty revision
is rejected, because rejection is terminal. Validation can never publish; the
store refuses the attempt.

Thresholds are configurable through `SIRKADIYEN_VALIDATION:*`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `MAXIMUM_DELETION_SHARE` | `0.20` | share of published records that may vanish |
| `MINIMUM_DELETION_COUNT` | `10` | absolute floor; both conditions must hold |
| `LOW_CONFIDENCE_THRESHOLD` | `0.50` | below this a record is held for review |
| `MINIMUM_LESSON_MINUTES` | `10` | shorter lessons are implausible |
| `MAXIMUM_LESSON_MINUTES` | `600` | longer lessons are implausible |
| `MAXIMUM_TOLERATED_OVERLAPS` | `1` | more than this quarantines the revision |
| `ACADEMIC_YEAR_GRACE_DAYS` | `30` | slack around the derived academic year |

The deletion rule needs **both** conditions, so a small source cannot trip on the
share alone:

```text
deletedCount > previouslyPublishedCount * 0.20  AND  deletedCount >= 10
```

## Publication

Publication is a third separate transaction (ADR-032). A revision that reached
`Validated` is published without anyone being asked: the safety nets that matter
already ran in validation, and holding a healthy schedule back helps nobody.

```text
Validated → Published, and the source's previous Published revision → Superseded
```

Both writes commit together. They are two `SaveChanges` calls inside one
transaction rather than one, because only one revision per source may be
`Published` and that is enforced by a partial unique index: the outgoing revision
has to vacate the slot in its own statement.

The worker publishes at the end of every polling cycle, driven by revision state
rather than by what that cycle happened to parse. A cycle killed between
validation and publication therefore resumes on the next pass.

Publication refuses in three cases, each reported rather than thrown:

| Outcome | Meaning |
| --- | --- |
| `NotValidated` | the revision is quarantined, already live, or terminal |
| `SupersededByNewerRevision` | a newer revision is already live, so this one would move the schedule backwards |
| `ConcurrentPublication` | another publication for the same source committed first |

### Approving a quarantined revision

A `ReviewRequired` revision reaches publication only through approval, which
records who decided and why in `ApprovedBy` and `ApprovalReason`. Approval moves
the revision to `Validated` and nothing further, so an approved revision goes
live through exactly the same publication transaction as one that was never held.

There is no administration frontend yet, so this runs over an internal API
guarded by `SIRKADIYEN_ADMIN__API_KEY`:

```text
GET  /api/revisions?state=ReviewRequired   the review queue
GET  /api/revisions/{id}                   one revision with the findings behind its state
POST /api/revisions/{id}/approve           { "approvedBy": ..., "approvalReason": ... }
```

The key establishes that the caller is an operator, not which operator they are,
so `approvedBy` is a recorded claim rather than a verified identity. The API
publishes the OpenAPI document at `/openapi/v1.json` for Postman; the requests
are also in `src/Sirkadiyen.Api/Sirkadiyen.Api.http`.

## Polling schedule

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
4. the semantic diff, and an administration frontend to replace the internal
   approval API;
5. real authentication in place of the shared administrative key.

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
