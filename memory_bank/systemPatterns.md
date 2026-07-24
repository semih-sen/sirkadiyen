# System Patterns

## 1. Overall architecture

Sirkadiyen uses a modular monolith for the primary .NET system, plus an isolated Python parsing service.

```text
Frontend
   │
   ▼
ASP.NET Core API
   │
   ├── Application layer
   ├── Domain layer
   ├── Infrastructure layer
   └── PostgreSQL
          │
          ▼
      Background jobs
          │
          ├── Google Sheets / Drive / HTTPS files
          ├── Python Parser
          └── Google Calendar
```

The architecture should remain deployable as a small number of containers while preserving clean module boundaries.

## 2. .NET project boundaries

Recommended projects:

```text
Sirkadiyen.Api
Sirkadiyen.Application
Sirkadiyen.Domain
Sirkadiyen.Infrastructure
Sirkadiyen.Worker
Sirkadiyen.Contracts
```

### Domain

Contains:

- entities
- value objects
- domain services
- domain rules
- domain events
- repository abstractions only where justified

Must not reference:

- ASP.NET Core
- Entity Framework Core
- Google SDKs
- Redis
- Hangfire
- HTTP clients

### Application

Contains:

- use cases
- commands and queries
- validation
- authorization policies
- orchestration
- interfaces for infrastructure services
- transaction boundaries

### Infrastructure

Contains:

- EF Core
- PostgreSQL implementation
- Google API adapters
- token encryption
- Redis
- Hangfire persistence
- parser HTTP client
- system clock
- external service implementations

### API

Contains:

- HTTP endpoints
- authentication middleware
- request mapping
- response mapping
- API-level exception translation
- OpenAPI configuration

Controllers or endpoints must be thin.

### Worker

Contains:

- scheduled jobs
- queue consumers
- retry orchestration
- source polling
- parsing pipeline execution
- calendar sync execution
- reconciliation jobs

## 3. Main modules

Suggested logical modules:

```text
Identity
Licensing
StudentProfiles
GoogleConnections
ScheduleSources
ScheduleIngestion
ScheduleParsing
SchedulePublication
ScheduleDiffing
CalendarSynchronization
Administration
Observability
```

Cross-module access should use application interfaces or explicit contracts, not arbitrary database access.

## 4. Source ingestion pattern

```text
Poll source
→ fetch values and structural metadata
→ normalize transport representation
→ calculate snapshot hash
→ if unchanged, stop
→ persist immutable snapshot
→ enqueue parse job
```

A source poll result must distinguish:

- unchanged
- changed
- unavailable
- unauthorized
- malformed
- rate-limited

Source transport and document format are separate concerns. A source catalog
selects a transport adapter (`GoogleSheets`, `GoogleDriveFile`, or `HttpFile`),
then a format converter (`GoogleSheetsGrid`, `Xlsx`, or `Docx`) produces the
versioned normalized snapshot. Parser profiles depend only on the snapshot
contract, never on the acquisition transport.

## 5. Snapshot pattern

A source snapshot is immutable and includes:

- source ID
- acquisition timestamp
- external document ID and source URI
- sheet identifiers
- requested ranges
- raw values
- merge metadata
- relevant formatting metadata
- normalized transport payload
- snapshot hash
- acquisition diagnostics

The snapshot is evidence. Never rewrite it after parsing.

The normalized payload is retained online for the source's current academic-year
anchor, latest content, last ten days of changed snapshots, and any input still
needed by parser recovery (ADR-044). After that window, maintenance may prune
only the large payload; immutable identity, hashes, counts, timestamps, parse
responses, revisions and diffs remain. A payload is never replaced with
different content.

## 6. Parser profile pattern

Parser behavior must be selected through a named, versioned parser profile.

Examples:

```text
grade1_yearly_v1
grade1_practice_v1
grade2_anatomy_fall_v1
grade2_vertical_corridor_v1
grade3_bedside_v1
grade3_faculty_practice_v1
weekly_amphitheatre_v1
```

A parser profile may contain:

- structural detector
- header aliases
- date propagation rules
- time-slot rules
- group syntax rules
- ignored region rules
- enrichment behavior
- validation thresholds

