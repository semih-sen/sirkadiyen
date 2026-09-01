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

`GoogleSourceCredentialFactory` builds the one unattended credential every
fetched source is read with, from either an offline OAuth refresh token or a
service-account file, scoped read-only to Sheets and Drive. Worker configuration
binds exactly one of those credential modes. Credentials remain outside source
control. See [Google source authentication](google-source-authentication.md).

## Google Drive acquisition

Sources catalogued under the `googleDriveFile` transport are downloaded over the
Drive v3 REST API by `GoogleDriveHttpClient` and converted by
`DriveDocumentAcquirer` (ADR-083). The Drive file identifier is the catalog's
`externalId`; the `sourceUri` is the link a person opens and is never parsed for
one.

Two calls per acquisition, in this order:

1. **metadata** — `GET files/{id}?fields=id,name,mimeType,size,md5Checksum,modifiedTime,trashed`,
   which decides whether the file is read at all;
2. **content** — `GET files/{id}?alt=media`, read under a bound of 8 MB that is
   applied to the declared length and again to every chunk, so a response that
   declares no length cannot make the host read without limit.

An acquisition is refused, rather than converted into a snapshot, when the file
is in the trash (its content is frozen and it is no longer published), is not the
MIME type the catalog's document format implies (a document converted into a
Google editor format cannot be downloaded at all), is larger than the bound, does
not match the length or digest Drive stated for it, or is not an Office container
— which is what a sign-in or error page served with a success status looks like.
Each refusal names what a person has to do. Everything else stays an ordinary
HTTP error for the next poll to retry.

Only DOCX is converted. The Grade 3 workbooks share the transport and are
reported as `UnsupportedDocumentFormat`: their download works, and what they lack
is a converter and a parser profile. That is a different gap from
`UnsupportedTransport`, which no catalogued source reports any more —
`SHARED-AMPHI` was the last one, and it now reads its Google Sheets workbook over
the transport that was already implemented (ADR-133).

