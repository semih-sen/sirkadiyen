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

## Administrative acquisition

Some documents are handed out rather than published. They are catalogued under
the `administrativeUpload` transport, name themselves
`urn:sirkadiyen:upload:{sourceId}` because they have no location (ADR-079), and
are acquired by a SuperAdmin uploading the file:

```text
GET  /api/sources/uploadable
POST /api/sources/{sourceId}/document   multipart form field "file"
GET  /api/sources/{sourceId}/document/uploads
```

The endpoint converts and stores; it does not parse. The worker then finds the
stored snapshot on its next cycle and runs the same parse, validation and
publication path as a polled source, so an uploaded document is subject to every
rule a fetched one is. Re-entering that path each cycle is safe because a parse
run is keyed by snapshot, profile and profile version.

**One upload can serve several sources.** Sources whose document is literally the
same file declare a shared `sharedDocumentGroup`, and an upload to any member
becomes a separate immutable snapshot for every member (ADR-080). The Grade 2
anatomy group list is the case: one document, one upload, and a Turkish and an
English revision, because a canonical record reaches a student only when its
program language matches theirs.

Each upload appends a `source_document_uploads` row per target recording who
uploaded, the file name as submitted, the byte count, the SHA-256 of the bytes,
and whether the content was new. A row is written even when the content matched
what the source already held, because that is what explains why no revision
followed.

The pipeline freeze applies: a frozen pipeline accepts no upload, since an upload
is an acquisition (ADR-034).

**The administrator uploads from `/admin`** rather than over the raw API (ADR-081).
`GET /api/sources/uploadable` projects the sources whose transport is
`administrativeUpload`, so the UI asks the server-owned catalog which sources accept
a document instead of restating a list that changes at academic-year rollover. It
also carries each source's `sharedDocumentGroup` and expected document format. The
panel groups by that shared group, so **one handed-out document is one choice**
naming every program it covers, and it merges every member's audit trail so an
interrupted fan-out is visible. The upload response reports one outcome per target,
and the panel says the document was stored as evidence — not that a schedule was
published, which is still the worker's next cycle and the review thresholds'
decision.

A browser upload carries the session cookie and the antiforgery token from
`GET /api/auth/csrf`. Note that an antiforgery request token is bound to the
claims-based user it was issued to, so a token minted before sign-in is refused
afterwards, and this endpoint validates it while binding `IFormFile` — which throws
rather than returning a problem a client can retry. The frontend therefore discards
its cached token on sign-in and takes a fresh one for every multipart request
(ADR-081 amendment).

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

There is no administration frontend yet, so this runs over the API under the
authenticated `SuperAdmin` policy:

```text
GET  /api/revisions?state=ReviewRequired   the review queue
GET  /api/revisions/{id}                   one revision with the findings behind its state
POST /api/revisions/{id}/approve           { "approvalReason": ... }
```

`ApprovedBy` comes from the verified session email rather than a caller-supplied
field. The write requires the CSRF token described in `docs/authentication.md`.
The API publishes the OpenAPI document at `/openapi/v1.json`; requests are also
in `src/Sirkadiyen.Api/Sirkadiyen.Api.http`.

## Semantic diff

Publishing makes a revision live; the diff records what publishing it actually
changed, and it is the only authority a later calendar deletion may come from.

It is a fourth separate transaction, after publication (ADR-039). The two are
deliberately not merged: a revision is live the moment publication commits, and
a diff that fails to calculate must not be able to take that back. Like
publication it is driven by revision state — a revision in `Published` or
`Superseded` with no diff row — so a worker killed between the steps recovers on
its next cycle, and a revision superseded before it was diffed is still diffed
rather than losing everything it changed.

```text
published revision without a diff
→ load it and the revision it superseded
→ diff (exact stable identity, then ADR-035 secondary matching)
→ Ready, or Held
→ store once
```

Exactly one diff exists per revision, enforced by a unique index on the current
revision. A retried or racing calculation reports `AlreadyCalculated` and the
existing diff, rather than writing a second set of future calendar operations.

### The dispatch gate

A stored diff is `Ready` or `Held`, and a `Held` diff yields no calendar
operation at all (ADR-040). It is held when:

- **any entry is `Ambiguous`.** Acting on the rest of the diff while ignoring an
  ambiguous pair would delete the previous record of that pair from a student's
  calendar.
- **deletions are both numerous and disproportionate**, over
  `SIRKADIYEN_DIFF__*`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `MAXIMUM_DELETION_SHARE` | `0.20` | share of the previous revision that may vanish |
| `MINIMUM_DELETION_COUNT` | `10` | absolute floor; both conditions must hold |

This does not replace the validation rule of the same name. Validation compares
stable-identity sets before publication and cannot know that a rescheduled
lesson will be recovered by secondary matching, or that a candidate set will
stay ambiguous. The diff gate runs on the semantic result, which is the number
that actually decides how many events would be deleted.

The reason a diff was held is stored in full on the diff, written invariantly so
it reads the same on a Turkish host.

### Releasing a held diff

A hold is not a dead end. An operator who has read the source can take
responsibility for it (ADR-042), which records who did and why and moves the
diff to `Released`. A released diff is dispatchable and still carries the reason
it was held, so it reads as "held for this, then released by that person".

```text
GET  /api/diffs?state=Held                  the hold queue
GET  /api/diffs/{id}?entryLimit=100         the changes behind the hold, deletions first
POST /api/diffs/{id}/release                { "releaseReason": ... }
```

The detail view names the lessons rather than record identifiers, and excludes
unchanged entries: they are the overwhelming majority and say nothing about
whether the hold is legitimate.

**An ambiguity hold cannot be released.** An operator can confirm that a large
deletion is real by reading the source, but cannot decide which of several
candidates a record became; releasing it would leave the previous lesson in
every affected calendar and never write its replacement. That is corrected at
the source, and the next revision produces the next diff. The endpoint refuses
with `409`.

Release is guarded by the diff's row version, so two operators acting at once
get a `409` rather than silently overwriting each other. Like approval, its actor
comes from the verified SuperAdmin session.

The ADR-035 matching thresholds are deliberately not configurable. They are a
matching rule with a decision record behind them, and loosening them from an
environment variable would let two different lessons become one update with no
trace of who decided that.

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

1. Google Drive and HTTP acquisition adapters plus DOCX conversion;
2. single-use licensing and student profiles;
3. Google Calendar authorization;
4. affected-user resolution and the Google Calendar adapter, which consume
   `Ready` and `Released` diffs;
5. authenticated freeze/unfreeze mutation and an administration frontend.

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