Do not branch primarily on spreadsheet ID inside general parser code. Spreadsheet IDs belong in configuration; parser profiles describe structure.

A profile describes structure only. Academic year, class year, program language
and interpretation timezone arrive with the parse request as source context
(ADR-017), so one profile serves several sources and never infers what the
workbook does not state.

A profile also declares how its source family writes an ambiguous value, not only
which values it refuses. `numeric_date_order` is required and has no default
(ADR-051): an undeclared profile publishes `12/11/2026` only if both readings
agree, and a declared one refuses a cell only the other order could explain.
Ambiguity is treated like absence — the profile states the rule or the parser
publishes nothing.

## 7. Canonical schedule pattern

The canonical model isolates business logic from source layout.

Core concepts:

```text
CanonicalScheduleRecord
ScheduleAudience
ScheduleLocation
ScheduleInstructor
SourceEvidence
CurriculumBlock
StableIdentity
ContentHash
ScheduleRevision
```

The model should support field-level provenance where practical.

A schedule item is either timed or all-day (ADR-046). `IsAllDay` and the two
nullable times are one invariant, asserted in the parser contract, the domain
constructor and a database check constraint: both times or neither, never one.
An all-day record covers exactly one local date, because the sources write a
closure as one row per closed day; the exclusive end date Google Calendar wants
is the calendar adapter's conversion. Holidays and semester breaks never receive
invented times, and a dated row with no times that names no closure is not
published at all.

`CurriculumBlock` is nullable and populated only when the source states a
block; it is content, not stable identity or audience (ADR-047).

`Departments` is a required list, empty when the source names none, holding every
academic department the source explicitly marked, in source order (ADR-049). An
integrated session names several and all of them are kept, because a student has
to see who teaches the session. A list is not a comparable value, so matching uses
this field only when both records name exactly one; the domain exposes that as
`ComparableDepartment`. Departments are content, not identity.

## 8. Stable identity pattern

A stable identity identifies the logical lesson across revisions.

It must not depend solely on:

- spreadsheet row number
- cell address
- parser output index
- Google event ID

Identity generation should use normalized academic and lesson attributes.

The annual profiles hash academic year, class year, program language, local
date, local start time, and normalized course identity, in that order (ADR-018).
Instructor, location, curriculum block, end time and title formatting are
content, not identity.

When exact identity is not possible, the diff engine may use deterministic weighted matching.

Because start time is part of identity, a rescheduled lesson reaches the diff
engine as an unmatched pair. Secondary matching must recognize it, otherwise a
time change becomes a delete and a create.

Ambiguous matches must remain ambiguous.

## 9. Revision pattern

Every successful parse creates a candidate revision.

Revision states:

```text
Received
Parsing
Parsed
Validating
ReviewRequired
Published
Rejected
Failed
Superseded
```

Only one published revision per source scope should be current.

Publishing a revision must be transactional.

## 10. Validation pattern

Validation has multiple levels:

### Record validation

Examples:

- date exists
- start precedes end, for a timed item
- supported group syntax
- supported class year
- valid event type

The duration and overlap rules read times, so they apply to timed items only. An
all-day item has no duration to find implausible and no time range to clash with
the teaching around it (ADR-046).

### Revision validation

Examples:

- event count anomaly
- deletion ratio
- date distribution anomaly
- excessive ambiguity
- overlap anomaly
- sudden unknown-course increase

The initial mandatory manual-review thresholds are:

- more than 20 percent of the previously published records disappear
- a group selector value not present in the supported profile schema appears
  (for example a new `İ4` cohort)
- multiple impossible overlaps occur for the same audience on the same local
  date and time

These conditions quarantine the semantic diff in `ReviewRequired`. They must
never auto-publish and must never start calendar deletion.

### Cross-source validation

Examples:

- room enrichment references no annual lesson
- practice program conflicts with annual program
- group appears in unsupported program

## 11. Semantic diff pattern

Diff uses:

1. exact stable identity match
2. content hash comparison
3. deterministic secondary matching for unmatched records using normalized
   lesson title and instructor, strengthened by the academic department when the
   source states exactly one on both sides