The snapshot records only that it was downloaded from Drive. The file name, its
modification time and its digest are deliberately absent, because acquisition
diagnostics are part of the content hash: recording any of them would make a
re-saved but unedited document look like a change, and produce a revision that
changes nothing. Drive metadata is therefore not used as a change signal at all —
the converted content hash is, and it ignores edits that alter no text.

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
→ compares the record set with the source's most recent revision
→ if it says the same thing, stops here: no revision, no diff, no dispatch
→ otherwise creates a candidate revision and canonical records in one transaction
→ validates the revision in a separate transaction
→ publishes it in a third, superseding the revision it replaces
```

The comparison step is `CanonicalRecordSetHash`: one digest over every record's
stable identity and content hash, which is exactly what the semantic differ
compares. It exists because a document is re-parsed for reasons that have nothing
to do with its content — a companion document edited beside it, a re-export that
moved no lesson — and without it every one of those produced a revision, a
publication, a diff of nothing, and a calendar dispatch that wrote nothing. The
poll reports this as `ParsedUnchanged`, which is distinct from `AlreadyParsed`:
the first parsed and found nothing, the second did not parse at all.

Every store that opens its own transaction runs through `RetriableTransaction`.
The hosts configure the context with `EnableRetryOnFailure`, and saving inside a
hand-rolled transaction under a retrying execution strategy throws; the failure
appears only under the host configuration, never under a plain test context.

An unchanged snapshot is normally already parsed and stops early. If the prior
parser transport attempt failed, the worker parses the stored immutable
snapshot again and increments its attempt count instead of creating duplicate
snapshot or parse-run rows. Parser responses must echo every contract identifier
exactly before they are persisted.

### Evidence beyond the snapshot

Two inputs besides the document itself take part in a parse, and both are part of
the parse run's identity, so a change in either opens a new run rather than being
short-circuited as already parsed:

- **Companion snapshots** (ADR-102): the latest stored snapshot of every source
  named in `companionSourceIds`, handed to the parser as supporting evidence. A
  companion that was never acquired is left out rather than waited for.
- **Group-rotation coverage** (ADR-126): the local dates on which the sources
  named in `groupRotationSourceIds` have a *published* revision for this source's
  own academic year, class year and program language. The Grade 2 annual
  workbooks name the anatomy group lists there. A date the lists cover keeps
  deferring to them; a date they do not cover is published by the annual program
  in full, all three dissection hours with the hour named. Uploading a group list
  therefore reparses the annual snapshot on the next poll, and the fallback hours
  it published are retired by the ordinary semantic diff.

Both are reduced into the run's `CompanionFingerprint`. Coverage is written into
that digest only when there is coverage, so sources that read neither keep the
fingerprint they already had.

### Independent Calendar-work cadence

The adaptive source-polling interval is not the Calendar job-admission interval
(ADR-082). After each source cycle the worker retains the absolute next source
deadline. Between source deadlines it checks for newly queued initial sync,
incremental dispatch and reconciliation work every
`SIRKADIYEN_SYNC__CALENDAR_IDLE_CHECK_INTERVAL` (five seconds by default).

These short passes do not acquire sources, invoke the parser, publish revisions,
calculate diffs or prune snapshots. A quota-yielded Calendar pass uses
`SIRKADIYEN_SYNC__CALENDAR_CATCH_UP_INTERVAL`, also five seconds by default. If a
source deadline is closer than either Calendar interval, the worker shortens the
sleep and preserves that source deadline.

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

The `/admin` revision-review module uses these endpoints under the authenticated
`SuperAdmin` policy:

```text
GET  /api/revisions?state=ReviewRequired   the review queue
GET  /api/revisions/{id}                   one revision with the findings behind its state
POST /api/revisions/{id}/approve           { "approvalReason": ... }
```

`ApprovedBy` comes from the verified session email rather than a caller-supplied
field. The write requires the CSRF token described in `docs/authentication.md`.
The API publishes the OpenAPI document at `/openapi/v1.json`; requests are also
in `src/Sirkadiyen.Api/Sirkadiyen.Api.http`.

### Correcting a date the source states wrongly

A source date correction (ADR-139) is the third answer to a held revision, beside
approving it and rejecting it, and the only one available to a team that cannot
edit the faculty's document. It is source configuration rather than an edit to a
parsed record: the poller sends it with the parse request, so re-parsing applies
it again, and it is part of the parse-run key, so it takes effect on the next
ordinary poll.

```text
GET    /api/admin/sources/date-corrections               every correction, newest first
GET    /api/admin/sources/{sourceId}/date-corrections/   one source's corrections
POST   /api/admin/sources/{sourceId}/date-corrections/   { original, corrected, reason }
DELETE /api/admin/sources/{sourceId}/date-corrections/{id}
```

`POST` replaces any correction the source already has for the same `original`
date, so an operator changing their mind simply accepts again; the new decider,
time and reason are recorded and audited.

The review screen offers the decision in two places:

- **`RecordDateOutOfSequence`** — the parser lists the readings that fit the
  dates around the cell, each a button. It repairs an unambiguous mistyped year
  on its own; only the anomalies it refused are offered.
- **`RecordDateOutsideAcademicYear`** — the parser proposes nothing here, because
  the date is not out of sequence, it is simply in a year no lesson of this
  source can fall in. The distinct dates the finding names are offered instead,
  one decision per date rather than per lesson.

Both also take **a date typed from the document**, whether or not the parser
proposed anything: the parser's readings come from the neighbouring cells, the
operator's comes from the document, and where they disagree the document wins.
Every acceptance requires a reason and is audited.

Accepting settles nothing about the revision under review — it is still held, and
publishing the corrected schedule means re-polling the source and letting the new
revision supersede it (ADR-033). The **Sıradışı tarihler** tab of the revision
module lists every stored correction across sources with who decided it and why,
because the revision it was decided from is superseded within days and the
correction keeps applying long after: it is where an override is changed, and
where one the faculty has since fixed is retired.

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

## Weekly document discovery

One source's document is republished rather than edited in place. The faculty
drops a new amphitheatre workbook into a Drive folder every week, so the folder is
the address and the file is not (ADR-133).

A source declares `discoveryFolderId`. Before acquisition, the poller resolves it
to the most recently changed document of the expected MIME type in that folder and
acquires that. Nothing is written back onto the source: which file is current is a
fact about this week rather than configuration, so `externalId` stays what the
catalog was written against and is what a cycle falls back to.

Discovery never fails a cycle. A folder that cannot be listed — a revoked
permission, a moved folder — degrades the source to its catalogued document, and
the outcome and failure reason travel on the resolution rather than as an
exception. Listing is a separate port from reading a file, because it needs a
permission that fetching one file by ID does not.

Choosing by modification time cannot misplace a lesson: every room assignment is
dated from a day title row inside the document, so acquiring the wrong workbook
yields assignments for dates no current lesson falls on. The failure mode is a
missing room, never a wrong one.

### Verifying folder access

Whether the configured credential may list a discovery folder is a fact about the
deployment, not about the code, and discovery is built never to fail a cycle over
it — a folder it cannot read falls back to the catalogued document. That makes a
misconfiguration look like rooms quietly freezing rather than like an error, so it
has to be checked deliberately:

```bash
dotnet run --project tools/Sirkadiyen.SourceAccessCheck -- --repository-root . --source-id SHARED-AMPHI
```

It loads the repository `.env`, builds the credential with the production factory
and lists the folder through the same adapter the worker uses, then reports which
document the next poll would acquire. In service-account mode it prints the
service account address first, because that is what the folder has to be shared
with and it is printed even when the credential file itself turns out to be
unusable. Exit code 0 means resolved, 2 means it fell back, 1 means the folder
could not be listed at all; the failure names whether the folder is invisible to
the credential or the grant lacks `drive.readonly`.

It also acquires the resolved document, because listing a folder and opening a
document in it are separate permissions. `--write-snapshot <path>` saves that
acquisition, which is how a parser profile gets tested against the shape
production actually sends: the Sheets API reports far more cells than the local
XLSX converter emits, so a profile verified only against a converted fixture has
not been verified against a live one.

## Remaining integration work

Production ingestion still needs:

1. a workbook converter for the Drive-published Grade 3 sources, whose transport is
   implemented;
2. current-year fixtures and parser profiles for the unsupported Grade 1 and Grade 2
   English source families;
3. operator views for source status, snapshot evidence, parser warnings and held
   diff release;
4. production health checks, metrics, structured logging and alerts for acquisition
   and parsing workflows.

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
