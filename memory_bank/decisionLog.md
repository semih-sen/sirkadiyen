# Decision Log

This file is append-only. Do not erase historical decisions. Mark decisions as superseded when necessary.

---

## ADR-001: Use a .NET primary backend

**Status:** Accepted
**Date:** 2026-07-21

### Context

The rewrite requires authentication, licensing, user profiles, background jobs, transactional state changes, Google API integration, incremental synchronization, and administrative workflows.

### Decision

Use .NET 10 and ASP.NET Core for the primary backend, with a separate .NET Worker Service for background operations.

### Consequences

- Strong typing and mature background-service support.
- One primary application ecosystem for business logic.
- Python remains isolated to parsing.
- Domain boundaries must prevent infrastructure leakage.

---

## ADR-002: Keep Python parser-only

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Spreadsheet structures are irregular and Python is suitable for specialized parsing and fixture-driven experimentation.

### Decision

Python will only parse normalized source snapshots and return canonical candidate records, evidence, warnings, metrics, and confidence information.

Python will not manage users, licenses, OAuth, database publication, affected-user resolution, or Google Calendar writes.

### Consequences

- Clear ownership boundaries.
- Parser can be tested deterministically.
- .NET remains authoritative for business state.
- A versioned HTTP contract is required.

---

## ADR-003: Use Google-only authentication

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The product is tightly integrated with Google Calendar and does not require password-based accounts.

### Decision

Registration and login will use Google authentication only.

### Consequences

- No password storage or recovery system.
- Google identity does not imply product activation.
- Local user and onboarding state remain necessary.
- Google Calendar authorization scopes must be managed separately or incrementally as appropriate.

---

## ADR-004: Require administrator-issued license activation

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Access is controlled through codes distributed by administrators.

### Decision

A Google-authenticated user must redeem a valid license code before completing activation and synchronization.

### Consequences

- License redemption must be transactional.
- Codes must be stored as secure hashes.
- Redemption attempts require rate limiting and audit logs.
- Exact expiration, revocation, and reuse policy remains open.

---

## ADR-005: Use canonical schedule records between parsing and calendar sync

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Source spreadsheet formats differ and change. Direct mapping from cells to Google events would tightly couple parsing with synchronization.

### Decision

All parser outputs must be converted into a canonical schedule model and published as versioned revisions before calendar synchronization.

### Consequences

- Parsing, validation, diffing, and synchronization are independently testable.
- Every event can retain provenance.
- Publication and rollback workflows are required.

---

## ADR-006: Use incremental event synchronization

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The old system deleted and recreated a broad two-week event range.

### Decision

Routine synchronization will update only events affected by a published semantic schedule diff.

### Consequences

- Durable user-to-Google-event mapping is required.
- Stable lesson identity and content hashing are required.
- Calendar jobs must be idempotent.
- Full rebuild is reserved for explicit repair operations.

---

## ADR-007: Preserve immutable raw source snapshots

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Parser behavior must be explainable and reproducible.

### Decision

Every changed source acquisition is stored as an immutable snapshot before parsing.

### Consequences

- Historical parser runs can be reproduced.
- Storage use increases.
- Snapshot retention policy may be added later.
- Source evidence can be shown to administrators.

---

## ADR-008: Use parser profiles instead of one universal parser

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The source inventory contains several structurally distinct spreadsheet families.

### Decision

Implement shared parsing primitives and multiple named, versioned parser profiles.

### Consequences

- Changes to one schedule family are less likely to break others.
- Each profile requires fixtures and regression tests.
- Source configuration must map each source to a parser profile.

---

## Open decisions

The following require future ADRs:

- frontend framework and component system
- exact browser session strategy
- managed Google Calendar strategy
- license reuse, expiration, and revocation policy
- background queue implementation
- source polling interval policy
- automatic publication thresholds
- profile schema and supported group combinations
- snapshot retention policy
- whether the canonical record needs a curriculum-block field, which the annual
  sources state and the parser currently keeps only as evidence
- how recurring rows such as `HER HAFTA PAZARTESİ` become dated lessons
- whether holidays and semester breaks become all-day calendar entries

---

## ADR-009: Use a versioned normalized snapshot and parser JSON contract

**Status:** Accepted
**Date:** 2026-07-21

### Context

The inspected spreadsheet families contain Excel date serials, fractional time
values, merged headings, formatting-dependent structure, multiple meaningful
worksheets, and separate lookup tables. Sending only displayed cell text would
lose evidence and make deterministic parsing unreliable.

### Decision

Use an explicit versioned JSON contract between .NET ingestion and the Python
parser.

- JSON properties use camel case and enums use camel-case strings.
- Worksheet coordinates are zero-based and range ends are exclusive.
- Snapshots preserve user-entered, effective typed, formula, formatted, note,
  merge, hidden-dimension, and relevant formatting data.
- Parser responses echo correlation and source identifiers and include canonical
  candidates, evidence, warnings, metrics, and confidence indicators.
- Transport contract versions and parser-profile versions evolve independently.

### Consequences

- C# and Pydantic models must remain contract-compatible.
- Contract serialization requires regression tests.
- Snapshot payloads are larger than value-only grids.
- Acquisition may use sparse cells but must retain structurally meaningful blank
  cells.
- Breaking wire changes require a new contract version.

---

## ADR-010: Model anatomy group independently from general practice group

**Status:** Accepted
**Date:** 2026-07-21

### Context

First- and second-year students have an anatomy assignment that does not follow
their normal practice-group assignment. The confirmed Grade 2 autumn and spring
source lists rotate anatomy groups `1`, `2`, and `3`. The Grade 1 anatomy source
uses the same or a very similar grouping model.

### Decision

Represent anatomy group as an independent student-profile and schedule-audience
dimension with supported values `1`, `2`, and `3` for the confirmed Grade 1 and
Grade 2 model.

Grade 2 anatomy and vertical-corridor sources apply to both Turkish and English
programs. During source joining, `Diseksiyon` identifies anatomy entries in the
annual program, while `Uygulama` identifies vertical-corridor and other practice
entries that require source-specific disambiguation.

### Consequences

- General practice group must never be reused as anatomy group.
- Profile validation must request and validate anatomy group separately.
- Audience selectors need an `anatomyGroup` dimension.
- Annual and special-program sources must enrich the same logical lesson rather
  than publish duplicates.
- `Uygulama` alone is insufficient to identify vertical-corridor lessons; parser
  profile context and joined evidence are required.

---

## ADR-011: Shared normalization primitives never infer missing values

**Status:** Accepted
**Date:** 2026-07-21

### Context

Source cells hold dates as serials or as Turkish and English text, times as day
fractions or as text with inconsistent separators, and group labels in several
shorthand forms. A parser that guesses is convenient in the short term, but a
wrong guess silently invents or misroutes lessons, and the mistake reaches
student calendars without anything to review.

### Decision

Every shared resolver returns an explicit result carrying the rule that produced
it, a confidence score, and a reason code when it did not resolve. Three
behaviours that would otherwise be guesses are opt-in per parser profile:

- reading a numeric cell as a date serial when no number format declares a date
- completing a date written without a year
- reading a bare four-digit block such as `0900` as a time

Additionally:

- a range whose end does not follow its start stays unresolved rather than being
  reordered
- a group expression that is only partly understood resolves to nothing, because
  keeping the understood half would silently drop a cohort
- a digit run longer than two characters is never a group value, so serials,
  years and room numbers cannot be mistaken for cohorts
- a weekday found beside a date is cross-checked and reported, never used to
  correct the date

### Consequences

- Parser profiles must declare their column rules explicitly.
- Unresolved values become warnings, so revision validation sees them.
- More source rows will initially fail to parse than with a permissive parser.
  That is the intended trade: an unparsed row is visible, a wrong row is not.
- Confidence scores are fixed constants per rule so output stays deterministic.

---

## ADR-012: Golden files are the parser regression net

**Status:** Accepted
**Date:** 2026-07-21

### Context

Determinism is a contract requirement, but shared primitives are used by every
profile, so a small change to normalization can silently alter many candidates.

### Decision

Fixture output is compared against committed golden files. Regeneration is
explicit through the `SIRKADIYEN_UPDATE_GOLDEN` environment variable and the
resulting diff must be reviewed and explained. Each golden document records the
fixture, the subject, and the parser engine version.

Determinism is asserted directly by running each producer twice and requiring
identical serialized output.

Until the first fixture-backed profile exists, the golden subject is a
normalization trace over synthetic fixtures. Synthetic fixtures are labelled as
such and never presented as captured faculty data.

### Consequences

- A changed golden file is a reviewable statement that parser output changed.
- Real fixtures require normalized snapshot JSON, which the .NET ingestion layer
  does not yet produce. This blocks fixture-backed profiles.
- The same harness will compare full parse responses once profiles exist.

---

## ADR-013: Inbound wire models accept camel case only

**Status:** Accepted
**Date:** 2026-07-21

### Context

ADR-009 fixed camel case as the JSON convention. Pydantic models that only
accept aliases cannot be constructed from Python by field name, which the parser
needs for the response models it builds itself.

### Decision

Split the Pydantic contract bases. `ContractModel` accepts camel-case aliases
only and is used for the snapshot models and the parse request. The parser
builds outbound models, so `OutboundContractModel` additionally accepts field
names while still serializing camel case.

### Consequences

- A snake-case inbound payload remains a validation failure, not a tolerance.
- The wire format is unchanged in both directions.
- Tests construct snapshot models through the same camel-case validation path a
  real producer would use.

---

## ADR-014: Normalize Google Sheets responses before hashing

**Status:** Accepted
**Date:** 2026-07-21

### Context

Parser profiles require normalized snapshot JSON, but the production acquisition
path did not exist. Google Sheets responses can return sparse and offset grid
segments, repeated cells from overlapping ranges, typed and formatted values,
merge metadata, and hidden dimensions. Hashing the raw response would couple
change detection to API response order and incidental acquisition metadata.

### Decision

The .NET infrastructure layer uses the official Google Sheets v4 client and maps
responses into the v1 normalized snapshot contract before calculating a hash.

- The application port receives snapshot identity and acquisition time from its
  caller; infrastructure does not invent them.
- Worksheets, merges, cells, dimensions, and requested ranges are put in a
  deterministic order.
- Identical repeated cells are deduplicated. Conflicting repeated cells are
  omitted and produce an error diagnostic, so API response order cannot choose
  an arbitrary value and the conflict is never silently accepted.
- Theme colors remain explicit as `theme:{name}` when no resolved RGB value is
  available.
- The lowercase `sha256:` hash covers contract version, normalized worksheets,
  and acquisition diagnostics, but excludes source ID, snapshot ID, and
  acquisition time.
- Authentication is supplied at composition time and must use the read-only
  spreadsheets scope; credentials never enter snapshot mapping.

### Consequences

- Reacquiring identical content produces the same content hash even when API
  collections or acquisition metadata differ.
- Structural, range-scope, cell-content, formatting, and diagnostic changes are
  detectable by the future polling workflow.
- Real snapshot capture still requires source configuration, authenticated
  worker composition, and immutable persistence.
- The local `.xlsx` fixtures remain unable to drive profiles until live snapshots
  are captured or a clearly separated development converter is added.

---

## ADR-015: Separate source transport from document format

**Status:** Accepted
**Date:** 2026-07-21

### Context

The confirmed inventory contains seven Google Sheets sources, Google Drive XLSX
and DOCX files, and a direct HTTP XLSX download. A parser profile should not need
to know how its evidence was transported.

### Decision

Maintain a versioned source catalog that separately records transport, document
format, parser profile, and fixture mapping. Transport adapters acquire bytes or
grid data; format converters produce the normalized snapshot contract. The
local XLSX converter is fixture-only, emits an explicit diagnostic, and trims
formatting-only worksheet tails using a semantic used range.

### Consequences

- New transports and formats can evolve independently.
- Parser profiles consume one stable snapshot contract.
- Drive/HTTP adapters and DOCX conversion remain implementation work.
- Real XLSX fixtures can unblock parser-profile development without presenting
  local conversion as a production acquisition path.

---

## ADR-016: Require an unattended source credential

**Status:** Accepted
**Date:** 2026-07-21

### Context

Worker polling has no interactive browser session. A Google OAuth client ID and
client secret identify the application but do not authorize unattended access.

### Decision

Configure exactly one source-access mode: OAuth with an offline refresh token,
or a service-account credential file. Request only the read-only spreadsheets
scope. Never commit or log credential material, and keep source ingestion
credentials separate from end-user Calendar grants.

### Consequences

- The current environment still needs a source refresh token or service account.
- A service account is preferred when the faculty can share sources with it.
- Revocation and invalid-grant failures must become explicit poll outcomes.

---

## ADR-017: Parse requests carry an explicit source context

**Status:** Accepted
**Date:** 2026-07-22

### Context

A canonical candidate must state its academic year, class year, program language
and interpretation timezone. No workbook states any of them. The Grade 1 Turkish
and Grade 1 English annual workbooks are also served by the same parser profile,
so the profile name cannot carry the language either. The alternatives were to
infer the academic year from the observed date range, infer the language from
the profile name, and infer the class year from a cell, all of which are guesses
that ADR-011 rules out.

### Decision

`ParseSnapshotRequest` carries a required `sourceContext` holding
`academicYear`, `classYear`, `programLanguage` and `timeZoneId`. The values come
from source configuration owned by .NET. The parser uses them directly and never
derives them.

Where a workbook does state something the context also states, the profile
validates rather than infers: a row whose term cell names a different class year
is excluded and counted, and a term cell that states nothing usable is reported
as an anomaly.

### Consequences

- The wire contract changed; the C# record, the Pydantic model and the shared
  contract fixture were updated together and both suites assert on it.
- The source catalog must eventually carry academic year and language per
  source; today they are supplied by the caller.
- One profile can serve several sources without branching on source ID, which
  ADR-008 requires.

---

## ADR-018: Stable identity includes the lesson start time

**Status:** Accepted
**Date:** 2026-07-22

### Context

The annual sources repeat one title several times on one day: free study, a
practice block and a break all recur. Identity built from academic year, class
year, language, date and course identity alone therefore collides, and the
colliding rows are genuinely different lessons. Adding an occurrence index would
make identity depend on row order, which ADR-005 and the identity pattern forbid.

### Decision

The annual stable identity is the ordered hash of academic year, class year,
program language, local date, local start time, and normalized course identity.
Instructor, location, curriculum block, end time and title formatting are
content, not identity, and live in the content hash instead.

A lesson moved to another time therefore changes identity. The semantic diff
engine's deterministic secondary matching, not the identity, is responsible for
recognizing a moved lesson so it is patched rather than deleted and recreated.

### Consequences

- Room, instructor and title reformatting update the existing calendar event.
- Time changes reach the diff engine as an unmatched pair, so Phase 8 must
  implement secondary matching before incremental sync goes live, otherwise a
  rescheduled lesson would be deleted and recreated.
- Course identity ignores a leading list number (`1.`, `1)`, `1-`) because the
  source renumbers those lists whenever the schedule shifts.

---

## ADR-019: Golden files project large parse responses

**Status:** Accepted
**Date:** 2026-07-22

### Context

ADR-012 made golden files the parser regression net and anticipated comparing
whole parse responses once profiles existed. A real annual workbook produces
around 900 candidates and a 1.5 MB response. Committing every field of every
candidate produces a diff nobody reads, which would defeat the purpose.

### Decision

A parse golden file records:

- one digest line per candidate: candidate ID, date, start and end time, event
  type, truncated identity and content hashes, and a truncated display title
- the complete warnings, metrics and confidence indicators
- the first and last candidate in full, pinning the field shape
- a SHA-256 digest of the entire serialized response

### Consequences

- A moved, retimed, reclassified or reworded lesson is visible in the diff.
- A change to a field no digest line covers still fails, through the response
  digest, and is then investigated locally.
- Golden files stay around 140 KB per fixture instead of several megabytes.
- ADR-012 is amended, not superseded: regeneration remains explicit and every
  changed golden must be explained.

---

## ADR-020: Group expressions state their cohort model

**Status:** Accepted
**Date:** 2026-07-22

### Context

The Grade 1 practice rotation labels cohorts with letters: columns hold `A` to
`H`, `A2` names a subgroup of group A, and `AB` appears where two groups share
a session. The shared resolver was calibrated on numbered cohorts, where a
leading `G` abbreviates the word *grup* and `G2` is group two. Reading the
practice matrix with the numbered model silently turned group G into nothing and
subgroup G2 into group 2, which would have sent lessons to the wrong students.

One letter cannot mean two things at once, and no value inspection can settle
it: `G2` is a valid expression under both models.

### Decision

`parse_group_expression` takes an explicit `letter_groups` flag and the parser
profile states the model its source uses. With lettered cohorts:

- a bare letter is a group, and only the spelled-out words are stripped
- `<letter><digit>` is a subgroup of that letter's group
- a run of letters such as `AB` names the individual groups, scored below a
  value the source spelled out because the reading follows from the cohort model
  rather than from the cell

The practice profile reports a whole group under the `practiceGroup` dimension
and a subgroup under `practiceSubgroup`. A value of any other shape is refused
with its own warning.

### Consequences

- Audience resolution must treat `practiceSubgroup` as a refinement of
  `practiceGroup`: a student in group A with subgroup 2 matches both `A` and
  `A2`.
- A makeup cell that names no group (`TELAFİ`) publishes nothing and warns. That
  is deliberate: sending a makeup session to everyone is worse than omitting it.
- Sources with a third cohort model, such as the English practice program's
  `İ1`-style labels, still refuse and must be reviewed before the resolver is
  widened again.

---

## ADR-021: Persist the schedule pipeline before identity and licensing

**Status:** Accepted
**Date:** 2026-07-22

### Context

The pipeline can now acquire and parse real sources, but nothing survives a
process restart. Persistence is required for polling, change detection,
revisions and diffing. The full product also needs users, licenses, student
profiles and calendar event mappings.

License policy is an explicitly open decision: single-use or multi-use,
expiration, cohort restriction, revocation consequences. Modelling those tables
now would encode guesses in a migration, and an applied migration cannot be
edited.

### Decision

The first schema covers `schedule_sources`, `source_snapshots`, `parse_runs`,
`schedule_revisions` and `canonical_schedule_records`, using EF Core 10 with
Npgsql and version-controlled migrations. Identity, licensing, profiles and
calendar mappings wait for the ADRs that decide their behaviour.

Guarantees the schema enforces rather than the application:

- a source identifier is unique
- one source may have at most one published revision, through a partial unique
  index on the published state
- one revision may not hold the same stable identity twice
- a canonical record must end after it starts
- contested rows carry PostgreSQL's `xmin` as a concurrency token

The unchanged-source short circuit compares only the source's latest snapshot
and runs inside a transaction that locks the source row, so two concurrent polls
cannot both decide the source changed. Snapshot payloads, audience selectors and
parser evidence are stored as `jsonb`.

### Consequences

- Reverted content counts as a change, because only the latest snapshot decides.
  That is correct: the published schedule really did move back.
- Snapshot payloads are large, and the retention policy remains open.
- Identity and licensing migrations will come after their ADRs, not before.
- Database integration tests require a real PostgreSQL and report themselves as
  skipped when none is configured, so an unrun guarantee never looks like a
  passing one.

---

## ADR-022: Licenses are single-use and revocation suspends synchronization

**Status:** Accepted
**Date:** 2026-07-22

### Context

The licensing schema was deferred because reuse, expiration, and revocation
semantics were unresolved. Calendar deletion on revocation would be especially
risky because revocation can be administrative, temporary, or mistaken.

### Decision

Every license code is single-use and may activate at most one user. Redemption
is transactional and idempotent. Revoking the active license stops all future
synchronization for that user but preserves the user's dedicated Sirkadiyen
calendar and its existing events. Revocation never starts event deletion.

### Consequences

- The database needs a uniqueness guarantee preventing two redemptions.
- License status, redemption, revocation, and any later reactivation are audited.
- Sync job admission checks the authoritative license state server-side.
- Existing events may become stale after revocation; the UI must state that the
  calendar is no longer synchronized.

---

## ADR-023: Web sessions use backend-managed secure cookies

**Status:** Accepted
**Date:** 2026-07-22

### Context

Google sign-in establishes identity, but the browser still needs an application
session. Exposing bearer or Google refresh tokens to JavaScript would increase
the impact of XSS and complicate revocation.

### Decision

Use a backend-managed HTTP-only secure cookie for the web session. Configure an
explicit SameSite policy, expiry and rotation. State-changing endpoints require
anti-forgery protection. Authorization for admin role, license, profile, and
sync eligibility is always enforced by the backend.

### Consequences

- The frontend never receives Google refresh tokens or an application bearer
  token for normal browser use.
- Cross-origin deployment and callback URLs must be designed around cookie and
  SameSite rules.
- CSRF protection is a mandatory part of authentication implementation.

---

## ADR-024: Create one dedicated Sirkadiyen calendar per user

**Status:** Accepted
**Date:** 2026-07-22

### Context

Mixing managed events into a user's primary calendar makes ownership, repair,
and safe cleanup ambiguous. Asking users to select an existing calendar also
creates inconsistent deployment and support paths.

### Decision

Create one dedicated Sirkadiyen Google calendar for every activated user and
write all managed events to it. Persist its Google calendar ID and every event
mapping. Mark managed events with private extended properties. If the calendar
is deleted or inaccessible, stop normal sync and require an explicit audited
repair or recreation flow.

### Consequences

- Initial sync includes an idempotent calendar-creation step.
- Event insert, patch, delete, and reconciliation never target the primary
  calendar.
- License revocation preserves the dedicated calendar as required by ADR-022.

---

## ADR-025: Quarantine destructive or structurally unknown revisions

**Status:** Accepted
**Date:** 2026-07-22

### Context

A technically successful parse may still represent a broken or radically
changed source. Automatic publication would convert a source mistake into mass
calendar mutations.

### Decision

Move a revision and its semantic diff to `ReviewRequired` when any of these
conditions is true:

- more than 20 percent of the previously published records disappear
- an unknown group selector appears, such as a new `İ4` value not present in
  the supported profile schema
- multiple impossible overlaps occur for the same audience at the same local
  date and time

Manual approval is required before publication. Until approval, no affected-user
resolution or calendar deletion job may be created.

### Consequences

- Validation needs the prior published revision and the supported profile schema.
- Diff records need explicit anomaly evidence and review state.
- The 20 percent boundary means exactly 20 percent does not trigger this rule;
  any value greater than 20 percent does.

---

## ADR-026: Select polling intervals from an Istanbul-time policy

**Status:** Accepted
**Date:** 2026-07-22

### Context

Source change probability varies by day and hour. A single aggressive interval
wastes quota at night and weekends, while a single relaxed interval delays
weekday changes.

### Decision

Use a validated, configurable interval policy evaluated in `Europe/Istanbul`.
The initial schedule is 60 minutes on weekends; on weekdays, 45 minutes from
00:00-07:00 and 21:00-24:00, 15 minutes from 07:00-16:00, and 25 minutes from
16:00-21:00. Prevent overlapping poll cycles.

### Consequences

- Tests cover every boundary and weekend precedence.
- Operational changes alter configuration without changing polling code.
- A single worker cycle records per-source outcomes and schedules the next cycle
  only after the current cycle finishes.

---

## ADR-027: Store variable student group selectors as validated JSONB

**Status:** Accepted
**Date:** 2026-07-22

### Context

Practice cohorts (`İ1`, `İ2`, `İ3`), anatomy groups, third-year curriculum
groups, rotations, and future electives do not share one fixed set of columns.
An unconstrained EAV model would be flexible but would make validation, querying,
and referential rules harder to understand.

### Decision

Keep academic year, class year, and program language as relational fields. Store
the remaining group choices in a schema-versioned JSONB selector document. A
server-owned supported-profile schema defines valid dimensions, values, and
dependencies. Reject unknown or incompatible client values. Reuse the same
selector semantics for canonical audiences and affected-user resolution.

### Consequences

- New group dimensions can be introduced without adding nullable columns.
- JSONB documents and their schema versions must be migrated deliberately when
  selector semantics change.
- Intentional JSON indexes may be added only after real audience queries are
  measured; JSONB is not a substitute for core relational fields.

---

## ADR-028: Retry parser transport failures on one logical parse run

**Status:** Accepted
**Date:** 2026-07-22

### Context

ADR-021 permits one parser-profile version to run once per immutable snapshot.
If the HTTP call fails after a changed snapshot is stored, the next acquisition
is unchanged. Stopping at the content short circuit would strand that snapshot
forever, while inserting another parse run would violate deterministic identity
and allow duplicate revisions.

### Decision

Keep one logical `ParseRun` for each snapshot/profile/version and count transport
attempts on it. A failed run may resume with a new correlation ID and incremented
`AttemptCount`. When acquisition is unchanged, retry against the normalized
snapshot document already stored as immutable evidence, not the newly acquired
but intentionally unstored duplicate. Completed, warning, and rejected runs do
not execute again.

### Consequences

- Transient parser transport failure no longer requires a changed source to
  recover.
- The unique parse-run index remains the concurrency and idempotency guard.
- The current row retains the latest failure and total attempt count rather than
  a full attempt history; structured logs must retain per-attempt diagnostics.
- Recovery of a run left `Running` by abrupt process termination remains a
  separate worker-maintenance task.

---

## ADR-029: Revision validation quarantines rather than rejects

**Status:** Accepted
**Date:** 2026-07-22

### Context

ADR-025 named the anomalies that must stop publication but not the thresholds,
the severity model, or where validation runs. Implementation forced all three.
The accepted product direction is automated publication with safety nets, so the
nets carry the whole burden of preventing mass calendar damage.

### Decision

Validation is a pure function over a candidate revision, its source, and the
stable identities of the last published revision. It runs in its own transaction
after parse completion and moves the revision `Parsed → Validating → outcome`.

Severity determines the outcome:

- any `Error` finding moves the revision to `ReviewRequired`
- an empty revision is `Rejected`, the only terminal outcome
- otherwise the revision is `Validated`

`Rejected` is reserved for a revision no approval could rescue, because rejection
is unrecoverable. Everything a human could reasonably approve stays approvable.

The deletion rule requires **both** conditions to hold:

```text
deletedCount > previouslyPublishedCount * 0.20
  AND deletedCount >= 10
```

The share alone is meaningless on a small source, and the absolute count alone is
noisy on a large one. Exactly 20 percent does not trigger the rule (ADR-025).

Thresholds live in `RevisionValidationOptions` and are configurable per
deployment. Validation may never produce `Published`; the store rejects the
attempt.

### Consequences

- A source with no published revision cannot trip the deletion rule.
- One audience overlap is a warning; more than one quarantines the revision, per
  ADR-025's wording of "multiple" impossible overlaps.
- Overlap detection compares exact selector sets, so a group and its own subgroup
  do not register as overlapping. Detecting that needs the ADR-027 profile schema.
- The academic-year range is derived as 1 August to 31 July from a `YYYY-YYYY`
  label, widened by a configurable grace period. An unreadable label produces a
  warning rather than silently skipping the rule.
- The duplicate-stable-identity rule cannot fire through the normal pipeline,
  because the schema's unique index on `(ScheduleRevisionId, StableIdentity)`
  rejects the insert first. It is retained as defence in depth.
- Publication remains unimplemented and is the next step.

---

## ADR-030: Problem-based learning is out of scope

**Status:** Accepted
**Date:** 2026-07-22

### Context

The Grade 1 Turkish practice source states PDÖ sessions using a different group
partition from the rest of the workbook: `D3`, `F4`, `G4`, `H3` alongside the
regular `A1`/`A2` halves. The parser filed both partitions under the single
`practiceSubgroup` dimension, so a student profile holding one subgroup value
could not express which PDÖ group the student belongs to. Published PDÖ lessons
would therefore have reached the wrong students.

### Decision

Exclude PDÖ from the system entirely. It runs twice a year and its groups are
arranged between students out of band, so it is not schedule data Sirkadiyen can
own. `grade1_practice_v1` detects out-of-scope subject columns by whole-word key
and publishes none of their cells.

Exclusion is not silent. Each out-of-scope column produces an informational
warning with evidence, and every populated cell in it is counted through
`cells.ignored.outOfScopeSubject`, as AI_GUIDELINE section 9 requires.

### Consequences

- The Grade 1 Turkish practice source yields 402 candidates rather than 426.
- The declared cohorts collapse to a clean `A`-`H` with `1`/`2` subgroups, which
  is what `supportedAudienceSelectors` now states.
- This is a product decision, not a parser gap; it is not tracked as future work.
- A source that legitimately needs a finer partition later must introduce its own
  audience dimension rather than reusing `practiceSubgroup`.

---

## ADR-031: A student sees both subgroups of their practice group

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** not yet; recorded ahead of calendar synchronization

### Context

Vertical-corridor practices split a group into subgroups such as `A1` and `A2`.
Students frequently do not know which subgroup they are in at the start of the
academic year, so requiring the subgroup during onboarding would block or
mis-assign a large share of users.

### Decision

The parser continues to resolve and publish `A1`, `A2` and their siblings as
distinct audience selectors; canonical data stays precise.

At synchronization time, a student who has stated only their practice group
receives the events of **every** subgroup of that group, with the subgroup
prefixed in the event title:

```text
[A1] İletişim Becerileri
```

### Consequences

- Precision stays in the canonical model; the widening is a synchronization-time
  policy, so it can be narrowed later without reparsing anything.
- A student who does state their subgroup should receive only that subgroup's
  events. Affected-user resolution must handle both cases.
- Event titles become audience-dependent, so the title cannot be part of a
  content hash shared across users, or every user would appear to disagree.
- Duplicate-looking calendar entries are expected and intended; the prefix is
  what makes them readable.

---

## ADR-032: Healthy revisions publish themselves; held ones need a named approver

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** transactional publication, the approval audit trail, the internal
administration API

### Context

Validation leaves a revision in `Validated` or `ReviewRequired` (ADR-029) but
cannot publish either. Something had to decide when a revision becomes live, and
who is allowed to override a revision validation refused to clear. There is no
identity provider and no administration frontend, and both are deferred.

### Decision

A revision that reaches `Validated` is published without human intervention. The
transition to `Published` and the previous revision's transition to `Superseded`
commit in one transaction.

A `ReviewRequired` revision reaches publication only through approval, which
requires a named approver and a stated reason. Both are stored on the revision as
`ApprovedBy` and `ApprovalReason`, alongside `ApprovedAtUtc`. Approval moves the
revision to `Validated` and stops there, so an approved revision publishes
through exactly the same transaction as one that was never held.

While there is no frontend, approval runs over an internal API guarded by a
required shared key, `SIRKADIYEN_ADMIN__API_KEY`. The API refuses to start
without one.

Publication is driven by revision state rather than by the poll that produced the
revision. The worker publishes every `Validated` revision at the end of each
cycle.

### Consequences

- A crash between validation and publication costs nothing: the next cycle
  publishes from state. The same path recovers a revision approved through the
  API when that request failed after approving it.
- Superseding and publishing are two `SaveChanges` calls inside one transaction.
  The "one published revision per source" rule is a partial unique index, which
  cannot be deferred, so the outgoing revision must vacate the slot first.
- Publishing an older revision over a newer live one is refused. It would move a
  source's schedule backwards, which the semantic diff would read as a mass
  deletion.
- `ApprovedBy` is a claim, not a verified identity. The key proves the caller is
  an operator, not which operator. This is acceptable only until real
  authentication exists, and the field is designed to keep working unchanged when
  it does.
- Anyone holding the administrative key can push a quarantined schedule into
  student calendars. The API must not be publicly exposed while the key is the
  only gate.
- Publishing overwrites `StateReason`, so the approval reason survives in its own
  column rather than in the state text. The findings that held the revision are
  never deleted either way.

---

## ADR-033: Correct published schedules by forward-fix, never rollback

**Status:** Accepted
**Date:** 2026-07-22

### Context

A revision can be superseded by a newer revision, but restoring an older
revision would introduce a second publication path, complicate audit history,
and risk replaying schedule data that is no longer correct at its source.

### Decision

Sirkadiyen does not provide a rollback operation for published revisions. An
incorrect schedule is corrected in its authoritative source, such as Google
Sheets. Normal polling then acquires the correction, creates and validates a new
revision, publishes it, calculates the forward semantic diff, and updates the
affected calendars.

### Consequences

- A superseded revision remains immutable evidence and can never become live
  again.
- Operators repair the source of truth rather than application state.
- The UI and runbooks use the term `forward-fix`, not rollback.
- The absence of rollback makes an operational freeze and rapid source repair
  essential.

---

## ADR-034: A global freeze stops ingestion and publication

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** core persistence and pipeline gates; authenticated remote write
surface remains deferred

### Context

A source may change structure without warning or produce a large anomaly. The
normal validation barriers prevent suspicious publication, but operators also
need one global switch that stops new evidence and live-state changes while the
incident is understood.

### Decision

Add a dynamically readable global operational freeze. While it is enabled:

- no source acquisition starts
- no parse run starts or resumes
- no revision is published, including an approved or already validated one
- no semantic diff dispatch or calendar mutation starts

An operation already performing an external read may finish storing immutable
evidence, but it may not cross the publication boundary. Unfreezing resumes the
ordinary state-driven pipeline; it never skips validation and never creates a
special publication path. Freeze/unfreeze changes are audited with actor,
reason, timestamp and correlation ID.

A startup-only environment variable is insufficient because changing it needs a
restart. The implementation must use an authoritative runtime-readable control
and fail closed when its state cannot be read. The administration surface is
added after real operator authentication exists.

### Consequences

- The worker checks the freeze before every source and immediately before
  publication and downstream dispatch.
- The API exposes freeze state read-only until authenticated administration is
  implemented.
- Freeze preserves queued and validated work; it does not reject or delete it.
- Calendar jobs must check the same control so a queue backlog cannot bypass the
  switch.

---

## ADR-035: Secondary matching uses title, instructor and academic department

**Status:** Accepted, amended 2026-07-23 (see the amendment at the end of this
entry; the department is no longer a precondition)
**Date:** 2026-07-22
**Implements:** semantic diff model, pure matching engine, persistence and
post-publication orchestration

### Context

Start time is part of stable identity (ADR-018). A lesson moved to another time
therefore appears as an unmatched old and new record. Treating that pair as a
delete and create would lose the durable Google event mapping even when the
lesson itself did not change.

### Decision

After exact stable-identity matching, compare only structurally compatible
unmatched records: same source, academic context, local date, event type,
record status, audience and timezone.

Secondary matching requires all three explicitly sourced attributes on both
records:

1. normalized lesson title
2. instructor
3. academic department

Use deterministic normalized Levenshtein similarity. Initial thresholds and
weights are:

```text
lesson title       minimum 0.82, weight 0.50
instructor         minimum 0.85, weight 0.30
department         minimum 0.90, weight 0.20
composite score    minimum 0.88
```

Every component must cross its own threshold as well as the weighted composite.
If either side lacks any of the three fields, it is not eligible for secondary
matching; nothing is inferred from evidence. `Department` is therefore an
optional canonical field populated only by parser profiles whose source states
it.

A pair is accepted only when it is the sole eligible candidate for both the old
and new record. Any one-to-many or many-to-one candidate set is `Ambiguous` and
must never become delete-and-create automatically.

### Consequences

- Minor spelling, punctuation and Turkish-diacritic differences may still match.
- Similar lessons in the same slot stay quarantined when the data cannot choose
  uniquely.
- Scores and their three components are retained as evidence.
- Threshold changes are behavioral changes and require regression tests and an
  ADR amendment.
- Existing canonical records have null `Department` after the additive
  migration and remain safely ineligible until a future parsed revision states
  it.

### Amendment 2026-07-23: the department is evidence, not a precondition

**Status:** Accepted
**Supersedes:** the requirement above that all three attributes be present

The original rule was written expecting the department to be present on most
records. Fixture evidence says otherwise. Of the candidates the two committed
Grade 1 annual workbooks publish, 419 of 901 Turkish and 417 of 953 English name
a department at all — under half. Eleven candidates per source name *several*,
because an integrated session is taught by two to four departments, and a list is
not a comparable value: two integrated sessions that share one department out of
three are not the same lesson, and comparing the joined text would score them as
nearly identical.

Requiring the attribute therefore meant that most lessons whose time moved would
reach the calendar as a delete followed by a create — exactly the outcome this
ADR exists to prevent — and, through ADR-040, would hold the diff on the deletion
count.

The rule is now two-tier. Structural compatibility, the per-attribute title and
instructor minima, and the uniqueness requirement are unchanged.

1. **With a department.** When both records name exactly one department, score all
   three attributes against the thresholds above.
2. **Without a comparable department.** When either record names none, or names
   several, score title and instructor with their weights renormalized to sum to
   one, and require a composite of at least `0.94`.

A pair where both records name exactly one department but disagree about it is
*not* re-scored by the second rule. It was already offered to the stronger rule
and refused, and re-scoring it without the attribute that disagreed would turn a
rejection into a match.

`DepartmentScore` on the diff entry is null exactly when the second rule was
used, so a consumer and an operator reviewing a held diff can tell the weaker
basis from the stronger one. The composite threshold without a department is
configuration-validated to be at least the one with a department, so no operator
can make weaker evidence easier to satisfy.

**Consequences**

- Every lesson with a stable title and instructor survives a time change as an
  `Updated`, whether or not its source names a department.
- The weaker rule demands near-identical title and instructor: with an identical
  instructor, a title similarity below about `0.90` is refused.
- The department still strengthens a match and still blocks one when two single
  departments disagree.
- An integrated session is matched without its department list, and the list is
  kept for display (ADR-049).

---

## ADR-036: Use Next.js for the frontend

**Status:** Accepted
**Date:** 2026-07-22

### Context

The frontend stack was intentionally deferred while the schedule pipeline was
being established.

### Decision

Use Next.js with React and TypeScript. The browser consumes typed backend
contracts and uses the backend-managed secure-cookie session from ADR-023.

### Consequences

- Authorization, activation and synchronization truth stay server-side.
- A component system remains a later, narrower UI decision.
- Deployment includes a separate frontend container unless a later ADR changes
  the topology.

---

## ADR-037: Use Hangfire for durable background jobs

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** not yet

### Context

Calendar synchronization, affected-user resolution, reconciliation and
maintenance need durable retryable jobs. The technology baseline named Hangfire
as the initial preference but had not accepted it.

### Decision

Use Hangfire for durable background jobs, initially with PostgreSQL-backed
storage. Domain state and required job dispatch remain connected through the
transactional outbox; application logic does not call Hangfire directly.

### Consequences

- Jobs remain idempotent because queue delivery is at least once.
- Hangfire retries do not replace domain failure classification.
- Redis may still serve locks, caching and rate limiting, but is not the
  authoritative durable job store.
- The global freeze from ADR-034 gates Hangfire calendar and publication jobs.

---

## ADR-038: Recurring undated rows are out of scope

**Status:** Accepted
**Date:** 2026-07-22

### Context

Rows such as `HER HAFTA PAZARTESİ` name a recurrence but do not state the
individual dates or an unambiguous recurrence boundary. Expanding them would
invent schedule instances the source did not explicitly provide.

### Decision

Do not publish recurring undated rows. Parser profiles detect and account for
them with evidence, informational warnings and ignored-row metrics. They are not
future product work unless a source begins stating explicit dates or a later ADR
introduces a safe recurrence contract.

### Consequences

- The six currently known rows stay out of student calendars.
- Exclusion is visible and deterministic, never a silent discard.
- This decision does not apply to explicitly dated repeated lessons.

---

## ADR-039: Calculate and store the semantic diff after publication, not inside it

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented

### Context

Publication makes a revision live in one transaction that also supersedes the
revision it replaces. The semantic diff describes what that publication changed
and is the only authority for a later calendar deletion. It could be calculated
inside the publication transaction, or afterwards as a separate step.

### Decision

Calculate and store the diff in a separate transaction after publication, driven
by revision state rather than by the caller that published: a revision that
reached `Published` or `Superseded` and has no diff row is diffed on the next
worker cycle. Exactly one diff per revision is enforced by a unique index on the
current revision, so a retried calculation reports the existing diff rather than
writing a second one.

### Consequences

- A revision is live the moment publication commits. A diff that fails to
  calculate cannot take back a schedule students are already entitled to see.
- A worker killed between the two steps recovers without operator action.
- A revision that was published and superseded before its diff ran is still
  diffed, so nothing it changed is lost.
- The diff is stored evidence and is never recalculated. Correcting a bad
  publication remains forward-fix only (ADR-033).

---

## ADR-040: Hold a diff at dispatch on ambiguity or mass deletion

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented

### Context

Revision validation already applies a mass-deletion rule before publication, but
it compares stable-identity sets. It cannot know that a lesson whose time
changed will be recovered by secondary matching (ADR-035), nor that a candidate
set will refuse to resolve and stay `Ambiguous`. The number that decides how
many calendar events would actually be deleted only exists after the diff runs.

### Decision

A stored diff is created in `Ready` or `Held`. It is held when it contains any
`Ambiguous` entry, or when its deletions reach the configured minimum count and
exceed the configured share of the previous revision. A held diff may not be
turned into any calendar operation. The reason is stored in full on the diff.

### Consequences

- A single ambiguous pair holds the whole diff: acting on the rest would delete
  the previous record of that pair from a student's calendar.
- The minimum deletion count keeps a small source from being held on ordinary
  editing, which would train operators to approve without reading.
- This gate does not replace the validation rule; both run, at different stages,
  on different evidence.
- Releasing a held diff requires an operator path that does not exist yet. Until
  it does, a held diff simply stops there, which is the safe direction.

---

## ADR-041: Load the repository `.env` into the process environment at startup

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented

### Context

`dotnet run --project src/Sirkadiyen.Api` failed with
`Required configuration 'SIRKADIYEN_DATABASE:CONNECTION_STRING' is missing`
even though the value was in the repository's `.env`. Nothing read that file:
`.env` was only ever consumed by Docker Compose, so a developer had to export
every variable by hand in each shell, and the EF Core design-time factory fell
back to a hard-coded local host instead.

Adding a package such as `DotNetEnv` would have solved it, but the parser is
about eighty lines, the behaviour we need is specific (existing variables must
win) and a dependency in the dependency-free Infrastructure composition path
buys nothing here.

### Decision

`Sirkadiyen.Infrastructure.Configuration.DotEnvFile` searches upward from the
assembly's output directory for the nearest `.env`, parses it, and writes each
declaration into the process environment **only when that variable is not
already set**. Both hosts call it before creating their builder, because the
environment-variable configuration provider reads the environment as it is
added. The design-time context factory and the PostgreSQL test fixture call it
too, so migrations and integration tests need no manual export either.

It is a development convenience, not a configuration source: deployed
environments inject real variables and ship no file, so the call is a no-op
there. A malformed line raises `InvalidDataException` naming the file and line
number, never the value.

### Consequences

- `dotnet run`, `dotnet-ef` and `dotnet test` work from a clean shell.
- An exported or container-injected variable stays authoritative, so a stale
  file cannot quietly redirect a host at the wrong database.
- The loader mutates process-global state. That is why it runs once, at the top
  of a host's composition, and never from library code reached at request time.
- Integration tests now run whenever `.env` declares
  `SIRKADIYEN_TEST_DATABASE__CONNECTION_STRING`. That database is dropped and
  re-migrated on every run, so the file must never point it at a working one.
- There is deliberately no inline comment syntax: a password may contain `#`,
  and silently truncating a secret at one would be very hard to diagnose.

---

## ADR-042: Let an operator release a held diff, except an ambiguous one

**Status:** Accepted
**Date:** 2026-07-22
**Implements:** implemented
**Amends:** ADR-040

### Context

ADR-040 holds a diff on ambiguity or mass deletion and lets nothing act on it.
It left the release path unspecified, so a hold could only be cleared by
correcting the source and waiting for a newer revision to supersede the held
one. That is right when the hold reveals a parse fault and wrong when the source
really did drop a hundred lessons at the end of a semester: the schedule is
correct, nobody can say so, and every later cycle holds again.

ADR-040 also stated that a stored diff never mutates. That consequence is
superseded here, for the state and the release audit fields only. What the diff
says about the two revisions is still immutable and never recalculated.

### Decision

A `Held` diff gains a third state, `Released`, reached through
`POST /api/diffs/{id}/release` behind the existing operator key. The release
records `ReleasedBy`, `ReleaseReason` and `ReleasedAtUtc`, and keeps the hold
reason. `IsDispatchable` becomes true for `Ready` or `Released`.

A hold caused by ambiguity is **not** releasable, in the domain and not only in
the endpoint. The release is guarded by the diff's row version.

### Consequences

- A legitimate large deletion now has a way forward that is recorded, instead of
  depending on the source being edited into a shape the gate tolerates.
- An ambiguity can only be resolved at the source. An operator cannot decide
  which of several candidates a record became, and waving it through would leave
  the previous lesson in every affected calendar while its replacement is never
  written.
- `Released` is deliberately a separate state from `Ready`. A consumer, and
  anyone reading the table later, can tell an automatically safe diff from one a
  human vouched for.
- Two operators releasing at once get a refusal rather than a silent overwrite:
  the second must read the current state before vouching for it.
- `ReleasedBy` is a claimed identity, like `ApprovedBy` (ADR-032). It becomes
  verifiable only when real authentication replaces the shared key.
- Nothing dispatches diffs yet, so releasing one currently changes only its
  state. The value arrives with the calendar adapter.

---

## ADR-043: Persist the global freeze as a singleton with append-only transitions

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** ADR-034 core persistence and current pipeline gates

### Context

ADR-034 requires one runtime-readable switch, fail-closed reads, and an audit
trail for both freeze and unfreeze. A startup setting cannot change without a
restart, while a queue-local flag could disagree between the API, worker and
future Hangfire processes. Updating only one mutable row would retain the latest
reason but erase the incident history.

### Decision

Store the authoritative current state in exactly one PostgreSQL row,
`operational_freeze_control`, and append every actual transition to
`operational_freeze_audits`. A transition locks the singleton row, changes it,
and appends actor, reason, UTC timestamp and correlation ID in one transaction.
Repeating the current state is idempotent and creates no audit row.

Every pipeline boundary reads the state through `IOperationalFreezeStore` at
runtime. A missing row or database read failure throws and the mutation does not
start. The worker checks before every source; the source poller checks
immediately before acquisition and after immutable evidence storage; publication
checks before every revision.

The existing operator-key API exposes only `GET /api/operations/freeze`.
Freeze/unfreeze mutation is implemented behind the application port but receives
no remote endpoint until real operator authentication exists. Future semantic
diff dispatchers and Calendar jobs must use the same port.

### Consequences

- Every host and future queue consumer observes one durable state without a
  restart.
- A freeze enabled during an external source read permits its immutable snapshot
  to be stored but blocks the parse run that would follow.
- Work remains queued or validated and resumes through the ordinary state
  machine after unfreeze.
- Direct SQL changes are not a supported operational path because they would
  bypass the append-only audit.
- The authenticated write surface remains explicit follow-up work; the shared
  key is not extended with another safety-critical mutation.

---

## ADR-044: Retain the active-year anchor, latest source, and ten recent days

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** snapshot payload retention and worker maintenance
**Amends:** ADR-007

### Context

Changed polls store normalized snapshot documents that can be several megabytes.
The faculty schedule is continuously corrected, so keeping every old payload
online forever has little operational value. Operators need the main schedule as
the academic year began and a short recent history to answer "what changed?".

Deleting complete pipeline lineages is unsafe before diff dispatch has a durable
completion marker: parse runs, revisions, canonical records and diffs are still
audit and future synchronization evidence. The large snapshot JSON is the part
that drives unbounded storage.

### Decision

For each source, retain the normalized payload of:

- the first snapshot captured under the source's currently configured academic
  year
- the latest snapshot, even after a long quiet period
- every changed snapshot acquired in the last ten days
- every snapshot with no parse run or with a `Running` or `Failed` parse run

Snapshots are stored only when content changes, so the ten-day window contains
every change rather than one artificial copy per calendar day.

After the window, a maintenance batch removes only eligible normalized payloads.
The snapshot row, source and external identifiers, academic year at acquisition,
content hash, counts, acquisition time and prune time remain. Parse responses,
revisions, canonical records and semantic diffs are not deleted. The payload is
never overwritten with different evidence; it is either retained exactly or
explicitly pruned.

The maintenance boundary reads the global operational freeze and does nothing
while frozen. The initial window is ten days and the batch size is operational
configuration.

### Consequences

- Storage is bounded primarily by one annual anchor, one latest payload and the
  changed snapshots from the recent window per source.
- A parser-profile version can still reparse the latest unchanged source.
- Abrupt-shutdown and transport-failure recovery never loses its input.
- Historical metadata and the "what changed?" diff record remain queryable after
  raw JSON is pruned.
- Whole-lineage retention waits until diff dispatch records durable completion;
  this ADR does not silently delete an undispatched calendar change.

---

## ADR-045: Bootstrap administration with one Google SuperAdmin identity

**Status:** Accepted, amended 2026-07-23 with the address and the role column
**Date:** 2026-07-23
**Implements:** not yet; blocked only on Google sign-in existing
**Amends:** ADR-032, ADR-034 and ADR-042 operator identity follow-up

### Context

The first release has one operator. A general role-management UI and multi-role
RBAC model would add work that has no current user. The shared administrative
key is not an acceptable identity once Calendar dispatch exists.

### Decision

After Google sign-in is implemented, the backend will recognize exactly one
normalized, Google-verified email address as `SuperAdmin`. The address is a
backend-owned literal bootstrap allowlist, not a role claimed by the browser.
No role editor, delegated administration or general RBAC schema is added in the
first release.

Every approval, held-diff release and freeze/unfreeze transition derives its
actor from that authenticated Google identity. The existing shared key is then
removed.

### Consequences

- The first administration surface stays small while authorization remains
  server-side.
- The exact Google email must be supplied before implementation; it is not
  inferred from Git configuration, a service account or a client request.
- Adding a second operator or another role is a later explicit decision and data
  migration, not a comma-separated client setting.

### Amendment 2026-07-23: the address, and the users table carries a role

**Status:** Accepted

The bootstrap address is `halil.semih.sen@gmail.com`, normalized and required to
be Google-verified. It is a backend-owned literal, not configuration a client can
influence.

This is explicitly not a permanently single-operator system. The `users` table
will carry an explicit `role` column from the moment it is created, so the
bootstrap allowlist grants that role rather than standing in for one. The literal
is then the seed for the first row, not the authorization mechanism, and a second
operator becomes a data change instead of a code change.

What stays deferred is only the role *editor* and general RBAC: no delegated
administration, no permission matrix, no role-management UI in the first release.

**Consequences**

- `ApprovedBy`, `ReleasedBy` and every freeze transition can record a verified
  identity as soon as sign-in exists; until then they remain claims guarded by
  the shared key.
- The identity work is no longer blocked on a decision. It is blocked on Google
  sign-in, which is Phase 2 work.

---

## ADR-046: Publish holidays and semester breaks as all-day events

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** parser contract, both annual profiles, canonical domain model,
persistence, revision validation and semantic diff, 2026-07-23. See the
implementation note below for the four questions the sources answered.

### Context

Twenty-two annual-program rows state holidays or semester breaks with dates but
no times. Inventing a start or end time would violate ADR-011 and omitting them
leaves student calendars knowingly incomplete.

### Decision

The canonical schedule model will represent a schedule item as either timed or
all-day. A holiday or semester-break row with an explicit date becomes an
all-day canonical event; no synthetic time is assigned. Multi-day closures use
an inclusive local start date and an exclusive local end date when sent to
Google Calendar.

### Consequences

- Canonical contracts, persistence, validation, stable/content hashing and
  Calendar mapping must support all-day items before these rows publish.
- All-day/timed shape changes are content changes and remain deterministic.
- A row without an explicit date still does not publish.

### Implementation note 2026-07-23

Implemented through the parser contract, both annual profiles, the canonical
domain model, persistence, revision validation and the semantic diff. 22 Turkish
and 11 English rows now publish; `rows.ignored.noScheduledTimeAndNoClosure` is
zero for both sources, so every untimed dated row they contain is accounted for
as a closure.

Four decisions the ADR left open were settled by reading the sources:

1. **No span field.** The ADR anticipated an inclusive start and exclusive end
   date. The sources do not state a range: a closure is written as one row per
   closed day, and the ten `YARIYIL TATİL` rows skip 31 January and 1 February
   because the weekend is not a teaching day. A stored span would therefore have
   to either invent those two days or be populated from nothing. An all-day
   canonical record covers exactly one local date, and the exclusive end date
   Google Calendar wants is `LocalDate + 1`, computed by the calendar adapter.
   Consecutive rows are deliberately not merged: merging is an inference, and it
   would cover days the source excluded.
2. **The shape decides, not the title.** A row becomes all-day only when it states
   a date, a title naming a closure, and *no times at all*. The title alone is not
   enough, and the sources prove why: `CUMHURİYET BAYRAMI AREFESİ` is a timed
   three-hour session, and the English workbook writes its own semester break as
   eleven timed 08:30–16:20 rows. Those are published as the source states them.
   A dated row with no times whose title names no closure stays unpublished with a
   warning naming the cell, because a lesson whose times the faculty forgot must
   not become an all-day block on every student's calendar.
3. **The closure vocabulary is `tatil`, `bayram`, `holiday` and the phrase
   `labor day`.** The first three are matched as whole-word prefixes, which covers
   `TATİL`/`TATİLİ` and `BAYRAM`/`BAYRAMI`. `LABOR DAY` states no closure word at
   all; it is included because the English workbook puts it on 1 May, the same
   date the Turkish workbook calls `İŞÇİ BAYRAMI`, so the two sources identify it
   between them. Any other wording is refused and reported, and the list grows
   from that evidence rather than from knowing the Turkish calendar.
4. **A closure is `other`, decided by shape.** Its event type follows from being
   all-day rather than from its keywords, so the keyword classifier is not widened
   and no timed lesson is reclassified by this change.

Consequences that fell out of the implementation:

- `IsAllDay` and the two nullable times are one invariant, enforced in the Pydantic
  contract, the domain constructor and a database check constraint. Every branch of
  the constraint tests nullness explicitly, because a check constraint passes on
  NULL and a bare `"EndLocalTime" > "StartLocalTime"` would have let a timed record
  with no times through the one gate meant to catch it.
- Revision validation excludes all-day records from the lesson-duration and
  overlap rules. Measuring a closure as zero minutes would have quarantined every
  revision that published one, and treating it as booking its whole date would
  have reported the teaching around a half-day holiday as a clash.
- Secondary matching cannot pair an all-day record with a timed one: the shape
  joins the structural context, and matching also demands an instructor, which no
  closure states. A closure that gains times is a delete and a create, which is
  correct — the calendar cannot patch an all-day event into a timed one.
- The identity component that normally carries the start time holds the literal
  `allDay` for a closure, because an identity component may not be empty. Timed
  identities are byte-identical to before.
- Every content hash moved again, because `isAllDay` is now part of it for both
  profiles. Adding the key only to all-day records would have avoided the churn but
  would have made the hash schema depend on the record's shape, which is how a
  field silently stops being covered. Taken now for the same reason as ADR-047: no
  student calendar exists yet.
- The migration's `Down` refuses while all-day records exist. The old schema cannot
  represent one: making the time columns required again would have to invent
  midnight, and deleting the rows would discard published schedule data a diff may
  cite. The guard raises a message naming the count instead.
- Known asymmetry, left as the sources state it: the Turkish source publishes the
  semester break as ten all-day items and the English source publishes the same
  break as eleven timed lessons labelled `theory`. Correcting that belongs to the
  source or to a separate event-type decision, not to this one.

---

## ADR-047: Curriculum block is canonical when the source states it

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** parser contract, canonical domain model, persistence and both
implemented Grade 1 profiles, 2026-07-23. The annual profiles read it from the
same cell as the department, so it shipped together with ADR-049.

### Context

Annual and practice sources name curriculum blocks ("dilim"), such as `Hücre`,
`Doku`, `Solunum` and `Dolaşım`. The parser currently keeps that value only in
evidence and the content hash, so downstream product code cannot query or show
it as a first-class schedule attribute.

### Decision

Add nullable `CurriculumBlock` to the canonical parser contract, domain model
and persistence. Populate it only when the source explicitly states the block.
It is lesson content and provenance, not an audience selector and not inferred
from the course title.

### Consequences

- A corrected curriculum-block label updates the existing logical lesson rather
  than changing its stable identity.
- Profiles that do not state a block keep it null.
- Parser fixtures and migrations must add the field before Calendar
  synchronization consumes it.

### Implementation note 2026-07-23

The annual profiles read the block from the first slash-separated segment of the
`DİLİM ADI / ANABİLİM DALI` cell, verbatim, including compound values the source
writes such as `DOKU + HAREKET-1 DİLİMİ`. The practice profile reads it from the
merged block heading above each rotation table. 611 of 901 Turkish and 547 of 953
English annual candidates state one.

Replacing the raw block cell in the content hash with the split fields changed
every content hash. That was done deliberately now, while no student calendar
exists: after launch the same change would patch every managed event for every
user.

---

## ADR-048: Derive supported profile selectors from academic-year fixtures

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** partially in the source catalog
**Amended by:** ADR-084, which verifies the Grade 2 English practice fixture's
content as 2025-2026 despite its 2024-2025 filename and confirms `İ1`/`İ2`

### Context

Exact class/language group combinations were left open even though the committed
faculty workbooks contain the values. Guessing them is unsafe, but fixture
inspection can make the server-owned selector schema evidence-based.

### Decision

For each academic year, derive the supported selector matrix from the committed
or captured source fixture and record it in the source catalog with a fixture
test. Values from an older academic year are evidence for parser design but do
not become the current year's validation allowlist without a current capture.

The currently confirmed matrix is:

- Grade 1 Turkish practice: groups `A`-`H`, subgroups `A1`-`H2`
- Grade 1 English practice: group `İ`, subgroups `İ1`, `İ2`, `İ3`
- Grade 2 Turkish practice: groups `A`-`H`, no subgroup values in the fixture
- Grade 1 and Grade 2 anatomy: independent groups `1`, `2`, `3`
- Grade 3 Turkish curriculum sources: curriculum groups `A` and `B`

The available Grade 2 English fixture states `İ1` and `İ2` but belongs to
2024-2025, so those values remain provisional until a current fixture is
captured.

### Consequences

- Unknown-selector validation can be enabled source by source as evidence is
  confirmed.
- A new cohort in a future workbook is held for review rather than silently
  accepted.
- Fixture refresh is part of each academic-year rollover.

---

## ADR-049: A department is read only from an explicit marker, and all of them are kept

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** shared parser primitive, parser contract, canonical domain model,
persistence and both implemented Grade 1 profiles
**Relates to:** ADR-035 as amended, ADR-047

### Context

`Department` existed on the canonical record and was a precondition for ADR-035
secondary matching, but nothing could ever populate it: the parser contract had
no such field, so every record ever produced had it null. The whole
secondary-matching path was therefore unreachable in production while passing its
own unit tests, which construct records directly.

The sources do state the department. The Grade 1 Turkish annual header is
`DİLİM ADI / ANABİLİM DALI` — block name, then owning department — and the
English workbook uses the same convention under a generic `Description` header
with Turkish department names.

Splitting that cell on the slash would fabricate departments. The sources also
write a second curriculum block, a nested block name or a whole faculty after it:
`YAŞAMIN MOLEKÜLER TEMELLERİ DİLİMİ / DİKEY KORİDOR` names no department, and
`HAYATIN EVRELERİ DİLİMİ / DİŞ HEKİMLİĞİ FAKÜLTESİ` names a faculty. A naive
split would have invented a department for 29 Turkish and 10 English rows.

### Decision

A slash-separated segment becomes a department only when it carries an explicit
marker: `AD.`, `A.D.` or `ANABİLİM DALI`, compared on the folded comparison key,
optionally followed by a parenthesised sub-department. The sub-department is kept
verbatim — `İÇ HASTALIKLARI AD. (ENDOKRİNOLOJİ BD.)` is one department, because
that is what the cell states.

The first segment is the curriculum block (ADR-047), unless it carries a marker
itself, in which case the cell states a department and no block.

The canonical record carries **every** department the source names, in source
order, as a required list that is empty when none was named. An integrated
session ("entegre oturum") is taught by several and a student must see all of
them.

Within a segment that carries at least one marker, the source is enumerating
departments, so an unmarked member of that dashed list is kept too, at reduced
confidence with a confidence indicator naming the rule. That is a rule about the
list the source wrote, not an inference about the words in it. It affects two
candidates per annual source today and is what keeps `İÇ HASTALIKLARI HEMATOLOJİ`
visible in `BİYOFİZİK AD. - TIBBİ BİYOLOJİ AD. - İÇ HASTALIKLARI HEMATOLOJİ`.

A segment with no marker anywhere in it is never a department. It is counted in
`departments.ignored.unmarkedSegment` and reported once per distinct wording as
an informational finding with the address of the first cell that carries it, so
widening the rule later starts from evidence.

Departments are content, not identity: a corrected department updates the existing
logical lesson.

### Consequences

- Secondary matching can now actually use a department, and ADR-035's amended
  two-tier rule decides when it is comparable.
- A department the source states without a marker — `TIBBİ EKOLOJİ VE
  HİDROKLİMATOLOJİ` — is deliberately not published. Publishing it would make the
  rule depend on knowing Turkish faculty structure rather than on the cell.
- The practice profile states no department: its columns are practice subjects,
  and reading one as a department would be an inference.
- The single `Department` column is replaced by a `Departments` JSONB list. The
  migration carries any existing single value into the list before dropping the
  column, even though no row can hold one.
- A future profile whose source marks departments differently adds an alias to
  this rule with its fixture, and does not weaken the marker requirement.

---

## ADR-050: A parse run left running past a timeout is recovered, not abandoned

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** domain boundary, persistence path, worker logging and
configuration

### Context

A parse run is keyed by snapshot, parser profile and profile version, and
`Resume` accepted only a `Failed` run. A worker killed mid-parse — a deploy, a
crash, a lost host — therefore left the run `Running` forever with no lease,
heartbeat or timeout anywhere. Every later cycle reported `ParseAlreadyRunning`
for that snapshot and did nothing.

Because the key includes the snapshot, the source recovered only when its content
next changed. A schedule change captured in the wedged snapshot was silently never
published until the faculty edited the workbook again, which can be days. That
violates the rule that every background job must be safe to retry.

### Decision

A run is stale when it is `Running` and has been open at least
`SIRKADIYEN_PARSER__STALE_RUN_TIMEOUT`, default 30 minutes. A stale run is
recovered in place: same run, new correlation ID, `AttemptCount` incremented, and
`LastStaleRecoveryAtUtc` recorded and never cleared by a later resume.

Recovery reuses the run rather than creating a second one. The run is the logical
execution of one profile version against one snapshot, and a duplicate would break
that uniqueness and could produce a second revision for the same snapshot.

The timeout is the caller's policy, passed into the store, not a constant inside
it. It must stay well above the parser transport timeout.

Recovery is not silent: the poll result carries `ParseRunStartKind.RecoveredStale`
and the worker logs a warning, because a recovered run says something about the
host rather than about the source.

### Consequences

- A killed worker costs one cycle, not an unbounded wait for the source to change.
- If the original worker was merely slow rather than dead and answers after
  recovery, its response is rejected because the correlation ID no longer matches
  the run. No duplicate revision can be created; the attempt is wasted and looks
  like a transport fault, which is why the default timeout leaves a wide margin.
- Two workers recovering the same run at once may both increment the attempt
  count and both call the parser. Only one response can complete the run. There
  is no row version on `parse_runs` yet; the worker processes sources sequentially
  and avoids overlapping cycles, so this needs a second worker instance to occur.
- A run that failed and was recorded as failed keeps using the existing resume
  path, and is never treated as stale.

---

## ADR-051: A parser profile declares the component order of a numeric date

**Status:** Accepted
**Date:** 2026-07-23
**Implements:** `normalization/dates.py`, the profile registry, both implemented
profiles, `GET /v1/profiles`

### Context

The shared date resolver read every numeric date as day-first. The assumption was
never declared anywhere and no source had been shown to need it. It is the one
resolver rule that can be wrong without producing any evidence: `25/12/2026` has
a single possible meaning, but `05/06/2026` is a real date under either reading,
so a month-first source would publish lessons a few months from where they belong
on every date whose components are both twelve or lower, and refuse nothing.

Every other reading rule in the resolver already fails loudly. A bare serial, a
missing year and a compact time all stay unresolved unless the caller opts in
(ADR-011). Numeric order was the exception, and it was the most dangerous one.

Measured evidence: no numeric date exists in any committed fixture. The two annual
sources and the practice source write dates as spreadsheet serials or as text
naming the month — 896 serial and 5 month-name dates in Grade 1 Turkish annual,
953 serial in Grade 1 English annual, 60 serial and 100 month-name rotation rows
in Grade 1 Turkish practice. The day-first branch was dead code against every
source we hold, which is why nothing had failed and why the fix is cheap now.

### Decision

`ParserProfileDefinition` carries a required `numeric_date_order`, one of
`dayFirst`, `monthFirst` or `undeclared`. The field has no default: a profile
added without considering its source's date layout must not inherit an order.

Both readings are computed before anything is decided, and what the alternative
yields is treated as evidence:

- a declared order reads as declared. A cell that only the other order can explain
  is refused as `numericDateImpossibleUnderDeclaredOrder`, because either the cell
  or the declaration is wrong and silently reordering it would hide both.
- an undeclared profile publishes a numeric date only when the order cannot change
  the answer — when just one order names a real date, or when the components are
  equal. That is arithmetic, not an inference: the result is identical under either
  declaration, so no rule is being guessed.
- an undeclared profile refuses a date that means two different things, as
  `numericDateOrderNotDeclaredByProfile`, with the row unpublished and the cell
  cited.

Every profile declares `undeclared` today, because that is what the fixtures
support. Declaring an order is a claim about a document, so it waits for a document
that shows one.

The declaration lives on the parser profile rather than in the source catalog or
`sourceContext`. It describes a document family's layout, which is exactly what a
profile is; putting it in the .NET catalog would move a spreadsheet-interpretation
rule outside the parser boundary.

`dates.rule.<rule>` is now counted per published date, so a source that changes
how it writes dates is visible in the metrics of the first parse after the change
rather than only in a reader's attention.

### Consequences

- A month-first source can no longer be silently misparsed. Its rows are refused
  with the cell address until a profile declares `monthFirst`.
- One profile serves the Turkish and English workbooks of a family, so if those
  two ever disagree on date order the family needs two profile versions. That is
  the correct pressure: the declaration is about the document.
- An undeclared profile meeting a numeric source publishes the unambiguous part of
  it and refuses the rest. The parse is `completedWithWarnings`, and the warnings
  name the cells the declaration would fix.
- Golden output for all three fixtures is unchanged except for the new metrics, so
  no content hash moved and no calendar event would be touched by this change.
- `resolve_date_text` and `resolve_cell_date` default to `undeclared`, so a future
  caller that forgets to pass the profile's declaration refuses ambiguity rather
  than inheriting day-first.

---

## ADR-052: Exchange a verified Google ID credential for a local cookie session

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** user domain/persistence, Google sign-in API, secure cookie,
anti-forgery protection and SuperAdmin authorization
**Relates to:** ADR-003, ADR-023, ADR-045

### Context

ADR-003 selected Google-only authentication and ADR-023 selected a
backend-managed cookie, but the browser-to-backend Google exchange was still
open. A redirect handler would work, but the planned frontend already owns the
Google sign-in entry point and Google Identity Services can return a short-lived
ID credential without granting Calendar or other Google API access.

Sign-in identity must also remain separate from the later incremental Calendar
authorization. Persisting an access or refresh token during login would couple
the two and broaden the first consent unnecessarily.

### Decision

The frontend obtains a Google Identity Services ID credential and posts it once
to the same-site API. The request is anti-forgery protected. The backend uses
`Google.Apis.Auth` to validate the Google signature, issuer, configured client-ID
audience, expiry and `email_verified`. It persists only the immutable `sub`,
verified email/profile fields and the local role, then discards the credential
and issues the ADR-023 application cookie.

The browser client ID is required as
`SIRKADIYEN_GOOGLE__AUTH_CLIENT_ID`. It is intentionally separate from
unattended source-access credentials. Google Calendar scopes and refresh-token
storage remain a later, separate authorization flow.

The session cookie is `HttpOnly`, `Secure`, `SameSite=Lax`, host-prefixed and has
an eight-hour sliding expiry. State-changing browser endpoints carry explicit
anti-forgery metadata. The API reloads the local user for every authenticated
request; a missing user invalidates the session and changed role/profile claims
renew it. Google sign-in is rate-limited per remote address; a proxy deployment
must configure trusted forwarded headers before relying on that address as the
internet client.

The `users` table gives Google subject and normalized email independent unique
constraints. A second subject with the same email is rejected rather than
silently linked. Concurrent first callbacks for the same subject converge on the
unique winner and retry as an update.

### Consequences

- Normal sign-in grants no Calendar access and stores no Google credential.
- Same-site HTTPS is the supported browser topology for this foundation. A later
  cross-site frontend deployment must explicitly revisit CORS, credentialed
  requests and SameSite policy rather than weakening cookies implicitly.
- The frontend must first fetch `/api/auth/csrf`, then send its request token with
  the Google credential.
- Revoking Google API access will not terminate a local session; account/session
  revocation is a separate local concern to add with licensing and suspension.
- A containerized or multi-instance deployment must configure a shared persistent
  ASP.NET Core Data Protection key ring; the host default does not guarantee
  cookie continuity across restarts or instances.
- The old shared admin key is removed. Revision approval and diff release now
  require the persisted `SuperAdmin` role and derive their actor from the
  verified session.

---

## ADR-053: Store keyed license hashes and derive activation onboarding

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** license domain/persistence, administration and redemption APIs,
rate limiting, audits, onboarding state and PostgreSQL concurrency tests
**Relates to:** ADR-004, ADR-022, ADR-045, ADR-052

### Context

The product requires administrator-issued, single-use activation codes. A code
must be looked up during redemption without retaining plaintext, while ordinary
fast hashes of a human-entered value allow cheap offline guessing after a
database leak. The same user can also submit two codes concurrently, which a
lock on either individual code does not serialize.

Onboarding must resume from backend facts. At this stage licensing exists but
student profiles and Calendar authorization do not, so persisting a client-set
workflow state would invent authority the later modules need to own.

### Decision

Generate 100 random payload bits in a display code prefixed `SIRK`. Return its
plaintext only in the successful creation response. Store a deterministic
HMAC-SHA256 lookup hash keyed by the required Base64
`SIRKADIYEN_LICENSING__HASH_KEY`, never the code itself.

Licenses use explicit `Active`, `Redeemed`, `Revoked`, and `Expired` states. An
optional expiration is an unused-code redemption deadline; it does not turn a
completed activation off later. Redemption locks the license row. A partial
unique index on `RedeemedByUserId` where status is `Redeemed` also permits at
most one current activation per user, so two different codes cannot both win a
same-user race. Revoked history leaves that index and remains auditable.

Creation, redemption, expiration discovered during redemption, and revocation
append an audit row in the same transaction. Redemption is idempotent only for
the winning user and code. The public failure does not distinguish unknown,
expired, revoked, or another user's code and is rate-limited by authenticated
user plus remote address.

Derive onboarding as follows until later authoritative records exist:

```text
no redeemed license        -> LicenseRequired
redeemed license           -> ProfileRequired
redeemed then revoked      -> Suspended
```

### Consequences

- Two users racing one code and one user racing two codes each produce exactly
  one winner under real PostgreSQL.
- Rotating the HMAC key invalidates lookup for every unredeemed code. Key
  rotation is therefore an explicit invalidate-and-reissue operation.
- A revoked user may later receive a new license without deleting historical
  redemption and revocation records.
- Student profile, Calendar permission and sync modules extend onboarding from
  their own records; the browser never submits a trusted onboarding state.

---

## ADR-054: Generate short human-friendly codes and support manual activation

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Amends:** ADR-004 and the generated-code format in ADR-053
**Implements:** compact code generation, legacy-code redemption, explicit
license kind, audited SuperAdmin manual activation, additive migration and
PostgreSQL concurrency tests

### Context

The first generated format, `SIRK-XXXXX-XXXXX-XXXXX-XXXXX`, carried 100 random
bits but was unnecessarily long for the actual distribution channel. Codes are
sent to students through WhatsApp, where quick copying, reading and occasional
manual typing matter more than carrying entropy far beyond the online attack
surface.

The future administration panel also needs to activate a user directly. Creating
an undisclosed fake code for that action would make the record lie about how
activation happened and would leave an unredeemable hash with no operational
meaning.

### Decision

Generate new codes as `SRK-XXXXX-XXXXX`. The ten random characters use the
32-character alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, omitting `I`, `O`,
`0`, and `1`. This carries 50 random bits. HMAC lookup, the five-per-minute
user/address redemption limit, unique hash constraint, and collision retry from
ADR-053 remain unchanged. Long `SIRK` codes already generated remain redeemable;
only generation changes.

Add explicit `LicenseKind` values `Code` and `Manual`. A `Code` license requires
a 32-byte keyed hash. A `Manual` license requires no hash, starts in `Redeemed`,
names the target user, and writes one `ManuallyActivated` audit with the
SuperAdmin actor and mandatory reason.

Manual activation and code redemption use the same partial unique index allowing
only one current `Redeemed` license per user. The manual endpoint is idempotent,
CSRF-protected, and restricted to `SuperAdmin`.

### Consequences

- A code is short enough to share and type comfortably in WhatsApp while online
  brute-force remains impractical under the keyed hash and rate limit.
- At one million generated codes, the approximate probability of at least one
  random collision is 0.044 percent; the unique index and generation retry
  resolve one without exposing the discarded plaintext.
- The admin panel can display whether activation came from a code or a manual
  decision and can show the responsible actor and reason.
- Migration rollback is refused while `Manual` rows exist, because the older
  schema cannot represent an activation without inventing a code hash.

---

## ADR-055: The student profile and its server-owned supported schema

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** the `StudentProfile` aggregate, a code-defined supported-profile
schema and validator, transactional upsert persistence, onboarding advancement to
`CalendarAuthorizationRequired`, the profile API, and unit/PostgreSQL tests
**Depends on:** ADR-027 (validated JSONB selectors), ADR-048 (evidence-based
selector matrix), ADR-052/053 (identity and derived onboarding)

### Context

An activated account could not progress past `ProfileRequired`: no profile module
existed, so audience resolution had nothing to match a schedule change against and
the pipeline had no one to synchronize to. A profile has to record the academic
year, class year and program language, plus variable cohort selectors that differ
by class year, and it must never trust a client's claim about which cohort exists.

Two questions had to be answered. Where does the allowlist of valid cohorts live,
and how are the group/subgroup dependency and the required/optional distinction
expressed — neither of which the source catalog's flat
`supportedAudienceSelectors` map carries.

### Decision

Model a `StudentProfile` aggregate with relational `AcademicYear`, `ClassYear`
and `ProgramLanguage`, and a schema-versioned JSONB selector document keyed by
dimension, each holding the single value the student belongs to (systemPatterns
§22). The aggregate enforces only structural bounds; it never carries the
allowlist, which changes at year rollover.

Define the supported-profile schema as **server-owned code**, not a runtime config
file and not a projection of the source catalog. It covers exactly **one current
academic year** because there is one at a time, and each `(classYear,
programLanguage)` program lists selector dimensions. A dimension is either
independent, with an explicit value list, or dependent, naming a parent and the
child values allowed per parent value (`practiceSubgroup A1` is valid only under
`practiceGroup A`). Only cohorts a committed current-year fixture confirms appear;
Grade 1 anatomy, Grade 2 and Grade 3 selectors are deliberately absent until their
sources are captured (ADR-048). A unit test cross-checks every allowed value
against the catalog's declared selectors, so the schema cannot drift from the
evidence even though it is defined separately.

One validator serves the profile write and, later, audience matching, so a stored
selector is always one the sources publish. The profile write path first requires
an active license (`ActivationRequired` otherwise), enforcing the onboarding order
in the backend rather than the UI. Onboarding now derives
`CalendarAuthorizationRequired` when a profile row exists and `ProfileRequired`
when it does not, so an interrupted onboarding resumes at the right step.

Persistence is a transactional upsert against a `UserId`-unique row; a concurrent
first-time save reruns once as an update rather than failing.

### Consequences

- The supported matrix changes only at year rollover, which is a deployment
  anyway, so code with a fixture-backed test is simpler and safer than a second
  runtime config loader in the API host.
- The frontend renders its form from `GET /api/profile/options`, which returns the
  schema including the parent-keyed child values for dependent dropdowns.
- The one-level dependency model does not express a deeper chain; a future
  rotation-within-subgroup requirement would extend it, and a test asserts the
  current depth so the assumption is visible.
- A profile stored under one schema version is not re-validated when the schema
  changes; re-validation on rollover is future work, tracked as a known risk.

## ADR-056: Store the university student number with layered semantic validation

**Status:** Accepted and implemented
**Date:** 2026-07-23
**Implements:** a `StudentNumber` field on the `StudentProfile` aggregate, its
format and cross-validation in `StudentProfileValidator`, the EF column and format
check constraint, migration `AddStudentNumberToProfile`, the read/write DTO fields,
and domain/validator/store tests
**Depends on:** ADR-055 (the student profile and its validator)

### Context

The profile must record the student's university number (Öğrenci Numarası) for
future integrations. The number is not an opaque identifier: its ten digits encode
a faculty code (1–2), a program-language code (3–4), an entry year (5–6) and an
entry sequence (7–10), which lets it cross-validate against the profile the student
is submitting. It is stored as text because the leading zeros are significant and
must never be truncated.

### Decision

Enforce the number in three layers, each owning the rule that belongs to it.

- **Domain** (`StudentProfile`) guards only the structural invariant: a trimmed,
  exactly-ten-character, all-ASCII-digit string. The aggregate can therefore never
  hold a structurally corrupt number, and leading zeros survive because it is text.
- **Application** (`StudentProfileValidator`) owns the semantic cross-validation,
  because these rules depend on business scope and on the row's own program
  language. Digits 1–2 must be `01` (Istanbul Medical Faculty — the project's
  scope); digits 3–4 must match the selected program (`01` Turkish, `02` English).
  Format is re-checked here too so a malformed number returns a clean validation
  error (400) rather than a domain throw, and the digit-slice rules are skipped once
  the number is known to be malformed. New error codes: `InvalidStudentNumber`,
  `StudentNumberFacultyMismatch`, `StudentNumberProgramMismatch`.
- **Database** pins the same structural invariant as a check constraint
  (`"StudentNumber" ~ '^[0-9]{10}$'`), defence in depth. The semantic faculty and
  language rules stay out of the database because a check constraint cannot read the
  row's program language in a way that keeps the two codes in sync as policy evolves.

The migration adds the column as `NOT NULL` with **no** server default: the profile
table is empty at this migration, and an empty string could never satisfy the
format constraint, so a default would be both useless and a latent trap. Verified
Up → Down → Up against real PostgreSQL on an empty table.

### Consequences

- The faculty scope (`01`) and the language-code map are constants in the validator;
  widening scope to another faculty, or adding a program, is a code change with a
  test, consistent with the server-owned schema of ADR-055.
- The entry-year digits (5–6) are **not** cross-checked against the academic year,
  and the number is **not** enforced unique. Both are deliberate omissions recorded
  as known risks; a repeat student or a legitimately shared-prefix cohort must not
  be rejected by a rule we have not confirmed.

---

## ADR-057: Grant Calendar access as a separate, minimally scoped offline authorization

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the `GoogleCalendarConnection` aggregate, encrypted refresh-token
storage, the authorization-code exchange and its client abstraction, the Calendar
authorization API, onboarding advancement to `ReadyForInitialSync`, migration
`AddGoogleCalendarConnections`, and unit/PostgreSQL tests
**Depends on:** ADR-003 (Google-only authentication), ADR-024 (one dedicated calendar
per user), ADR-052 (verified credential for a local cookie session)

### Context

ADR-052 deliberately left sign-in free of any Calendar scope or stored Google
credential, noting that Calendar scopes and refresh-token storage were a later,
separate authorization flow. That flow did not exist, so an activated student with a
profile stopped at `CalendarAuthorizationRequired` and nothing could ever be
synchronized. Unattended synchronization needs a long-lived refresh token, which is a
credential the product must hold and protect.

### Decision

Keep the Calendar grant a **separate, second consent**, and model it as its own
`GoogleCalendarConnection` aggregate rather than as fields on the user.

**Flow.** The frontend obtains a one-time authorization code with offline access and
posts it to a same-site, anti-forgery protected `POST /api/calendar/authorization`; the
backend performs the code exchange because it carries the client secret. This mirrors
ADR-052's shape — the frontend owns the Google entry point, the backend does the
sensitive part — so there is no redirect handler and no server-side redirect state. The
exchange `redirect_uri` is configurable and defaults to `postmessage`, the value Google
requires for the browser popup code flow.

**Scope.** Request only
`https://www.googleapis.com/auth/calendar.app.created`, which grants access solely to
calendars this application itself creates. That is exactly ADR-024's dedicated-calendar
model and structurally cannot reach the user's primary calendar. Google reports what was
actually granted, and a user can clear the permission while still completing consent, so
the service verifies the required scope is present and refuses the grant otherwise
rather than storing an authorization that cannot synchronize.

**Credential at rest.** The refresh token is encrypted with ASP.NET Core Data
Protection under a dedicated purpose string. The domain never sees plaintext: the
application layer protects the value through an `ICalendarTokenProtector` abstraction
before the aggregate is constructed, so the aggregate stores opaque ciphertext and the
domain keeps no cryptographic dependency. The read projection deliberately omits the
credential entirely, so no API response can carry it by accident.

**Ordering.** Authorization requires an active license *and* an existing profile,
enforced in the backend exactly as the profile write requires an active license. The
code is not even sent to Google when the account may not connect.

**Boundary.** This is authorization only. Creating the dedicated calendar remains part
of initial sync (ADR-024); the aggregate reserves a nullable `ManagedCalendarId` and a
re-authorization deliberately preserves it, so re-granting access never orphans the
calendar the user's events already live in.

### Consequences

- Onboarding now derives `ReadyForInitialSync` once an authorized connection exists, and
  a connection marked `NeedsReauthorization` does not count as authorized, so a revoked
  grant returns the user to consent instead of stalling in a state that cannot sync.
- The Calendar client is configured separately from both the public sign-in client and
  the unattended source credential, because it is the only one of the three that is a
  confidential client acting for a signed-in student.
- A multi-instance or containerized deployment **must** configure a shared, persistent
  Data Protection key ring. Without one, a host restart makes every stored token
  undecryptable and forces all users to authorize again. The Worker will need the same
  key ring when synchronization consumes the token.
- `Microsoft.AspNetCore.DataProtection` is pinned to the patched servicing release;
  10.0.0 carries a critical advisory and the build audits packages as errors.
- The live consent and exchange cannot be exercised without a registered Web OAuth
  client, so the exchange is abstracted behind an interface and the service is tested
  against a fake. The real client is not covered by automated tests.
- Disconnect, token refresh, and reacting to Google-side revocation are not implemented;
  they belong with synchronization.

## ADR-058: Populate each user's dedicated calendar with a worker-driven, idempotent initial sync

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the connection initial-sync lifecycle (`InitialSyncState`), the
`UserCalendarEventMapping` aggregate, the pure `CalendarAudienceResolver` and
`ManagedCalendarEventFactory`, the `InitialCalendarSyncService` and its resumable worker
stage, the `IUserCalendarClient` abstraction with a real `Google.Apis.Calendar.v3` client,
the `POST`/`GET /api/calendar/sync` endpoints, the shared Data Protection key ring, migration
`AddInitialCalendarSync`, and unit/PostgreSQL tests
**Depends on:** ADR-024 (one dedicated calendar per user), ADR-034/043 (global operational
freeze), ADR-055 (student profile), ADR-057 (Calendar authorization and encrypted refresh
token)

### Context

An activated student with a profile and an authorized `GoogleCalendarConnection` stopped at
`ReadyForInitialSync`. Nothing created their dedicated calendar or wrote any events, so the
whole pipeline — polling to a stored diff — still reached no calendar. Populating a calendar
means creating it (ADR-024), resolving which currently-published events belong to that
student, and writing them safely enough to survive crashes, retries and Google quota.

### Decision

Implement **per-user initial sync** as the first synchronization slice; diff-driven
incremental sync (patch/delete, reconciliation, quota batching) is deferred, but the mapping
and idempotency below are built for it.

**Worker-driven and state-machine-resumable.** The API only records intent:
`POST /api/calendar/sync` moves the connection's `InitialSyncState` `Pending → InProgress`.
The worker does the slow, quota-bound work across cycles, driven by connection state exactly
like the publication and diff stages, so a killed worker resumes from what is not yet mapped.
`InitialSyncState` (`Pending`/`InProgress`/`Completed`) lives on the connection, orthogonal to
the authorization `Status`; a re-authorization preserves both `ManagedCalendarId` and the sync
progress. A separate sync-job aggregate is deliberately avoided for this slice.

**Idempotency by a deterministic event id plus a durable ledger.** Each event is inserted
with a client-chosen id `base32hex(sha256(userId ‖ stableIdentity))`, whose alphabet is
exactly Google's allowed id set, so a re-insert of the same id returns 409 and is treated as
success. A `UserCalendarEventMapping` row — unique on `(UserId, StableIdentity)`, keyed by the
identity that survives room and instructor changes (ADR-018) — is the ledger of what has been
written; the worker inserts only records with no mapping. Together they survive a crash
between the Google write and the local commit without creating a duplicate.

**Affected-events resolution is a pure function.** A published record applies to a student
when academic year, class year and program language match and either it is program-wide, or it
is cohort-scoped and at least one of its `{Dimension,Value}` audience selectors equals one of
the profile's selectors; a cancelled record never applies. The set is filtered in memory over a
"current published records for this program" query (the records of each source's `Published`
revision), because the per-class-year volume is bounded and the rule is worth unit-testing
without a database.

**Safety and secrecy.** Initial sync reads the global operational freeze and does nothing
while frozen (ADR-034/043), like every other calendar job. The refresh token is decrypted only
in memory for the duration of a sync call, through the same `ICalendarTokenProtector`.

**Shared Data Protection key ring.** The worker must decrypt what the API encrypted, but Data
Protection isolates key rings by application name (defaulted to the content root) and a generic
host configures none. Both hosts now call `AddSirkadiyenDataProtection`, pinning one
application name and a configurable file-system key location.

### Consequences

- Onboarding derives `InitialSyncInProgress` while the worker runs and `Active` once every
  applicable event is written; a large first load spans several cycles under a per-user,
  per-cycle event budget, so the state is a real progress signal, not instantaneous.
- The credential-bearing pending-sync projection is separate from the read `View`, which still
  omits the token, so only the backend sync path ever sees ciphertext and nothing else can leak
  it.
- Calendar **creation** is the one non-idempotent Google step. The id is persisted immediately
  after creation to shrink the window, but a crash in that window could orphan a calendar and
  create a second on resume; a marker-tagged lookup is deferred with reconciliation.
- The real `Google.Apis.Calendar.v3` client (`1.75.0.4206`, audit-clean) is not covered by
  automated tests; only the fake `IUserCalendarClient` is exercised, so the first live sync is
  the first real exercise of that path.
- A single-host deployment shares the key ring correctly; multi-instance production still needs
  genuinely shared, backed-up key storage (carried forward from ADR-052/057).
- Migration `AddInitialCalendarSync` defaults `InitialSyncState` to `Pending` rather than EF's
  empty string, which would violate the state check constraint.
- `IGoogleCalendarConnectionStore.IsAuthorizedForUserAsync` was removed: onboarding reasons
  over the connection view's `Status` and `InitialSyncState`, so the boolean query was
  redundant.

## ADR-059: Fan each dispatchable diff out onto calendars with a resumable, idempotent dispatcher

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the `ScheduleDiff` dispatch lifecycle (`CalendarDispatchState`, `DispatchAttempts`,
`NextAttemptAtUtc`, `DispatchedAtUtc`, `DispatchFailureReason`, `MarkDispatched`,
`RecordDispatchFailure`), `GoogleCalendarConnection.MarkNeedsReauthorization`, the pure
`IncrementalSyncPlanner`, the `IncrementalCalendarSyncService` and its resumable worker stage, the
reverse-lookup / update / remove mapping-store methods, `ICanonicalScheduleReadStore.ListRecordsByIds`,
the new `ICalendarSyncTargetReadStore`, `IUserCalendarClient` patch/delete plus the transient/credential
exception taxonomy and the real client's bounded back-off, migration `AddCalendarDispatch`, and
unit/PostgreSQL tests
**Depends on:** ADR-018 (identity vs content), ADR-033/042 (forward-fix, diff hold/release), ADR-034/043
(operational freeze), ADR-058 (initial sync, the mapping ledger and idempotency this reuses)

### Context

After initial sync marks a connection `Completed`, nothing kept that student's calendar in step with
the schedule. The worker already calculated a `ScheduleDiff` (`Ready`/`Released`) per republication, but
the diff was only stored and logged — it never reached a calendar. A lesson that moved, was cancelled, or
was added silently never updated for a synchronized student. AI_GUIDELINE §13 makes the diff the sole
authority for deletion ("a published revision and a valid semantic diff"), so the fix must be
diff-driven, not a level-triggered reconcile against current truth.

### Decision

Implement **diff-driven incremental sync**: a worker stage that dispatches each dispatchable diff into
per-user insert/patch/delete operations. Scope is the core plus a failure/backoff taxonomy; a
reconciliation sweep and global quota-aware batching are deferred.

**The diff carries its own dispatch lifecycle; no separate sync-job aggregate.** Mirroring ADR-058, the
`ScheduleDiff` gains `CalendarDispatchState` (`Pending → Dispatched`, terminal `Failed`) plus retry
fields. A diff is eligible when it is `IsDispatchable` (Ready/Released), still `Pending`, and past any
back-off time. This is "creating sync jobs from a diff" (AI_GUIDELINE §14) modelled on the existing
authoritative row rather than a construct incremental and reconcile would both have to own.

**Coarse per-diff tracking is safe because fine-grained idempotency lives in the ledger.** A diff's
fan-out over many users is not atomic. Rather than a per-`(diff,user)` table, a worker killed mid-fan-out
re-runs the whole diff; each per-user operation converges — insert → deterministic id → 409
`AlreadyExists` + ledger `AlreadyPresent`; patch → skipped when the ledger hash already equals the target
(Google patch is idempotent regardless); delete → event already gone (404/410) + mapping already removed.
The diff is marked `Dispatched` only after the fan-out completes with no transient failure.

**The mapping ledger is authoritative for who holds a lesson; audience decides only insertions.** Per
entry, using the referenced canonical record(s): a **Deleted** record → delete for every ledger holder; a
**Created**/**Updated** record → for existing holders, patch when content moved, delete when it no longer
applies (audience narrowed or the record is `Cancelled`, both of which `CalendarAudienceResolver.Applies`
already rejects), and for cohort users with no mapping, insert when it applies (audience widened). The
per-`(user, entry)` decision is the pure `IncrementalSyncPlanner`; the fan-out (which users) is store
queries.

**The initial/incremental boundary needs no special handling.** Initial sync captures current published
state and records each content hash, so re-dispatching a diff whose revision predates a user's completion
is a no-op for them. Only `Completed` users are dispatched to; users still `InProgress` reach the same
final state through initial sync.

**Failure taxonomy at two levels.** The real client classifies Google failures and retries transient ones
(429/5xx/network) with bounded exponential back-off inside the call, then raises a typed transient
exception. The service defers the diff with a growing back-off (`RecordDispatchFailure`), giving up to
`Failed` after `MaxDispatchAttempts`. A dead credential (`invalid_grant`/401/403) instead flags the
connection `NeedsReauthorization`, skips that user, and leaves their events — it does not count against
the diff or block other users.

### Consequences

- A held diff is never auto-dispatched (it is not dispatchable), so the diff safety gate (ADR-033/042)
  also bounds dispatched fan-out size: a mass deletion is held and only a named operator's release turns
  it into deletions.
- A user set `NeedsReauthorization` during a dispatch and later re-authorized does not automatically
  catch up on diffs marked `Dispatched` while they were dead — that repair is the deferred reconciliation
  sweep. Nothing here silently deletes their events.
- Fan-out over a cohort runs to completion within a cycle; only the number of diffs per cycle is bounded.
  In practice Ready diffs are small, so this holds; a large *Released* diff over a big cohort runs long
  until intra-diff quota batching lands.
- A patch that finds no event (404) re-inserts it: the ledger says the event should exist, so the calendar
  is made to match rather than left divergent (idempotent via the deterministic id).
- The real client's patch/delete, token refresh, and back-off classification remain untested without live
  Google; only the fake `IUserCalendarClient` is exercised.
- Migration `AddCalendarDispatch` defaults `CalendarDispatchState` to `Pending` rather than EF's empty
  string (which would violate the state check constraint); existing Ready/Released diffs become
  dispatch-eligible, which is correct and idempotent since no completed-sync user exists yet.
- `GoogleCalendarConnectionStatus.NeedsReauthorization` now has a producer for the first time; a
  distinct `GoogleCalendarCredentialException` was added to avoid clashing with the ADR-057
  authorization-code-exchange `GoogleCalendarAuthorizationException`.

## ADR-060: Preserve a durable semantic-diff replay cursor across Calendar re-authorization

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `GoogleCalendarConnection` reconciliation boundary/cursor state,
`PendingCalendarReconciliation`, connection-store list/advance/complete operations,
migration `AddCalendarReconciliationCursor`, the freeze-gated
`CalendarReconciliationService`, ordered dispatched-diff replay query, Worker stage, and
mocked Calendar/PostgreSQL tests
**Depends on:** ADR-033/042 (forward-fix and diff release), ADR-034/043 (global freeze),
ADR-058 (completed initial sync and managed calendar), ADR-059 (credential failure and
diff dispatch lifecycle)

### Context

ADR-059 deliberately lets one dead credential be skipped so it cannot block a global
diff fan-out. The diff is still marked `Dispatched`; after that user authorizes again,
the normal edge-triggered dispatcher will never revisit the missed diff. Rebuilding
state by comparing only the current schedule to the mapping ledger would make deletion
level-triggered and violate AI_GUIDELINE section 13, which requires a published revision
and valid semantic diff.

### Decision

Start reconciliation with a **durable per-connection missed-diff boundary and ordered
cursor**. On the first credential failure after initial sync has completed,
`GoogleCalendarConnection.MarkNeedsReauthorization` stores:

```text
ReconciliationRequiredSinceUtc = failure time
ReconciliationCursorDispatchedAtUtc = failure time
ReconciliationCursorDiffId = Guid.Empty
```

Repeated failures do not move the boundary forward. Re-authorization replaces the
credential and restores `Authorized` while preserving the tuple. A credential failure
before initial sync completes creates no reconciliation request, because the resumable
initial sync remains the authoritative route to current state.

The worker-facing queue returns only connections that are `Authorized`, initial-sync
`Completed`, attached to a managed calendar, and have a complete cursor tuple. Replay
ordering is `(DispatchedAtUtc, DiffId)` because one dispatcher pass can give several
diffs the same timestamp. Cursor advancement is monotonic. Both advancement and
completion require the original `ReconciliationRequiredSinceUtc` as an optimistic
workflow token, so stale work cannot mutate a newer request.

The replay worker will consume only `Ready`/`Released` diffs whose
`CalendarDispatchState` is `Dispatched`, after the cursor. It will apply those entries
for the one user using the same mapping ledger and idempotent Calendar operations as
incremental sync. In particular, a reconciliation delete must be traced to a replayed
semantic diff; current-state absence alone never authorizes deletion.

### Consequences

- A re-authorized user is durably discoverable for catch-up even though the global diff
  was already marked dispatched.
- The database requires the three cursor fields to be all null or all populated; a
  populated cursor also requires completed initial sync, a managed calendar, and an
  ordered timestamp.
- The ciphertext credential appears only in the backend-only pending-work projection,
  like initial and incremental synchronization.
- Missed diffs are now enumerated and applied by the freeze-gated replay worker. The
  cursor advances after each complete diff and an empty scan completes the request; a
  Calendar/store failure preserves the current cursor for idempotent retry.
- Actual Google inventory comparison, duplicate/missing-event repair, and marker-based
  orphan-calendar recovery remain later reconciliation work.

## ADR-061: Preserve the Google event ID when a secondary match changes stable identity

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** atomic `UserCalendarEventMapping.Reidentify`, mapping-store transition,
ledger-targeted patching in incremental dispatch and reconciliation, and unit/PostgreSQL
regression tests
**Depends on:** ADR-018 (start time is part of stable identity), ADR-035 (secondary
matching recognizes a moved lesson), ADR-058/059 (deterministic event IDs and ledger),
ADR-060 (ordered replay)

### Context

A lesson moved to another start time changes its stable identity (ADR-018), while the
semantic diff correctly classifies it as one `Updated` entry through secondary matching
(ADR-035). Calendar patching nevertheless re-derived the deterministic Google event ID
from the **new** identity, and the mapping lookup considered only that new identity. A
healthy user holding the previous mapping could therefore receive a new event while the
old event and ledger row remained, contradicting the decision to update in place. Replay
would have inherited the same defect.

### Decision

For a secondary-matched update, treat the durable mapping as the patch target:

1. load the user's mapping under the previous stable identity;
2. patch its stored `GoogleEventId` with the current canonical content and private
   properties;
3. after the external write succeeds, atomically change the mapping's stable identity,
   canonical record ID and content hash while preserving its Google calendar/event IDs.

The transition is idempotent: finding only the current identity is
`AlreadyReidentified`. Finding neither identity, finding both identities, or finding an
identity owned by another source is not guessed into a merge; the diff/reconciliation
cursor remains unadvanced for operator-visible retry or repair.

Every later patch also targets the ledger's stored Google event ID rather than deriving
one again. This matters because a reidentified mapping deliberately retains the ID
created from its original identity.

### Consequences

- A time move patches one existing Google event instead of creating a duplicate.
- A crash before the mapping commit repeats the same patch against the same Google event;
  a crash after it sees the idempotent current identity.
- The mapping's unique `(UserId, StableIdentity)` constraint continues to prevent two
  ledger rows for the same current lesson.
- A pre-existing both-sides conflict is left untouched. Removing an extra Google event
  belongs to the later inventory reconciliation sweep and must not be inferred here.

## ADR-062: Reconcile Calendar inventory as a non-destructive three-way repair

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `CalendarInventoryReconciliationService`, inventory target scheduling,
managed-event enumeration/comparison, connection inventory health state,
`AddCalendarInventoryReconciliation` migration, worker stage, and unit/PostgreSQL tests
**Depends on:** ADR-033/042 (forward-fix and release authority), ADR-034/043 (global
freeze), ADR-058/059 (managed event identity and ledger), ADR-060/061 (ordered replay
and identity-preserving patch)

### Context

Semantic replay repairs changes missed while a credential was dead, but it cannot find
out-of-band drift: a user may delete or edit a managed event, a process may write Google
successfully and die before committing its ledger row, or local metadata may become stale.
Conversely, treating every object absent from current published truth as removable would
bypass the semantic-diff hold/release safety boundary.

### Decision

Run a periodic, freeze-gated three-way inventory over:

1. current published records applicable to the user;
2. all of the user's durable event mappings;
3. actual Google events carrying Sirkadiyen private markers.

Repair only positive expected state. Recreate a missing mapped event with its ledger event
ID; adopt and patch one exact unledgered marked event; otherwise insert the deterministic
event and create its ledger row. Patch visible or private-property drift even when the
stored content hash is current, and update stale canonical IDs or hashes in the ledger.

Do not delete from inventory. A mapping without a current expected record, a duplicate or
unexpected marked event, and an identity/source conflict are counted and preserved for
operator-visible handling. Only a `Deleted` entry or a no-longer-applicable `Updated` entry
from a valid published dispatchable semantic diff authorizes deletion.

Persist `LastCalendarInventoryAtUtc` for cadence and
`ManagedCalendarUnavailableAtUtc` when the attached calendar returns unavailable. The latter
removes the connection from writers and makes onboarding report `ActionRequired`.

### Consequences

- Manual deletion and visible edits to an expected managed event converge on the next
  inventory without waiting for that lesson's next source change.
- A crash between the Google write and ledger commit can be repaired without creating a
  second event.
- Ambiguous external state stays intact; cleanup requires an explicit audited design and
  cannot silently inherit reconciliation authority.
- Successful inventories, including those reporting conflicts, advance the cadence.
  Transient failures retain the due state and currently retry on the next worker cycle.
- Automatic recreation of a deleted whole calendar is not part of this decision.

## ADR-063: Recover one marker-matched orphan calendar before creating another

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `ManagedCalendarIdentity`, Calendar description marker, CalendarList lookup,
minimal additional OAuth scope, initial-sync recovery policy, and mocked tests
**Depends on:** ADR-024 (one dedicated calendar), ADR-057 (separate least-privilege consent),
ADR-058 (resumable initial sync)

### Context

Google Calendar creation has no client-supplied idempotency key. If a worker creates the
dedicated calendar and dies before persisting `ManagedCalendarId`, retrying creation can
orphan the first calendar and provision a duplicate.

### Decision

Write an exact, versioned, per-user marker to the created calendar's description. When an
initial-sync connection has no stored calendar ID, list the user's owned CalendarList
entries with that exact marker and validate candidates by reading Sirkadiyen-marked events
through the app-created grant.

- exactly one accessible candidate: attach and resume;
- zero candidates: create a new marked calendar and persist its ID immediately;
- multiple candidates: stop safely and do not guess.

Retain `calendar.app.created` for all calendar/event mutations and add only
`calendar.calendarlist.readonly` for the lookup. Authorization verifies both granted scopes.
An older grant lacking the new scope can continue using an already-attached calendar; if
orphan lookup is required, the credential is marked for re-authorization.

### Consequences

- The create/commit crash window converges to the original calendar instead of duplicating it.
- Existing unrelated and primary calendars remain outside mutation scope.
- Exact-marker ambiguity is visible rather than resolved destructively.
- The real CalendarList behavior still needs a live-consent smoke test.

## ADR-064: Fence dispatch and reconciliation across worker instances

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `ICalendarDispatchReconciliationFence`,
`PostgresCalendarDispatchReconciliationFence`, worker orchestration, DI registration, and
PostgreSQL exclusion test
**Depends on:** ADR-059 (global dispatch), ADR-060 (empty scan completes catch-up), ADR-062
(periodic inventory)

### Context

In one worker process, dispatch precedes replay. With multiple workers, one process could
complete a user's replay after an empty scan while another process is concurrently
dispatching a diff that already skipped that user. Inventory could also inspect an
intermediate dispatch/replay state.

### Decision

Serialize the Calendar maintenance critical section with a PostgreSQL session advisory lock.
Acquire it non-blockingly on a dedicated connection and keep that connection alive across:

```text
global Ready/Released diff dispatch
-> per-user dispatched-diff replay, including empty-scan completion
-> due Calendar/ledger inventory
```

A worker that cannot acquire the lock yields that section for the cycle. Disposal explicitly
unlocks; connection loss also releases the session lock. Initial sync remains outside because
it does not participate in the completed-user dispatch/replay ordering proof.

### Consequences

- The empty replay scan is a valid cross-process completion proof.
- External Calendar calls are not wrapped in a long database transaction.
- Only one worker performs Calendar maintenance at a time; throughput remains bounded until
  the work is partitioned with a stronger per-partition protocol.
- Any future entry point that invokes dispatch, replay, or inventory must go through the same
  fenced coordinator.

## ADR-065: Bound one diff's fan-out with the existing idempotency ledger

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** per-diff Calendar mutation budget, `PartiallyDispatched` outcome, worker
configuration/logging, and insert/delete resume regression tests
**Depends on:** ADR-058 (durable event mapping), ADR-059 (coarse diff state over fine-grained
idempotency), ADR-064 (single fenced dispatch coordinator)

### Context

`DiffDispatchBatchSize` limits how many diffs a cycle admits, but ADR-059 still fanned every
user affected by one diff to completion. A normal `Ready` diff is small because safety
thresholds hold mass changes, while an operator may legitimately release a large held diff.
That released diff could perform a long, quota-heavy Calendar fan-out in one worker cycle.

A persisted `(diff, entry, user)` cursor appears attractive, but the affected-user set is
derived from live profiles, connection health and mappings. Paginating that mutable set by a
cursor can skip a user who becomes eligible behind the cursor, and a work-item table would
duplicate the fine-grained progress already represented by the mapping ledger.

### Decision

Set `CalendarOperationsPerDiffBatch` (environment key
`SIRKADIYEN_SYNC__CALENDAR_OPERATIONS_PER_DIFF_BATCH`, default 100). Count one per-user
semantic Calendar mutation: insert, patch, delete, or identity-moving patch. When the planner
finds a required mutation after the budget is exhausted:

1. return `PartiallyDispatched`;
2. leave `CalendarDispatchState` as `Pending`;
3. do not increment `DispatchAttempts`, set back-off, or record a failure;
4. replan the immutable diff in a later worker cycle.

Use the existing mapping ledger as the durable checkpoint. A successful insert adds a mapping,
a patch updates its content or stable identity, a delete removes it, and a credential rejection
makes that user ineligible while creating the reconciliation boundary. Those completed units
therefore disappear from the next plan. Only after a full scan finds no additional required
mutation is the diff marked `Dispatched`.

### Consequences

- A large released diff is spread over worker cycles without weakening its publication,
  hold/release or deletion authority.
- Quota yield is observably different from transient failure and never consumes the failure
  retry budget.
- No schema change, dispatch work table or mutable cursor is required.
- The limit bounds semantic mutation units, not exact provider requests. A patch that receives
  not-found may issue one recovery insert in the same unit.
- Each partial pass replans and may enumerate a whole cohort for the current entry; write volume
  is bounded, read/planning pagination is a possible later optimization.

---

## ADR-066: Develop the frontend same-origin behind an HTTPS proxy edge

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** the `web/` Next.js scaffold (App Router, TypeScript), its
`next.config.mjs` `/api/*` rewrite proxy, a CSRF-aware typed API client, Google
Identity Services sign-in and the Calendar popup code flow, and the onboarding
route gating
**Depends on:** ADR-023 (secure-cookie session), ADR-036 (Next.js), ADR-052
(sign-in boundary), ADR-057 (Calendar authorization)

### Context

The backend session and antiforgery cookies are `__Host-` prefixed, `Secure`, and
`SameSite=Lax`/`Strict` by deliberate design. ADR-052 recorded the constraint that
a cross-site frontend "must explicitly revisit CORS, credentialed requests and
SameSite policy rather than weakening cookies implicitly." Local development must
therefore exercise those cookies faithfully, not disable them.

Running the frontend cross-origin against Kestrel (`http://localhost:3000` →
`http://localhost:5080`) would force a Development-only CORS policy *and* dropping
`Secure`/`__Host-` over plain HTTP, so dev would test a weaker configuration than
production ships.

### Decision

In development the Next.js dev server is the only origin the browser talks to. It
runs over HTTPS (`next dev --experimental-https`) and proxies `/api/:path*` to the
backend server-side (`BACKEND_ORIGIN`, default the HTTP Kestrel URL
`http://localhost:5080`). The browser therefore makes only same-origin requests.

Consequences of the topology, and why the backend needs no change:

- Same-origin means **no CORS** and **no SameSite relaxation**; the cookies work
  unchanged.
- The HTTPS edge means the backend's `Secure` + `__Host-` cookies are accepted and
  stored exactly as in production. Kestrel stays on plain HTTP locally; only the
  edge terminates TLS, mirroring the production reverse proxy in front of Kestrel
  (`CookieSecurePolicy.Always` still emits `Secure` regardless of the Kestrel-side
  scheme).
- Both Google flows are popup/`postMessage`, so only the dev frontend origin
  (`https://localhost:3000`) is added to the OAuth client's Authorized JavaScript
  origins; no localhost redirect URI is registered.

### Consequences

- Dev is faithful to the hardened production cookie configuration; the class of
  bug ADR-052 warned about cannot hide behind a weakened dev config.
- A future genuinely cross-site production topology (separate apex domains) would
  still have to revisit CORS/SameSite explicitly; this ADR only removes the need
  for the *development* environment by keeping it same-origin.
- The frontend consumes string-serialized enums (the API uses
  `JsonStringEnumConverter`) and treats backend onboarding state as authoritative,
  mapping each state to a route rather than deciding activation client-side.
- Production deployment still ships a separate frontend (ADR-036); the same-origin
  guarantee there is provided by the reverse proxy, not by this dev proxy.

### Amendment (2026-07-24): the backend does need a forwarded-scheme change

The original claim above — "why the backend needs no change" — was wrong, and is
corrected here rather than rewritten. The HTTPS edge makes the browser↔edge hop
secure, but Kestrel still receives the proxied request over plain HTTP, so
`Request.IsHttps` is `false`. The antiforgery system has a hard runtime guard
(`CheckSSLConfig`) that throws `The antiforgery system has the configuration value
AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an
SSL request` on the first `GET /api/auth/csrf`. (Cookie *emission* was fine —
`CookieSecurePolicy.Always` writes `Secure` regardless of scheme — which is why the
gap was not obvious until runtime.)

The fix is the same mechanism production requires behind a TLS-terminating reverse
proxy, so it is not a dev-only shim:

- **Frontend:** `web/src/middleware.ts` sets `X-Forwarded-Proto: https` on `/api/*`.
  This is necessary because Next's rewrite proxy forwards `X-Forwarded-Host` but
  **not** `X-Forwarded-Proto` (verified empirically against an echo upstream and in
  `next/dist/server/lib/router-utils/proxy-request.js`, which enables neither
  `xfwd` nor a proto header). Note the file must live at `src/middleware.ts` when a
  `src/` directory is used, not the project root.
- **Backend:** `Program.cs` calls `UseForwardedHeaders` (first in the pipeline) with
  `XForwardedFor | XForwardedProto`. In `Development` it clears
  `KnownIPNetworks`/`KnownProxies` to trust the immediate loopback peer (which can
  present as `::ffff:127.0.0.1` and miss the default known-network entries). A
  production reverse proxy on another host MUST instead be pinned through
  `KnownProxies`/`KnownIPNetworks` (ADR-052) — otherwise the same antiforgery error
  will surface in production.

Verified: `GET /api/auth/csrf` returns `500` without the header and `200` with a
valid token when `X-Forwarded-Proto: https` is present; the middleware injects that
header into the upstream request.

---

## ADR-067: Route a SuperAdmin to the admin panel, not student onboarding

**Status:** Accepted and implemented
**Date:** 2026-07-24
**Implements:** `routeForUser` role-based routing, the `web/src/app/admin` panel
with the operational-freeze control and SuperAdmin self-activation, and a
role guard
**Depends on:** ADR-045 (SuperAdmin role), ADR-052 (session role claim), ADR-066
(frontend), ADR-034/043 (operational freeze)

### Context

Onboarding state is derived purely from student activation records — license,
profile, Calendar connection (`OnboardingStateService`). Role is not an input, so a
SuperAdmin with no license correctly computes to `LicenseRequired`. The first
frontend routed every signed-in user by onboarding state, so a SuperAdmin was sent
to the license-redemption page and could not reach any operator surface.

Making onboarding itself role-aware would conflate operator identity with student
activation and pollute the student state machine. Letting the frontend fabricate an
"activated" state would violate AI_GUIDELINE §6/§16 (backend is authoritative; the
frontend must not duplicate or fake authorization).

### Decision

Keep onboarding state student-only and unchanged. The frontend decides the *landing
route* from the backend-authoritative session `role`: a `SuperAdmin` lands on
`/admin`, everyone else on their onboarding route (`routeForUser`). This is
navigation, not authorization — every admin API remains enforced by the
`SuperAdmin` policy server-side, and the panel reads/writes only through those
protected endpoints.

The initial `/admin` panel surfaces the one fully-wired admin capability, the
runtime operational freeze (`GET`/`POST /api/operations/freeze`, ADR-034/043), lists
the not-yet-built Phase 10 surfaces, and offers audited SuperAdmin self-activation
(`POST /api/admin/users/{id}/activate`, ADR-053) so the operator can also walk the
student flow with their own account to test synchronization.

### Consequences

- A SuperAdmin is never blocked by the student license gate; a student is never
  routed to the admin panel (a role guard on `/admin` redirects non-admins to their
  onboarding route).
- No backend change was required; the role claim and the freeze/activation
  endpoints already existed.
- Self-activation makes the SuperAdmin a normal activated student from that point,
  so they resume the ordinary onboarding routes — the operator and student roles
  coexist on one account without special-casing the state machine.
- The panel is intentionally minimal; the remaining Phase 10 admin surfaces are
  still to be built behind this same role gate.

---


## ADR-068: Exclude PDÖ/lunch from the annual program and tolerate parallel offerings

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** annual-parser PDÖ/PBL and lunch-break exclusion with regenerated
golden files and regression tests; the `AudienceOverlap` validator refined to
quarantine only same-course duplicates; the `grade1_yearly_v1` profile version
bumped 1.0.0 → 1.1.0 (parser registry, profile definition, catalog, golden) to
force the re-parse
**Depends on:** ADR-030 (PDÖ exclusion), ADR-029 (validation severity), ADR-035
(course identity), ADR-058 (audience resolution)

### Context

The `G1-TR-ANNUAL` revision was held with `AudienceOverlap`: 33 lessons booked the
same whole-class audience at the same local date and time. The fixture showed the
cause is structural. The annual grid schedules a group's long practice block (for
example `UYGULAMA (PDÖ D3)`, 08:30–10:20) in parallel with the lectures the rest of
the cohort attends, and the annual parser assigned **every** row
`ALL_STUDENTS_IN_PROGRAM` — it had no way to express a group-specific or
non-teaching row. So group blocks, lunch breaks and legitimately-parallel
offerings all collapsed onto the whole class and collided.

Two distinct problems were tangled together: rows that should never have been
whole-class (or lessons at all), and genuine parallel offerings the source
deliberately schedules for everyone (electives, a make-up/retake exam beside the
regular one, free study opposite an activity).

### Decision

**Parser (annual).** Exclude two row kinds, each counted through the existing
`rows.ignored.<reason>` metric so nothing is dropped silently (§9):

- **PDÖ/PBL** problem-based learning, extending ADR-030 from the practice source to
  the annual source. It is group-specific and already published there; on the
  annual sheet it is what overlaps the parallel whole-class lecture.
- **Lunch/interval breaks** (`ÖĞLE ARASI` and interval rows). **Free study**
  (`SERBEST ÇALIŞMA`) is deliberately kept as a real whole-class entry, per the
  product owner.

TR dropped from 923 to 855 candidates and EN from 964 to 893; every removed
candidate was verified to be PDÖ/PBL or a break, with no lecture removed.

**Validator (`AudienceOverlap`).** Distinguish the two meanings of a same-audience
time overlap. Two records of the **same course** are a parsing duplication that
would put one lesson on a calendar twice, and quarantine the revision over the
tolerated count. Two records of **different courses** are a legitimate parallel
offering; they are reported for visibility as a non-blocking `Warning` and never
quarantine. Sameness is the normalized course identity, falling back to the display
title when identity is unresolved so an unresolved duplicate is still caught.

### Consequences

- The annual revision auto-publishes once its remaining overlaps are all parallel
  offerings, so the theoretical program finally reaches student calendars, while a
  real parser duplication is still held.
- The `AudienceOverlap` guard keeps its purpose (catching duplication) instead of
  being weakened by raising a tolerated count, which would have masked real bugs.
- Both content-affecting parser changes were made now, before launch; after launch
  the same exclusion would delete every affected managed event on the next diff.
- Free study remaining whole-class means two identical free-study blocks in one slot
  are still flagged as a same-course duplicate; that is a genuine redundancy worth an
  operator's glance, and deduping it is possible later work.
- Retake-only exams and electives are shown to the whole cohort (the product owner's
  choice); a future per-student elective/retake audience would narrow them, but that
  needs a profile concept that does not exist yet.
- The fix reaches an already-parsed source through the version bump: a parse run is
  keyed by `(snapshot, profile, ParserProfileVersion)`, so `grade1_yearly_v1` 1.1.0
  makes the poller open a new run and re-parse the stored annual snapshots without
  the source content changing. Profiles are now versioned independently (the shared
  `_PROFILE_VERSION` no longer applies to the annual profile), and the golden test
  resolves each profile's version from the registry rather than a shared constant.

---

## ADR-069: Model free study explicitly and do not quarantine its overlaps

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** canonical/parser `FreeStudy` event type, non-blocking
`AudienceOverlap` classification, `grade1_yearly_v1` 1.2.0 re-parse trigger,
real-fixture golden updates, and validator/contract regressions
**Amends:** ADR-068

### Context

After ADR-068, the real `G1-TR-ANNUAL` revision was still held. Its only error
was two same-title overlaps:

- `2025-10-23`: `SERBEST ÇALIŞMA` 15:30–16:30 and 16:00–16:40
- `2026-05-18`: `SERBEST ÇALIŞMA` 13:30–15:20 and 15:00–16:50

The parser had not duplicated either row: each record cited a different source
row with the exact time the sheet stated. The false quarantine came later.
Free study was represented as generic `Other`, so ADR-068's same-course rule saw
the shared normalized identity and classified the intersections as duplicated
teaching.

Approving the revision manually would treat a deterministic modeling defect as
an exceptional source anomaly. Merging the time ranges would invent one
canonical row not stated by the source and collapse its row-level evidence.
Dropping free study would reverse the product decision to keep it visible.

### Decision

Add `FreeStudy` to the parser contract and domain `ScheduleEventType`. The annual
classifier emits it for titles whose first normalized token is `serbest` or
`free`, before examining exam or practice keywords in parenthetical text.

Classify a timed intersection between two `FreeStudy` records as a non-blocking
`AudienceOverlap` warning. It remains visible to operators, but does not count
toward the tolerated same-course-duplication threshold. A same-course overlap
for every other event type still follows ADR-068 and can quarantine; a
different-course parallel offering remains a warning.

Bump `grade1_yearly_v1` from 1.1.0 to 1.2.0 in the implementation registry and
both Grade 1 annual catalog entries. The worker therefore re-parses the retained
snapshots after restart rather than reusing the held 1.1.0 runs.

### Consequences

- The source-authored free-study rows and their exact evidence remain intact.
- The two known intersections no longer withhold 862 otherwise healthy Turkish
  annual records from student calendars.
- Real duplicated teaching remains fail-safe; the exception is semantic and
  explicit rather than a title-prefix check inside the validator.
- `FreeStudy` changes serialized parser output and content hashes but not stable
  identities. No database migration is needed because event types are stored as
  strings.
- The obsolete 1.1.0 `ReviewRequired` revision remains historical evidence. The
  1.2.0 forward-fix creates and publishes a new revision; it is not manually
  approved or rewritten.

---

## ADR-070: Resume quota-yielded Calendar work independently of source polling

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** Calendar-only catch-up cycles, configurable
`SIRKADIYEN_SYNC:CALENDAR_CATCH_UP_INTERVAL`, worker scheduling regressions
**Amends:** ADR-058, ADR-060, ADR-065
**Amended by:** ADR-082, which also admits work created after an empty pass

### Context

Publishing the repaired Grade 1 annual revision created a diff with 862 Calendar
insertions for the test user. Incremental dispatch behaved as designed and yielded
after 100 mutation units, leaving the diff pending and the user's calendar at 151
events (51 existing practice events plus 100 annual events).

The remaining work was safe and durable, but it could resume only when the worker's
outer source-polling loop ran again. On a weekend that interval is one hour. Calendar
quota protection had therefore become accidental user-visible latency, even though no
Google credential had failed and the log reported zero re-authorization flags.

Raising the mutation budget enough for this one annual diff would remove the intended
bound and scale poorly across users. Re-polling every source every few seconds merely
to resume Calendar work would waste network and parser resources.

### Decision

Keep the per-diff Calendar mutation budget unchanged. When incremental dispatch
returns `PartiallyDispatched`, initial sync returns `InProgress`, or reconciliation
returns `InProgress`, schedule the next worker pass after a configurable short delay
(five seconds by default).

That continuation runs only Calendar work. It skips source polling, revision
publication, diff calculation, and snapshot retention. When no ordinary quota-yielded
Calendar work remains, the worker returns to the existing adaptive source polling
interval.

### Consequences

- Large initial loads and diffs drain in bounded 100-operation passes without waiting
  15–60 minutes between passes.
- Completed mutations remain durable in the mapping ledger, so every continuation is
  still idempotent and naturally plans only unfinished work.
- Source acquisition frequency and Google Calendar write pacing are now independent.
- Transient provider failures still use their existing persisted exponential back-off;
  they do not request immediate catch-up.
- A worker restart is required to activate this orchestration change; no migration or
  user re-authorization is required.

---

## ADR-071: The group-specific practice source supersedes bare annual slot placeholders

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** exact annual placeholder exclusion, parser diagnostics and regressions,
real-fixture golden updates, `grade1_yearly_v1` 1.3.0 re-parse trigger
**Amends:** ADR-030, ADR-068

### Context

After the Grade 1 Turkish annual revision reached Calendar, a Group B student saw
both the whole-program annual event `UYGULAMA` and the group-specific event such as
`Temel Biyofizik` from the practice table in the same time slot.

The sources were not two independent lessons. The published annual revision contained
134 rows whose entire title was `UYGULAMA`, with an `AllStudentsInProgram` audience.
The companion practice source supplied the real lesson identity and selected practice
groups for those slots. The annual row was therefore a coarse grid placeholder, not a
calendar event in addition to the detailed practice.

Excluding every annual title containing `uygulama`, `practice`, or `lab` would be
destructive. The same source also contains real named sessions such as anatomy
practice, physiology practice, laboratory skills, and theory titles in which
“uygulama” is ordinary course wording.

### Decision

In the Grade 1 annual parser, exclude a row as
`outOfScopePracticePlaceholder` only when its normalized title words are exactly
`("uygulama",)` or `("practice",)`. The curriculum block does not turn that bare
title into a real lesson; the companion practice source remains authoritative for
the actual audience and name.

Retain every longer or otherwise named title, including `Anatomi Uygulama 14/21`,
`FİZYOLOJİ UYGULAMA`, `LABORATORY SKILLS (...)`, and theory titles containing an
application-related word.

Bump `grade1_yearly_v1` to 1.3.0 for both TR and EN catalog entries. Re-parse retained
snapshots and publish the reduced revision normally. Already-created placeholder
events are deleted only by its semantic diff, preserving the deletion authority and
mapping ledger rules.

### Consequences

- A student receives only their detailed group-specific practice event for those
  slots, not an additional generic whole-class event.
- The real TR fixture drops from 855 to 721 candidates, exactly matching the 134
  excluded placeholders. Named practice records remain. The EN fixture has no bare
  placeholder and stays at 893 candidates.
- The live database source may differ slightly from the committed fixture, but the
  deterministic title rule applies identically and the normal mass-deletion guard
  still validates the resulting revision.
- No database migration, direct Google Calendar edit, or user re-authorization is
  required. Worker restart is required to seed 1.3.0 and drive the forward fix.

---

## ADR-072: Calendar presentation is shared, department-colored, and repairable

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** shared Calendar presentation policy, deterministic custom event
labels, labeled descriptions, source-faithful summaries, deferred-location omission,
`grade1_yearly_v1` 1.4.0 re-parse trigger
**Amends:** ADR-024, ADR-058, ADR-062

### Context

All managed events inherited the dedicated calendar's single color, so unrelated
departments were difficult to distinguish. Their descriptions were unlabeled lines,
the annual source's leading lecture number remained in theory titles, and the literal
instruction `FAKÜLTEMİZ WEB SİTESİ ÖĞRENCİ AĞI AMFİ PROGRAMINA BAKINIZ` was written as
though it were a physical location.

Google's legacy per-event color palette has only eleven colors, which cannot keep every
department distinct as later class-year parsers are added. Google Calendar now supports
up to 200 calendar-scoped event labels with a UUID, name, and arbitrary RGB background.

### Decision

Derive every Google-visible field through one application-layer
`CalendarEventPresentationPolicy`, shared by all current and future parser profiles.
Use calendar-scoped event labels: Anatomi is red, Fizyoloji navy, Tıbbi Biyokimya
orange, Tıbbi Biyoloji green, Histoloji ve Embriyoloji purple, exams gray, and free
study blue. Every other source-stated department receives a deterministic UUID and
RGB color derived from its normalized name, so it remains stable across users and
runs without a database registry.

Before an insert or patch, the Google adapter reads the current calendar labels once,
merges the required definition without removing unrelated labels, and sends events
with `eventLabelVersion=1`. Inventory snapshots retain `EventLabelId` and equivalence
checks it, making older monochrome events patchable through the ordinary repair pass.

Preserve the canonical display title as the Calendar summary, including a leading
numeric lesson sequence marker. Build labeled description lines in instructor,
curriculum-block, department order. Treat any amphitheatre-program lookup instruction
as no location. Profile 1.4.0 publishes those annual location values as `null`; the
presentation policy also hides legacy values until their forward-fix diff arrives.

Continue sending timed events as local `yyyy-MM-ddTHH:mm:ss` values in
`Europe/Istanbul`. Google may render 12- or 24-hour notation according to the user's
Calendar/account locale; the event API has no per-event display-format override.

### Consequences

- Departments are visually distinct without creating one calendar per department or
  being capped by the legacy eleven-color palette.
- The same canonical department receives the same label ID and color in every managed
  calendar. Later parsers opt in automatically by filling the existing canonical
  department fields.
- Existing events are repaired by inventory; records whose deferred location changes
  in 1.4.0 also produce normal semantic updates. No direct Calendar mutation or
  database migration is introduced.
- The first event of a label category may require one calendar metadata patch. The
  adapter caches the calendar's label registry for the process lifetime and preserves
  provider failure classification.

---

## ADR-073: Grade 2 annual is one profile for both languages, and a stated group rotation is not a whole-class lesson

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** `grade2_yearly_v1` 1.0.0 for `G2-TR-ANNUAL` and `G2-EN-ANNUAL`, per-profile
group-rotation exclusion, day-fraction time refusal (parser engine 0.2.0,
`grade1_yearly_v1` 1.5.0), Grade 2 real-snapshot fixtures and goldens
**Extends:** ADR-030, ADR-051, ADR-071

### Context

The Grade 2 Turkish (`DÖNEM 2`) and English (`CLASS 2`) annual workbooks are the same
row-oriented layout as Grade 1: term, date, start time, end time, subject,
`DİLİM ADI / ANABİLİM DALI` / `Description`, and location, differing only in header
wording and in the term cell (`Dönem 2` against `Time Table 2`). The catalog already
maps both sources to one profile, `grade2_yearly_v1`, which had no implementation.

Reading the two workbooks raised three source facts the Grade 1 rules did not cover.

1. **Dissection is a group rotation the annual program states in full.** Each Turkish
   and English workbook holds 159 `DİSEKSİYON (n/13)` / `DISSECTION (n/13)` rows,
   written as three consecutive daily slots — 13:30-14:20, 14:30-15:20, 15:30-16:20 —
   carrying the *same* session number. The separate anatomy source
   (`2. SINIF SALON GRUP SAATLERİ`, `G2-ANATOMY-AUTUMN`/`SPRING`) assigns anatomy groups
   1, 2 and 3 to those three hours in rotation, so a student attends exactly one. The
   annual row states no group, and nothing in it could be inferred into one.
   The three slots do not overlap, so revision validation would not catch them either.
2. **The bare `UYGULAMA` placeholder states its own deferral.** 119 Turkish rows whose
   whole title is `UYGULAMA` carry the location
   `FAKÜLTEMİZ WEB SİTESİ ÖĞRENCİ AĞI DÖNEM 2 UYGULAMA PROGRAMINA BAKINIZ`, which is the
   source itself pointing at the companion practice program that ADR-071 already
   treats as authoritative.
3. **A numeric time cell need not be a time.** The English workbook holds a bare `9` in
   an `hh:mm`-formatted start-time cell. The shared resolver reduced any number to its
   fractional part, so nine whole days became `00:00` and published a free-study block
   from midnight to 13:00 — 780 minutes, which revision validation quarantines as an
   impossible duration. The workbook itself renders that cell `00:00`; the hour was
   never stated by anyone.

### Decision

Register `grade2_yearly_v1` 1.0.0 against the existing row-oriented annual
implementation for both language sources. The class year is taken from the request
context, as it already was, so no language- or grade-specific parser is introduced.

Add `group_rotation_subjects` to the parser profile definition, reported by
`GET /v1/profiles`. `grade2_yearly_v1` declares `diseksiyon` and `dissection`; a row
whose title names one is excluded as `rows.ignored.outOfScopeGroupRotation` and
accounted for like every other exclusion. The declaration is per profile rather than a
shared word list, because it depends on which companion sources exist for that grade:
Grade 1 declares none and keeps such rows.

Refuse a numeric time cell that is not a day fraction. Only a cell whose number format
declares a full timestamp may carry a whole-day part; a cell declaring a time of day,
or a column a profile confirmed holds fractions, must hold a value in `[0, 1)`. This is
a behavioural change to a shared primitive, so the parser engine goes to 0.2.0 and the
one affected profile, `grade1_yearly_v1`, goes to 1.5.0. Its committed goldens do not
move, but a stored snapshot cannot be proved free of such a cell, so the bump reparses
them. `grade1_practice_v1` reads only textual time ranges and is untouched.

Do **not** infer an audience from the practice-group labels the English workbook writes
inside titles (`LABORATORY SKILLS (HISTOLOGY AND EMBRYOLOGY) İ2`,
`LABORATORY SKILLS Team Work İ1`-`İ5`). They are published verbatim to the whole
program, as the source writes them, until `G2-EN-ANNUAL` declares supported selectors
on evidence (ADR-048) and the supported-profile schema carries Grade 2.

### Consequences

- `G2-TR-ANNUAL` publishes 790 candidates from 1156 rows and `G2-EN-ANNUAL` 935 from
  1252. Every unpublished row is counted by reason: 159 group-rotation rows in each,
  119 Turkish practice placeholders, 77 Turkish and 145 English breaks, 7 and 6 PDÖ
  rows, and single-figure source faults that are reported as warnings.
- Grade 2 students see no dissection at all until `grade2_anatomy_autumn_v1` and
  `grade2_anatomy_spring_v1` publish it with its real audience. That is deliberate: an
  event a student must not attend is a worse failure than a missing one, and the
  omission is visible in the metrics rather than silent.
- Both revisions are predicted to validate rather than quarantine, but each sits at the
  tolerance boundary with exactly one same-course overlap (`Ek Ders ( ANATOMİ)` in
  Turkish, `LABORATORY SKILLS EXAMINATION` in English, each written twice for one slot).
  A second such source typo would hold the revision for review.
- No Grade 2 student can onboard yet: the supported-profile schema (ADR-055) still
  covers class year 1 only, so these revisions publish to an empty audience until it is
  extended. No database migration is required.

---

## ADR-074: The Grade 2 practice table is a slot-column rotation with its own parser

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** `grade2_practice_v1` 1.0.0 for `G2-TR-PRACTICE`, the
`parsers/practice_slots.py` layout reader, a bounded multi-letter cohort run, and the
Grade 2 practice fixture and golden
**Extends:** ADR-020, ADR-030, ADR-048, ADR-051, ADR-073

### Context

The Grade 2 Turkish practice table (`Uygulama Tablosu`) is not a variant of the Grade 1
rotation table: it is its transpose. In Grade 1 a row is a dated slot and a column is a
practice subject. Here a **column** is a dated slot — its header holds a slot label, a
date and a time range on separate lines — and a **row** is a practice subject, naming
the subject in the first column and its room in the second. The cell where they meet
holds the group or groups attending.

The worksheet interleaves nine curriculum blocks, 15 slot-header rows and the topic
lists that belong to each block, plus a room-and-telephone lookup table at the end.
Reading it also has to survive several shapes the Grade 1 table never produced:

- a whole-cohort session written into the body of the table as `TÜM GRUPLAR` with its
  **own** date and time, merged across a run of columns, and stating a date that is not
  the one its column header states
- concatenated cohort letters (`ABCD 1/1`, `GH 1/3`), where the trailing `n/m` numbers
  the session rather than naming a group
- a bare `*`, which the source's own note explains: the skill-practice groups and rooms
  "ayrı bir tablo ile duyurulacaktır"
- an `Anatomi (13)` row whose cells hold dissection **dates** rather than groups
- four slot dates whose year is a year out (`3 Şubat 2025`, `24 Aralık 2024`,
  `26/27 Şubat 2025`), each contradicting the weekday typed beside it

### Decision

Add `parsers/practice_slots.py` rather than generalizing the Grade 1 reader. The two
layouts share the primitives, the cohort model (ADR-020) and the "a candidate is a cell"
rule, but a reader parameterized over both axes would be harder to reason about than two
readers of one axis each.

Classify every row of the worksheet exactly once — block heading, slot header, subject
row, or none of them — so `rows.scanned` equals the worksheet's row count and the topic
lists are counted rather than skipped by a range computed in advance.

**Refuse a slot whose stated weekday contradicts its own date.** The annual profiles
publish such a row with a warning, because their dates are spreadsheet serials and the
weekday text is a formatting artifact. These headers are typed by hand and the weekday
is the cell's only corroboration, so the contradiction is the source telling us it is
wrong. Publishing the four affected columns would put practices a year in the past on
real calendars and quarantine the whole revision for a date outside the academic year;
correcting the year would be inference.

**Read a cell that dates itself from the cell**, not from its column header, and publish
it once at the anchor of its merged run. **Refuse any cell that states a session but no
audience**, and read a letter run only within the eight cohorts this source states
(ADR-048). The bound is what makes runs safe: without it the same rule reads `SINAV` as
the five cohorts S, I, N, A and V, one of which is real.

Declare `anatomi`/`diseksiyon` as this profile's `group_rotation_subjects` too (ADR-073),
so the dissection row is deferred to the anatomy sources here exactly as in the annual
profile.

`parse_group_expression` gains `max_letter_run`, defaulting to two. Every existing
caller's outcome is unchanged — a three-letter token was refused before and is refused
now — so the parser engine version does not move.

### Consequences

- `G2-TR-PRACTICE` publishes 163 candidates from 352 scanned cells: 144 single-cohort
  slots, 8 two-cohort and 6 four-or-two-cohort sessions, 2 whole-cohort sessions, and 7
  practical examinations. Every unpublished cell is counted by reason: 95 skill-practice
  cells whose groups are announced elsewhere, 53 dissection cells, 8 "no session"
  markers, 7 cells under a refused slot, and 5 that state no readable audience.
- The revision is predicted to validate with no findings at all: no overlaps, no
  impossible durations, and every selector inside the `A`-`H` the catalog declares.
- 95 vertical-corridor sessions reach no calendar until `grade2_vertical_corridor_v1`
  publishes them with their real groups. The source states no audience for them, so this
  is the only faithful reading.
- **The first real numeric date any source has written is now refused** (ADR-051):
  `TÜM GRUPLAR 8.10.2025` is 8 October read day-first and 10 August read month-first, so
  one whole-cohort session is unpublished and its cell is named. The Turkish annual
  program states the same session on 8 October, which is evidence for declaring
  `dayFirst` — but that is a claim about this document and is left as a deliberate,
  separate decision rather than taken silently.
- No database migration, no .NET change, and no catalog change: `G2-TR-PRACTICE` already
  pointed at `grade2_practice_v1` 1.0.0 with `practiceGroup` `A`-`H`.
- The English Grade 2 practice source is deliberately not registered. Its committed
  fixture is from 2024-2025, and ADR-048 requires current evidence before its cohorts
  are declared.

---

## ADR-075: The Grade 2 practice source declares dayFirst, read off a second source

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** `grade2_practice_v1` 1.1.0 for `G2-TR-PRACTICE` and `G2-EN-PRACTICE`
**Extends:** ADR-051, ADR-074
**Amended by:** ADR-084, which bumps the same profile to 1.2.0 for the verified
English fixture while retaining `dayFirst`

### Context

ADR-051 requires every parser profile to declare how its source writes `12/11/2026`,
and every profile declared `undeclared` because no committed fixture wrote a numeric
date at all. ADR-074 produced the first one: the Grade 2 Turkish practice workbook
writes a single cell as `TÜM GRUPLAR / 8.10.2025 / 08:30-10:20`, and version 1.0.0
refused it, named the cell, and left one whole-cohort session unpublished.

`8.10.2025` is 8 October read day-first and 10 August read month-first. Both are real
dates, so the cell alone cannot settle it.

### Decision

Declare `numeric_date_order = dayFirst` for `grade2_practice_v1` and bump it to 1.1.0.
Every other profile keeps `undeclared`.

The declaration is read off a second source, not off the Turkish writing convention:

- The Grade 2 Turkish **annual** workbook schedules the same session as a spreadsheet
  serial — 2025-10-08, 08:30-10:20, `FİZYOLOJİ 1. UYGULAMASI (TÜM GRUPLAR Amfide
  yapılacak)`. Subject, time, audience and room all match the practice cell.
- The month-first reading, 10 August 2025, falls outside the academic year and outside
  the `DOLAŞIM-1` block's own 3-16 October range.

The convention argument is deliberately not used. It would have justified declaring
every Turkish profile at once, and the point of ADR-051 is that a declaration is a
claim about a specific document.

A declared order does not weaken the other refusals. `_as_declared` refuses a date the
declared order cannot explain (`numericDateImpossibleUnderDeclaredOrder`) rather than
falling back to the other order, and a numeric text with no year is still refused
because this profile supplies no year rule — which is what keeps a slot label such as
`2/6` from being completed into 2 June.

### Consequences

- `G2-TR-PRACTICE` publishes 164 candidates instead of 163. The one recovered candidate
  is `1!R34C3`, `Fizyoloji 1` in `AMFİ` on 2025-10-08 08:30-10:20, for the whole cohort,
  at confidence 0.95 (`dates.rule.numericDayFirstDate`). Nothing else in the golden file
  moved: no other candidate, warning or metric changed except the ones this cell owns.
- The bump to 1.1.0 is required even though the implementation is a day old. A parse run
  is keyed by (snapshot, profile, version), so leaving the version alone would let a
  stored 1.0.0 run stand while the parser now reads that cell differently. Both catalog
  entries naming the profile move with it, including the unimplemented English one, so
  neither points at a version the registry no longer holds.
- All three whole-cohort practice sessions now published (`Fizyoloji 1` on 2025-10-08,
  `Fizyoloji 2` on 2025-10-23, `Fizyoloji` on 2026-04-17) are also published by
  `grade2_yearly_v1` from the annual workbook, which writes them as titled rows rather
  than as the bare `UYGULAMA` placeholders ADR-071 excludes. That duplication is
  pre-existing — two of the three were already published at 1.0.0 — and is a
  cross-source concern, not a consequence of this decision. It is recorded here because
  this ADR adds the third instance and because no rule yet reconciles a titled annual
  row against the practice source that restates it.
- No engine version change: `dates.py` was not modified. No .NET change and no database
  migration.

---

## ADR-076: A Word document is converted onto the normalized snapshot contract

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** `LocalDocxSnapshotConverter`, `--document` in `Sirkadiyen.SnapshotTool`,
and the four Grade 2 DOCX snapshot fixtures
**Extends:** ADR-014 (normalized snapshot contract), ADR-015 (transport is not format),
ADR-073, ADR-074

### Context

Not every program is published as a sheet. The Grade 2 anatomy group lists, the Grade 2
vertical-corridor calendar and the Grade 3 bedside programs are Word documents, and the
pipeline could do nothing with them: `ScheduleSourcePoller` answers
`UnsupportedTransport` for any source that is not Google Sheets, and the snapshot tool
refused anything but `.xlsx`.

Two of those documents are the sources ADR-073 and ADR-074 defer to. 159 annual
dissection rows and 95 practice cells marked `*` reach no calendar until they are read,
so this is not a future format: it is the missing half of work already shipped. ADR-015
separated transport from document format for exactly this reason and left DOCX
conversion as implementation work; this is that work.

The two Grade 2 families also differ in how they are maintained. The anatomy documents
are handed out once at the start of each semester and are not edited afterwards. The
vertical-corridor documents are edited by Student Affairs during the year.

### Decision

Convert a Word document onto `NormalizedSpreadsheetSnapshot`, the same contract the
workbooks produce, rather than adding a second document contract. A Word table is rows,
columns and merges; everything downstream — immutable snapshot storage, the
unchanged-snapshot short circuit, the parser's grid primitives, A1 evidence, golden
files — then works unchanged, and a parser profile never learns which format its source
was published in.

The mapping is stated rather than inferred, because a Word document is a sequence of
blocks and not a set of named sheets:

- each body-level table is one worksheet, and each run of paragraphs between tables is
  one single-column worksheet, both in document order. The paragraphs are not
  decoration: the Grade 3 bedside documents write their practice topics that way.
- worksheet titles are `Table n` and `Text n`, assigned by the converter. Word names
  nothing, and the snapshot says so in its own diagnostics rather than letting a reader
  assume the titles came from the document.
- a paragraph boundary and an explicit break both become a newline. The line structure
  is load-bearing: a vertical-corridor slot cell writes a label, a date and a time range
  as three lines, exactly as the Grade 2 practice sheet does.
- text is transcribed untrimmed. Collapsing ragged whitespace is the parser's
  normalization step, and it already does it for the spreadsheet sources.
- a cell or paragraph whose text is only whitespace states nothing and produces no cell.
  The grid position is already implied by the table's row and column count.

What cannot be represented is reported, never dropped quietly: a blank paragraph run is
counted in a diagnostic, and a table nested inside a cell is an **Error** diagnostic
naming the cell's A1 address. Flattening a nested table into its containing cell would
state a single value the document never wrote.

The Grade 2 anatomy sources are **not** added to `config/schedule-sources.json`. The
catalog requires an absolute HTTPS URI and these documents have no published location —
they are handed out. Inventing a URL to satisfy the schema would put a false statement
about provenance in the one file the pipeline trusts. The snapshot tool instead accepts
`--document`, converting a file under a source ID the manifest reserves, and the catalog
stays the authority for every source it can actually describe.

### Consequences

- Four Grade 2 documents are converted and committed as fixtures. The anatomy documents
  yield two worksheets each and confirm ADR-073 directly: the three dissection hours of
  one date carry anatomy groups 1, 2 and 3 in rotation, so a student attends one.
- The anatomy documents write that rotation two ways in one table — an empty neighbour
  row up to autumn row 45, a vertical merge from row 46 — and the profile that reads
  them will have to handle both.
- Every converted cell is text and none declares a number format, so a DOCX-backed
  profile resolves dates and times from text alone. There is no serial to fall back on
  and no format to corroborate a reading, which makes the ADR-051 declaration a live
  question for `grade2_vertical_corridor_v1` before it publishes anything.
- Conversion alone changes nothing at runtime. `ScheduleSourcePoller` still answers
  `UnsupportedTransport` for a DOCX source, because acquiring one needs a transport that
  does not exist yet: a Drive download for the vertical-corridor documents, which change,
  and an administrative upload for the anatomy documents, which do not. Those are
  separate decisions, and the maintenance difference above is the reason they will not
  be the same mechanism.
- No parser profile reads these fixtures yet, so they are committed evidence rather than
  covered behaviour. What is covered: seven converter unit tests, and a parser-side test
  that each snapshot validates against the inbound contract and states only text.

---

## ADR-077: The vertical-corridor calendar selects students by their practice group

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** `grade2_vertical_corridor_v1` 1.0.0 for `G2-VERTICAL-AUTUMN` and
`G2-VERTICAL-SPRING`, the `parsers/vertical_corridor.py` reader, and
`parsers/cohort_rotation.py`
**Extends:** ADR-020, ADR-048, ADR-071, ADR-074, ADR-076

### Context

Two shipped profiles defer to this document. The Grade 2 annual program writes these
sessions as a bare `UYGULAMA` placeholder (ADR-071) and the Grade 2 practice table marks
them with a bare `*`, its own note saying the groups and rooms "ayrı bir tablo ile
duyurulacaktır" (ADR-074). Until this is read, those sessions reach no calendar at all.

The document is a Word file, so it arrives through the DOCX conversion (ADR-076). Its
axis is the practice table's transposed back — a row is a dated slot, a column is one of
five skill practices — but the whole slot is written as separate lines of one cell, the
way the practice table writes its column *headers*.

### Decision

**Publish to the practice group a student already has.** The profile previously declared
a `verticalCorridorGroup` dimension. The document states the same lettered cohorts
`A`-`H` the practice table states — its `*` cells are the ones this document answers —
and its `EKİP OLMA` column halves them into `A1`-`H2`. Inventing a third grouping would
make every Grade 2 student declare a group the faculty never asks them for, so the
profile declares `practiceGroup` and `practiceSubgroup` (ADR-020).

**Share the cell-level rules with the practice profile, not the reader.** The two
sources need different readers, one per axis (ADR-074), but the eight-letter cohort
alphabet and the weekday-contradiction refusal are one definition in
`parsers/cohort_rotation.py`. The alphabet is what makes reading a run such as `CD`
safe; a drifting second copy of it would not be visible until an ordinary word reached
real calendars. The document proves the point twice over: `Telafi` expands to T, E, L, A,
F and I, three of which are real groups, and only the bound refuses it.

Three properties of the document drive the rest:

- **It is filled in over the year.** Student Affairs edits it, so most dated rows state
  no groups yet. A dated row with no group cells publishes nothing and raises nothing;
  a row whose groups cannot be dated is a warning naming the cell, because a session
  with an audience is being lost.
- **It carries a second programme.** `İ1`-`İ3` and the separately published `EK-1`-`EK-3`
  lists sit in the same grid as `A`-`H`. Each is counted under its own reason and refused:
  the English source is the one that may declare English cohorts (ADR-048).
- **Its examinations name cohorts with hyphens** (`A-B-C-D SINAV`). The hyphen is read as
  a separator only when every part is one of the eight declared letters, so `EK-1` and
  any numeric range keep theirs and are refused. Adding `-` to the shared token separator
  was rejected: it would silently turn the range `1-3` into `{1, 3}` for every caller.

One thing is repaired rather than transcribed. Four of the seven spring tables write
`OKSİJEN (Doç. Dr. Bengüsu MİRASOĞLU` and never close the bracket, while the first table
closes it. An unclosed trailing parenthetical becomes the instructor only when it starts
with an academic title; without that the same practice reaches calendars under two
titles, one ending mid-bracket.

### Consequences

- **42 sessions now reach calendars that previously reached none**: 12 from the autumn
  document and 30 from the spring one, for cohorts `A`-`H` and fourteen of their
  subgroups, plus the two whole-cohort examinations on 28 and 29 March 2026. Both
  revisions are predicted to validate with no findings: no overlap for any cohort, every
  duration plausible, every selector inside the declared set.
- **It is not the whole programme.** The practice table marks 95 skill-practice slots and
  this document assigns groups to a fraction of them, because the faculty has not
  scheduled the rest yet. Re-acquisition, not a parser change, is what publishes the
  remainder — which is the operational difference ADR-076 recorded between these
  documents and the anatomy ones.
- **Nine dated rows contradict their own weekday and are refused**, four of them naming a
  year that is a year out. Three of the nine carry groups, so four cohort-sessions are
  lost until the faculty corrects them: `E` on 24 Aralık 2024, `A2` and `G` on 26 Şubat
  2025, `C2` on 27 Şubat 2025. The same four wrong dates appear in the practice table,
  which is evidence the two documents are maintained together.
- A header row is recognized by its first column and by naming at least one practice.
  Requiring the place header beside it dropped a whole spring table — eleven dated rows,
  three of them published — into "no table in force".
- The English programme still receives nothing from this document. Publishing it needs an
  English source entry over the same file and a current Grade 2 English practice fixture
  to declare its cohorts against.
- No engine version change, no .NET change and no database migration. Both catalog
  entries gain their supported selectors.

---

## ADR-078: A dissection day is a run of hours, not a row with a date

**Status:** Accepted and implemented
**Date:** 2026-07-25
**Implements:** `grade2_anatomy_autumn_v1` and `grade2_anatomy_spring_v1` 1.0.0, and the
`parsers/anatomy.py` reader
**Extends:** ADR-020, ADR-048, ADR-073, ADR-076

### Context

ADR-073 excluded 159 dissection rows from each Grade 2 annual workbook. The annual
program states all three of a day's dissection hours with the same session number, and
publishing them would have booked every student into two hours they must not attend. The
anatomy group list is the document that says which hour each student attends, so until it
is read those sessions reach no calendar at all.

It is the simplest schedule the faculty publishes: three columns — a date, one of the
three hours, and the anatomy group `1`, `2` or `3` — and three rows per teaching day. The
anatomy group is independent of a student's practice group.

One thing about it is not simple. **The same document states a day two different ways.**
In the later rows a day is a vertical merge over its three hours. In the earlier ones the
date is typed into the middle row of the three, with the cells above and below left
empty. To a reader those are identical. To a grid the second is one dated row and two
undated ones, and 30 of the autumn document's 90 rows are written that way.

### Decision

**Recognize a day by its own shape: a run of consecutive rows whose hours advance,
stating exactly one date between them.** The boundary is read from the document rather
than assumed — its hours always run 13:30, 14:30, 15:30, so a row whose hour does not
follow the previous one begins the next day. A run that states no date, or more than one,
publishes nothing.

**The day is the unit of refusal.** Publishing the hour that happens to state the date and
dropping the two beside it would give two of the three groups no session and the third one
that may not be theirs — worse than publishing nothing, because it looks complete.

**A date attributed from a neighbouring row is published at 0.8 and says so**, in a
confidence indicator naming `dateFromNeighbouringRowInDayBlock`. The date is the
document's own, but the association is a rule of this profile, and the precedent is
`CONFIDENCE_YEAR_FROM_PROFILE`: a value supplied by a profile rule scores below one the
cell states. A date reached through a merge is *not* marked, because a merge is the
document itself saying the three hours are one day.

**The lesson title comes from the profile's declared annual marker**, `Diseksiyon`. These
rows name no lesson — they are a date, an hour and a group — and the marker is the name
the annual program gives the same lesson, so the two sources agree on identity. A profile
declaring no marker rejects the snapshot rather than inventing a name.

One implementation serves both profiles, the way `grade2_yearly_v1` serves both languages.
The profile names stay separate because the sources are: each document states its own
semester's dates, and a semester is a different source, not a different layout.

### Consequences

- **156 dissection sessions now publish**: 90 from the autumn document over 30 teaching
  days, 66 from the spring one over 22. Each of the three anatomy groups gets exactly one
  session per teaching day, which is the rotation ADR-073 predicted from the annual
  program and could not prove from it.
- Both revisions are predicted to validate with no findings: no group attends two hours on
  one day, every session is 50 minutes, every selector is one of the three the source
  states, and every date falls inside the academic year.
- The spring document states `9 Nisan 2025` where it means 2026, and its own weekday says
  so. That day is refused whole — three sessions — and named. It is the fourth Grade 2
  document to carry a date whose year is a year out.
- **Neither source is in `config/schedule-sources.json`,** so neither profile can run
  outside a fixture. The catalog requires an absolute HTTPS URI and these documents are
  handed out once a semester with no published location (ADR-076). Two things follow: no
  real revision can be produced yet, and `supportedAudienceSelectors` cannot be declared,
  so the unknown-selector rule is not enforced for `anatomyGroup` — the profile's own
  three-value bound is the only guard. Both are fixed by the same missing piece: an
  administrative upload path.
- `grade1_anatomy_v1` stays unimplemented. The source notes record it as the same
  structural family, but no Grade 1 anatomy fixture has been identified, and a profile is
  registered only when a fixture backs it.
- No engine version change, no .NET change and no database migration.

---

## ADR-079: An administratively uploaded source names itself, and Grade 2 Turkish onboards

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Implements:** the `AdministrativeUpload` transport and its catalog rule, the two
Grade 2 anatomy catalog entries, the Grade 2 Turkish supported profile, and their
tests
**Extends:** ADR-048 (evidence-based selectors), ADR-055 (server-owned supported
schema), ADR-076 (DOCX conversion), ADR-078 (the anatomy profiles)

### Context

Two things were blocked on each other. The anatomy documents could not be
catalogued, because the catalog requires an absolute HTTPS URI and these documents
are handed out once a semester with no published location (ADR-078). And Grade 2
students could not onboard, because the supported-profile schema admits only
cohorts a catalogued source declares (ADR-048) — and `anatomyGroup` is declared by
exactly those two uncatalogued documents.

The product answer to the first is that an administrator will upload each file
through the admin panel at the start of the semester. That is a real acquisition
path, not an absence of one, and the catalog had no way to say so.

The temptation was to give the entries a plausible Drive URL. That was tried and
reverted while implementing ADR-076: it is a false provenance claim, and every
downstream record would have carried it.

### Decision

Add a fourth transport, `AdministrativeUpload`, for a document that is handed out
rather than published. Such a source **names itself instead of naming a location**:
its URI is `urn:sirkadiyen:upload:{sourceId}`, and the catalog refuses any other
value for it — including an HTTPS one, which would claim a fetchable origin the
document does not have. The URN must spell out its own source ID, because it is
pure identity: a copied entry that kept another source's URN would attach one
document's evidence to the other, and nothing downstream could detect that.

A fetched source keeps the absolute-HTTPS rule unchanged. The rule is now chosen by
transport rather than applied to every source, so each transport states what it can
actually do.

The poller reports `AwaitingAdministrativeUpload` for such a source rather than
`UnsupportedTransport`. Both mean "nothing was read", but only one is true: the
transport is not missing an implementation, the document is missing an upload.

With the two anatomy sources catalogued and declaring `anatomyGroup` `1`, `2`, `3`,
**Grade 2 Turkish joins the supported-profile schema** with three required
selectors: `practiceGroup` `A`-`H` and its `practiceSubgroup` `A1`-`H2`, evidenced
by the practice table and the vertical-corridor calendar, plus the independent
`anatomyGroup`. The anatomy group is not a subdivision of the practice group — the
dissection rotation assigns it regardless of which letter a student carries — so it
is a third dimension rather than a deeper dependency chain.

**Grade 2 English stays out.** Its only current-year source is the annual program,
which states no cohorts; its practice fixture is from 2024-2025, and ADR-048
forbids promoting a prior year's values into this year's allowlist. Admitting it
with no selectors would let an English student onboard and receive a calendar
missing every practice and dissection session, which reads as complete.

The schema version moves to `1.1`. It is recorded on every stored profile, so a
profile written before Grade 2 existed stays identifiable as one.

### Consequences

- The two anatomy sources are catalogued, so `anatomyGroup` is validated against
  declared evidence on both sides: a student cannot select a fourth group, and a
  revision cannot publish one.
- **A Grade 2 Turkish student can now complete onboarding**, and the four Grade 2
  Turkish revisions have an audience. They still cannot be produced from the real
  documents: acquisition for Drive, HTTP and administrative upload does not exist
  yet, which is now the only remaining step for Grade 2 Turkish.
- Nothing polls an uploaded source, so cataloguing one starts no external traffic
  and creates no snapshot. The entry is a declaration of what the source is, not a
  claim that it has been read.
- The upload endpoint, its authorization, and the rule that an uploaded snapshot
  belongs to the source that names it are not implemented here. Until they are, the
  only way to produce these snapshots stays `tools/Sirkadiyen.SnapshotTool`.
- The four DOCX snapshot fixtures were regenerated through the catalog. Their cells
  are byte-identical; two `INFORMATION` diagnostics per fixture carried wording the
  converter stopped emitting, so the committed evidence was not reproducible from
  the committed document. It is now.
- The onboarding form needed no structural change: it renders class years and
  dimensions from `GET /api/profile/options`. It did need Turkish labels for the
  selector keys, which it had been rendering raw — tolerable for `practiceGroup`,
  not for `anatomyGroup`.
- No parser change, no engine version change, no database migration. The transport
  column already stores its enum as text.

---

## ADR-080: One upload, one document, every source it serves

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Implements:** the administrative upload endpoint and its audit trail, the
`sharedDocumentGroup` fan-out, `DocxSnapshotConverter.ConvertUpload`, the worker's
parse path for uploaded sources, the two English anatomy sources, and migration
`AddSourceDocumentUploads`
**Extends:** ADR-076 (DOCX conversion), ADR-079 (the upload transport)

### Context

ADR-079 catalogued the anatomy documents but left them unacquirable: the entry
declared what the source is, and nothing could turn a real file into a snapshot
outside the development tool.

The same document is handed to the Turkish and the English program. Serving both
is not a matter of tagging one revision: `CalendarAudienceResolver` matches a
canonical record to a student only when the record's program language equals
theirs, so a Turkish-sourced record can never reach an English student. Each
program needs its own source, its own snapshot and its own revision.

That is two sources for one file, and the naive consequence is asking an
administrator to upload the identical document twice — making double work the
normal case and a half-finished pair the routine failure.

### Decision

**The upload endpoint acquires; the worker does everything else.**
`POST /api/sources/{sourceId}/document` converts the file and stores it as an
immutable snapshot, then stops. The worker's next cycle finds the stored snapshot
and runs the same parse run, validation thresholds and publication rules as a
polled source. An uploaded document is therefore not a privileged path into the
schedule: the only thing an administrator can do is decide what the source
contains, which is exactly what a poll decides for a fetched source.

**Sources whose document is literally the same file declare a
`sharedDocumentGroup`, and one upload becomes a snapshot for every member.** The
group is symmetric — uploading to either member serves both — so there is no
primary source whose absence breaks the other. The catalog refuses the three ways
of getting it wrong: a group on a fetched source (which acquires its own copy and
shares nothing), a group of one (what a mistyped group name looks like), and two
members serving the same class year and language (which would publish every
lesson to those students twice).

Each target is stored in its own transaction rather than all in one. A target's
evidence is independently valid, and re-uploading is idempotent, so a partial
fan-out is completed by repeating the upload rather than by a rollback that would
throw away evidence already stored.

**An upload is audited per target.** `source_document_uploads` records who
uploaded, the submitted file name, the byte count, the SHA-256 of the bytes, the
snapshot it became, and whether the content was new. A row is written even when
the content matched, because "an administrator re-uploaded an unchanged file" is
what explains an absent revision. The digest of the file is deliberately not the
snapshot's content hash: one identifies the delivered bytes, the other identifies
the normalized content.

**An uploaded snapshot says how it arrived.** The converter emits
`snapshot.administrative_upload` instead of the local-fixture diagnostic, and that
diagnostic is part of the hashed content, so an uploaded snapshot is never
mistaken for the committed development conversion of the same document. The
converter is renamed `DocxSnapshotConverter`, since a class called "Local" now
reads production uploads.

A frozen pipeline accepts no upload: an upload is an acquisition (ADR-034). The
endpoint is SuperAdmin-only, antiforgery-protected, and bounded at 8 MB against
documents that are tens of kilobytes.

### Consequences

- **An administrator uploads the anatomy document once and both programs get it.**
  Four sources, two documents, two uploads per semester.
- The English anatomy sources exist but reach nobody yet: Grade 2 English is still
  absent from the supported-profile schema (ADR-079), so its revisions publish to
  an empty audience. That is the same state Grade 2 Turkish was in before ADR-079,
  and it is now the only thing between an English student and their dissection
  sessions — along with the current-year practice fixture ADR-048 requires.
- `G2-ANATOMY-AUTUMN` remains the Turkish source's identifier while its English
  counterpart is `-EN`, which does not match the `G2-TR-*`/`G2-EN-*` house style.
  Renaming would rewrite the committed snapshot fixtures and goldens that carry the
  identifier, and stable evidence identifiers are worth more than symmetry.
- The upload endpoint has no UI. An administrator uses it over the API until the
  admin surface is built.
- A refactor of the poller made the parse tail shared between the fetched and the
  uploaded path. Its first version reused the freshly acquired document even when
  the store reported the content unchanged; an existing test caught it. The parse
  must read the stored snapshot, because that is the evidence the parse run is
  keyed to.
- `ScheduleSourceStore` now also copies `SupportedAudienceSelectors` when reseeding
  the catalog. It never did, so an edited cohort allowlist applied to a fresh
  database and silently not to a running one.

---

## ADR-081: The upload surface asks the catalog which sources accept a document

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Implements:** `GET /api/sources/uploadable`, the `/admin` document-upload module,
and multipart support in the frontend API client
**Extends:** ADR-080 (administrative acquisition), ADR-066 (the frontend foundation)

### Context

ADR-080 left the upload endpoint with no UI, so the only way to acquire a
handed-out document was an API client. That path is worse than inconvenient: the
session is an HTTP-only `__Host-` cookie with antiforgery (ADR-023), so driving it
by hand means reproducing a cookie and a double-submit token the browser already
holds. An operator who cannot authenticate ends up looking for a way to weaken the
cookie rules, which is exactly what ADR-052 warned against. The browser is the
authenticated client; the admin panel is where the upload belongs.

The UI needs to know which sources accept an upload. That is catalog knowledge:
four sources today, all Grade 2 anatomy, and it changes at academic-year rollover
and whenever a new handed-out document is catalogued. Restating it in the frontend
would be a second copy of server-owned configuration, and the copy that drifts is
the one that attaches the wrong evidence to a source.

### Decision

**The server answers which sources are uploadable.** `GET /api/sources/uploadable`
returns the catalog entries whose transport is `AdministrativeUpload`, ordered by
identifier so the rendered list does not depend on catalog order. It is a
projection, not a new store or a new abstraction: the same SuperAdmin group, the
same `IScheduleSourceStore.ListAsync`. The transport decides, not the polling flag,
because an upload source is never polling-enabled.

**The projection carries what the operator must know before uploading**, and
nothing that would be a false claim. It carries the display name, academic year,
class year, program language, the expected document format, and the
`sharedDocumentGroup` — so the panel can say "this document also serves
`G2-ANATOMY-AUTUMN-EN`" before the fan-out happens rather than after. It
deliberately omits the poll timestamps: an upload source is never polled, so
`LastPolledAtUtc` and `LastChangedAtUtc` are permanently null and rendering them
would read as "never acquired" for a source uploaded this morning. The upload
history endpoint is the answer to when a document last landed.

**The panel reports acquisition, not publication.** A successful upload renders
one line per target with its `Stored`/`Unchanged` outcome and says the worker will
parse it on its next cycle under the same rules as a polled source. An upload whose
every target is `Unchanged` says so explicitly, because "no revision followed" is
otherwise indistinguishable from a failure. This is AI_GUIDELINE §16: never claim
synchronization succeeded before backend confirmation, and the backend has
confirmed storage only.

**The client-side checks mirror the server's and do not replace them.** The file
picker accepts `.docx`, and the extension, emptiness and 8 MB bound are checked
before the request so a rejected document is not uploaded first. The endpoint
enforces all three regardless; the UI renders the reported problem detail, and maps
409 to the freeze ("lift it and upload the same document again") and 403 to the
missing SuperAdmin role.

**Multipart goes through the existing typed client.** `request` sends a `FormData`
body as-is and skips its JSON `Content-Type`, so the browser writes the boundary
itself, and the CSRF header plus its one stale-token retry apply to the upload
exactly as they do to every other mutating call.

**Tailwind was not introduced.** The frontend styles with a small hand-written
system in `globals.css`; adding a CSS framework for one panel is the speculative
dependency §4 refuses. The module reuses `card`, `status-row`, `muted`, `error` and
`button.primary`.

### Consequences

- **The anatomy documents are uploadable from the browser**, with the session the
  browser already holds. No manual cookie or CSRF handling.
- The catalog stays the single source of truth for what is uploadable. A new
  handed-out source appears in the panel by being catalogued, with no frontend
  change.
- One more admin read endpoint exists. It is SuperAdmin-only like the rest of the
  group, and it exposes no snapshot content — only what the catalog already
  declares about a source.
- The API must be restarted for the new route to exist; a running instance answers
  404 and the panel then reports that no source accepts an upload.
- Frontend behaviour is still untested automatically (no frontend test runner
  exists). The projection and its ordering are unit-tested in
  `Sirkadiyen.Api.UnitTests`; the component is not.
- The interrupted-fan-out risk from ADR-080 is unchanged, but now visible: the
  panel reloads the per-target audit trail after a failure too, so which targets
  landed is on screen rather than in the database.

---

## ADR-081 amendment: the antiforgery token is identity-bound, and the shared anatomy program stays two sources for now

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Amends:** ADR-081 (the upload surface), and defers a question against ADR-080

### The upload failed with an antiforgery error, and the cause was not the upload

The first real upload returned "An error occurred while processing your request."
The backend log named it exactly: *the provided antiforgery token was meant for a
different claims-based user than the current user*.

An antiforgery request token binds the claims-based user it was issued to. The
frontend client cached one token for the whole page lifetime, and the first token
of a page is the one minted for `POST /api/auth/google` — issued while the caller
is still **anonymous**. After sign-in that token belongs to nobody, so every later
mutation sends a token minted for a different user.

This had been invisible because every other mutating endpoint takes JSON: the
failure comes back as a 400 the client already retries once with a fresh token, so
the stale token was silently corrected on first use. The upload endpoint cannot do
that. Its token is validated while binding `IFormFile`, which throws
`BadHttpRequestException` instead of returning a problem this client could
recognize — and in Development `ThrowOnBadRequest` turns that into a 500 with a
generic body. The endpoint that most needs the retry is the one endpoint the retry
cannot reach.

**Two fixes, at the cause and at the shape.** The cached token is discarded on the
identity transition that invalidates it: a successful sign-in clears it, as logout
already did. And a multipart request always takes a freshly issued token rather
than a cached one, because its antiforgery failure is the one that is not
gracefully recoverable. An upload is rare, so one extra same-origin GET is the
cheap side of that trade.

The generic 500 shape is Development-only; in Production the same failure is a bare
400, which the existing retry would have absorbed. That is worse, not better: it
would have hidden a stale-token bug behind a silent retry in production while
failing loudly only on the developer's machine.

### The Grade 2 anatomy program is shared between the programs

Confirmed with the faculty owner: the anatomy dissection program is **one program**
serving the Turkish and English tracks, with the same `1`/`2`/`3` groups. The four
catalog entries are not a claim otherwise — they differ only in program language,
and they exist because `CalendarAudienceResolver` matches a record to a student
only on `record.ProgramLanguage == profile.ProgramLanguage`, so a Turkish-sourced
record can never reach an English student.

**Decision: the two entries per document stay, and the operator surface stops
showing them.** The upload panel groups by `sharedDocumentGroup`, so one document
is one option — "Dönem 2 · Türkçe + İngilizce · anatomi salon grup saatleri güz" —
and it names the sources one upload will serve. It also merges the audit trail of
every member, which is what makes ADR-080's interrupted fan-out visible: one member
holding the document and the other not now shows on screen.

The alternative — one source whose records apply to both programs — is the model
that matches the fact, and it is deliberately deferred rather than rejected. It
needs a shared-audience concept on the source and the canonical record, the
resolver, the published-records read store, the audience-overlap rule, a migration
for the `ProgramLanguage` column and its check constraint, and a stable-identity and
content-hash review. That is the code that decides who gets which events, and
nothing is currently lost by waiting: Grade 2 English is not in the
supported-profile schema (ADR-079), so both -EN revisions publish to an empty
audience either way.

**Revisit when Grade 2 English is admitted**, which ADR-048 gates on a current-year
English practice fixture. Admitting English while the duplication stands is what
makes it a real duplication rather than a dormant one.

### Consequences

- Any future identity transition in the client must discard the cached antiforgery
  token. Sign-in and logout are the two that exist.
- A shared document is one choice in the panel and still several sources in the
  catalog. The label states every program it covers, so the operator is not asked
  to know the fan-out rule.
- The deferred audience question is recorded as an open risk, not as done.

---

## ADR-082: Check for newly queued Calendar work independently of source polling

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Implements:** a configurable idle Calendar-queue check, retained adaptive source
deadline, and worker scheduling regressions
**Amends:** ADR-070

### Context

ADR-070 resumed Calendar work promptly after a pass explicitly returned
`InProgress` or `PartiallyDispatched`. It did not cover work created after an empty
pass. If a student requested initial synchronization while the worker was sleeping
on the adaptive source interval, no in-process signal interrupted that delay. On a
weekend the new request therefore stayed `InProgress` for up to one hour; restarting
the worker only appeared to fix it because startup begins with a full pass.

Polling every source every few seconds would hide the latency by creating unnecessary
Google Sheets traffic and parser work. Raising Calendar mutation budgets would not
help, because the missed request had not been admitted at all.

### Decision

The worker retains an absolute next-source-poll deadline after every source cycle.
Between those deadlines it checks the database for initial sync, incremental dispatch
and reconciliation work on
`SIRKADIYEN_SYNC:CALENDAR_IDLE_CHECK_INTERVAL`, five seconds by default. An ordinary
quota yield continues to use `CALENDAR_CATCH_UP_INTERVAL`.

An idle Calendar check is Calendar-only: it does not acquire sources, publish
revisions, calculate diffs or prune snapshots. When the retained source deadline is
closer than the selected Calendar interval, the worker sleeps only until that deadline
and the next pass includes source polling.

### Consequences

- A sync request created just after an empty pass is admitted within the idle-check
  interval without restarting the worker.
- Weekend source polling remains hourly; the shorter loop adds database queue scans,
  not source downloads or parser calls.
- Source deadlines do not drift behind repeated Calendar-only cycles.
- Deployment requires only a worker restart; there is no database migration.

---

## ADR-083: A Drive document is fetched by identifier and trusted only after it is checked

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Implements:** `IGoogleDriveFileClient` and `GoogleDriveHttpClient`,
`IDriveDocumentAcquirer` and `DriveDocumentAcquirer`,
`GoogleSourceCredentialFactory`, `DocxSnapshotConverter.ConvertDownload`, the
`GoogleDriveFile` branch of `ScheduleSourcePoller`, and the
`UnsupportedDocumentFormat` poll outcome
**Extends:** ADR-014 (normalized snapshot contract), ADR-015 (transport is not
format), ADR-034 (freeze before acquisition), ADR-076 (DOCX conversion), ADR-077
(the vertical-corridor profile), ADR-079 (a transport states what it can do)

### Context

The two vertical-corridor calendars are the last Grade 2 Turkish sources with no
way to be read. ADR-076 converted them and committed the fixtures; ADR-077 wrote
the profile that parses them, 12 candidates from autumn and 30 from spring. What
was missing was acquisition: `ScheduleSourcePoller` answered
`UnsupportedTransport` for anything that was not Google Sheets, so a profile that
works ran against nothing.

They differ from the anatomy documents in the way that decides the mechanism.
The anatomy group lists are handed out once a semester and never edited, which is
why ADR-079 gave them an upload. Student Affairs edits the vertical-corridor
calendars during the year, and both are published at a Drive URL the catalog has
recorded since the inventory. A document that changes and has a location must be
re-acquired, not uploaded again by hand each time it changes.

### Decision

Read Drive over its v3 REST API with an ordinary typed `HttpClient`, not the
`Google.Apis.Drive.v3` client library. The pipeline needs two calls; the library
would add a package, a service object and a second way of holding the credential
to reach them. `Google.Apis.Auth` is already referenced and is kept, because
minting and refreshing an access token is the part worth not writing.

**One credential for every fetched source, scoped read-only to Sheets and
Drive.** A program may be a sheet this year and a Drive file the next, and two
grants would be two things to keep alive and two ways for polling to half-work.
`drive.readonly` covers everything the credential can see; Drive has no narrower
scope that can download somebody else's shared file, so service-account mode is
the least-privilege mode in practice — the account sees exactly what was shared
with it.

**The token is attached by a delegating handler.** It is never held by, passed
to, or formatted by the client that builds the request, so there is no code path
along which it could reach a log line or an exception message.

**Metadata first, then content.** The metadata call is not a courtesy; it is what
makes the download trustworthy. It answers whether the file is in the trash,
whether it is the format the catalog declared, and how long the bytes should be.
An acquisition is refused — never converted into a snapshot — when:

- the file is **trashed**. Drive keeps serving its last content indefinitely, so
  reading it would let a document nobody publishes any more keep feeding
  calendars;
- its **MIME type is not the one the document format implies**. A calendar
  someone converted into a Google Doc cannot be downloaded at all, and saying so
  is more useful than a 403 that reads like a permission problem;
- it is **larger than 8 MB**, the bound the upload path already applies. Checked
  against the declared length and again against every chunk, so a response that
  declares no length cannot make the host read without limit;
- the bytes do not match the **length or digest** Drive stated;
- the payload is **not an Office container**, which is what a sign-in or error
  page served with a success status looks like.

Everything else stays the ordinary HTTP error the next poll retries. The refusals
are the cases that need a person, and each message says what that person has to
do. Google's error body is not repeated into any of them: it can name the file,
its owner and the authenticated principal.

**The snapshot records that it was downloaded, and nothing else about the file.**
Acquisition diagnostics are part of the content hash. A file name, a modification
time or a digest recorded as provenance would differ between two downloads of an
unedited document, and the pipeline would store a snapshot, run a parse and
produce a revision every poll, each changing nothing. For the same reason Drive
metadata is **not** used as a change signal at all, which answers a question open
since the inventory: the converted content hash is the better signal, because it
ignores a re-save that altered no text, and `modifiedTime` does not.

**A missing transport and a missing reader are separate outcomes.** The poller
gains `UnsupportedDocumentFormat` beside `UnsupportedTransport`. The Grade 3
workbooks are on this same transport and are now downloadable, but nothing
converts a workbook from bytes and no profile reads them; calling that an
unsupported transport would point the next reader at work that is already done.
This follows ADR-079's precedent, where `AwaitingAdministrativeUpload` replaced a
technically true but misleading `UnsupportedTransport`.

The poller branches on transport with two named dependencies rather than a
registry of adapters. Two are legible; the HTTP transport is where a third would
start earning an abstraction.

### Consequences

- **The two vertical-corridor sources become live once the worker restarts with a
  Drive-scoped credential.** They are polling-enabled, their profile is
  implemented, and the snapshots they produce go through the same parse,
  validation thresholds and publication rules as any other source. This is the
  intended effect and it is not a quiet one: Grade 2 Turkish students can onboard
  (ADR-079), so these revisions have an audience, and nine dated rows that
  contradict their own weekday are refused by the profile (ADR-077).
- **The credential needs Drive access before this works.** In service-account
  mode, which is what is configured, the account asserts its own scopes and needs
  no re-consent — but the Drive API must be enabled on its Cloud project, and the
  two documents must be shared with the account's address. In refresh-token mode
  the grant is fixed when it is issued, so a Sheets-only token gets a 403 for
  every Drive file until it is re-issued with both scopes. Either way it surfaces
  as an access-denied acquisition naming the missing scope, not as a missing file.
- The first real acquisition stores a snapshot whose content hash differs from
  the committed fixture's, because the origin diagnostic differs. That is
  correct — they are different acquisitions of the same document — and the golden
  parse tests are unaffected, since they parse the fixture.
- A Drive source is addressed by `externalId`. A source without one is refused
  rather than having an identifier derived from its `sourceUri`, which is the
  link a person opens.
- `GoogleSheetsServiceFactory` no longer builds its own credential. The Sheets
  adapter is otherwise untouched and its behaviour is unchanged.
- No database migration, no parser change, no contract version change. The
  transport was already in the catalog and in the enum; what was missing was the
  code behind it.
- The failure taxonomy is per-file and typed, so an operator can tell "not shared
  with us" from "moved" from "arrived damaged" without reading a stack trace.

---

## ADR-084: Trust the English practice workbook's schedule content, not its filename

**Status:** Accepted and implemented
**Date:** 2026-07-26
**Implements:** `grade2_practice_v1` 1.2.0 for `G2-TR-PRACTICE` and
`G2-EN-PRACTICE`, the normalized English fixture and its golden parse
**Amends:** ADR-048, ADR-074, ADR-075, ADR-079

### Context

Grade 2 English was kept outside the supported-profile schema because the only
committed practice workbook was classified as 2024-2025 from its filename:
`2024-2025 Term 2 Medicine Program in English PRACTICUM TABLE.xlsx`. ADR-048
correctly forbids using a prior year's cohort values for the current allowlist.
The catalog nevertheless points at that file as the 2025-2026 source, so the
classification needed to be checked against the document rather than repeated.

The worksheet itself tells a different story. It has 39 dated practice slots
from 17 September 2025 through 22 May 2026, and its block order matches the
2025-2026 Grade 2 program. A single cell does contain `23.10.2024`, but it is in
an `Anatomi (6)` row: that row contains dissection dates rather than practice
audiences and `grade2_practice_v1` already defers it whole to the anatomy
sources. No dated practice slot is outside 2025-2026.

Running the existing 1.1.0 profile against a normalized snapshot made the real
implementation gap visible. It published only the three whole-program cells.
The other 47 group cells are `i1`, `i2`, or once `i1+i2`, while the Turkish
cohort reader accepts only `A`-`H`. Two otherwise complete slot headers also use
compact presentation: `23Aralık 2025` has no day/month space, and
`18 Mayıs 2026 Pazartesi 13:30-15:20` keeps date and time on one line.

### Decision

Treat the workbook as current-year evidence because schedule content, not a
delivery filename, is the immutable evidence that dates lessons. Keep the
original file and its original name; renaming it would hide the discrepancy
rather than document it.

Bump `grade2_practice_v1` from 1.1.0 to 1.2.0. The two workbooks share the same
slot-column structure and therefore keep one profile, but audience grammar is
selected from authoritative source context:

- Turkish accepts the bounded lettered `A`-`H` model, including the combined
  runs the source writes.
- English accepts only the independent practice-group values `İ1` and `İ2`,
  canonicalizing the source spellings `i1` and `i2`. They are `practiceGroup`
  values, not children of a synthetic `İ` group.
- A cohort token belonging to one program is refused under the other program,
  so source-language context cannot leak a lesson across audiences.

The slot reader accepts the two compact forms by separating only text the cell
already states: it inserts a boundary between a day number and an immediately
following month word, and extracts a time range only when it is the trailing
component of a line. It does not correct a year, infer a missing component, or
weaken the weekday-contradiction refusal.

Declare `practiceGroup: [İ1, İ2]` on `G2-EN-PRACTICE`, add its deterministic
normalized snapshot and golden parse, and retain `dayFirst` from ADR-075. The
version bump forces retained Turkish and English snapshots to reparse; the
Turkish candidate set does not change.

Do **not** add Grade 2 English to the supported-profile schema in this change.
The practice source is now sound, but two audience paths remain unsafe:

- the English annual workbook embeds `İ1`-`İ5` in lesson titles while
  `grade2_yearly_v1` currently emits no group selector for them;
- the shared vertical-corridor documents contain English cohort entries, but
  only Turkish catalog entries and Turkish cohort parsing currently publish
  from them.

Opening onboarding before both are resolved would label an over-broad or
incomplete calendar as synchronized.

### Consequences

- The English practice fixture publishes 49 candidates: 45 practices and four
  examinations, dated 2025-09-17 through 2026-05-22. Group-specific candidates
  carry 24 `İ1` and 24 `İ2` selectors; three candidates cover the whole English
  program.
- All 39 slot headers are read. The compact cells are regression-tested, and
  no weekday contradiction or out-of-academic-year practice slot is hidden.
- Thirty-five `*` cells remain deferred to the vertical-corridor source, five
  anatomy rows remain deferred to anatomy, two PDÖ rows remain out of scope, and
  one bare `TELAFİ` remains refused because it names no audience. These are
  explicit diagnostics, not missing parser coverage.
- Grade 2 English practice is parser-complete, but Grade 2 English onboarding
  remains deliberately unavailable. English anatomy revisions still have no
  users until the annual and vertical-corridor audience gaps are closed.
- No database migration and no parser contract or engine-version change. The
  source catalog reseed updates the profile version and selector allowlist.

---

## ADR-085: Use the student number to suggest roster data, while keeping profile groups editable

**Status:** Accepted, not yet implemented
**Date:** 2026-08-03
**Amends:** ADR-055, ADR-056, ADR-084

### Context

The faculty's student lists already associate a university student number with
the student's name, surname, and some cohort assignments. Grade 2 English makes
the distinction important: students first belong to one of two general groups
(`İ1`/`İ2`) and independently to one of three general subgroups
(`i1`/`i2`/`i3`). Some practices use the two-way division and others use the
three-way division. Team Work uses a separate five-way `i1`-`i5` rotation whose
assignment source is not yet known.

Requiring a student to retype every value the faculty has already published is
unnecessary friction, but treating a roster as an unchangeable authority would
also be unsafe: lists can be stale, incomplete, corrected late, or omit an
independent rotation. The product must therefore distinguish a roster-derived
suggestion from the profile the user finally confirms.

### Decision

At the beginning of academic-profile onboarding, the user enters only their
ten-digit university student number. The backend looks that number up in the
configured faculty student lists and returns every profile field the matching
list explicitly states, together with the student's name and surname for visual
confirmation.

The returned name and surname are ephemeral display data. They are not written
to the Sirkadiyen database, copied into the student profile, or included in
application logs. The existing student number remains part of the persisted
profile under ADR-056.

Roster-derived group values prefill the onboarding form but remain editable.
The user confirms or changes them before saving, and the confirmed selector
document is what the backend persists and later uses for audience resolution.
The backend still validates every submitted key, value, dependency, and
supported combination against its server-owned profile schema; editability does
not mean accepting arbitrary selector data.

Any required group that no available student list states is entered by the
user. Responsibility for confirming those additional or corrected group values
belongs to the user. The UI must distinguish values suggested from a roster
from values that still need user input, and must not imply that a successful
lookup proves the final profile is complete or current.

The student-list lookup is a backend concern, not a schedule-parser concern.
The Python parser continues to interpret schedule documents and emit audience
selectors; it does not receive student identities, query rosters, or decide a
student's assignments.

Grade 2 English models the confirmed divisions as separate selector dimensions:
one two-way general-group dimension and one three-way general-subgroup
dimension. They must not be collapsed merely because their source labels differ
mostly by typography or casing. Team Work will use a third independent selector
dimension only after its assignment source and publication rules are confirmed.

### Consequences

- Onboarding becomes student-number-first: lookup, review/edit, validation, then
  persistence.
- A lookup miss does not invent identity or group data. The UI reports the miss
  and allows the user to enter supported group values manually.
- A duplicate or ambiguous student-number match is an error requiring review;
  the backend does not choose one silently.
- Name and surname may be shown in the lookup response but are never retained by
  Sirkadiyen.
- Roster suggestions are not authorization claims and do not bypass profile
  validation.
- Schedule parsing remains deterministic and free of user data. Parsed lesson
  audiences are matched later against the user-confirmed selectors stored by
  the backend.
- Grade 2 English onboarding remains closed until the two-way and three-way
  audience paths are parser-complete and the unresolved Team Work rotation has
  a safe product rule; accepting this onboarding interaction does not make an
  incomplete calendar complete.

---

## ADR-086: Department identity is catalogued; its Calendar color is layered and configurable

**Status:** Accepted and implemented
**Date:** 2026-08-03
**Amends:** ADR-072

### Context

ADR-072 gave every source-stated department a deterministic Google Calendar label and
color, but the color policy lived entirely in code. Changing a palette required a
deployment, and every student received the same presentation even though color is a
personal readability preference. At the same time, letting clients submit arbitrary
department names would fragment one department into several labels and make source
normalization less reliable.

The Faculty's official inventory contains 45 departments: 10 Temel Tıp Bilimleri,
21 Dahili Tıp Bilimleri and 14 Cerrahi Tıp Bilimleri. Source schedules write several
of those names with `AD.`, full suffixes, English translations, abbreviations and
minor spelling variants.

### Decision

Keep department **identity** in a reviewed, code-owned catalog. Each entry has a stable
slug key, official display name, division, system color and explicit Turkish/English
aliases. Normalization removes typography and an explicit department suffix, but an
unknown value is not guessed into a known entry; it retains ADR-072's deterministic
fallback label and color.

Resolve a known department's effective color in this order:

1. the current user's override;
2. the SuperAdmin-managed default;
3. the code-owned system default.

Exam and free-study colors remain product-owned categories rather than departments.
An integrated session that names several departments remains its own deterministic
presentation category.

Persist admin defaults and per-user overrides separately. Every mutation creates an
append-only audit entry; an admin mutation additionally requires a reason. Validate
department keys against the server catalog and colors as uppercase `#RRGGBB`. The
browser never defines a new key.

A color mutation is presentation work, not a schedule revision. It marks the affected
completed calendar (all completed calendars for an admin default) due for the existing
non-destructive inventory. The worker rebuilds desired events with the effective palette;
the Calendar adapter updates the calendar-scoped label definition and inventory repairs
events without creating a semantic diff or deletion authority.

### Consequences

- Administrators can change faculty-wide defaults without redeploying, and users can
  personalize only their own managed calendar.
- User choices survive later admin changes because the user layer has precedence;
  resetting an override reveals the current admin or system default.
- Stable label IDs do not change when RGB values change, so event identity and ledger
  mappings are unaffected.
- The catalog is intentionally version-controlled. Adding/renaming a department or an
  alias is a reviewed identity change; changing its admin color is ordinary runtime data.
- Updating a global default can make many calendars immediately due for inventory. The
  existing bounded inventory worker absorbs that fan-out instead of the API calling
  Google Calendar synchronously.

---

## ADR-087: Admin operations live in dedicated workspaces; overview is orientation only

**Status:** Accepted and implemented
**Date:** 2026-08-03
**Amends:** ADR-066, ADR-086

### Context

The first runnable administration page embedded operational freeze, source upload,
revision approval, department-color editing and SuperAdmin self-activation in one card
grid. This made the overview increasingly dense and left each workflow too little room
for context and safety cues.

### Decision

Keep `/admin` as an orientation-only map. Give every navigation item a stable route and
place each backend-supported operation in its own workspace. A route without an
authoritative backend renders an explicit capability-oriented empty state; it never
shows invented metrics or enables a mutation.

Use one shared client-side admin frame for the backend-authoritative SuperAdmin guard,
freeze-state banner, operator identity and sign-out behavior. Backend authorization
remains mandatory and is not replaced by this navigation guard.

Treat department colors as a palette-management workflow rather than a dense settings
list. The editor supports search, medical-division filtering, override filtering,
calendar-like previews and explicit save/reset controls. Admin changes continue to
require the server-audited reason defined by ADR-086; changing the browser color picker
alone performs no mutation.

### Consequences

- An operator can bookmark the correct admin context without losing global freeze visibility.
- General overview remains scannable as more administration domains are added.
- Missing backend domains are visible without implying that they work.
- The shared frame reads freeze state on each route load; a future system-summary endpoint
  may consolidate this with other overview health data.

---

## ADR-088: Integrated sessions and practices are configurable presentation categories

**Status:** Accepted and implemented
**Date:** 2026-08-03
**Amends:** ADR-072, ADR-086

### Context

Integrated sessions were labelled from the complete ordered department combination.
That preserved source detail but produced a different Calendar label and derived color
for every combination. Practices were resolved after department inference, so a
physiology practice inherited physiology's lecture color and did not stand out as a
practice. Anatomy practices (dissections) had the same problem.

### Decision

Introduce two code-owned presentation-category identities beside the reviewed
department identities: `integrated-session` and `practice`. They use the same layered
color precedence as departments: user override, administrator default, system default.
The existing audited color persistence and inventory-refresh path stores these bounded
keys; no new table or migration is required.

Resolve exams and free study first, then every practice type, then integrated sessions,
and only then department colors. A record explicitly typed `IntegratedSession`, or one
that states several departments, uses the single stable `Entegre oturum` Calendar label.
`Practice`, `AnatomyPractice`, `BedsidePractice`, `FacultyPractice`, and
`VerticalCorridor` use the single stable `Uygulamalar` label. Their system color is the
attention-oriented orange `#FF6D00`; the integrated-session default is `#5E35B1`.

Calendar summaries are presentation-only: ordinary practice titles become
`UYGULAMA - {SOURCE TITLE IN TURKISH UPPERCASE}`, while `AnatomyPractice` becomes the
exact title `DİSEKSİYON`. Canonical source titles, identities, and content hashes remain
unchanged.

### Consequences

- Department combinations no longer fragment integrated-session colors.
- Every application/dissection is visually recognizable independently of its course.
- Admin and user palette panels expose both categories above the department catalog.
- Changing either category color marks the same completed calendars due for bounded,
  non-destructive inventory; existing events are patched in place.
- Existing department-color tables retain historical names but accept only keys from
  the server-owned department or presentation-category catalogs.

---

## ADR-089: An account-access audit log and a read-only admin/observability surface

### Context

The backend pipeline was complete, but the product had no way to *see* the data it
already produced. There was no login record at all — `User.LastSignedInAtUtc` is
overwritten each sign-in — so `progress.md` Phase 1 "audit event model" and the Phase 10
access-log, user-detail, source-status, health-check and metrics items were unbuilt, and
the finished admin/dashboard screens in `web/` rendered as "Yakında" placeholders
(`web/GAPS.md`).

### Decision

Add read/query and observability surfaces over existing data, plus one new append-only
domain concept — an account-access and activity log — without changing any pipeline
behavior.

- **AuditEvent** (`Domain/Auditing`) is an append-only cross-cutting log distinct from the
  per-domain audits (license, freeze, colour, upload), which remain authoritative for their
  domains. It records the events that had no home: `SignIn`, `ReconcileRequested`, and
  `IpUnmasked`. The client IP is stored masked by default (IPv4 last octet / IPv6 host bits
  cleared) and the full address is encrypted at rest with a dedicated Data Protection
  purpose; revealing it is a separate, reason-required, itself-audited SuperAdmin action
  (`POST /api/admin/access-logs/{id}/unmask`). Category is stored as a string with no check
  constraint so new categories are a code change, not a migration.
- **Student reads** (`GET /api/schedule/upcoming`, `/api/schedule/changes`,
  `GET /api/calendar/sync/progress`, `GET /api/licenses/status`) and **admin reads**
  (`GET /api/admin/users(+detail)`, `/api/admin/licenses(+detail)`, `/api/admin/sources(+detail)`,
  `/api/admin/audit`, `/api/admin/metrics`) are thin read stores projecting existing tables.
- **User-initiated reconcile** (`POST /api/calendar/reconcile`) reuses the existing
  inventory "due" mechanism (nulling `LastCalendarInventoryAtUtc`, as a colour change does),
  so it needs no new column and no change to the worker: it only records intent and is
  gated by the same operational freeze.
- **Health/metrics**: `/health/live` and `/health/ready` (a real PostgreSQL connectivity
  check) split liveness from readiness; `GET /api/admin/metrics` is a JSON aggregation of
  operational counts. A correlation-id middleware stamps every request and log line.

### Honest limits (deliberately not fabricated, per AI_GUIDELINE §9, §21)

- Sync progress reports only what the mapping ledger records — total mapped events and how
  many were patched since first written. It cannot report "unchanged", "failed", or an
  applicable-record total, so those are omitted rather than invented.
- License status reports activation state and date only. Sirkadiyen access does not lapse
  after redemption (`License.ExpiresAtUtc` is a pre-redemption code deadline), so no "time
  remaining" is returned; time-limited access would be a separate decision.
- `GET /api/schedule/changes` reports creations and updates, not deletions: a removed event
  leaves no ledger row. `GET /api/calendar/sync/history` is deferred for the same reason — a
  faithful timeline needs a per-user activity log written by the sync services.
- `GET /api/admin/metrics` is a read aggregation, not a metrics-stack exporter. A
  Prometheus/OpenTelemetry `/metrics` exporter and host CPU/RAM/Redis gauges remain a
  separate decision.

### Consequences

- The access-log, user-list/detail, license-list/detail, source-status, audit-query,
  health and metrics gaps in `web/GAPS.md` §2–3 now have backing routes; the remaining work
  for those screens is the React surface and the hand-maintained `web/src/lib/types.ts` /
  `api.ts` contract mirror.
- New table `audit_events` (migration `AddAuditEvents`); the per-domain audit tables are
  unchanged and not migrated.
- The unified `GET /api/admin/audit` covers the AuditEvent categories only; the per-domain
  audits stay queryable through their own surfaces and could be unioned later.
- New product domains (contact, bulk event, user warning, finance) and the Python
  parser/ingestion profile gaps are out of scope of this change.

---

## ADR-090: Frontend contract integrations use Vitest and React Testing Library

**Status:** Accepted and implemented
**Date:** 2026-08-04

### Context

The Next.js frontend previously had only TypeScript and production-build verification.
ADR-089 introduced privacy-sensitive IP reveal and audited reconciliation interactions,
plus independent partial-failure read surfaces. Compile checks cannot verify these
interaction and honesty constraints.

### Decision

Use Vitest with jsdom and React Testing Library/user-event for frontend unit and component
tests. Keep tests at the typed browser-contract boundary; do not add a second schema
generator or a browser end-to-end service dependency. Continue to require TypeScript and
the Next production build beside the test suite.

### Consequences

- CSRF request construction, reconciliation outcomes, masked-IP reveal and honest
  source/metrics presentation have automated regression coverage.
- The frontend adds development-only test dependencies and an `npm test` command.
- Authentication, proxy and database integration still require a local smoke test;
  component mocks do not replace environment-level verification.

---

## ADR-091: Persistent browser sessions, scoped pipeline freezes and explicit service health

**Status:** Accepted and implemented
**Date:** 2026-08-04
**Amends:** ADR-034, ADR-043, ADR-052, ADR-089

### Context

An eight-hour session forced ordinary users to repeat Google sign-in despite returning from
the same browser. The global-only operational freeze also stopped unrelated academic programs
when an incident affected one line, and the admin server page could not distinguish the worker
or parser process from API/database readiness. Finally, source status showed parser warning
counts but not the persisted warnings that explained `CompletedWithWarnings`.

### Decision

- Issue the existing backend-managed persistent secure cookie with a 30-day sliding ticket.
  Keep it HTTP-only, `Secure`, `SameSite=Lax`, CSRF-protected and revalidated against the user
  row on every authenticated request. The short-lived Google ID credential is still discarded.
- Keep the audited global freeze as the emergency stop and add audited controls keyed by
  `(ClassYear, ProgramLanguage)`. A mutation is blocked when either global or exact-scope state
  is frozen. Shared uploads skip only frozen targets; unrelated source and calendar lines run.
- Host internal `/health/live` and `/health/ready` endpoints in the worker process. Readiness
  exposes its instance, start time, last in-process activity and current pipeline stage. The API
  probes that endpoint and the parser's existing `/health` endpoint only on explicit SuperAdmin
  refresh; do not persist health heartbeats, fold either dependency into API liveness, or claim
  CPU/RAM/Redis telemetry.
- Project warning details by deserializing the latest stored parser response. This read is
  evidence-only and never starts a new parser run.

### Consequences

- Returning users normally need no Google sign-in for 30 days in the same browser, with the
  window sliding during use. Explicit logout, role/user invalidation, cookie deletion, key-ring
  loss or expiry still ends the session.
- Scoped controls were introduced by `AddScopedFreezeAndServiceHeartbeats`; the later
  `RemoveServiceHeartbeats` migration removes the superseded heartbeat table. Both freeze
  histories remain append-only.
- API and worker processes must restart after deployment to load the new cookie policy,
  endpoints and internal Worker HTTP listener.
- Worker readiness reports process reachability and the latest in-process stage/activity. It is
  not proof that every queued job is progressing; queue and source counts remain separate
  operational signals.

---

## ADR-092: Worker composition uses focused pipeline tasks

**Status:** Accepted and implemented
**Date:** 2026-08-04

### Context

The Worker executable accumulated host configuration, dependency registration, health routing,
cycle scheduling, source processing and four Calendar workflows in two files. Although the
runtime behavior was correct, the composition made responsibilities difficult to review and
made isolated changes unnecessarily risky.

### Decision

- Keep `Program.cs` as a minimal composition entry point and move configuration parsing,
  validated option construction, dependency registration and health route mapping to focused
  Worker types.
- Keep `Worker` responsible only for hosted-service lifecycle, health-stage transitions and
  cycle scheduling.
- Represent catalog initialization, polling, publication, diff calculation, initial sync,
  incremental dispatch, reconciliation, inventory and retention as separate internal classes.
- Preserve the exact pipeline ordering and keep the existing PostgreSQL lease continuously held
  across incremental dispatch, Calendar reconciliation and inventory.
- Use one top-level type per Worker source file. Do not introduce a new scheduler, queue,
  persistence model or public contract as part of this structural refactor.
- Mirror responsibilities in physical folders and namespaces: host wiring belongs to
  `Composition`, option construction to `Configuration`, process state to `Health`, cadence
  types to `Scheduling`, and executable pipeline tasks to `Sources` or `Calendars`. Keep only
  the executable entry point and hosted lifecycle class in the project root namespace.

### Consequences

- Failures remain isolated at the same task boundaries, but log categories now identify the
  responsible task instead of reporting every message under `Worker`.
- Configuration keys, defaults and validation remain backward compatible and have focused unit
  coverage.
- Pipeline tasks are independently replaceable/testable through dependency injection; runtime
  behavior and deployment topology are unchanged.

---

## ADR-093: Finance module — an audited mutable cash ledger with derived balances, a separate accrual layer, and non-repeatable profit distribution

**Status:** Accepted and implemented (all seven phases; admin UI intentionally out of scope)
**Date:** 2026-08-04 (implementation completed 2026-08-05)

### Context

Sirkadiyen has revenue (license sales, sponsorships, donations) and costs (servers, domains,
charitable activity) that lived nowhere in the system. `web/GAPS.md` recorded finance as
"Entirely new domain … Needs revenue/expense models, profit-distribution + audit", ADR-089 put it
explicitly out of scope, and `progress.md` sequenced it last among new product domains. This
decision establishes the module's shape: a backend that can answer, for any period, where the
money is and where it went — across several real accounts held by two operators (cash boxes and
bank accounts) — and that can execute a profit distribution safely, once, with a complete audit
trail. Scope is backend only; the admin UI stays a placeholder until the API is proven.

There is no money type, no `HasPrecision` on a currency value, and no decimal-money precedent
anywhere in the codebase before this change.

### Decision

1. The ledger is **cash-basis**. `FinanceTransaction` is the business event; one or more signed
   `FinanceLedgerEntry` postings (one row per affected account) are its ledger effect. Kinds are
   `OpeningBalance`, `Income`, `Expense`, `Transfer`, `Distribution`, each with a fixed entry/leg/sign
   shape enforced by both the domain factory and a mirrored database check constraint.
2. **Balances are derived, never cached.** An account's balance is `SUM(Amount) WHERE OccurredOn
   <= X` over its ledger entries — no stored balance column, no opening-balance column (an opening
   balance is itself an `OpeningBalance` transaction). The `finance_accounts` row is still real: it
   is the `SELECT … FOR UPDATE` lock target that makes "read balance, then debit" safe under
   concurrency for transfers and (later) distribution payouts. Ordinary income/expense entry needs
   no lock and may legitimately produce a negative balance (overdraft is reported, not blocked);
   only operations that move money the ledger claims to have — transfers today, distribution
   payouts once Phase 6 lands — check the balance under that lock.
3. **Transactions are editable and hard-deletable**, not reversal-only. Correctness rests entirely
   on `finance_audits`, the module's own append-only audit log (distinct from the cross-cutting
   `audit_events` table), which records a full before/after image — including the entries, not just
   the transaction row — of every create, edit and delete in the same commit as the change. Editing
   rewrites the whole posting (old entries deleted, new entries inserted, transaction row updated)
   under `FOR UPDATE` locks on every account involved, old and new, taken in a fixed order to avoid
   deadlock; deleting captures the before-image and then removes the entries and the transaction.
   `RevisionNumber` and a `RowVersion` (`xmin`-backed) make edits contestable: a client-supplied
   stale row version is refused before anything is touched.
4. `Kind` may move among `Income`/`Expense`/`Transfer` on edit; converting to or from
   `OpeningBalance` or `Distribution` is refused, because those kinds carry structural guarantees an
   ordinary edit must not manufacture. A `Distribution` transaction additionally refuses edit and
   delete outright, naming what to undo first — the `Restrict` foreign key from
   `profit_distribution_shares` (Phase 6) backstops this at the schema level; the Phase 2 store
   already refuses by `Kind` alone, since nothing can produce that kind before Phase 6 exists.
5. Money is `decimal` mapped `numeric(18,2)`; there is **no `Money` value object**. The codebase's
   only existing value object (`SourceId`) exists to prevent silent misattribution across sources —
   a `decimal` in a single-currency ledger has no equivalent failure mode. Instead, a domain static
   `FinanceAmount` (`Scale = 2`, `MaximumAmount = 1_000_000_000m`) **rejects** any value whose scale
   exceeds 2 rather than rounding it; Postgres itself does not protect this (a raw `numeric(18,2)`
   insert silently rounds, proven by `FinanceConstraintTests`), so the domain is the only real
   guard. `CurrencyCode` is a constrained `char(3)`; TRY only in v1.
6. `OccurredOn` is a `DateOnly` accounting date; `RecordedAtUtc`/`UpdatedAtUtc` are UTC from the
   injected `TimeProvider`. Period selectors resolve against Istanbul "today" using the same
   `TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")` idiom as `ScheduleEndpoints`, duplicated
   rather than extracted into a shared helper since no such helper exists yet (a follow-up, not a
   blocker).
7. Accrual obligations (`FinanceObligation`/`FinanceSettlement`, Phase 4) are a layer beside the
   ledger, not inside it: an obligation posts no ledger entries, and settling one writes an ordinary
   Income/Expense transaction plus a settlement row linking the two. This keeps double counting
   structurally impossible, which is why `Collections ≤ Income` and `Payments ≤ Expenses` hold by
   construction.
8. Profit distribution (`FinanceDistribution`/`FinanceDistributionShare`, Phase 6) uses
   largest-remainder allocation over integer minor units (`ProfitShareAllocator`, pure, tested
   exhaustively ahead of and independent from the transactional wiring it sits inside), and is
   non-repeatable per period and idempotent per confirmation token, both by unique index rather than
   application logic alone. Distribution is outflow-only: partners are paid externally, so no
   destination account is credited.
9. Finance is **SuperAdmin-only** (`AuthorizationPolicies.SuperAdmin`, the only policy that exists
   per ADR-045). `FinanceAccountHolder` names whose cash box or bank account something is; it is not
   a role and does not participate in authorization.
10. Finance is **not freeze-gated**: `IOperationalFreezeStore` protects student calendar mutation,
    and bookkeeping is not calendar mutation.
11. **No period close in v1.** Carried-over and to-be-carried-over figures are derived from the same
    balance aggregate at different dates; back-dating into an already-reported period is surfaced
    (a distribution's `PlanHash` proves the basis it was computed from) rather than prevented. A
    period lock is a deliberate Phase 8+ follow-up.
12. Recorded non-declarative assumptions (AI_GUIDELINE §18): a transfer's two legs sum to zero; a
    ledger entry's denormalized `Kind`/`OccurredOn` match its owning transaction's. Both are
    domain-enforced today and swept by `FinanceIntegrityTests` against the real database rather than
    trusted on faith.

### Deviation from the original plan

The plan sketched `FinancePosting` as `(Transaction, Entries, Audit)`, built entirely inside the
domain factory. `Sirkadiyen.Domain` has zero project references — not even to `Sirkadiyen.Contracts`
— so it cannot serialize `BeforeState`/`AfterState` with `ContractJson.CreateOptions()`. Domain
factories return `(Transaction, Entries)` only; the audit row (before/after JSON via the
application-layer `FinanceSnapshotSerializer`, plus `ChangedFields`/`AmountDelta`) is assembled by
the infrastructure store in the same commit. `FinanceAudit`'s own invariants (a reason is required
for update/delete/write-off/cancel/distribution actions; append-only, no update or delete method
exists on its store) are unchanged from the plan.

### Consequences

- Three migrations: `AddFinanceLedger` (Phase 2: `finance_account_holders`, `finance_accounts`,
  `finance_transactions`, `finance_ledger_entries`, `finance_audits`), `AddFinanceObligations`
  (Phase 4: `finance_obligations`, `finance_settlements`), and `AddFinanceDistributions` (Phase 6:
  `finance_distributions`, `profit_distribution_shares`, plus the FK from
  `finance_transactions.FinanceDistributionId` deferred from Phase 2 because that table did not
  exist yet — EF cannot model a relationship to a type outside the current model).
- All seven phases are implemented: domain, ledger persistence and API (Phases 1–3), obligations
  (Phase 4), summary/trend reporting (Phase 5), profit distribution (Phase 6), and CSV export
  (Phase 7). The admin UI remains a `web/GAPS.md` "endpoint exists, UI not built" item — this
  decision was backend-only throughout, per its own scope statement.
- `FinanceConstraintTests`, `FinanceIntegrityTests`, `FinanceConcurrencyTests`,
  `FinanceObligationStoreTests`, `FinanceSummaryReadStoreTests` and `FinanceDistributionStoreTests`
  all run against a real PostgreSQL instance (not just the no-database `FinanceModelTests`). This
  caught three real bugs rather than only confirming assumptions: Postgres's `numeric(18,2)` column
  truncation rounds half-away-from-zero, not half-to-even; an EF `GroupBy`-into-`DefaultIfEmpty`
  join computing outstanding obligations did not translate reliably and was rewritten as two
  queries joined in memory; and a distribution-execute race where two callers used the *same*
  confirmation token concurrently could report `AlreadyDistributedForPeriod` instead of the correct
  `ReplayedExistingExecution`, because the token-replay check ran once before acquiring the source
  account lock and so could miss a row that had not committed yet — fixed by re-checking the token
  under the lock before mapping the period conflict. All three concurrency scenarios (competing
  transfers, competing edits, edit-vs-delete, competing distribution executions, competing
  distributions for one period) pass on every re-run after those fixes, but "concrete evidence" for
  points 2 and 3 above should be read as "evidence obtained after finding and fixing what the tests
  caught," not "passed unmodified on the first attempt."
- Two additional shared-test-database lessons, kept here because they generalize to any future
  finance test: partner shares and the ten summary figures are *global* (not scoped to one test's
  own accounts or holders), so a test that seeds nonzero-share partners must first deactivate any
  other active partner left behind by an earlier test in the same collection, and a test asserting
  an obligation-derived figure (Receivables/Debts/Collections/Payments) should assert a before/after
  delta across its own seed rather than an absolute value.
- This is a management ledger, not statutory accounting; no VAT/withholding/tax, invoice generation,
  or bank statement import is in scope.

---

## ADR-094: Finance administration is one typed, server-authoritative workspace

**Status:** Accepted and implemented
**Date:** 2026-08-05

### Context

ADR-093 completed the finance backend and deliberately left `/admin/finance` as a placeholder.
The prototype showed summary cards, a transaction table and a distribution calculator, while the
live backend additionally exposes accounts, holders, obligations and audit history. One contract
gap made the existing historical settlement-cancellation route unreachable: obligation detail did
not return settlement IDs.

### Decision

1. Finance remains one `/admin/finance` route with six tabs: overview, transactions, obligations,
   accounts/holders, profit distribution and audit. This keeps the prototype's single-workspace
   model while separating dense operations inside it.
2. The frontend mirrors every finance contract in TypeScript and treats backend responses as
   authoritative. It validates decimal input and confirmation text but never calculates balances,
   partner allocations or authorization locally.
3. Distribution execution is bound to the server preview. The browser sends the received
   confirmation token, plan hash and exact expected phrase unchanged; plan-change conflicts require
   a fresh preview.
4. Obligation detail additively returns settlement identity, linked transaction identity, amount,
   settlement/recording dates and the transaction reference. The list response stays lightweight;
   the detail store performs a read-only settlement/transaction join. No database migration or
   mutation rule changes.
5. Historical settlement cancellation is presented as unlinking attribution, not deleting money.
   The UI explicitly states that the ordinary cash transaction remains in the ledger.
6. The existing design system and native SVG/CSS are sufficient. No chart, form or modal dependency
   is introduced.

### Consequences

- Every ADR-093 finance capability is now reachable by a SuperAdmin UI, including CSV export,
  optimistic-concurrency feedback, reason-required destructive actions and distribution reversal.
- The finance page carries a persistent warning that this is a management ledger rather than
  statutory accounting.
- Settlement detail has PostgreSQL coverage for identity/reference projection and cancellation;
  frontend tests cover preview binding and the highest-risk mutation paths.

---
## ADR-095: An active license is a precondition of every calendar write

**Status:** Accepted and implemented
**Date:** 2026-08-05

### Context

ADR-022 accepted single-use licenses with "sync suspension on revocation", and systemPatterns Â§13
states that "license revocation stops future synchronization but preserves this calendar and all
events already written to it". Neither was true in code. `LicenseStore.RevokeAsync` moved the
license row to `Revoked` and wrote its audit; nothing else observed that transition.

Every path that selects users for Calendar work joined the connection to the student profile and
asked only whether the credential worked and initial sync had finished:

- `CalendarSyncTargetReadStore.ReadyTargets` â€” diff fan-out (cohort and ledger-holder targets) and
  the periodic inventory,
- `GoogleCalendarConnectionStore.ListPendingInitialSyncAsync` â€” initial sync,
- `ListPendingReconciliationAsync` â€” re-authorization replay.

A revoked student therefore kept receiving every schedule change, every inventory repair and, if
they had asked for it before revocation, a full initial sync. Onboarding derived `Suspended` for
them, so the UI said one thing while the worker did another.

### Decision

1. **An active license is a precondition of selecting a user for any Calendar write.** The four
   selection queries above each require one. This is a gate on *future* work only: no event is
   deleted, no calendar is touched, and the mapping ledger is left intact, exactly as ADR-022
   requires.
2. **"Active" has one definition, expressed once.** `ActiveLicenseQuery.UserIds` in the persistence
   layer projects the users holding a `Redeemed` license, mirroring
   `LicenseStore.GetUserLicenseStateAsync`. A manual `SuperAdmin` activation is already a `Redeemed`
   license with no code hash (ADR-053), so it satisfies the gate with no special case.
3. **Gating happens in the read stores, not in the services.** The rule belongs where a user is
   *chosen* for work, so no future caller can add a fifth selection path that forgets it â€” and so a
   revocation takes effect on the next cycle without a job to sweep anything.
4. **Revocation is not made destructive and initial sync is not rewound.** A user revoked while
   their initial sync is `InProgress` simply stops being listed; the connection stays `InProgress`
   and resumes if the license is ever restored. The alternative â€” failing the connection â€” would
   discard resumable progress over an administrative action that is often reversed.

### Consequences

- Revocation now means what the documentation always claimed. A revoked student keeps the calendar
  and events they already had, and receives no further creations, updates, deletions or repairs.
- Restoring access is just a new redemption or manual activation: the next worker cycle re-admits
  the user, and the ledger-versus-truth difference is written by ordinary initial sync, dispatch
  and inventory. No catch-up mechanism was needed, because none of those paths were destructive
  while the user was out.
- A revoked user's calendar drifts from the live schedule for as long as revocation lasts, and
  nothing tells them so. That is the intended product behaviour, not an oversight; the honest
  user-facing message belongs to the suspended-onboarding surface.
- Persistence tests cover each of the four selection paths with a revoked, a never-licensed and an
  actively licensed user.

---

## ADR-096: A profile change re-synchronizes the student's calendar

**Status:** Accepted and implemented
**Date:** 2026-08-05

### Context

`StudentProfileService.SaveAsync` validated a profile and persisted it. Nothing else happened. For
a student who had already completed initial sync, that meant a corrected practice group, anatomy
group or program language changed the audience rule while their calendar stayed exactly as it was:

- Initial sync runs once and is `Completed`.
- Diff dispatch is edge-triggered by a published revision, so it moves nothing until the *schedule*
  changes, and then only for the cohorts that revision touches.
- Inventory (ADR-062) is deliberately non-destructive: it recreates and patches expected events but
  reports rather than removes a ledger row with no current expected record.

So the old cohort's events remained forever and the new cohort's arrived only by accident. A
student who fixed a group they had entered wrongly during onboarding was left with a calendar that
was wrong in both directions and looked complete.

### Decision

1. **A profile change that alters the audience records durable intent on the connection.**
   `GoogleCalendarConnection.ProfileResyncRequiredSinceUtc` is set in the *same transaction* as the
   profile upsert, so a crash between the two is impossible. This follows the ADR-060 shape: a
   nullable timestamp that is both the request and its optimistic workflow token.
2. **Only an audience change counts.** `StudentProfile.DescribesSameAudienceAs` compares academic
   year, class year, program language and the normalized selector document. Correcting a student
   number, or re-saving an identical profile, queues no calendar work.
3. **Only a completed connection is flagged.** A profile change during onboarding needs nothing:
   initial sync computes the applicable set when it runs. A connection with no calendar attached is
   likewise skipped.
4. **The worker converges the calendar to the ledger's difference from the new applicable set.**
   `ProfileChangeResyncService` is freeze-gated (global and the *new* scope), fenced with dispatch,
   replay and inventory, and per user:
   - inserts every currently-published record that applies to the new profile and is not in the
     ledger, reusing the deterministic event id and the ledger's uniqueness, so a re-run converges;
   - deletes every ledger row whose lesson is **still currently published** but no longer applies.
5. **Deletion authority here is the student's own profile, and it is bounded by publication.** A
   ledger row is removed only when its `(SourceId, StableIdentity)` is present in the currently
   published schedule for the profile's academic year *and* the audience rule says it is not this
   student's. A stable identity that is absent from published truth is left completely alone â€” that
   is the "do not delete because a parser failed to see it" rule (AI_GUIDELINE Â§13), and removing it
   remains the semantic diff's job. Absence is never authority here either.
6. **Bounded, resumable, and completed only by a clean pass.** Connections per cycle and mutations
   per connection are bounded (`ProfileResyncOptions`). A pass that hits its budget leaves the
   marker in place and returns a normal partial outcome. The marker is cleared only when a full
   pass finds no remaining work, presenting the original timestamp as the workflow token, so a
   second profile change made mid-pass survives instead of being cleared by the older worker.
7. **A dead credential is not a failure of the request.** The connection is flagged
   `NeedsReauthorization` and skipped with the marker intact; it is picked up again after
   re-authorization, alongside the ADR-060 replay.

### Consequences

- A student can correct their cohort and see it. The events that no longer apply leave, the ones
  that now apply arrive, and both happen without a schedule revision, a diff or a calendar wipe.
- The stable identity is the join key throughout, not the canonical record id: a mapping's record
  id points at whichever revision wrote it, and an `Unchanged` diff entry never advances it, so it
  is not a reliable liveness signal. `ICanonicalScheduleReadStore.ListCurrentPublishedIdentitiesAsync`
  answers liveness by identity instead.
- Scoping the published-identity set to the profile's academic year bounds the query. It also means
  an event written under a *previous* academic year would never be cleaned up by this path. That is
  correct while no academic-year rollover exists (there is no such data yet) and must be revisited
  when one does.
- A profile change is still not written to the cross-cutting `AuditEvent` log, so the trail for
  "why did these events disappear" is the ledger and the worker log rather than an audit row.
  Recorded as an open risk, not closed by this ADR.

---

## ADR-097: A held revision can be rejected, and a failed diff can be retried

**Status:** Accepted and implemented
**Date:** 2026-08-05

### Context

Two states in the pipeline were reachable but not leavable, and both were listed as unbuilt in
`progress.md` Phase 10 ("Manual publish and reject", "Retry failed jobs").

`RevisionState.ReviewRequired` had exactly one exit: `POST /api/revisions/{id}/approve`. The
transition table already permitted `ReviewRequired` to `Rejected`, but nothing called it. An
operator who read the findings and concluded the parse *was* wrong could only leave the revision in
the queue forever, where it is indistinguishable from one nobody has looked at yet.

`CalendarDispatchState.Failed` is terminal by design (ADR-059) so a broken diff stops churning
every cycle. But nothing could ever move it out, so a diff whose fan-out exhausted its attempts
during a Google outage stranded every affected student until a later revision happened to touch
the same lessons. Neither state was even *findable*: the diff list filters on `ScheduleDiffState`,
and a failed diff is `Ready`/`Released` in that dimension.

### Decision

1. **Rejection is a first-class, recorded decision, not a reuse of approval.** `ScheduleRevision`
   gains `RejectedBy`, `RejectionReason` and `RejectedAtUtc`, and `Reject` moves a `ReviewRequired`
   revision to the terminal `Rejected` state, carrying the finding it was held for into
   `StateReason` exactly as `Approve` does. Recording a rejection in the approval fields would make
   the audit trail lie.
2. **Only a quarantined revision may be rejected.** Not a `Validated` one (publication is the next
   step and forward-fix is the correction, ADR-033), and never a `Published` one â€” there is no
   rollback, and a published revision leaves live state only by being superseded.
3. **Retry clears the failure and returns the diff to the ordinary dispatcher.** `RetryDispatch`
   moves `Failed` back to `Pending`, resets `DispatchAttempts` and clears `NextAttemptAtUtc`, so
   the diff is picked up by the next cycle and runs through the same idempotent, ledger-resumable
   fan-out. Retry does **not** re-run anything itself and grants no new authority: the diff must
   still be dispatchable, and every per-user operation converges through the deterministic event id
   and the mapping ledger, so a partially-applied diff completes rather than double-applying.
4. **A retry is attributable and counted.** `DispatchRetryCount`, `LastDispatchRetriedBy`,
   `LastDispatchRetryReason` and `LastDispatchRetriedAtUtc` are kept, and the previous
   `DispatchFailureReason` is preserved until the next attempt overwrites it. An operator retrying
   the same diff five times is visible as such rather than looking like one bad night.
5. **Both actions require a named actor and a non-empty reason**, derived from the verified session
   and never from the payload, and both are CSRF-protected `SuperAdmin` routes â€” the same shape as
   revision approval (ADR-032) and diff release (ADR-042).
6. **The failed queue is made findable.** `GET /api/diffs` accepts `dispatchState`, and the diff
   summary now carries `CalendarDispatchState`, `DispatchAttempts`, `DispatchFailureReason` and the
   retry fields. Without this, the retry route would exist for a queue nobody could enumerate.

### Consequences

- The two dead ends now have an audited, reason-required exit each, and neither weakens a safety
  rule: rejection is terminal and forward-fix stays the only correction, and retry re-enters the
  existing gate rather than bypassing it.
- A repeatedly retried diff is a visible operational signal rather than a hidden one. There is
  still no automatic alert on either queue â€” that remains the unbuilt alerting work.
- One migration, `AddRevisionRejectionAndDiffRetry`, adds the six nullable columns and the retry
  counter; no existing column changes meaning.

---


## ADR-098: The Grade 3 English program has no curriculum group, so its term cell states only a class year

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `grade3_yearly_v1` 1.0.0 for `G3-EN-ANNUAL`
**Relates to:** ADR-017, ADR-048

### Context

The Grade 3 Turkish class year is split into two curriculum groups, A and B, each
with its own annual workbook. `grade3_yearly_v1` reads that split out of the
unlabelled term column (`Dönem 3A Grubu`, `Dönem 3A+3B Grubu`, `Dönem 3B/3A
Grubu`) and publishes each row to the groups it names.

The English workbook is read by the same profile and writes the same kind of term
cell — but its program has no A/B division at all. It writes `Time Table 3` on
most rows and `Dönem 3A Grubu` on 49 of them. Those 49 are the joint lectures
English students attend together with the Turkish A group; they are not a
statement that the English program has an A group.

Reading the term cell literally there would publish those 49 lectures to
`curriculumGroup: 3-A` — a cohort no English student can declare, because the
English program has no such cohort to select. Every English student would lose 49
real lectures, and nothing would report a fault.

### Decision

When `sourceContext.programLanguage` is English, `grade3_yearly_v1` uses the term
cell **only** for its class-year check and publishes every row as
`allStudentsInProgram`. The `curriculumGroup` audience gate is off for that
language.

This is a language branch inside a parser, which the codebase otherwise avoids.
It is justified here because the fact being encoded is about the *program*, not
about the *document*: the English program is not divided, so no cell in its
workbook can name a division of it. The alternative — a second profile name
differing only in this one gate — would duplicate an 800-line implementation to
express one boolean.

Consequently the supported-profile schema carries no Grade 3 English program: it
would have no selector to offer.

### Consequences

- English Grade 3 students receive every lecture their workbook states, including
  the joint ones.
- A future English division would be a real change to this rule, not a
  configuration tweak, and it would be caught by the golden file for
  `g3-en-annual` the moment any row stopped being program-wide.

---

## ADR-099: The rotation hyphen enumerates, and a contradictory rotation row is refused per cohort

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `grade3_faculty_practice_v1` 1.0.0 for `G3-TR-A-FACULTY` and
`G3-TR-B-FACULTY`
**Relates to:** ADR-073

### Context

The Grade 3 faculty-practice workbooks are matrices: eight blocks, each with a
department header row and eight date rows, and each cell naming the cohorts that
sit with that department on that date. Two questions had no obvious answer.

**What does `A1-A2` mean?** A hyphen between two cohort labels can plausibly mean
"A1 and A2" or "A1 through A2". It was settled against the data rather than by
preference: under the enumerating reading, 127 of the 128 date rows state each of
A1-A8 exactly once, and *zero* rows require the spanning reading. Under the
spanning reading many rows would double-book cohorts.

**What happens to the one row that does not add up?** `G3-TR-A-FACULTY` row 240
(2027-03-24) reads `A4, A1, A2, A3, A4, A5, A6, A7`. The block rotates by one per
row, so column B should read `A8`; someone typed `A4` twice. Refusing the whole
row — the codebase's usual response to an ambiguous row — would deny six cohorts
a session they are unambiguously assigned.

### Decision

1. **The hyphen enumerates.** `A1-A2` is A1 and A2. Separators `- / + , ; &` and
   whitespace all enumerate.
2. **Refusal is per cohort, never per row.** Within a date row:
   - a cohort named in exactly one cell is **published**;
   - a cohort named in more than one cell is ambiguous, and **every** cell naming
     it is refused with a warning citing all their addresses;
   - a cohort named in no cell is recorded absent under its own reason.

   The row itself is never refused.
3. **The rotation pattern is not used to repair the row.** It is obvious from the
   surrounding rows that column F holds the real A4, but inferring which of two
   contradictory cells is correct is repairing the source. The parser refuses; it
   does not repair.

On row 240 this publishes A1, A2, A3, A5, A6 and A7, refuses both A4 cells as
ambiguous, and records A8 absent — six of eight cohorts keep their session
instead of none.

### Consequences

- 510 candidates for the A workbook and 512 for B, and the two refused cells plus
  the absent cohort are visible as warnings in the golden file rather than as a
  silently shorter list.
- Faculty correcting the typo produces exactly two new candidates and no identity
  churn for the other six.
- A cohort's rotation is scoped to its curriculum group: `facultyPracticeGroup`
  depends on `curriculumGroup` in the supported-profile schema, because `A5` and
  `B5` are different rotations in different documents.

---

## ADR-100: The Grade 3 annual owns the bedside slot; the bedside document owns the topic

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `grade3_yearly_v1` 1.0.0, `grade3_bedside_v1` 1.0.0
**Relates to:** ADR-046, ADR-075, ADR-102

### Context

Two Grade 3 sources describe the same 92 bedside practice sessions.

The **annual** workbook states each session's date, times and each group's
department, one row per session. Its times are 13:30-14:20 (70 sessions),
14:00-14:50 (21, all Fridays) and 13:30-14:10 (1).

The **bedside** document states one time for all of them, in a preamble heading:
`HASTA BAŞI UYGULAMA KONULARI (13.30-14.20)`. It is therefore wrong for 22 of the
92 sessions. What it uniquely holds is the *topic* of each session: a schedule
table of `(date, group)` to topic code, and a prose catalogue mapping each code to
a sentence or two describing the session.

Publishing from both would produce duplicate events. Publishing from the bedside
document alone would put 22 sessions at the wrong time.

### Decision

1. **The annual publishes all 92 sessions** as timed `bedsidePractice` events. It
   is the only source that proves a date *and* a time per session.
2. **`grade3_bedside_v1` publishes zero candidates.** It is registered and parsed
   anyway, so the document is accounted for in metrics and so the reader the
   annual profile calls is itself exercised by a golden file.
3. **The topic reaches the event as `notes`**, joined on `(local date, curriculum
   group)`, via the companion-snapshot mechanism of ADR-102.
4. **The catalogue is joined on section and ordinal, never on the code prefix.**
   The source writes `İçH`, `IçH` with an ASCII I, `ÇSH` and one `İÇSH` in the
   catalogue while the table writes `İçH` and `ÇSvH`; prefix matching fails on 69
   of 91 codes. The prose is cleanly sectioned by department heading, so the
   catalogue is keyed by (section, ordinal) and only the schedule's two consistent
   prefixes are mapped to sections.
5. **A code that does not resolve leaves the event without a topic**, and is
   counted. No topic is ever guessed.

### Consequences

- 88 of 92 topics resolve for the A group and 87 of 92 for B. The remainder are
  genuine gaps: those codes have no catalogue entry in the document at all.
- The two documents differ structurally — A has a spacer column between its
  semester pairs and five tables, B has neither — so the reader pairs date/topic
  columns by header rather than by index, and carries catalogue state across
  worksheets.
- `grade3_bedside_v1` declares `dayFirst`, the second profile after
  `grade2_practice_v1` to need it (ADR-075). The document proves the order itself:
  it writes days above twelve, such as `22.10.2026`.

---

## ADR-101: A canonical record carries free-text notes, which are content and not identity

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `CanonicalScheduleCandidate.notes`, `CanonicalScheduleRecord.Notes`,
migration `AddCanonicalScheduleRecordNotes`, `CalendarEventPresentationPolicy.Description`
**Relates to:** ADR-018, ADR-047, ADR-049, ADR-100

### Context

A Grade 3 bedside session's topic is a sentence or two of prose — "Hastaya
yaklaşım, hastaya adıyla hitap, hasta ile ilk karşılaşmanın özellikleri, kendini
tanıtma". It is not an instructor, a curriculum block, a department or a location,
and it belongs in the event a student reads rather than in evidence nobody sees.
The canonical record had no field that could hold it.

### Decision

1. **One optional `notes` field**, from the parser contract through
   `CanonicalScheduleRecord` to a `character varying(4000)` column. It is bounded
   generously rather than at the width of the other text columns because it holds
   a paragraph a faculty member wrote, not a label.
2. **It is part of the content hash and of no stable identity.** A corrected topic
   must move the event a student already has, not create a second one beside it.
   This was verified rather than assumed: attaching the companion changes 88
   candidates' content hashes and **zero** stable identities.
3. **It renders last in the description, on its own paragraph**, prefixed `Konu:`.
   Everything above it is a short labelled value; a student wants to know what the
   session is about after they know whose it is.
4. **It is deliberately general.** Nothing about the field is bedside-specific, so
   the next source with prose that has no field of its own needs no schema change.
   It is not a dumping ground either: a value that has a field belongs in that
   field.

### Consequences

- Regenerating every existing golden file after adding the field moved no
  candidate digest, identity or content hash — only the explicit `notes: null` on
  the two sampled candidates and the whole-response digest.
- A record with no notes stores `NULL`, not an empty string, so "no topic stated"
  and "an empty topic" cannot be confused.

---

## ADR-102: A parse may read companion snapshots, and they are part of the parse run's identity

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `ParseSnapshotRequest.auxiliarySnapshots`,
`ScheduleSource.CompanionSourceIds`, `ParseRun.CompanionFingerprint`,
migration `AddCompanionSourceEvidence`
**Relates to:** ADR-007, ADR-014, ADR-044, ADR-080, ADR-100

### Context

The parser receives one snapshot per parse, so the Grade 3 annual profile could
not see the bedside document that holds its practice topics (ADR-100). Three
approaches were possible: publish reference records from the bedside source and
merge them in .NET afterwards; give the parser a way to fetch a second document;
or hand the second document to the parse as an input.

A post-parse merge in .NET would need a new record status for records that are not
schedule, would put those records into revisions and diffs where they would be
counted and compared, and would need calendar dispatch to learn to skip them.
Letting the parser fetch anything would break the boundary that keeps it
deterministic and offline.

### Decision

1. **Companions are an input.** `ParseSnapshotRequest` carries an optional
   `auxiliarySnapshots` list. A source names its companions in the catalog
   (`companionSourceIds`), the poller loads each companion's latest stored
   snapshot and attaches it, and the profile decides what to read from it.
2. **A companion is supporting evidence, never a second schedule.** It publishes
   nothing through the source that reads it; its own source and profile still
   parse it separately if it has anything of its own to publish.
3. **It must degrade, never block.** A companion that has never been acquired is
   simply absent from the request. The annual publishes its bedside sessions with
   no topic line — exactly the behaviour before this existed. A missing topic is
   far cheaper than a schedule that never reaches a student, and the same test
   decides both what is sent and what the fingerprint covers, so a run's identity
   always describes exactly the evidence it read.
4. **The companion set is part of the parse run's identity.** A run was keyed by
   `(snapshot, profile, profile version)`; it is now keyed by that plus a
   `CompanionFingerprint`, a SHA-256 digest of the ordered `(source id, content
   hash)` pairs. Without it, editing the bedside document alone would leave the
   annual short-circuited as "already parsed" and the corrected topic would never
   reach a calendar. The column is non-nullable with `""` meaning "read none",
   because PostgreSQL treats NULLs in a unique index as distinct and would permit
   exactly the duplicate runs the key forbids.
5. **The catalog refuses a companion it cannot resolve.** A mistyped identifier is
   invisible at runtime — it looks identical to a companion that has not been
   polled yet — so catalog load rejects a companion that is not a source, is the
   source itself, is named twice, or itself declares companions. Companion
   evidence is one level deep by construction, which disposes of cycles.

### Consequences

- `G3-TR-A-ANNUAL` and `G3-TR-B-ANNUAL` name their bedside documents. No other
  source has a companion today.
- The faculty-practice room lookup (`G3-FACULTY-LOCATIONS`) can reuse this
  mechanism unchanged when that join is built; it is a catalog entry and a reader,
  not new machinery.
- Two golden cases cover the same Grade 3 A annual, with and without its
  companion, so the degradation guarantee is asserted rather than described.

---

## ADR-103: Each supported program states its own academic year

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `SupportedProfileProgram.AcademicYear`, supported-profile schema 1.2
**Amends:** ADR-048, ADR-055

### Context

The supported-profile schema stated one academic year for every program, on the
reasoning that there is one current year at a time. The Grade 3 rollover broke
that: the faculty published the 2026-2027 Grade 3 documents while Grades 1 and 2
were still on 2025-2026, so the catalog now holds sources for two years at once.

This is not bookkeeping. `CalendarAudienceResolver` matches a canonical record to
a student only when the record's academic year equals the one stamped on their
profile, and the profile is stamped from the schema at save time. A Grade 3
student stamped 2025-2026 would match none of the 2026-2027 records published for
them: an empty calendar, with the profile saved, the revision published, the diff
computed and every check downstream reporting success.

### Decision

1. **`SupportedProfileProgram` states the academic year its own sources were
   captured for**, and `StudentProfileService` stamps *that* year on the profile.
2. **`SupportedProfileSchema.AcademicYear` remains**, meaning the year the schema
   revision was cut for. The two agree except during a rollover. The onboarding
   form shows the selected program's year once one is chosen, and the schema's
   before that, so the year a student sees is the one their profile will carry.
3. **Schema version 1.2.** Every stored profile records the version that validated
   it, so a profile written under 1.1 stays identifiable as one written when all
   programs shared a year.

### Consequences

- Grade 3 Turkish joins the schema with `curriculumGroup` (`3-A`, `3-B`) and a
  dependent `facultyPracticeGroup` (`A1`-`A8` under `3-A`, `B1`-`B8` under `3-B`),
  and is the first program whose year differs from the schema's.
- Rollover is now incremental: a grade moves to the new year when its sources are
  captured, rather than all grades moving on one deployment.
- Grade 3 English is absent, having no selector to declare (ADR-098); Grade 1
  anatomy and Grade 2 English remain absent for their own recorded reasons.

---

## ADR-104: The two diff operator queues are separate, and a refusal is stated

**Status:** Accepted and implemented
**Date:** 2026-08-15
**Implements:** `web/src/components/DiffQueues.tsx`, `/admin/diffs`, the rejection path in
`web/src/components/RevisionReview.tsx`, `ScheduleRevisionDetail` rejection fields
**Amends:** ADR-042, ADR-097

### Context

ADR-042 and ADR-097 built three operator actions with no surface: releasing a held diff,
retrying a terminally failed one, and rejecting a quarantined revision. Each existed to give a
stuck state a way out, and each was reachable only by an operator able to reproduce a session
cookie and an antiforgery token by hand, which is the same practical dead end ADR-081 hit for
administrative upload.

Two things about the shape of that queue are not obvious and were decided rather than defaulted
into.

### Decision

1. **The held queue and the failed-dispatch queue are separate screens.** They read the same
   route with different filters because the axes are orthogonal: `state` answers "may this diff
   be acted on", `dispatchState` answers "has it been". A terminally failed diff is still `Ready`
   or `Released`, so the held queue cannot show it. One merged list would present a released diff
   that failed its fan-out as if it were awaiting review, and would make the two very different
   actions look interchangeable.
2. **An action that is forbidden is replaced by the reason, not disabled.** An ambiguity hold is
   never releasable, and a diff whose dispatch has not failed is not retriable. Both render the
   explanation in place of the field and the button. A disabled control teaches an operator to
   wait for it to become enabled; an ambiguity hold never will, because it is only ever fixed at
   the source.
3. **The changed lessons are shown before the action.** A row expands into the diff's actionable
   entries with previous and current side by side, and states how many of the total are
   displayed. Releasing a diff without seeing which lessons it deletes is rubber-stamping, which
   is what the hold exists to prevent.
4. **Rejection is confirm-then-reason, and reads back.** It is behind a confirmation step whose
   text states the action is terminal and that the correction is a newer revision published over
   it, never a rollback (ADR-033). `ScheduleRevisionDetail` now projects `RejectedBy` /
   `RejectionReason` / `RejectedAtUtc`, and the review screen gained a `Rejected` queue: without
   both, a terminal decision would leave a record no operator surface could ever return.

### Consequences

- `web/GAPS.md` §3.2 is empty. Every backend-supported operator action has a surface.
- The rejection fields are additive on a read-only projection: no migration, no behaviour change,
  and the approval fields stay separate so the trail cannot state the opposite of what happened.
- `DispatchRetryCount` and the last retrying operator are now visible beside the failure reason,
  because a diff retried repeatedly is the signal the failure is not transient. Nothing watches
  it; alerting remains unbuilt.
- Nothing new is authorized. Both writes are the existing SuperAdmin, CSRF-protected,
  reason-required endpoints, and a retry re-runs the same immutable diff through the same
  idempotent, ledger-resumable fan-out.

---

## ADR-105: A profile change is a student-reachable action and an audited one

**Status:** Accepted and implemented
**Date:** 2026-08-16
**Implements:** `AuditEventCategory.ProfileUpdated`, `ProfileUpdatedAuditMetadata`,
`SaveStudentProfileResult.AudienceChanged`, `web/src/components/AcademicProfileForm.tsx`,
`web/src/app/profile/page.tsx`
**Amends:** ADR-055, ADR-096

### Context

ADR-096 made a profile change converge the student's calendar: a write that alters the resolved
audience records `ProfileResyncRequiredSinceUtc`, and a fenced worker stage inserts what now
applies and removes what no longer does. `PUT /api/profile` reports `calendarResyncRequested` so a
screen can say so.

Two things were missing, and they compound.

**No screen could reach it.** The only academic-profile UI was `/onboarding/profile`, gated by
`OnboardingGate` to the single onboarding state `ProfileRequired`. Once a student had a profile
they were never in that state again, so an Active student could not change their group at all —
the dashboard showed the profile read-only. The entire ADR-096 feature, the fenced worker stage
included, was unreachable from the product. `web/GAPS.md` §3.2 claimed the "endpoint exists, UI
not built" category was empty; this belonged in it. The typed browser contract had also never
carried `calendarResyncRequested`, so the field the backend had been returning since ADR-096 was
invisible to TypeScript.

**No audit row was written.** AI_GUIDELINE §19 lists a profile change as auditable, and after
ADR-096 the change can *delete* calendar events. The only trail was the mapping ledger and the
worker log, so "why did these lessons disappear from my calendar" had no answer in the audit
surface an operator actually reads.

### Decision

**1. A sixth `AuditEvent` category, `ProfileUpdated`, written by the endpoint.** It carries the
resolved audience as structured metadata — academic year, class year, program language, selectors —
plus `audienceChanged` and `calendarResyncRequested`.

The two flags are recorded separately, and this is the substance of the decision rather than
bookkeeping. They differ for a real case: an audience change on an account with no completed
calendar connection queues nothing, because initial sync will resolve the new audience when it
runs. Recording only the queued flag would make that indistinguishable from a change the audience
rule does not read at all — the two have opposite meanings when reading the trail backwards from a
calendar that lost events. `SaveStudentProfileResult` therefore gained `AudienceChanged`, which the
store already reported and the service had been dropping.

**The student number is deliberately not recorded.** It identifies the person and answers nothing
about which lessons the profile resolves, so it has no place in a log an operator reads to explain
a calendar change (AI_GUIDELINE §15). A test asserts its absence rather than trusting the shape of
the record.

**2. The form is shared, and the edit surface is its own route.** `AcademicProfileForm` is the
onboarding step's form extracted unchanged, plus an `initial` prefill. `/profile` renders it for
every onboarding state in which a profile already exists — `CalendarAuthorizationRequired`,
`ReadyForInitialSync`, `InitialSyncInProgress`, `Active`, `ActionRequired` — and the dashboard's
academic-profile card links to it.

`ProfileRequired` is excluded because the onboarding step owns it, and `Suspended` is excluded
because the backend refuses the write for an unactivated account (`ActivationRequired`): offering
the form there would be a promise the API cannot keep.

**3. What the save is allowed to claim is a component, not a string at the call site.**
`ProfileSaveNotice` renders the requested re-synchronization as background work the worker will
perform, never as a finished one — the response says it was *requested* (AI_GUIDELINE §16). When
the flag is false it claims nothing about the calendar at all, because false has more than one
cause and the screen cannot tell which.

### Consequences

- The ADR-096 worker stage now has a way to be triggered by the person it exists for. Until this,
  it could only fire through a direct API call.
- A prefilled form is a deliberate choice over a blank one: re-declaring an unchanged cohort by
  hand invites a mistyped selector, and a mistyped selector silently moves a calendar.
- A stored program the schema no longer defines is shown as an unsupported combination rather than
  blanked, so a schema change is visible to the student instead of looking like they never chose.
  This is the ADR-055 open risk (a profile stored under an older schema version is not
  re-validated) becoming visible at the one moment the student can act on it.
- The audit read path needed no change: `GET /api/admin/audit` binds the category from the enum, so
  a new member is queryable immediately. The admin filter's hardcoded option list did need
  extending — it had also been missing both finance categories since ADR-093.
- Not addressed: an operator cannot yet change a student's profile on their behalf, and the audit
  row records the student as the actor because only the student can perform the write today.

---

## ADR-107: Administrator-authored calendar announcements are one domain behind two screens

**Status:** Accepted (2026-08-17)

### Context

`web/GAPS.md` §3.1 listed the last two prototype screens with no backend at all: the bulk calendar
event (`admin-bulk-event.html`, plan §4.4/§5.11) and the single-user warning
(`admin-user-warning.html`, plan §4.5/§5.12). Both write an event an administrator composed onto
students' managed calendars, which is a calendar mutation the system had no path for: every
existing write derives from published schedule truth.

Three questions had to be answered before any code:

**Are they one feature or two?** The plan presents them as separate screens with different flows.
But the recipient set is the only thing that differs: both compose an event, resolve who receives
it, freeze that set, write idempotently, track delivery, patch on edit and remove on cancel.

**Where do these events live relative to the schedule?** They are not lessons. They must not
produce canonical records, revisions or semantic diffs, and they must not enter the mapping ledger
that decides what published truth owes a student.

**What authorizes deleting one?** AI_GUIDELINE §13 says a calendar deletion requires a published
revision and a valid semantic diff. An announcement has neither.

### Decision

**1. One aggregate, `CalendarAnnouncement`, with a `Kind` of `Bulk` or `UserWarning`.** Splitting
them would duplicate the delivery ledger, the deterministic event id, the freeze gate, the licence
gate and the cancel path — all of it high-risk calendar code, and all of it identical. Two API
shapes and two React screens sit on top; only the audience step and the templates differ.

**2. The recipient set is frozen at confirmation, as rows.** `CalendarAnnouncementDelivery` is
written for every resolved candidate in the same transaction as the announcement — including the
*excluded* ones, with the reason they were excluded. An announcement is a decision about who is
being told something, so a student who changes cohort afterwards neither gains nor loses it; that
is what makes the count on the confirmation screen mean anything. Keeping the excluded rows is what
keeps the approved exclusion counts explainable a day later.

This is a deliberate departure from systemPatterns §27, which keeps dispatch tracking coarse because
idempotency lives in the shared mapping ledger. Here the per-recipient row *is* the ledger, and the
counters the operator is shown (written / pending / skipped / removed / failed) are precisely this
table. They are always derived from it and never stored on the announcement, so the number shown
cannot disagree with the rows it summarizes.

**3. Announcement events are marked as a different kind of managed event.** They carry
`sirkadiyenKind=announcement` in their private extended properties, and
`CalendarInventoryReconciliationService` skips them. Without this, inventory — which groups managed
events by `stableIdentity` — would have counted every announcement as an unexpected marked event
and reported a conflict on every pass, making the inventory signal useless exactly when it matters.

The marker is added only to the new kind. A lesson written before this ADR carries no such key, so
its *absence* means "lesson"; giving lessons a marker would have made every existing event on every
calendar look drifted and triggered a mass patch.

**4. The event id shares the schedule's derivation but not its id space.** The deterministic id is
`base32hex(SHA256(userId + "\n" + identity))` as before, with `identity` being
`announcement:{id:N}`. A parser-produced stable identity is a hex digest and cannot spell that
prefix, so the two spaces are disjoint without a second hashing scheme to maintain.

**5. Deletion is authorized by a named operator, not by a diff.** AI_GUIDELINE §13's rule protects
*schedule* events: it exists so a parser failure can never retire a lesson. An announcement was
never schedule truth, so the authority is the operator who asked for it, recorded on the aggregate
with their required reason and written to the cross-cutting audit log. Cancellation removes the
written events and keeps every delivery row, so the trail still says who received what.

**6. The six-step high-risk pattern, with the plan hash binding the confirmation.** Preview
resolves the audience server-side and returns a SHA-256 over the content, the criteria *and the
recipient identities*; execute recomputes it and refuses on mismatch. Hashing identities rather
than the count is the point: two audiences can have the same size and different members, and
confirming "412 recipients" must not authorize writing to a different 412 people. This is the
`FinanceDistribution` shape from ADR-093 applied to a second high-risk operation, as plan §4.3
intends.

The confirmation phrase is the recipient count for a bulk announcement — the fact hardest to
overlook — and the recipient's own address for a warning, whose count is always one and would
confirm nothing.

**7. Deduplication is a derived campaign key with a unique index.** A bulk key covers the audience,
the date and the normalized title; a warning key is user + template + local date, exactly as plan
§4.5 specifies. Body text, location and colour are outside it, because correcting the wording of a
delivered announcement must patch the existing events rather than produce a second one. The unique
index is the real guarantee — two operators confirming concurrently must not both win — and the
application's earlier lookup only makes the common case cheap.

**8. Account status, licence status and sync eligibility are not audience filters.** The plan lists
them among the audience dimensions. They are not choices: an account with no active licence has
stopped synchronizing (ADR-095) and an account with no completed initial sync has no calendar to
write to. Offering them as toggles would promise an outcome the calendar cannot deliver, so they
appear on the other side, as `AnnouncementExclusionReason` values the operator reads before
confirming.

**9. The audience rule is "all selectors must match", the opposite of the lesson rule.** A lesson
lists the groups it is *for*, so a student matching any of them attends it. An announcement's
selectors are the operator narrowing who they address, so "Dönem 2, uygulama grubu C" means
students who are both. A dimension the profile does not carry at all is a mismatch, not a pass —
otherwise a message meant for one anatomy group would reach every student in a programme that has
no anatomy group.

**10. The "trial ending" template was not built.** Plan §5.12 lists it. There is no such state:
Sirkadiyen access does not lapse after activation and `GET /api/licenses/status` reports no time
remaining (ADR-089). Shipping it would have an operator send students a deadline the product does
not have. The four templates that exist each name a state the system can actually be in.

**11. Delivery runs last inside the shared Calendar fence,** after diff dispatch, replay and
profile convergence. The schedule is what students depend on, so an announcement campaign must
never consume the per-cycle Calendar budget those stages need first.

**12. Reminders are opt-in per event.** `ManagedCalendarEvent.ReminderMinutesBefore` is nullable and
left null by every schedule lesson, so a student's own notification defaults keep working; a value
replaces them with one popup reminder, which is the only way an announcement can be made to arrive
at a chosen moment.

**13. `ICalendarConnectionHealthWriter` was split out of `ICalendarSyncConnectionStore`.** Delivery
discovers dead credentials and missing calendars like any other Calendar write, and recording them
where they are found is what stops a student's connection staying `Authorized` until the next
published revision happens to write to them. It needs two of that interface's fourteen members, so
it depends on a role interface instead — the same ISP shape `GoogleCalendarConnectionStore` already
serves three interfaces with.

### Consequences

- `web/GAPS.md` §3.1 is now empty. No prototype screen is left without a backend except the three
  areas that need their own product decisions (contact, notifications, sync history).
- Two new tables, one migration (`AddCalendarAnnouncements`), and three new `AuditEvent`
  categories (`AnnouncementQueued`, `AnnouncementUpdated`, `AnnouncementCancelled`).
- The audit row records counts, audience and campaign key, never the recipient list: the delivery
  ledger already holds every recipient with their outcome, and copying hundreds of accounts into an
  audit table nothing prunes would duplicate personal data (AI_GUIDELINE §15).
- An announcement cannot be re-addressed. Editing changes what it says; changing who receives it is
  a new announcement with its own confirmation, because the recipients were frozen.
- **Not built:** a per-recipient retry for an individual failed delivery (the campaign-level retry
  is the attempt cap and the operator's re-edit), scheduling an announcement for future delivery
  rather than a future *event date*, and recurring announcements.
- **Open risk:** a scoped freeze leaves an announcement in `Delivering` indefinitely, re-checked
  every cycle. That matches every other stage's behaviour, but an announcement whose event date
  passes while its class/program is frozen will eventually be written into the past. Nothing
  currently warns about that.
- **Open risk:** the audience query reads one academic year's student profiles into memory to apply
  the JSONB selector match, because EF cannot translate a dictionary lookup. Correct and small at a
  medical faculty's scale; it is a scan that grows with the student body.

---

## ADR-108: The account directory filters on the server, and one account is a page

**Status:** Accepted and implemented
**Date:** 2026-08-17
**Implements:** `AdminUserQuery` (extended), `AdminUserReadStore`, `AdminUserEndpoints`
(`GET /api/admin/users/{id}/calendar-events`, `.../calendar-changes`),
`AdminUserDetailResponse.RecentActivity`, `AdminUserCalendarConnection`,
`IAnnouncementStore.ListAsync(targetUserId)`, `web/src/components/AdminUserFilterBar.tsx`,
`web/src/components/AdminUserDetail.tsx`, `web/src/components/UserWarningForm.tsx`,
`web/src/app/admin/users/[userId]/page.tsx`
**Amends:** ADR-089 (the admin user read), ADR-107 (the warning composer)

### Context

`GET /api/admin/users` accepted an e-mail substring and a role. The screen over it was a table and
a drawer: an operator could see that an account existed but could not answer any question about a
*group* of accounts ("which second-years never finished initial sync", "who in practice group A has
no licence"), and could not act on one account without leaving for another screen.

Three separate problems, and the first is the one that mattered.

**The questions an operator asks are conjunctions.** Every attribute needed to answer them was
already stored — licence rows, the student profile, the Calendar connection — and none of it was
reachable through the query. A frontend-side filter over a fetched page would have been worse than
nothing: the record count under a filter would have described the page, not the population.

**Search threw on the terms operators actually type.** The store passed the term through
`User.NormalizeEmailValue`, which validates a whole address. `"zeyn"` or `"@ogr"` raised
`ArgumentException` — an unhandled 500 from a search box, present since ADR-089.

**The per-user operations existed but were scattered.** Manual activation lived on a self-service
button, licence revocation in the licences tab keyed by licence id rather than by person, and the
warning composer in a screen whose first step was searching for the user the operator was already
looking at.

### Decision

**1. Every filter is a backend filter.** `AdminUserQuery` gained licence state, profile presence,
academic year, class year, program language, selectors, Calendar-connection presence, authorization
status, initial-sync state, created and last-signed-in ranges, and an explicit sort. The browser
sends them and renders the page it receives. This is AI_GUIDELINE §16's "backend state is
authoritative" applied to a count: a number under a filter has to mean the population, or it means
nothing.

**2. The selector filter resolves in memory, and says so.** EF cannot translate a lookup into the
JSONB selector dictionary. The matching accounts are resolved first — narrowed by the academic year,
class year and program language that *are* translatable — and the directory query is narrowed to
that id set. Same trade ADR-107's audience query makes, recorded here rather than discovered later.

**3. `LicenseState` is derived in one projection, used by list and detail.** It was already derived
rather than stored, which is why an ADR-095 revocation needs no sweep. Extracting `Project` means
the list, the detail and the licence-state filter cannot drift into describing the same account
differently.

**4. `ManagedEventCount` is one grouped query, not a correlated count.** A subquery per row would
have issued up to 200 counts to render one page.

**5. An account is a page, not a drawer.** The operations an operator performs on a user do not fit
a panel, and each of them deserves an address that can be pasted into a message to a colleague.

**6. The calendar tab reads the mapping ledger, through the store the student's own screen uses.**
`IUserScheduleReadStore` already takes a user id, so the admin route is a projection of the same
truth rather than a second definition of "what is on their calendar". It shows what was *written*,
not what the published schedule says should be — which is the only version of the question worth
asking when a student reports a missing lesson. The screen states that deletions cannot appear,
because the ledger holds only events still on the calendar (the ADR-089 limit, unchanged).

**7. The warning composer is shared, not copied.** `UserWarningForm` carries the composition,
preview and confirmation; `UserWarningComposer` keeps the user search around it and the account page
drops it in. The confirmation path — server-computed plan, binding `planHash`, hand-typed phrase,
required reason — is the only control that stops an approved preview from writing to a different set
of people (ADR-107). It exists once.

**8. The page offers exactly the three writes the backend supports for one account**, and names the
missing fourth. Manual activation (ADR-053), licence revocation (ADR-022) and a warning (ADR-107).
There is no profile edit, because no operator-authored profile write exists; the page says so in
words rather than rendering a disabled form, which would imply the capability lives somewhere.

**9. "Deactivate" is not offered as its own action.** Revoking the active licence is what stops
synchronization, and it is offered under that name with its real consequence stated: written events
are preserved and the student is not told (ADR-022, and the open risk recorded with it).

### Consequences

- `GET /api/admin/announcements` gained a `targetUserId` filter, so the account page can answer
  "what have we already told this person" from the same list the standalone screen reads.
- `AdminUserDetailResponse` gained `RecentActivity` (every audit category, not only sign-ins) and
  `CalendarConnection`. The connection projection deliberately omits the protected refresh token and
  the granted scopes: one is a credential and neither helps an operator (AI_GUIDELINE §15).
- The frontend role filter offered `Student`, which is not a member of `UserRole` (`User` /
  `SuperAdmin`) and would have been rejected by model binding. Corrected.
- A malformed `selector=key:value` pair is a 400, not a skipped filter: silently dropping one would
  return a wider result set than was asked for and nothing on the screen would say so.
- **Open risk:** the selector pre-resolution is a scan bounded only by the cohort filters applied
  beside it. An operator filtering on a selector with no class year selected reads every profile.
- **Open risk (unchanged, now more visible):** an operator still cannot correct a student's academic
  profile. The account page is where that absence is now obvious.

---

## ADR-109: Audience selectors enumerate within a dimension and narrow across dimensions

**Status:** Accepted and implemented
**Date:** 2026-08-18
**Implements:** `CalendarAudienceResolver.TargetsProfile` replacing `TargetsAnyOf`, with
four regression tests covering the multi-dimension cases
**Depends on:** ADR-020 (group expressions state their cohort model), ADR-027
(validated JSONB selectors), ADR-058 (audience resolution), ADR-099 (per-cohort
faculty-practice rotation)

### Context

A Grade 3 student reported the same faculty-practice session ("öğretim üyesi
uygulaması") appearing eight times on one date and hour.

`grade3_faculty_practice_v1` states two selectors per record, because a cohort
number only means something inside its curriculum group:

```text
[ curriculumGroup=3-A , facultyPracticeGroup=A3 ]
```

`CalendarAudienceResolver.TargetsAnyOf` returned `true` on the first selector that
matched the student's profile, whatever dimension it belonged to. A student in
`{curriculumGroup: 3-A, facultyPracticeGroup: A5}` therefore matched the
`curriculumGroup` half of all eight cohort records, and received the whole
rotation instead of their own session. The `g3-tr-a-faculty` snapshot produces 510
such candidates, eight per department slot.

The rule was not merely permissive, it was unstated: nothing declared whether two
selectors meant "either" or "both", and the two source families that state
selectors had drifted into needing opposite answers.

### Decision

State the rule and enforce it: **selectors sharing a dimension enumerate
alternatives; distinct dimensions each narrow the audience further.**

A record's selectors are grouped by dimension. The student must match at least one
value in *every* dimension the record names. A dimension the student has not
declared fails the record rather than widening it — an unconfirmable membership is
treated like ADR-011 treats a missing value, never guessed in the student's favour.

This is the reading both sources already needed:

- `Dönem 3A+3B Grubu` states `curriculumGroup` twice, and either half of the class
  attends. One dimension, enumerated.
- Faculty practice states a curriculum group *and* a cohort within it, and only the
  intersection attends. Two dimensions, narrowed.

The change is confined to one pure function. Every write path — initial sync,
the incremental planner, profile-change resync and both reconciliation services —
already routes audience decisions through `CalendarAudienceResolver.Applies`, so
there is exactly one place where this could have been wrong and exactly one to fix.

### Consequences

- A Grade 3 student receives one faculty-practice session per slot instead of eight.
- Verified against every committed real snapshot before landing: faculty practice is
  the only source family emitting more than one dimension per candidate
  (`curriculumGroup` + `facultyPracticeGroup`, 510 candidates). Grade 1 and Grade 2
  practice, vertical corridor, and all annual sources emit exactly one dimension per
  candidate, so their audiences are unchanged by construction, not by hope.
- Every dimension in `CurrentSupportedProfileSchema` is `Required = true`, so no
  existing student loses a lesson to the undeclared-dimension rule. A dimension added
  as optional in future would silently withhold lessons from students who skip it;
  that is the intended failure direction, but it must be a deliberate choice when it
  happens.
- **Existing users are not repaired by this change.** The seven surplus events per slot
  are already written and mapped, and nothing sweeps them: inventory reconciliation
  repairs missing and stale events but never deletes from absence (ADR-089), so the
  surplus mappings survive it untouched. `ProfileChangeResyncService` *does* remove a
  mapping that is no longer applicable while still live, so a Grade 3 student who
  re-saves their profile is cleaned up as a side effect — which is not a plan.
  A one-off audited repair for the affected Grade 3 cohort is required follow-up, and
  §13 makes it an explicit operation rather than something a background job may infer.
- The 887 existing .NET tests pass unchanged, which is the evidence that no other
  audience depended on the old reading.

---

## ADR-110: A source publishes only the audience it owns

**Status:** Accepted and implemented
**Date:** 2026-08-18
**Implements:** `authoritativeAudienceSelectors` on the source catalog, the domain
source, the parse contract and `ParseSourceContext`; audience narrowing in the annual
parser with the `rows.ignored.audienceNotOwnedBySource` metric; `grade3_yearly_v1`
bumped 1.0.0 → 1.1.0; migration `AddSourceAudienceAuthority`; regenerated Grade 3 goldens
**Depends on:** ADR-017 (source context is configuration), ADR-018 (identity and
content are separate), ADR-048 (evidence-based selector matrix), ADR-098 (the
English program states no A/B division), ADR-109 (dimension-narrowed audiences)

### Context

A Grade 3 student received the sessions both halves of the class attend twice.

Both Turkish Grade 3 workbooks state those sessions. On 11 January 2027 the A
workbook writes them as `Simüle Hasta Uygulaması /Anamnez +Serbest Çalışma` under
`ZORUNLU SEÇMELİ`, and the B workbook as `Simüle Hasta **FM** Uygulaması /Anamnez
+Serbest Çalışma` under `SEMİYOLOJİ DİLİMİ`. Same date, same 08:30–12:00 block,
same room, same `Dönem 3A+3B Grubu` audience — different titles.

The course identity is an identity component, so the two rows produce different
stable identities, different deterministic Google event ids, and two events.
Nothing existing could collapse them:

- Sorting the group pair (`3A+3B`, `3B+3A`, `3B/3A`) already gives both rows one
  audience key, which is necessary but not sufficient.
- Initial sync deduplicates on stable identity, and the identities differ.
- The `AudienceOverlap` validator sees one revision of one source, so it cannot
  see a cross-source duplicate at all.

It is not a stray case: the A workbook holds 60 joint rows and the B workbook 46.
The sets are not even mirror images, so no content-matching rule could pair them
without guessing which of two differently-worded rows is the copy.

### Decision

**A source declares the audience values it is the authority for, and publishes a
row only to the values it owns.** `G3-TR-A-ANNUAL` owns `curriculumGroup: ["3-A"]`
and `G3-TR-B-ANNUAL` owns `["3-B"]`. A joint row is narrowed to the owning group,
so each workbook publishes the session to the half it was written for.

**Ownership is source configuration, not a parser rule.** It travels in
`ParseSourceContext` beside academic year and program language, for exactly the
ADR-017 reason: the workbook does not state which half of the class it belongs to.
The parser applies the declaration; it does not invent it, so §5's "the parser must
not decide which users receive an event" holds.

**Narrowing happens before identity.** The audience is part of the stable identity
and the content hash, so it has to be final when they are computed — which is in
the parser. Narrowing downstream would mean the backend recomputing hashes the
parser owns, and golden determinism would stop meaning anything.

**Absence means "not narrowed", not "narrowed to nothing".** A dimension missing
from the mapping is untouched, so every other source publishes exactly what it did.
This mirrors how `supportedAudienceSelectors` treats silence (ADR-048): a source
that has not declared its cohorts must not be read as permitting none.

**A row addressed only to an unowned group is refused and counted**, through the
existing `rows.ignored.<reason>` metric, never widened and never silent (§9).

**The catalog refuses authority outside support.** A source claiming a value it
does not declare as supported would narrow every row naming it to nothing and
silently unpublish the lot, so the catalog fails to load instead.

### Rejected alternative

Deduplicating at synchronization time on a semantic key (date, time, audience). It
would silently pick a winner between two differently-worded records, be
order-dependent across sources, and collapse the identity/content separation
ADR-018 rests on. Ownership is decided once, in configuration, by someone who knows
which document is which.

### Consequences

- A Grade 3 student receives each joint session once, worded as their own half's
  programme committee published it.
- **No content is lost, and this was checked rather than assumed.** The A workbook's
  term column holds 1195 `3A` rows and 60 joint ones and *no* B-only row; the B
  workbook 1196 `3B`, 46 joint and no A-only row. Candidate counts are unchanged at
  1119 (A) and 1110 (B) — the joint rows are narrowed, not refused, and
  `rows.ignored.audienceNotOwnedBySource` stays at zero for both real fixtures.
- 60 A and 46 B stable identities changed, so the first republication after this
  lands is a create-and-retire of those lessons rather than an update. That is a
  real diff on real calendars: the affected joint sessions are removed and rewritten
  once. It must go out as a deliberate publication, watched, not as a quiet
  redeployment — ADR-018's identity guarantee is being deliberately broken here, and
  the profile version bump is what forces the re-parse that makes it visible.
- `grade3_yearly_v1` 1.0.0 no longer exists in the registry, so all three sources
  using it — including the English one, whose output changes only in version and
  digest — move to 1.1.0.
- The English Grade 3 source is unaffected in substance: it states no A/B division
  and declares no authority (ADR-098).
- **Open risk:** ownership is stated per source in the catalog and nothing checks
  that a group is owned by exactly one source. Two sources owning `3-A` would
  reintroduce the duplicate; none owning it would publish those rows nowhere. A
  catalog-wide coverage check is the obvious follow-up and is not built.

---

## ADR-111: A corrected audience rule is repaired by an audited, scoped convergence request

**Status:** Accepted and implemented
**Date:** 2026-08-18
**Implements:** `CohortCalendarRepairService`, `ICohortCalendarRepairStore` and its
PostgreSQL store, `POST /api/operations/calendar-repairs/preview` and
`POST /api/operations/calendar-repairs`, the `CalendarRepairRequested` audit
category, and the catalog rule requiring exactly one owner per audience share
**Depends on:** ADR-089 (inventory never deletes from absence), ADR-096 (profile-change
convergence), ADR-107 (plan-hash-bound confirmation), ADR-109 (dimension-narrowed
audiences), ADR-110 (source audience ownership)

### Context

ADR-109 stopped eight faculty-practice cohorts reaching one Grade 3 student. It did not
remove the seven surplus events already on every affected calendar, and nothing else
would either:

- The canonical records never changed, so no semantic diff mentions them. Diff-driven
  sync is edge-triggered and there is no edge.
- The periodic inventory pass repairs missing and stale events and **deliberately never
  deletes from absence** (ADR-089), so it walks past them by design.
- `ProfileChangeResyncService` does remove a held-but-no-longer-applicable event — but
  only when a student happens to re-save their profile.

"Wait for students to re-save their profiles" is not a cleanup strategy; it is an
accident that fixes some accounts and not others. Meanwhile AI_GUIDELINE §13 permits
exactly one way out: an explicit, audited repair operation.

The joint-session duplicates of ADR-110 are a different matter and need no repair. Those
records *do* change, so publishing the 1.1.0 revision emits `Deleted` for the old
identities and incremental sync removes them. Only the ADR-109 surplus is orphaned.

### Decision

**The repair does not delete anything itself. It requests convergence.** It computes what
is surplus, shows the operator that plan, and on confirmation flags the affected
connections for the existing profile-change convergence pass.

Every deletion is therefore still made by `ProfileChangeResyncService`, under the bounds
it already has and is already tested for: publication-gated, budgeted per cycle,
freeze-aware, resumable, and skipping a user whose credential has died. A second deletion
path would mean a second copy of those guarantees to keep true, and the first thing such
a path would be asked to do is bypass the ADR-089 rule that makes the first one safe.

**A repair is scoped to one program.** An unscoped pass would be a whole-population
calendar operation authorized by one click.

**The confirmation is bound to the plan by hash**, with the per-user counts in the
material and not only the totals (the ADR-107 rule): the same total surplus spread over
a different set of students is a different repair.

**Rows whose lesson is no longer published are counted and never touched.** The plan
reports them so an operator can investigate, and the repair leaves them exactly where
they are — removing one would be deleting from absence (ADR-089). The cohort total counts
them for *every* user in scope, including those with nothing else to converge, because a
student whose only anomaly is such a leftover would otherwise never appear at all. That
total is deliberately not the sum of the per-user list.

**A freeze declines the request rather than queueing it.** Queueing would defer the
writes rather than refuse them, which is not what freezing a program asked for.

**The audit entry records the plan hash, the counts and a required reason**, because
these deletions are ones no published revision derived, and "why did these lessons
disappear" has to be answerable from the trail alone (§19).

### Ownership coverage, added here

ADR-110 left an open risk: nothing checked that each audience share had exactly one owner.
The catalog now refuses to load unless, among sources sharing one program *and* one parser
profile, every share is owned once — no share owned twice (which reinstates the duplicate),
none owned by nobody (which unpublishes it silently), and no sibling declaring no authority
beside one that does (which leaves the duplicate on one side). Sources where nobody claims
authority are left entirely alone, which is every other source in the catalog.

Writing this rule immediately caught an unrealistic test fixture of ADR-110's own — a lone
source owning half of what it supports — which is the kind of gap it exists to find.

### Consequences

- The Grade 3 Turkish surplus is now removable deliberately, by a named operator, with a
  reason and a plan hash on the audit record.
- The repair inherits its safety from the convergence path rather than restating it, so
  ADR-089 continues to hold with no exception carved into it.
- Convergence also *writes* what a calendar is missing. That is correct and is shown in
  the plan, but it means a repair is not a deletion-only operation and must not be
  described as one.
- **Open risk:** the plan is computed per user against the full published set, so a large
  program is a linear scan over records × users at preview time. It is an operator-driven
  screen rather than a hot path, but it is not free, and no bound is imposed on cohort size.
- The operator screen is `CalendarRepairControl` on the admin operations panel; editing
  any part of the scope drops the previewed plan so a stale hash can never stay attached
  to a changed form.
- **Pre-existing, unchanged:** the admin audit-category dropdown lists six of the ten
  categories, having drifted since ADR-107 added three; `CalendarRepairRequested` makes
  four missing. The audit API filters on any category, so the trail is complete either way.

### Amendment, 2026-08-18: the audit is written before the side effect

The first Grade 3 repair returned 500 and still ran. Two defects, one visible and one not.

The visible one: `audit_events.Metadata` is a `jsonb` column and the endpoint built the
value as a delimited `key=value` string, which Postgres rejected (`22P02`). The property
is a plain `string?`, so nothing failed until insert time. It is now serialized like every
other caller's, and the contract is stated on `AuditEventDraft.Metadata` rather than left
to be inferred.

The one that mattered more: the audit was appended **after** the convergence request. The
flagging had already committed, so the failed insert produced a 500 over work that was
already queued — 446 events were then deleted with no record that anyone authorized it.
That is precisely the outcome §19 exists to prevent, and the ordering, not the serializer,
was what made it possible.

`RequestAsync` now takes the audit as a callback and invokes it after the freeze check and
the plan-hash match but **before** flagging any connection. A throw from it abandons the
repair. The safe failure is a repair that does not happen; a repair that happens
unrecorded is not a failure the system may choose.

The metadata records the *authorized* plan's user count rather than the number of
connections that could take the flag, because the audit answers what was approved. How
many were actually flagged is in the response.

**Not backfilled:** the run that deleted the 446 events has no audit row and will not get
one. Writing a record after the fact for an event that was never audited would make the
trail assert something it did not observe.

---

## ADR-112: The companion declaration is catalog-owned, like every other configured field

**Status:** Accepted and implemented
**Date:** 2026-08-18
**Implements:** `ScheduleSourceStore.ConfigurationOf` copying `CompanionSourceIds` on
update, and a persistence regression test for a row seeded before its companion was
declared
**Depends on:** ADR-100 (the annual owns the bedside slot, the bedside document owns the
topic), ADR-102 (companion snapshots and the companion fingerprint)

### Context

Reported from a real calendar: every Grade 3 bedside event reached students with an empty
description. The topic the bedside document states for each of the 92 sessions was
nowhere on them.

Every part of the mechanism was in place and correct. Parsed against the real fixtures,
`grade3_yearly_v1` puts a topic on 88 of the A group's 92 bedside sessions and 87 of the
B group's; `CanonicalScheduleRecord.Notes` persists it; `CalendarEventPresentationPolicy`
renders it as the `Konu:` paragraph; the content hash covers it, so a corrected topic
moves the event. The catalog declares `companionSourceIds` on both Turkish annuals, and
both bedside documents had been acquired with their payloads intact.

The database disagreed with the catalog. Every Grade 3 row held `CompanionSourceIds = []`,
and every annual parse run — including the ones re-parsed at profile version 1.1.0 the
same day — carried an empty `CompanionFingerprint`.

`ScheduleSourceStore.UpsertAsync` copies only the fields it names into an existing row, and
it did not name this one. The insert path passed the whole source, so a database seeded
after the declaration would have been right; these rows were seeded before it, so they kept
an empty list through every redeploy. The comment above that dictionary already said the
declared cohorts and the shared-document group are catalog-owned "so an edited allowlist
does not apply to a fresh database and silently not to a running one". The companion is the
same kind of field and was simply left out of it.

### Decision

**`CompanionSourceIds` is copied on update, like every other catalog-owned field.**

### Consequences

The reason this hid for as long as it did is worth stating, because it generalizes past
this field. A missing companion is *designed* to be survivable: ADR-102 requires the annual
to publish its full schedule whether or not the document that annotates it can be read, so
the pipeline reported success at every stage. The parse completed, the revision validated,
the diff was clean, the sync succeeded, and 184 events reached calendars — correct in date,
time, title, room and colour, and missing only the one thing that came from the companion.
Nothing in the pipeline can distinguish "no companion is declared" from "the declared
companion did not arrive", because by the time the poller has the row, the declaration is
gone.

Recovering is ordinary operation rather than repair. The worker reseeds the catalog at
startup, which writes the companions onto the two annual rows; the next poll resolves them,
the changed companion fingerprint opens a new parse run over the same snapshot, and the
published revision changes the content hash of exactly the bedside records. Incremental
sync updates those events in place. No stable identity moves and nothing is deleted.

**Open risk, not addressed here:** a declared companion that cannot be resolved at poll
time is still silent. `ResolveCompanionsAsync` leaves it out, which is the required
degradation, but neither the poll result nor a log says it happened, so the same class of
fault would again be visible only as an empty description on a student's calendar.

---

## ADR-113: The department a title states for a group belongs to the record addressed to it

**Status:** Accepted and implemented
**Date:** 2026-08-18
**Implements:** `resolve_group_departments` in the parser's department normalization,
`_stated_departments` in the annual profile, `grade3_yearly_v1` 1.2.0, the two source
abbreviations as `DepartmentCatalog` aliases, and department naming in
`CalendarEventPresentationPolicy.Description`
**Depends on:** ADR-098 (the English program states no curriculum group), ADR-100 (the
annual owns the bedside slot), ADR-110 (a source publishes only the audience it owns)

### Context

Grade 3 bedside events carried no department. The description rendered the curriculum
block and nothing else, while the department a student sits the session with — the single
most useful thing about a bedside practice — appeared only inside the event title.

The sources state it there and nowhere else. The block cell of those rows says
`SEMİYOLOJİ DİLİMİ`; the title says
`Hasta Başı Uygulama-1 A Grubu (İç H.) B Grubu (ÇSvH)`. One row carries the session both
halves of the class attend, and it names the department of each. The English workbook
writes the same construction, in the same Turkish wording, as
`Practice with the patient-1 A Grubu (İç H.) B Grubu (ÇSvH)`.

### Decision

**A record carries the departments its title states for the groups it addresses.**

A row published to one curriculum group takes the department stated for that group; a
program-wide row takes every department the title states, in the order the title states
them, because it is published to all the groups named. Nothing is inferred for a group the
title does not name. The block cell keeps precedence in the order, and a department stated
in both places is kept once.

The construction is recognised only when the title states it for **more than one group**.
That is not a stylistic preference. A Grade 1 lesson is titled
`BİLGİ KURAMI ve BİLİMSEL DÜŞÜNMEYE GİRİŞ A-B GRUBU (İngilizce Tıp ile Ortak Ders)`, where
the parenthesis is a note about the lesson rather than a department; the first
implementation read its `B` as a group and gave five Grade 1 lessons a department the
source never stated. The golden files caught it. The group letter must therefore stand
alone, and a lone `X Grubu (...)` states no department at all.

**The record keeps the source's words; the calendar names the department in full.** The
canonical value stays `İç H.`, because that is what the source wrote (§10). The
description resolves each department through `DepartmentCatalog` and prints the catalog's
name when it resolves, or the source's words when it does not. The two abbreviations are
registered as aliases of the departments they abbreviate, which is what the catalog is
for: `İç H.` in a Grade 3 title and `İÇ HASTALIKLARI AD.` in a Grade 1 block cell are one
department, and a student should read one name for it.

### Consequences

Against the real workbooks this publishes a department on 91 rows of each Turkish annual
and 92 of the English one. `grade3_yearly_v1` goes to 1.2.0 and the four Grade 3 goldens
move; the Grade 1 and 2 goldens are unchanged, which is the assertion that the shared
implementation did not shift under them.

Departments are content, never identity, so the affected events are updated in place. The
label is unaffected: a practice is coloured as a practice before its department is
consulted, so no event changes colour and the multi-department English rows do not become
integrated sessions.

**Every department-bearing event is rewritten once.** Naming departments through the
catalog changes descriptions that were already correct — `BİYOFİZİK AD.` now reads
`Biyofizik` — across roughly eight thousand canonical records. The comparer sees a
description that differs from the event on the calendar, and the inventory pass updates
each in place. That is one bounded rewrite over every synchronized user, not a
delete-and-recreate, but it is real Google API traffic and it should be expected rather
than discovered.

**The English program remains a compromise.** Its rows address all its students and carry
both departments, so an English student reads both names and has to know which half they
are in. Publishing one of them would be a guess. Resolving it properly means modelling the
English program's own A/B division, which is an audience change (ADR-098) and not this
one.

---

## ADR-114: The source catalog is an administratively editable document with a history

**Status:** Accepted and implemented
**Date:** 2026-08-19
**Implements:** `ScheduleSourceCatalogEditingService`, `ScheduleSourceCatalogPlanner`,
`ScheduleSourceCatalogFile`, `ScheduleSourceCatalogRevision` and its store, the
`/api/admin/source-catalog` endpoints, the `SourceCatalogEditor` admin surface, and the
shared `/srv/sirkadiyen/shared/config/schedule-sources.json` deployment path
**Depends on:** ADR-017 (the source context is configuration, not inference), ADR-079
(an uploaded source names itself by URN), ADR-080 (shared document groups), ADR-102
(companion evidence), ADR-110 (audience ownership), ADR-112 (the catalog owns every
configured field)

### Context

`config/schedule-sources.json` states what every source is: where it is published, which
parser profile reads it, which academic year, class year and program language it belongs
to, which cohorts it may state and which of them it owns. It changes for entirely ordinary
reasons — the faculty republishes a workbook at a new URL, a practice group is added, a
parser profile is bumped, a source is retired mid-year.

Until now every one of those was a commit, a review and a redeploy of the worker, because
the file shipped inside the worker's release directory and was read from there at startup.
That is a poor fit for a document whose contents are facts about someone else's
spreadsheets, and the practical result was drift: the file said what was true when it was
last deployed.

Two things made this more than an inconvenience. The catalog is also the *only* place the
audience rules are declared, so a correction that a student is waiting for is gated on a
deployment. And nothing about the file was recoverable: there was no record of who last
changed what, beyond a commit in a repository the operator may not have.

### Decision

**The catalog is a document the SuperAdmin edits from the administration panel, stored
outside every release directory, applied through a previewed plan, and retained in full.**

Six parts, each of them load-bearing:

1. **One document, one loader.** The panel validates a proposed catalog with
   `ScheduleSourceCatalogLoader`, the same class the worker loads with at startup, so an
   edit the panel accepts can never be a catalog the worker refuses to start on. The
   loader now also refuses a property it does not know
   (`UnmappedMemberHandling.Disallow`): a mistyped `parserProfileVersionn` used to
   deserialize to nothing and validate cleanly, which was survivable while a reviewer read
   every diff and is not survivable in a text box.
2. **A plan, not a text box.** `ScheduleSourceCatalogPlanner` compares the proposed
   document with the one on disk field by field and classifies each change. `displayName`,
   `notes` and `fixturePath` are low risk; everything the pipeline reads is high risk, and
   an audience or parser change also raises a named warning that says what will happen to
   already-published lessons. The operator confirms that plan by its hash.
3. **Two hashes.** `baseContentHash` binds the edit to the document that was read, and
   `planHash` binds the confirmation to the plan that was shown. A file changed by anyone
   else — another administrator, a shell — turns both into a 409 rather than a silent
   overwrite.
4. **Atomic write, rolled back on a failed commit.** The document is written to a sibling
   temporary file and moved into place, then the revision, the source upsert and the
   retirement of dropped sources commit in one transaction. If that commit fails the
   previous document is written back, because a catalog the database has never seen would
   otherwise take effect silently at the next worker restart.
5. **A dropped source is retired, never deleted.** Absence from a configuration file is
   not a publication decision (AI_GUIDELINE §13). Its polling is disabled; its row, its
   snapshots, its revisions and every calendar event it published stay.
6. **The full document is retained.** `schedule_source_catalog_revisions` stores each
   applied document with its actor, reason and change summary, plus a baseline row holding
   what was on disk before the first edit. A rollback is loading a stored revision into the
   editor and applying it through the same preview and confirmation. The cross-cutting
   `AuditEvent` gets a `ScheduleSourceCatalogUpdated` entry so the change is visible in the
   activity log an operator actually reads; the evidence itself is the revision row.

**The live document moves out of the release directory.** Both hosts read
`/srv/sirkadiyen/shared/config/schedule-sources.json`, set by the systemd units. The copy inside
the worker artifact becomes a seed that `sirkadiyen-activate` installs only when the
directory has no catalog yet. The API unit gains that directory in `ReadWritePaths`; the
worker only reads it.

### Consequences

**A repository edit to `config/schedule-sources.json` no longer reaches a running server.**
It redeploys the worker and updates the seed, and the live document stays exactly as the
panel last left it. This is the deliberate trade: an administrative edit that a deployment
could revert would be worse than no editor at all. The repository file remains the
bootstrap for a new server and the fixture the tests load.

**An edit takes effect without a worker restart.** The API upserts the source rows in the
same transaction that records the revision, and the pipeline reads its configuration from
those rows. The written file is what a *restarting* worker reads, which is why the two must
never disagree.

**The dangerous edit is a legal one.** Changing `classYear` on a source with published
lessons is valid configuration and moves that source's whole audience at the next dispatch;
events already written to the old cohort's calendars are not removed by it. The plan says
so in as many words and points at the ADR-111 calendar repair, but nothing stops an
operator who has read it. That is the same position the freeze and the repair are in.

**Concurrency is optimistic and last-writer-refused.** Two administrators editing at once
is resolved by refusing the second, not by merging. For a document of this size and
sensitivity, a merge would be a guess.

**Not addressed here:** the panel cannot start a poll or a parse, so a corrected source is
picked up on its next scheduled cycle. And a catalog edited directly on the server by
someone with a shell is still legal — the panel notices (the stored revision no longer
matches the file) but cannot prevent it.

---

## ADR-115: A cohort's academic year is rolled over explicitly, and the divergence is reported

**Status:** Accepted and implemented
**Date:** 2026-08-19
**Implements:** `ProfileAcademicYearRolloverService`, `SupportedProfileSchemaCatalogCheck`,
`StudentProfile.MoveToAcademicYear`, `CohortCalendarRepairService.PlanForUserAsync` /
`RequestForUserAsync`, `POST /api/operations/profile-rollovers[/preview]`,
`POST /api/admin/users/{id}/calendar-recheck[/preview]`, supported-profile schema 1.3
**Amends:** ADR-103, ADR-111
**Caused by:** the Grade 2 Turkish rollover of 2026-08-19

### Context

The Grade 2 Turkish annual and practice sources were repointed at the 2026-2027
workbooks from `/admin/sources` (ADR-114) and their `academicYear` moved with
them. The revision published, the diff was reviewed and released, and the class
watched a year of lessons disappear from their calendars with nothing written
back.

Nothing was broken. Every stage did exactly what it was built to do, and the two
halves of incremental dispatch simply stopped agreeing about who a lesson is for:

- **Deletion is ledger-driven.** `DeleteForHoldersAsync` asks
  `IUserCalendarEventMappingStore.ListForStableIdentityAsync` who holds a lesson.
  A ledger row is keyed by `(source, stable identity)` and states no academic
  year, so every 2025-2026 event was removed correctly.
- **Insertion is cohort-driven.** `ReconcileRecordAsync` asks
  `ICalendarSyncTargetReadStore.ListCohortTargetsAsync(record.AcademicYear, …)`,
  which filters `StudentProfiles.AcademicYear`. Every stored Grade 2 profile
  still said 2025-2026, because a profile is stamped with its program's year once
  — at save time, from the schema — and nothing has ever restamped one.

So the new records resolved to nobody. The diff was marked `Dispatched` with
zero insertions planned, which is indistinguishable from a diff that had nothing
to insert.

ADR-103 predicted this exact failure — "an empty calendar, with the profile
saved, the revision published, the diff computed and every check downstream
reporting success" — and solved it only for *new* profiles. Grade 3 was a new
program with no existing students, so the gap it left was invisible until a
grade with a population rolled over.

### Decision

1. **A program's academic year and its sources' academic year are one fact
   stated twice, and moving one obliges moving the other.** The schema now
   states 2026-2027 for Grade 2 Turkish (schema version 1.3, so a profile still
   on 1.2 is identifiable as one stamped before the rollover).

2. **Existing profiles are moved by an explicit, audited operator action, not a
   migration.** `ProfileAcademicYearRolloverService` follows ADR-111's shape:
   plan, show, confirm by plan hash, audit before the side effect. A deployment
   that silently restamped stored student data would be a whole-population write
   authorized by nobody (AI_GUIDELINE §13), and a rollover legitimately needs a
   human to decide *when* — the sources for the rest of the cohort may not have
   moved yet.

3. **The target year is read from the deployed schema and never accepted from
   the caller.** An operator able to type it could stamp a year new sign-ups
   would not receive, splitting one cohort across two years — which is the
   failure being repaired, reproduced by hand. A scope naming a year the schema
   does not state is refused with a cause: deploy the schema first.

4. **A rollover writes only the academic year and the schema version.**
   `StudentProfile.MoveToAcademicYear` exists rather than reusing `Update`, so
   it is structurally impossible for an operator action to alter a selector or a
   student number. A profile whose selectors the target program refuses is
   excluded from the move entirely and reported, rather than restamped into a
   profile the schema would reject the next time its owner opened their settings.

5. **It queues convergence; it never writes a calendar.** Every event is still
   written by `ProfileChangeResyncService` under its existing bounds —
   publication-gated, budgeted per cycle, freeze-aware, resumable, and never
   deleting from absence. A second write path would mean a second set of those
   guarantees to keep true.

6. **The divergence is reported at runtime.** `SupportedProfileSchemaCatalogCheck`
   compares the deployed schema against the loaded catalog on every worker start
   and logs an error naming the cohort, both years, and the repair. It does not
   block anything: the pipeline is not wrong when the two disagree — every parse,
   validation and publication is correct — and refusing to publish would let one
   mistyped catalog field take a program offline. What was wrong is that nothing
   said it out loud.

7. **A calendar re-check exists for one student.** `PlanForUserAsync` /
   `RequestForUserAsync` are the cohort repair narrowed to a single row, on the
   same store, freeze check, plan hash and audit ordering. A narrower blast
   radius earns no weaker guarantees. It lives on `/admin/users/{id}` because
   that is where the question "is this person's calendar right?" is asked.

### Consequences

- Grade 2 Turkish profiles must be rolled over from `/admin/operations` before
  any 2026-2027 lesson reaches a Grade 2 calendar. Until then the cohort's
  calendars stay as the dispatch left them.
- **The Grade 2 Turkish anatomy and vertical-corridor sources are still on
  2025-2026.** Their lessons therefore do not apply to a rolled-over profile and
  will be absent from 2026-2027 calendars until those documents are captured.
  They are not deleted: convergence measures removals against the *new* year's
  published identities, so a ledger row from the old year is invisible to it and
  left alone (ADR-089). The plan reports the count as "stranded" so an operator
  is told rather than surprised.
- The three Grade 2 Turkish selector dimensions are now evidenced across two
  academic years rather than one. `EverySelectorValueIsDeclaredByAConfirmedSourceForThatCohort`
  is deliberately year-agnostic for that reason; the new
  `EveryProgramsYearIsOneTheCatalogPublishesForThatCohort` guards the pair that
  actually decides audience.
- The committed `config/schedule-sources.json` states 2026-2027 for the two
  rolled sources while its `sourceUri` and `fixturePath` remain the pre-rollover
  ones, because the running catalog is the authority (ADR-114) and the new
  year's workbook is not in the repository. The file is a deployment default,
  and the year is the half that must not start out wrong.
- Rollover is now a routine with a surface. The next grade to move needs a
  catalog edit, a schema constant, and one confirmed plan.

---

## ADR-116: A deleted managed calendar is rebuilt explicitly, by the student or an operator

**Status:** Accepted and implemented
**Date:** 2026-08-19
**Implements:** `GoogleCalendarConnection.ResetForCalendarRebuild`,
`ManagedCalendarRebuildService`, `IUserCalendarConnectionStore.RebuildManagedCalendarAsync`,
`GET`/`POST /api/calendar/rebuild`, `GET`/`POST /api/admin/users/{id}/calendar-rebuild`,
`AuditEventCategory.ManagedCalendarRebuilt`
**Completes:** ADR-062 ("automatic recreation of a deleted whole calendar is not part of this
decision")
**Amends:** ADR-024, ADR-058

### Context

ADR-062 deliberately left the deleted-calendar case to "an explicit repair flow" and
`MarkManagedCalendarUnavailable` says the same in its own documentation. That flow was
never written, and its absence was not a missing convenience. It was a closed loop:

1. The student deletes the Sirkadiyen calendar. The next write returns 404/410, which
   `GoogleCalendarClient` maps to `GoogleManagedCalendarUnavailableException`, and
   `ManagedCalendarUnavailableAtUtc` is stamped.
2. Every read store filters `ManagedCalendarUnavailableAtUtc == null`, so the student
   drops out of diff dispatch, inventory, profile convergence, announcements and cohort
   repair at once.
3. `OnboardingStateService` reports `ActionRequired`, which the frontend routes to the
   calendar consent step.
4. Consenting calls `Reauthorize`, which **does not clear the flag** — deliberately, since
   a dead credential and a deleted calendar are different problems. Onboarding therefore
   reports `ActionRequired` again, and the student is routed back to the same screen.

Nothing cleared the flag. The only writer that did was `AttachManagedCalendar`, which
throws when an id is already attached, and the recovery path that reaches it
(`InitialCalendarSyncService.EnsureCalendarAsync`) runs only during initial sync and only
when `ManagedCalendarId` is null. A `Completed` connection never entered it.

The screen also stated the wrong cause. `ActionRequired` has exactly one source — an
unreachable calendar — because a revoked grant clears `Authorized` and reports
`CalendarAuthorizationRequired` instead. The page said "Google access appears to have been
revoked", which is both untrue and the reason the loop looked like a reasonable next step.

### Decision

1. **`ResetForCalendarRebuild` returns the connection to the state initial synchronization
   starts from**: calendar detached, flag cleared, `InitialSyncState` back to `Pending`,
   and every queued piece of work scoped to the calendar that is gone (inventory cadence,
   reconciliation replay cursor, profile-resync request) cleared with it. Initial sync
   resolves the whole audience from the profile when it runs, which subsumes all three.

2. **Detaching the id is the mechanism, not bookkeeping.** It is what makes the existing
   recovery path reachable: initial sync looks for a marker-matched orphan calendar
   (ADR-063) before creating one. That also makes the operation safe when the calendar
   turns out *not* to be deleted — a transient 404, a permissions blip — because the
   marker search reattaches the same calendar and the deterministic event ids (ADR-024)
   make every re-insert a harmless already-exists. The repair therefore does not have to
   answer "was it really deleted?" correctly to be safe.

3. **The ledger rows are discarded, in the same transaction.** They describe events on a
   calendar that no longer exists. Leaving them would make initial sync skip every lesson
   they name — it writes what the ledger does not already record — producing an empty
   calendar with no state that explains it. This is not deleting from absence (ADR-089):
   there is no published decision being overridden and nothing on any calendar to remove.

4. **It is left `Pending`, not `InProgress`.** Populating a calendar is the user's
   decision to start (ADR-058) and a rebuild writes a year of events. `Status` is
   untouched, so a connection that *also* needs re-authorization still passes through
   consent first.

5. **Two doors, one service.** The student repairs their own account from the screen they
   were stuck on; a SuperAdmin repairs it for the student who does not find that button or
   writes in instead. The eligibility rule, the freeze check, the ledger discard and the
   audit record must not be able to differ between them, so both go through
   `ManagedCalendarRebuildService`. The operator's endpoint requires a reason and the
   student's does not: on their own account, that they asked *is* the reason.

6. **Refused during a freeze.** A rebuild queues no calendar write by itself, but it
   discards durable state, which is what a freeze exists to stop until someone has looked
   (ADR-034/043).

7. **The audit is written before the reset**, as everywhere else, so an unrecordable
   request leaves the ledger intact rather than untraceable.

### Consequences

- `ActionRequired` now has a real resolution, and the onboarding copy names the actual
  cause. `ReauthorizingDoesNotClearTheUnavailableFlag` is pinned as a test rather than
  left as an implicit assumption, because the temptation to "fix" the loop inside
  `Reauthorize` would silently merge two different repairs.
- A rebuilt student loses the history that lived on the deleted calendar. Nothing can
  recover it — the calendar is gone — and both screens say so before the action.
- The rebuild is the one operator action on `/admin/users/{id}` that discards student data
  rather than queueing convergence, so it is confirm-then-reason like the destructive
  operations, not a one-click button.
- Detection is still passive: the flag is stamped by the next write that fails, not by a
  watcher. A student who deletes their calendar and never opens Sirkadiyen may wait a
  full inventory cadence before the state is even noticed. That is unchanged by this ADR
  and remains open.

---

## ADR-117: Stored profiles follow the deployed schema's academic year without being asked

**Status:** Accepted and implemented
**Date:** 2026-08-19
**Implements:** `ProfileAcademicYearRolloverService.ReconcileDriftAsync`,
`IProfileAcademicYearRolloverStore.ListDriftedAsync`, `ProfileAcademicYearDriftOptions`,
`ProfileAcademicYearDriftTask`
**Amends:** ADR-115

### Context

ADR-115 made the academic-year rollover an audited operator action, on the reasoning that
restamping a cohort's stored profiles is a whole-population write and should not become
ordinary (AI_GUIDELINE §13). That is right about the *shape* of the operation and wrong
about *whose decision it is*, and the difference showed up immediately: the screen was
built, the schema was deployed, and the profiles sat on 2025-2026 because running it was a
step someone had to remember.

The decision is already taken before any operator opens that screen. The supported-profile
schema is compiled in, so deploying a schema that states a new year for a program **is**
the deliberate act, and every profile saved after that deployment is stamped with the new
year automatically (ADR-103). A profile written before it and still carrying the old year
is not a second decision waiting to be made — it is the first one not having finished. What
ADR-115 modelled as an authorization was really a manual step standing in for a
reconciliation.

### Decision

1. **A worker stage reconciles stored profiles against the deployed schema every cycle**,
   restamping any whose academic year its program no longer states and queueing the
   convergence that writes that year's lessons. It runs immediately before the
   profile-resync stage it feeds, inside the shared Calendar fence so two workers cannot
   restamp the same batch, and it is bounded per program per cycle.

2. **It never restamps onto a year that publishes nothing for the cohort yet.** Between
   deploying a schema that names a new year and publishing the first revision under it
   there is a window in which moving a student guarantees them an empty calendar. Waiting
   for that publication costs nothing and removes the only way this stage could make things
   worse.

3. **The scoped freeze is the off switch.** An operator who wants to time one program's
   rollover by hand freezes that program and uses the ADR-115 screen. No separate feature
   flag: a second switch with its own semantics is a second thing to keep true, and the
   freeze already means exactly "stop touching this program".

4. **A profile whose selectors the target program refuses is reported, never restamped.**
   That case needs a person — a re-onboarding, or a schema that still declares the dropped
   dimension — and the operator screen is where its owner is named.

5. **The audit entry is per program batch, written before the batch is applied.**
   Unattended does not mean unrecorded; a change nobody asked for is precisely the one that
   must be reconstructable. Per student would be an entry each for several hundred
   profiles, burying the log in the place someone goes to understand what happened.

6. **The ADR-115 screen stays.** It is not redundant: it previews what a move would put
   back before anything happens, names the blocked profiles, and acts immediately instead
   of within a cycle. The reconciler is the same service, unattended.

### Consequences

- A future grade rollover needs a catalog edit and a schema constant. Nothing else.
  Forgetting the operator step is no longer a way for a cohort to lose a year of calendars.
- **It moves the academic year and never the class year.** That is correct only while the
  same students remain in the same class — which is what this rollover is: the faculty
  republished the Grade 2 Turkish documents for the new year, and the students are the same
  people. It is *not* correct for the case where a cohort advances a class year, and
  running this against such a cohort would give every student the incoming class's
  schedule.
- **There is still no class-year progression mechanism, and by decision there will not be
  an automatic one.** A student who advances updates their own profile: the class year is
  freely editable on the profile form, saving restamps their program's academic year and
  queues convergence through the existing path (ADR-096). No machine can infer which
  practice, anatomy or curriculum group they belong to in the new class, so inventing one
  would be inventing an audience. Recorded here so the boundary above is not read as an
  oversight.
- The worker now writes to the cross-cutting `AuditEvent` log, which it never did before.
  `AuditEventRecorder` and `IAuditIpProtector` are registered in its composition; there is
  no client IP on a background pass, and the protector is required only to construct the
  recorder. `WorkerCompositionTests` resolves the whole fenced stage from the real service
  collection, because a missing registration was previously invisible to every test and
  visible only as a crash on a deployed worker's first cycle.

---

## ADR-118: Account deletion — erase the person, keep the anonymized trail

**Status:** Accepted and implemented
**Date:** 2026-08-20
**Implements:** `AccountDeletionService`, `IAccountDeletionStore`/`AccountDeletionStore`,
`IExternalAccountCleanup`/`ExternalAccountCleanupService`,
`IUserCalendarClient.DeleteManagedCalendarAsync`,
`IGoogleCalendarAuthorizationClient.RevokeRefreshTokenAsync`,
`AuditEventCategory.AccountDeleted`, `POST /api/account/delete`,
`POST /api/admin/users/{id}/delete`, frontend `DeleteAccountCard` and the operator danger zone
**Relates to:** ADR-089 (the `AuditEvent` trail), ADR-057 (the encrypted grant), ADR-024 (the
managed calendar), ADR-116 (one service, two doors)

### Context

There was no way to delete an account — neither for the owner (a KVKK/GDPR "right to erasure"
request) nor for an operator. The data an account accretes is spread across cascading personal
tables and several `RESTRICT`-bound ones, plus an external Google footprint (a dedicated managed
calendar and an encrypted refresh-token grant). Doing this wrong either strands foreign-key
references, destroys the audit history the platform depends on, or leaves a live Google grant and
calendar behind after the account is gone.

### Decision

1. **Erase the person, keep the trail anonymized.** The personal aggregates (student profile,
   Calendar connection and its encrypted token, event-mapping ledger, department-colour
   preferences, any single-user announcement addressed to them and its deliveries) are deleted by
   database `ON DELETE CASCADE` when the user row is removed. The cross-cutting `audit_events` log
   is **kept** with the deleted person's identifying fields cleared (actor id, actor e-mail, both IP
   forms, user agent). This balances erasure against AI_GUIDELINE §19's append-only, traceable
   audit requirement: the history of what happened on the platform survives without naming a deleted
   person. Nulling the actor id is also what releases the `RESTRICT` link so the user row can be
   deleted at all.

2. **The `RESTRICT`-bound licensing rows are handled explicitly, not cascaded.** A redeemed
   single-use license row is **kept** (the code must stay unusable) but detached — its
   `RedeemedByUserId`/`RevokedByUserId` link to the deleted user is nulled, so the row survives as an
   anonymized fact that a redemption happened. The erased subject's own `license_audits` rows (whose
   actor id cannot be null) are the record of *them* acting, so they are removed as part of the
   erasure; the detached license row and the `AccountDeleted` event preserve the platform-level fact.

3. **The external Google cleanup is best-effort and runs first, outside the transaction.** The
   managed calendar is deleted and the refresh-token grant revoked *before* the database erasure,
   never inside it (systemPatterns §16 forbids an external call in a DB transaction). Every failure —
   a dead token, an unreachable API, a calendar the user already removed — is logged and folded into
   the outcome, never rethrown: a person's local erasure must not depend on Google being reachable.
   What could not be done is recorded in the deletion's audit metadata. This lives in an
   infrastructure port (`IExternalAccountCleanup`) so the use case keeps neither the plaintext
   credential nor the provider exceptions.

4. **Deleting a whole calendar container is authorized by the erasure, not a diff.** AI_GUIDELINE §13
   protects *schedule* events so a parser failure can never retire a lesson; account deletion is a
   different authority — the owner's own request, or an operator's audited one — exactly as ADR-107's
   announcement cancellation is. This is the only path that deletes an entire calendar; every sync
   path still operates one event at a time.

5. **One service, two doors** (as ADR-116): the student's `POST /api/account/delete` and the
   operator's `POST /api/admin/users/{id}/delete` share `AccountDeletionService`, so the eligibility
   rule, the external cleanup, the erasure and the single `AccountDeleted` audit record cannot differ
   between them. The confirmation phrase is the account's own e-mail, retyped (§30's "confirm the
   subject's own identifier"); the operator additionally states an audited reason, the student does
   not (that they asked is the reason). A self-deletion's `AccountDeleted` record is itself
   anonymized with the rest of the owner's trail — subject id, reason and metadata survive, the actor
   is cleared — while an operator's keeps the operator as the readable actor.

6. **A `SuperAdmin` cannot be deleted through either door.** The bootstrap operator is re-granted the
   role on every sign-in (ADR-045) and is the only administrator; deleting them would strand the
   system, and the loss cannot be undone.

### Consequences

- A revoked-but-detached license row now has a null redeemer. Any future read that assumed
  `RedeemedByUserId` is non-null for a `Redeemed` license must tolerate null (it means "redeemed by a
  since-deleted account").
- The audit trail can now contain rows with a null actor that are not system/anonymous events but
  *erased* ones. They are indistinguishable from anonymous events by shape; the `AccountDeleted`
  record with the matching `SubjectId` is what explains them.
- If Google is unreachable at deletion time the managed calendar can outlive the account. The audit
  metadata records `googleCalendarDeleted:false`, and the user can remove the "Sirkadiyen" calendar
  themselves. There is no retry — the local account is already gone, so nothing re-attempts it.
- **Not built:** a soft-delete/grace-period ("undo within N days"), a data-export-before-delete, and
  a bulk operator deletion. Deletion is immediate and permanent by decision.
- **Verification limit:** the `AccountDeletionStore` persistence test (cascade + anonymization +
  license detach + license-audit removal against real PostgreSQL) could not be executed in the
  implementing environment — no Docker/Postgres — so it is written and compiled but was run only in
  CI. The service orchestration, both API doors and both frontend surfaces are covered by unit tests
  that do run.

---

## ADR-119: Administrative role change from the panel

**Status:** Accepted and implemented
**Date:** 2026-08-20
**Implements:** `User.ChangeRole`, `IUserRoleStore`/`UserRoleStore`, `UserRoleService`,
`AuditEventCategory.RoleChanged`, `POST /api/admin/users/{id}/role`, frontend `RoleCard`
**Amends:** ADR-045 (the bootstrap-only role model)

### Context

Until now the only `SuperAdmin` was the one bootstrap e-mail (`GoogleSignInService.SuperAdminEmail`,
ADR-045), granted its role from backend-owned data at every sign-in. There was no way to make a
second operator, so there was exactly one administrator and no way to add or remove one from the
panel. `User.GrantRole` deliberately only ever *promotes* (so a re-authenticating operator is never
silently demoted at sign-in), which is the wrong primitive for a deliberate administrative change
that must be able to lower a role too.

### Decision

1. **A new domain primitive, `User.ChangeRole`, sets the role to an explicit value** — promotion or
   demotion — and returns whether it changed, distinct from `GrantRole`'s promote-only bootstrap
   behaviour. The two never merge: sign-in must keep using `GrantRole` so an admin-promoted operator
   is not demoted when they sign in with a non-bootstrap `bootstrapRole` of `User`.

2. **A promoted operator persists across sign-ins for free.** `GrantRole(User)` at a non-bootstrap
   sign-in is a no-op against an existing `SuperAdmin`, so no extra persistence is needed — the
   stored role already wins.

3. **`UserRoleService` owns the guards, in one place:** an operator cannot change **their own** role
   (no self-demotion that strands the panel mid-action, no self-promotion that makes the check
   pointless), and the **bootstrap operator cannot be demoted** (its role is re-granted at every
   sign-in, so a demotion would silently reverse itself and the audit record would claim a change
   that did not last). An unchanged role writes nothing and records nothing.

4. **The change is audited** as `AuditEventCategory.RoleChanged` with the previous and new roles in
   the metadata and a required reason, recorded *before* the write via the same callback shape the
   other operator flows use. A role is authorization itself, so who changed whose role and why is
   exactly what the trail is for (systemPatterns §19).

5. **The surface is the per-account operator page**, a "Yetki (rol)" card that promotes a user to
   operator or removes operator rights with a reason. Self and bootstrap-demote refusals come back as
   a 409 and are shown there.

### Consequences

- There can now be more than one `SuperAdmin`, and operator rights can be removed. Combined with
  ADR-118, deleting an operator account is a two-step, deliberate act: demote, then delete (deletion
  still refuses a `SuperAdmin` outright).
- The bootstrap e-mail remains special and irremovable — it is the recovery path if every other
  operator is demoted or deleted. It is still a source constant, not configuration; changing *which*
  e-mail bootstraps is still a deployment, by decision (ADR-045).
- **Not built:** a general role/permission system (still just `User`/`SuperAdmin`), and an
  operator-count floor beyond the bootstrap guarantee (nothing stops demoting every non-bootstrap
  operator, which is safe precisely because the bootstrap one cannot be removed).

---

## ADR-118 amendment: redeemed licences are deleted, not detached

**Status:** Accepted and implemented
**Date:** 2026-08-20
**Amends:** ADR-118 (decision point 2)

### Context

ADR-118 point 2 said a redeemed licence would be *kept but detached* by nulling its
`RedeemedByUserId`. The first real deletion failed with `23514` — the `ck_licenses_redemption`
check constraint requires a `Redeemed` licence to name both a redeemer and a redemption time
(`("RedeemedByUserId" IS NULL) = ("RedeemedAtUtc" IS NULL)` and `Status = 'Redeemed'` implies both
non-null). A null redeemer on a redeemed row is rejected by the database, so detaching is
structurally impossible without either a constraint/status change or a licence migration.

### Decision

The licences an account redeemed are **deleted**, together with their `license_audits` (whose
`LicenseId` foreign key is `RESTRICT`, so they go first) and any audit row naming the account as
actor. Only a redeemed link is ever present for a deletable account, because a deletable account is
never a `SuperAdmin` and so never created or revoked a licence.

### Consequences

- The consumed single-use code hash is deleted with the row. This is correct: a spent code for a
  deleted account has no reason to persist, and its absence cannot enable reuse (redemption looks the
  code up by hash and finds nothing). The earlier "keep an anonymized redemption fact" goal is
  dropped — the `AccountDeleted` audit event is what records that an activation happened and was
  erased.
- `AccountDeletionStoreResult.DetachedLicenses` became `DeletedLicenses`. No caller read it.
- The persistence test now asserts the redeemed licence and its audits are gone while an unrelated
  admin-created licence and its Created audit survive.

---

## ADR-120: Operator-triggered snapshot payload prune from the source panel

**Status:** Accepted and implemented
**Date:** 2026-08-20
**Implements:** `ISnapshotRetentionStore.FindPruneCandidateAsync`/`PrunePayloadAsync`,
`SnapshotPayloadPruneService`, `AuditEventCategory.SnapshotPayloadPruned`,
`POST /api/admin/sources/snapshots/{snapshotId}/prune-payload`,
`SourceSnapshotSummary.PayloadPrunedAtUtc`, frontend snapshot prune control in `AdminSourceStatus`
**Relates to:** ADR-044 (automatic snapshot payload retention), AI_GUIDELINE §9 (immutable evidence)

### Context

The source panel already *showed* each source's ten most recent snapshots, but the only way a
snapshot's stored payload was ever reclaimed was the automatic ADR-044 retention batch, gated by a
recent-time window and a per-cycle batch size. An operator who wanted to reclaim the storage of a
specific old snapshot on demand had no surface for it. The request was framed as "delete old
snapshots", but a snapshot is immutable evidence (AI_GUIDELINE §9): the ADR-007/ADR-044 model
already draws the line at the large normalized payload — that alone may be pruned, while the identity
row (hashes, counts, timestamps) and every downstream parse/revision/diff decision must remain. The
`ParseRun → SourceSnapshot` foreign key is `RESTRICT`, so hard-deleting a snapshot row would sever
that traceability chain. The user chose payload pruning over row deletion.

### Decision

1. **A manual prune is the automatic retention's eligibility, minus the recent-time window.** One
   definition of "prunable payload" governs both paths. The operator's authorization replaces the
   time window; every other guard the batch applies still holds: the snapshot's scope must not be
   frozen, it must not be the newest for its source (kept so a parser-profile change can reparse it),
   it must not be the current academic year's first snapshot (the baseline), it must have a parse run
   that reached a terminal successful state, and no `Running`/`Failed` run may still need its payload
   to recover. A snapshot that fails any guard is **refused with the specific reason**, not silently
   skipped, so the operator learns the action is wrong here rather than merely unavailable.

2. **The freeze is read through `IOperationalFreezeStore.IsFrozenAsync(scope)`**, so the global
   emergency stop and the source's scoped control both apply, exactly as every other mutating
   pipeline boundary. `SnapshotPayloadPruneService` resolves the snapshot's class/program scope from
   its source before checking, then delegates the transactional prune to the store.

3. **The prune is idempotent.** `PrunePayloadAsync` returns `false` when the snapshot is gone or its
   payload was already null (the retention batch or a concurrent prune won the race), which the
   service reports as `AlreadyPruned` rather than a spurious success. Eligibility is not re-checked
   inside the write transaction: the guards are monotonic for an old snapshot (it cannot become the
   newest again, and no new run appears for it), so the only realistic concurrent change is another
   prune, which the idempotent no-op already handles.

4. **Dropping a payload is audited.** `AuditEventCategory.SnapshotPayloadPruned` records who pruned
   which snapshot, the source and acquisition time in the metadata, and a required reason — because
   even a recoverable-by-repoll payload is evidence, and AI_GUIDELINE §19 wants a destructive-of-data
   maintenance action in the one activity log an operator reads.

5. **The endpoint lives on the existing read-only source-status group** (`/api/admin/sources`),
   SuperAdmin-only and antiforgery-protected, as its one mutating action. The frontend adds a
   per-snapshot "Payload'ı buda" control in the source detail drawer: a reason field, a confirm, and
   a reload; the button appears only where the payload is still stored, and a refusal reason from the
   backend is shown inline.

### Consequences

- An operator can now reclaim a specific old snapshot's storage immediately instead of waiting for
  the retention window, without any new power to destroy traceability: the identity row and the whole
  parse/revision/diff trail always remain, and the protected snapshots (newest, year baseline,
  recovery-needed) are refused.
- "Delete an old snapshot" is deliberately *not* a row deletion. Should a true hard-delete ever be
  wanted, it would need its own decision — a constraint/cascade design and a justification against
  AI_GUIDELINE §9 — and is out of scope here.
- `SourceStatusEndpoints` is no longer purely read-only; its summary comment now says so.
- **Verification limit:** the two new `SnapshotRetentionStoreTests` (manual eligibility + idempotent
  prune) are written and compile but could not run here — Docker/PostgreSQL were unavailable, so they
  run in CI like every persistence test in a Docker-less environment. The six-case
  `SnapshotPayloadPruneServiceTests` unit suite runs and passes.

---

## ADR-121: On-demand read-only calendar verification against Google

**Status:** Accepted and implemented
**Date:** 2026-08-21
**Implements:** `CalendarVerificationService`, `CalendarVerificationComparer`,
`GET /api/admin/users/{userId}/calendar-verify`, frontend `CalendarVerify` in `AdminUserDetail`
**Relates to:** ADR-058/059 (mapping ledger), ADR-062+ (worker inventory reconciliation), ADR-118
(the API-side precedent for decrypting a user's token and calling Google directly),
AI_GUIDELINE §5 (architecture boundaries), §13 (calendar safety)

### Context

The admin panel's per-user calendar tab reads the **local mapping ledger** — what Sirkadiyen
*recorded* writing — not the live Google Calendar (`AdminUserEndpoints.ListCalendarEventsAsync`). The
only code that reads the real Google calendar was the worker's periodic inventory reconciliation
(ADR-062+), which also *repairs* (writes). An operator asking "is what we think is on this calendar
really there?" had no read-only, on-demand answer. The user asked for a direct Google verification
feature and chose, from the two forks offered: a **synchronous, API-side read** (over a worker job)
and **inventory-depth comparison** (presence + content, over presence-only).

### Decision

1. **A read-only three-way comparison, never a repair.** `CalendarVerificationService` reads the
   actual Sirkadiyen-marked Google events, current published truth (audience-filtered to the student),
   and the mapping ledger, and reports the differences. It writes nothing — not the calendar, not the
   ledger, and crucially **not the connection's health state**: unlike inventory, it does not mark a
   connection `NeedsReauthorization` or the calendar unavailable when a read fails, it only reports it.
   Recording those is the worker's job; a verification must be safe to run at any time. Fixing drift
   already exists (calendar re-check, ADR-115; inventory, ADR-062+), so verification deliberately only
   observes.

2. **Synchronous API read, justified by the ADR-118 precedent.** The design had kept live Google
   Calendar I/O out of the API — `ICalendarSyncConnectionStore` even states "the API never calls
   these". But account deletion (ADR-118) already established that the API host may decrypt a user's
   token and call Google directly for a bounded, one-off operation, with the plaintext credential and
   provider exceptions confined to the infrastructure/adapter layer. Verification follows the same
   shape: the service unprotects the token only in memory for one `ListManagedEventsAsync` read and
   maps every Google failure (`GoogleManagedCalendarUnavailableException`,
   `GoogleCalendarCredentialException`, `GoogleCalendarTransientException`) to a typed non-verified
   outcome. The cost is a live Google round-trip inside the request (~1-3s), acceptable for an
   infrequent, explicit SuperAdmin action on one user. A worker-driven job was rejected as far more
   machinery for no safety gain, since the operation is read-only.

3. **Inventory-depth "drift", one definition.** The comparison reuses `ManagedCalendarEventFactory`
   and `ManagedCalendarEventComparer.IsEquivalent`, so a `ContentDrift` here is exactly what a repair
   pass would act on, and it skips announcement-kind events exactly as inventory does. The pure
   classification lives in `CalendarVerificationComparer.Compare`, a function of its three inputs, so
   it is unit-tested without a database or the Calendar API. Categories: MissingOnGoogle, ContentDrift,
   ExtraOnGoogle, Duplicate, StaleLedger, plus counts for matched and unmarked events.

4. **The per-user target reader is the eligibility gate.** `ICalendarSyncTargetReadStore
   .ListTargetsByUserIdsAsync([userId])` returns the profile, calendar id and credential only for an
   authorized, initial-sync-completed, actively-licensed connection — precisely the precondition a
   live read needs — so an ineligible user comes back as a typed reason (NoConnection,
   NoManagedCalendar, NeedsReauthorization, NotSyncReady) that the panel explains rather than a bare
   error. Non-verifiable outcomes return HTTP 200 with the outcome + detail so the UI renders them.

5. **Not audited, matching the existing ledger view.** The endpoint reads the same managed events the
   ledger tab already exposes to operators without an audit record, so no extra personal data is
   revealed and no new audit category is added. The read is a GET because it mutates nothing.

### Consequences

- An operator can now confirm a student's real Google calendar against both the ledger and published
  truth, on demand, without changing anything — the honest read-only complement to the repair actions
  already on the page.
- The API now makes a live Google Calendar **read** in the request path (verification), a second place
  after ADR-118's delete. Both keep the credential and provider exceptions in the adapter layer. If a
  third such need appears, promoting this to a worker job is the reconsideration point.
- **Verification limits:** the pure `CalendarVerificationComparer` is covered by six unit tests
  (in-sync, missing, extra, drift, duplicate, announcement-skip + unmarked). The service's Google
  read path and the endpoint were **not** exercised end to end here — that needs a live Google
  credential, a real managed calendar and PostgreSQL, none available in this environment. Release
  build is clean (0 warnings), frontend typecheck + production build + 74 tests pass.

---

## ADR-122: Initial calendar sync must run inside the cross-instance fence

**Status:** Accepted and implemented
**Date:** 2026-08-21
**Implements:** `FencedCalendarMaintenanceTask` now runs `InitialCalendarSyncTask` as its first fenced
stage; `Worker.RunCalendarWorkAsync` no longer runs initial sync unfenced; `WorkerCompositionTests`
guards the invariant
**Relates to:** ADR-058 (initial sync), ADR-024 (one calendar per user), systemPatterns §16 (the
PostgreSQL session advisory fence), AI_GUIDELINE §13/§14 (calendar safety, idempotency)

### Context

A live incident: a student's managed calendar was missing ~500 of ~817 currently-published events,
while the mapping ledger recorded all of them as written (ledger 1632 = ~817 current year + ~815 a
previous academic year still present). The operator noted two users ran initial sync at the same time.

Root cause found by reading the worker: **initial calendar sync was the one calendar-mutating stage
not covered by the shared cross-instance advisory fence.** `ICalendarDispatchReconciliationFence`
(`pg_try_advisory_lock`, systemPatterns §16) serialized dispatch, replay, resync, announcements and
inventory across worker instances, but `InitialCalendarSyncTask` ran before it, unfenced. Combined
with two facts:

- `ListPendingInitialSyncAsync` is a plain read — it does not claim or lock the connection rows, so
  two worker instances list the same pending users; and
- `InitialCalendarSyncService.EnsureCalendarAsync` creates the dedicated calendar with a check-then-act
  sequence (read `ManagedCalendarId` is null → search for the marker → create), and calendar creation
  is the one step the provider offers no idempotency key for (ADR-063).

…two worker instances processing the same pending user concurrently could each create a **separate**
calendar and split the user's events between them. The ledger's writes are idempotent on
`(UserId, StableIdentity)`, so each identity gets one row pointing at whichever worker won it, while
the connection ends up pointing at only one of the two calendars. Events written to the other calendar
are then "missing" relative to the one the connection (and the verification, ADR-121) reads — exactly
the ledger-ahead-of-Google symptom. On a later cycle `EnsureCalendarAsync` would also find two marked
calendars and throw "automatic attachment is unsafe", stranding the user. A single worker instance was
always safe: its loop is sequential and its crash recovery reattaches the one marked calendar.

The `~815` previous-academic-year events are a **separate, known** matter: profile-resync/rollover
never deletes an event absent from current published truth (deletion-by-absence is forbidden,
AI_GUIDELINE §13), so last year's events are not cleaned by that path (a documented open risk).

### Decision

**Initial sync runs inside the same advisory-lock lease as every other calendar stage**, as the first
stage. `FencedCalendarMaintenanceTask` acquires the fence once and runs initial sync, then dispatch,
replay, academic-year drift, resync, announcements and inventory under it; `Worker` no longer runs
initial sync separately. When another worker holds the fence, this worker yields all calendar work,
initial sync included. This makes exactly one worker perform calendar work at a time across instances,
which is the only safe way to run the non-idempotent calendar creation — matching §16's intent, which
initial sync had simply been left out of.

A structural regression test (`WorkerCompositionTests.InitialSyncRunsInsideTheFencedStage`) asserts the
fenced stage takes `InitialCalendarSyncTask` as a dependency, so a refactor that pulls initial sync back
out to run unfenced breaks the build's tests.

### Consequences

- The event-splitting race is closed for multiple worker instances (a redeploy overlap, an un-drained
  old container, an accidental second instance). The fence, not the deployment, is now the guarantee —
  though running a single worker replica remains the sane default.
- Initial sync no longer runs concurrently with dispatch/inventory across instances either, which is
  strictly safer and matches how the rest of calendar work already behaved.
- **Not fixed by this ADR (deliberately):** repairing the calendars already corrupted (the 500 missing
  events need re-insertion; the ~815 stale previous-year events need an authorized deletion, since
  inventory will not remove them by absence). Those are a repair feature, tracked separately.
- **Verification limit:** the fix is structural — initial sync now executes only while the single
  advisory lease is held, exactly like the other stages, asserted by the composition test. A true
  two-instance race reproduction needs two live workers against one PostgreSQL and Google, which this
  environment cannot run. Release build is clean (0 warnings) and the worker composition tests pass.

---

## ADR-123: On-demand non-destructive calendar repair from the verification screen

**Status:** Accepted and implemented
**Date:** 2026-08-21
**Implements:** `POST /api/admin/users/{userId}/calendar-repair` (reuses
`IUserCalendarConnectionStore.RequestReconciliationAsync`), frontend `CalendarRepair` in the verify card
**Relates to:** ADR-121 (verification), ADR-062 (inventory reconciliation), ADR-122 (the concurrency
fix), AI_GUIDELINE §13 (deletion authority), systemPatterns §16 (the fence)

### Context

The verification (ADR-121) could show that a user's Google calendar had drifted from our records — for
the live incident, 500 events the ledger recorded but Google was missing — but offered no way to fix
it. The operator asked for a repair. Of the two halves of the observed drift, the safe half (re-insert
"missing on Google", patch "content drift") and the destructive half (delete the ~815 stale previous-
year surplus events), only the safe, non-destructive half was chosen for this decision.

### Decision

**The repair records intent for the worker's existing non-destructive inventory pass rather than
writing to Google from the API.** This is deliberate and follows from ADR-122: calendar writes belong
in the fenced worker, run by exactly one instance at a time. The endpoint calls the same
`RequestReconciliationAsync` the student self-service reconcile (ADR-062) already uses, which marks the
connection due; the worker's fenced inventory pass then re-inserts each mapped event missing from
Google (deterministic id) and patches drifted ones, and by design never deletes from absence. So the
repair fixes "missing on Google" and "content drift" and leaves surplus / previous-year events for a
separate authorized action — exactly the chosen scope.

- **An operator action, audited with a reason.** Unlike the student's own reconcile (no reason — asking
  is the reason), an operator acting on another account states why. Reuses
  `AuditEventCategory.ReconcileRequested` with `requestedBy: operator` in the metadata; no new category,
  because the underlying action is identical, only the actor differs. No plan hash, because nothing is
  deleted.
- **Outcomes map honestly:** `Requested` -> 202 + audit; `NotEligible` -> 409 (needs a completed initial
  sync and a healthy connection); `NotFound`/no calendar -> 409; a missing user -> 404.
- **The UI is on the verification result**, shown only when `missingOnGoogle + contentDrift > 0`, and it
  says plainly that it queues the worker, deletes nothing, and leaves any surplus/previous-year events.

### Consequences

- The operator can now correct the safe half of the drift the verification finds, without the API ever
  performing a Calendar write and without any deletion — consistent with ADR-122's "one fenced writer".
- The repair is asynchronous (the worker does it next cycle), which the UI states rather than pretending
  the fix is instant.
- **Still not addressed:** removing the ~815 stale previous-year surplus events, which needs an
  authorized, audited deletion (the destructive half the operator deferred).
- **Verification limit:** the endpoint is thin over the already-tested `RequestReconciliationAsync`; the
  Api.UnitTests project tests helpers/config, not endpoints, so this was covered by build + typecheck +
  the frontend suite rather than a new endpoint test. Release build clean (0 warnings).

---

## ADR-124: Persisted worker-instance heartbeats for multi-instance visibility

**Status:** Accepted and implemented
**Date:** 2026-08-21
**Supersedes:** ADR-091's "do not persist health heartbeats" decision (that clause only; its scoped
freeze, persistent session and warning-projection decisions stand)
**Implements:** `WorkerInstanceHeartbeat` (domain), `worker_instances` table + migration
`AddWorkerInstanceHeartbeats`, `IWorkerHeartbeatStore`/`WorkerHeartbeatStore`, `WorkerHeartbeatTask`,
`GET /api/admin/workers`, the "Worker instance'ları" panel on the admin server page
**Relates to:** ADR-091 (in-process worker health probe), ADR-122 (the double-sync incident)

### Context

ADR-091 hosts an in-process `/health/ready` in the worker (instance id, start, last activity, current
stage) and has the API probe **one** worker URL on explicit refresh. It deliberately chose not to
persist heartbeats. That model cannot show that **more than one** worker instance is running — which
is exactly the condition behind the ADR-122 incident, where two instances raced on initial sync and
split a user's calendar. An operator asked to observe what the worker instances are actively doing.

A single-URL probe is structurally incapable of revealing concurrent instances (it hits whichever one
answers, or a load balancer). Seeing instances plural requires each to publish to a shared store.

### Decision

**Re-introduce a lightweight heartbeat, justified by the incident ADR-091 did not foresee.** Each
worker instance upserts one row (`machine:pid` key) into `worker_instances` on every cycle, carrying
its start time, current stage, last in-process activity and heartbeat time. The admin panel reads all
rows and shows every instance and what it is doing; the API marks an instance "active" when its last
heartbeat is within 150 s, and the panel warns when **more than one** is active — the signal that
would have surfaced the incident. This supersedes only ADR-091's no-heartbeat clause; its reasoning
(avoid fabricated CPU/RAM/Redis telemetry) still holds, and this adds none of that — only the worker's
own honestly-reported stage.

- **Disposable telemetry with a 1-day auto-retention.** The operator asked that old records not
  accumulate. It is an upsert (one row per instance), and every write also deletes rows not seen for a
  day, so dead instances fall off on their own with no separate scheduler. Nothing downstream depends
  on the table; losing it loses only the monitor.
- **Best-effort, never on the critical path.** `WorkerHeartbeatTask` swallows and logs any failure, so
  a database hiccup writing the heartbeat can never interrupt the worker's actual work. Written twice
  per loop iteration (top and before the idle delay) so idle instances stay fresh; a long active stage
  can briefly read stale, which the panel tolerates via the generous active window.
- **One port, two hosts.** `IWorkerHeartbeatStore` is written by the worker and read by the API,
  registered in the shared persistence DI. `GET /api/admin/workers` (SuperAdmin) returns each instance
  with a server-computed `isActive` and the active count.

### Consequences

- An operator can now see, in the admin panel, every running worker instance, its current stage, its
  uptime and when it last reported — and is warned when a second instance appears, closing the
  observability gap that let the ADR-122 incident go unnoticed.
- The single-URL service-health probe (ADR-091) stays as the reachability check; the heartbeat panel
  is the multi-instance view beside it.
- **Not added (still, per ADR-091):** CPU/RAM/disk/Redis telemetry and any fabricated signal. The
  heartbeat carries only what the worker actually knows about itself.
- **Verification limit:** the store has a Persistence test (upsert + 1-day prune + newest-first list)
  and the panel a frontend test (renders instances, warns on >1 active). The Persistence test needs
  PostgreSQL and runs in CI (Docker down here). The worker's own heartbeat-write loop was covered by
  build + the worker composition resolving; a live two-instance run was not exercised here. Release
  build clean (0 warnings), frontend 75 tests + typecheck + production build pass.

---