4. ambiguity quarantine

Secondary matching is limited to records with the same source, academic
context, local date, event type, status, audience and timezone. Title and
instructor must exist on both sides and cross their individual thresholds.

The department is evidence, not a precondition (ADR-035 as amended): under half of
the lessons the sources publish name one, and an integrated session names several,
which is not comparable. When both records name exactly one, all three attributes
are scored against the composite threshold. Otherwise title and instructor are
renormalized and must clear a higher composite bar, and the entry records a null
department score so the weaker basis stays visible. Two records that each name one
department and disagree are never re-scored without it.

A one-to-many or many-to-one candidate set is always ambiguous.

Output:

```text
Created
Updated
Deleted
Unchanged
Ambiguous
```

Deletion is produced only from a valid published revision.

A diff is calculated after publication, in its own transaction, driven by
revision state (ADR-039). Exactly one diff is stored per published revision. The
stored diff is created `Ready` or `Held`; a `Held` diff yields no calendar
operation at all (ADR-040):

```text
Published revision without a diff
→ load it and the revision it superseded
→ diff
→ Ready, or Held on ambiguity or mass deletion
→ store once
```

A held diff is released only by a named operator stating a reason, which moves
it to `Released` and keeps the hold reason (ADR-042). A hold caused by ambiguity
is never releasable: the source has to state which lesson is which.

Published data is corrected only by forward-fix: the authoritative source is
fixed and a newer revision supersedes the bad one. A superseded revision is
never restored to live state (ADR-033).

## 12. Audience resolution pattern

A canonical record contains an audience expression.

Examples:

```text
All grade 2 Turkish students
Grade 3 Turkish curriculum group A
Grade 2 anatomy group C2
Grade 3 English bedside group B4
```

Audience resolution converts a schedule change into affected users.

Do not enqueue every active user for every change.

## 13. Calendar event mapping pattern

The database maintains a durable mapping:

```text
User
CanonicalScheduleRecord
GoogleCalendar
GoogleEventId
LastAppliedContentHash
SyncState
```

This mapping is the primary path for updates.

Google private extended properties provide repair and reconciliation support.

Every user owns one dedicated Sirkadiyen Google calendar. Managed events are
never mixed into the user's primary or another existing calendar. License
revocation stops future synchronization but preserves this calendar and all
events already written to it.

## 14. Idempotent job pattern

Every externally mutating job must have an idempotency key.

Examples:

```text
initial-sync:{userId}:{profileVersion}
calendar-upsert:{userId}:{recordId}:{contentHash}
calendar-delete:{userId}:{recordId}:{publishedRevisionId}
license-redeem:{userId}:{licenseId}
```

Jobs must tolerate duplicate delivery.

## 15. Outbox pattern

Use a transactional outbox for events that must be published after database state changes.

Examples:

- revision published
- schedule diff created
- affected users resolved
- initial sync requested
- user profile changed

The transaction writes domain state and outbox records together. A worker dispatches outbox messages.

## 16. Reconciliation pattern

Routine synchronization is event-driven from schedule diffs.

A completed-sync connection whose credential fails records a durable reconciliation
boundary and ordered cursor:

```text
ReconciliationRequiredSinceUtc
+ ReconciliationCursorDispatchedAtUtc
+ ReconciliationCursorDiffId
```

Re-authorization preserves that tuple. A worker may admit the connection only when it
is authorized again, initial sync is complete, and the managed calendar still exists in
the local connection state. Cursor advancement and completion use the required-since
value as an optimistic workflow token so stale work cannot clear a newer request.

Re-auth catch-up replays only dispatchable semantic diffs already marked `Dispatched`,
ordered by `(DispatchedAtUtc, DiffId)`. This preserves the deletion boundary: absence
from current truth is never enough to remove an event.

The freeze-gated worker bounds connections and diffs per connection. It advances the
cursor only after every entry in one diff converges; a crash or external failure leaves
that diff eligible for idempotent replay. Completion requires a later empty scan and the
original required-since workflow token. A `Deleted` entry authorizes removal; an empty
scan never does.

When an `Updated` entry was matched by secondary attributes because its start-time-based
stable identity changed, patch the Google event ID already stored in the ledger and
atomically move the mapping to the new identity. Re-deriving an event ID from the new
identity would create a second event and strand the old one (ADR-061).

A separate reconciliation job periodically checks:

- expected managed events
- stored mappings
- actual Google events
- missing or duplicated events
- deleted calendars
- stale content hashes

Reconciliation repairs drift. It must not replace the normal incremental sync flow.

## 17. Error classification pattern

Classify failures:

```text
TransientExternal
PermanentExternal
AuthorizationRequired
ValidationFailure
ParserFailure
ConcurrencyConflict
ConfigurationError
UnexpectedInternal
```

Retry only failures that are plausibly transient.

## 18. Time pattern

- Store instants in UTC.
- Interpret faculty schedules in `Europe/Istanbul`.
- Store local schedule date and local time explicitly where useful.
- Convert to Google Calendar timestamps using an explicit timezone.
- Inject a clock abstraction.
- Test daylight and date-boundary behavior even though Türkiye currently uses fixed UTC+3.

## 19. Audit pattern

Audit at least:

- license creation, redemption, revocation
- role changes
- profile changes
- source configuration changes
- revision publication or rejection
- manual retry or repair actions
- calendar destructive operations

Audit entries must be append-only from the application perspective.

## 20. Session pattern

Web sessions use backend-managed HTTP-only secure cookies. The browser receives
a short-lived Google Identity Services ID credential and sends it once to the
backend; the backend validates its signature, issuer, audience, expiry and
verified email, then discards it. Google access/refresh tokens and Calendar OAuth
credentials remain server-side and are never exposed to browser JavaScript.
Cookie authentication uses `Secure`, an explicit `SameSite` policy,
rotation/expiry, anti-forgery protection for state-changing requests, and
server-side authorization for role, license, and onboarding state (ADR-052).

The initial administration bootstrap has exactly one backend-owned,
Google-verified email mapped to `SuperAdmin` (ADR-045). This is not a
client-supplied claim or a general RBAC system. Approval, release and freeze
actors come from the authenticated identity. The bootstrap literal grants the
explicit persisted role; authorization reads the role rather than comparing the
email on every request.

## 21. Single-use licensing and onboarding pattern

License plaintext exists only in the successful administrator creation response.
The database stores a deterministic HMAC-SHA256 lookup hash keyed by
`SIRKADIYEN_LICENSING__HASH_KEY`; the key is separate from authentication and
token-encryption secrets (ADR-053).

New codes are generated as `SRK-XXXXX-XXXXX` from an ambiguity-reduced
32-character alphabet (ADR-054). This is 50 random bits, protected against
offline enumeration by the keyed hash and against online guessing by endpoint
rate limiting. Legacy long codes remain readable but are no longer generated.

Redemption locks the submitted license row and uses a partial unique index to
permit only one current `Redeemed` license per user. The row lock prevents two
users from winning one code; the index prevents one user from winning two codes
submitted concurrently. Repeating the winning user/code pair is idempotent.
Every lifecycle transition and its audit row commit in one transaction.

A `SuperAdmin` may activate an existing Google-authenticated user without a
code. That creates an explicit `Manual` license already in `Redeemed`, with no
code hash, and records the actor and required reason as `ManuallyActivated`.
Manual activation and code redemption share the same one-current-activation
constraint, so they cannot both win a race.

Onboarding state is derived, never accepted from the client. With only the
implemented modules, no license maps to `LicenseRequired`, a redeemed license to
`ProfileRequired`, and a later revocation to `Suspended`. Profile, Calendar
authorization and sync modules extend that derivation from their own
authoritative records.

## 22. Flexible profile selector pattern

Keep `academicYear`, `classYear`, and `programLanguage` as relational columns.
Store variable cohort dimensions in a schema-versioned JSONB document, for
example:

```json
{
  "schemaVersion": "1.0",
  "selectors": {
    "practiceGroup": "İ",
    "practiceSubgroup": "1",
    "anatomyGroup": "2",
    "curriculumGroup": "3-A"
  }
}
```

The supported schema defines allowed keys, dependencies, and values per class
year and language. Both profile writes and audience matching use the same
validator. Do not use an unconstrained EAV model and do not trust arbitrary
JSONB supplied by a client.

The schema is implemented as server-owned code covering one current academic
year, cross-checked by test against the source catalog's declared selectors
(ADR-055). A dimension is either independent (explicit values) or dependent (a
parent key plus child values per parent value, so a subgroup is valid only under
its group). It is not a runtime config file: the confirmed matrix changes only at
year rollover, which is a deployment. The profile write requires an active
license first, and derived onboarding advances to `CalendarAuthorizationRequired`
once a profile row exists.

Fixed identifiers with a semantic structure — such as the university student
number (Öğrenci Numarası) — stay relational, stored as text to preserve leading
zeros, and are validated in three layers by ownership (ADR-056): the domain guards
the structural invariant (a fixed-length, all-digit string), the database pins the
same rule as a check constraint for defence in depth, and the application validator
owns the semantic cross-validation whose rules depend on business scope or on
another field of the same row (the number's faculty and program-language digits are
checked against the selected program). A rule that a check constraint cannot express
without the row's other fields belongs in the validator, not the database.

## 23. Adaptive polling interval pattern

Polling intervals are selected in `Europe/Istanbul` and remain configuration,
not hard-coded scheduling assumptions. The initial policy is:

```text
Weekend                         60 minutes
Weekday 00:00-07:00            45 minutes
Weekday 07:00-16:00            15 minutes
Weekday 16:00-21:00            25 minutes
Weekday 21:00-24:00            45 minutes
```

The exact boundaries and durations may be changed through validated worker
configuration. A configuration change must not create overlapping polling runs.

## 24. Global operational freeze pattern

A runtime-readable, audited global freeze gates every mutating pipeline boundary
(ADR-034). While frozen, the worker does not start acquisition, parsing,
publication, semantic-diff dispatch or calendar jobs. Work already persisted is
left in its current state and resumes through the ordinary state machine after
unfreeze. Failure to read the authoritative freeze state fails closed.

The authoritative state is the singleton PostgreSQL row
`operational_freeze_control`; `operational_freeze_audits` is its append-only
transition history (ADR-043). A transition updates the row and appends actor,
reason, UTC timestamp and correlation ID in one transaction. The worker checks
before every source, the source poller checks immediately before acquisition and
again after immutable evidence storage, and the publication service checks
immediately before each revision. A later diff dispatcher and every Calendar
job must consume the same application port rather than introducing a queue-local
flag.

The `SuperAdmin` API exposes both the read and the audited transition. Mutation
is CSRF-protected, requires a non-empty reason, derives the actor from the
verified session and uses the request trace as its correlation ID.

## 25. Delegated third-party authorization pattern

Sign-in and resource authorization are separate consents. Authenticating a user
grants no API access, and the scope needed to act on their data is requested later,
only from an account that has already met the product's prerequisites (ADR-052,
ADR-057).

Request the **narrowest scope that satisfies the design**. Where the product manages
only objects it creates, prefer a create-scoped grant over a full-access one, so the
user's existing data is out of reach structurally rather than by convention.

Never trust the requested scope: the provider reports what was **actually granted**,
and a user can complete consent while withholding a permission. Verify the required
scope is present and refuse the grant otherwise, rather than storing an authorization
that cannot do its job.

The browser obtains a one-time authorization code and posts it to the same-site,
CSRF-protected API; the exchange happens server-side because it carries the client
secret. The long-lived credential is **encrypted at rest** through an application-layer
abstraction, so the aggregate stores opaque ciphertext and the domain keeps no
cryptographic dependency. Read projections omit the credential entirely, so no response
can carry it by accident.

A stored grant is not proof of continued access. Model an explicit
"needs re-authorization" state for the moment the provider rejects the credential, and
treat only a healthy grant as satisfying onboarding. Re-authorizing must preserve the
resources already provisioned under the old grant.

Encrypting at rest binds the data to a key ring: any deployment that does not share a
persistent one loses every stored credential on restart.

## 26. Resumable per-user job with a deterministic idempotency ledger

Long, quota-bound, per-user work against an external service (initial calendar sync,
ADR-058) is driven by **persisted state**, not an in-memory queue. A request records
intent by moving an entity through a small lifecycle (`Pending → InProgress →
Completed`); a background worker acts on everything in the acting state each cycle, so a
crash resumes from the entity's state rather than losing the job. This is the same
state-driven, recovery-by-replay shape as the publication and diff stages ([[24. Global
operational freeze pattern]] gates all of them).

Make each unit of external work **idempotent two ways at once**, because a crash between
the external write and the local commit is always possible. First, derive a
**deterministic key** for the external object from stable inputs (here a base32hex hash
of the user and the lesson's stable identity, chosen so its alphabet is a valid provider
id); re-submitting the same key makes the provider reject the duplicate, which the client
reports as success rather than an error. Second, keep a **durable local ledger** of what
has been written (a row unique on the natural key); the worker computes the remaining set
as "applies to this user" minus "already in the ledger", and writes only the difference.
Either mechanism alone leaves a gap; together they converge without duplicates.

Bound the work per cycle (a per-entity, per-cycle budget) so a large first load spreads
across cycles and stays within external quota; completion is declared only when the
remaining set is empty, which also self-heals a ledger written after a crash. Isolate one
entity's failure from the batch: catch it, leave the entity in its acting state to retry,
and report it so the worker can log it — but note that without a permanent-failure or
backoff state, a persistently failing entity retries every cycle.

Resolution of "what applies to this user" belongs in a **pure function** over the user's
profile and the currently-live records (matching program dimensions, then cohort
selectors; an inactive record never applies), so the rule is unit-tested without a
database and is the single authority the query only optimizes for.

The one step the provider cannot make idempotent — creating the container object itself
(the calendar) — is the residual risk: persist its id immediately after creation to
shrink the crash window, and defer full protection (a marker-tagged lookup) to
reconciliation.

## 27. Edge-triggered fan-out with coarse job state over a fine-grained ledger

Propagating a decided change to many recipients (a schedule diff onto every affected
student calendar, ADR-059) is **edge-triggered**: the authoritative change record — the
diff — is the job, not a periodic reconcile against current truth. This is required when
a safety rule gates the action (deletion needs a published revision and a dispatchable
diff, AI_GUIDELINE §13): a level-triggered reconcile would bypass the hold that protects
against a mass change. The change record therefore carries its **own dispatch lifecycle**
(`Pending → Dispatched`, terminal `Failed`) rather than a separate job aggregate, reusing
[[26. Resumable per-user job with a deterministic idempotency ledger]] and the same freeze
gate.

The key move: keep dispatch tracking **coarse (per-job)** because idempotency lives in the
**fine-grained ledger** (§26). A worker killed mid-fan-out re-runs the whole job; each
recipient operation converges — a create is a deterministic-id conflict treated as success,
an update is skipped when the ledger's last-applied hash already matches, a delete of an
absent object is a no-op — so the job is marked done only after a clean pass. No
per-`(job, recipient)` progress table is needed, which is what makes the fan-out affordable.

The **ledger is the authority for who currently holds an item**; audience/recipient
resolution decides only additions. A pure planner maps each recipient to one operation:
delete when the item was removed or no longer applies to them, update when its content
moved, add when it now applies and they lack it. Because §26 records the last-applied hash
at creation time, re-dispatching a change that predates a recipient's own catch-up is a
no-op — the two triggers compose without double-applying.

Give failure a **two-level taxonomy**. Transient provider failures (rate limits, 5xx) retry
with bounded exponential back-off inside the call, then defer the whole job with a growing
back-off (`NextAttemptAtUtc`) and give up to `Failed` for an operator after a capped number
of attempts — so a stuck job stops churning every cycle. A dead **credential** is not a job
failure: flag that recipient's connection for re-authorization, skip them, leave what they
have, and let the job complete for everyone else. The residual gap — a recipient who was
skipped and later recovers must catch up on jobs finished while they were down — is the
deferred **reconciliation** concern, never a reason to block the job or delete their data.
