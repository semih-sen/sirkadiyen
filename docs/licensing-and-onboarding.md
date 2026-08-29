# Licensing and onboarding

Sirkadiyen accounts are Google-authenticated but remain inactive until one
single-use license is redeemed (ADR-004, ADR-022, ADR-053).

## License-code security

- An administrator creates a code with `POST /api/admin/licenses`.
- New codes use the WhatsApp-friendly `SRK-XXXXX-XXXXX` format. The alphabet
  omits `I`, `O`, `0`, and `1` to reduce transcription mistakes.
- The response contains the plaintext code exactly once. It is never persisted
  or logged.
- PostgreSQL stores only a deterministic HMAC-SHA256 lookup hash. The key comes
  from `SIRKADIYEN_LICENSING__HASH_KEY`, which must be Base64 for at least 32
  random bytes.
- Codes carry an explicit `Active`, `Redeemed`, `Revoked`, or `Expired` state.
- An optional expiration is the deadline for redeeming an unused code. A
  successfully redeemed activation does not later expire merely because that
  deadline passes.
- Every create, redeem, expire, and revoke transition writes an append-only
  `license_audits` row in the same transaction.

The ten random characters provide 50 bits of online-guessing space. The keyed
hash prevents offline code enumeration after a database leak, redemption is
rate-limited, and the unique hash retries the extremely unlikely generation
collision. Previously generated `SIRK-XXXXX-XXXXX-XXXXX-XXXXX` codes remain
redeemable, but the backend no longer generates them.

Rotating the HMAC key makes every still-unredeemed code impossible to look up.
Treat key rotation as a deliberate invalidation procedure, not routine secret
rotation.

## Redemption guarantees

`POST /api/licenses/redeem` requires an authenticated local session, CSRF token,
and is limited to five attempts per user and remote address per minute.

PostgreSQL locks the submitted license row during redemption. A partial unique
index also permits at most one `Redeemed` license per user. Together these
guarantee:

- two users racing for one code produce one winner;
- one user racing two different codes receives only one activation;
- submitting the same code again by its winner is idempotent;
- a code owned by another user, expired, revoked, or unknown is reported through
  one generic unavailable response so the endpoint does not enumerate codes.

Revoking a redeemed license changes the user's authoritative activation state
to `Suspended`. It stops future synchronization admission but never deletes the
dedicated calendar or its existing events.

## Resumable onboarding

`GET /api/onboarding` and `GET /api/auth/me` derive state from backend records:

| Authoritative license state | Onboarding state | Next action |
| --- | --- | --- |
| No redeemed license | `LicenseRequired` | `RedeemLicense` |
| Redeemed license | `ProfileRequired` | `CompleteAcademicProfile` |
| Redeemed license later revoked | `Suspended` | `ContactSupport` |

Profile, Calendar authorization, and initial-sync records do not exist yet, so
the backend cannot truthfully advance beyond `ProfileRequired`. Their later
modules will extend the derivation rather than accepting an onboarding state
from the browser.

## Student-list lookup

Academic profile onboarding is student-number-first (ADR-085, ADR-132). The
student types their ten-digit number and nothing else; the number identifies
them and the faculty list that holds it states which program they are in, so a
student cannot mis-select their own class year.

`POST /api/profile/roster-lookup` takes `{ "studentNumber": "0101250001" }` and
returns one of three outcomes.

| Outcome | Meaning | What the form does |
| --- | --- | --- |
| `Matched` | Exactly one row of one list states the number | Prefills the class year, program language and every selector the list states, all editable |
| `NotFound` | No configured list states it | Nothing is prefilled; the student fills the form in by hand |
| `Ambiguous` | Two rows claim it, in one list or across two | Nothing is prefilled; the backend does not choose, and the student is sent to Student Affairs |

It is a `POST` although it reads: a student number does not belong in a URL or
in an access log. It requires the CSRF token and is rate limited to ten calls
per five minutes per caller, because a lookup answers a ten-digit guess with a
name.

A match is a suggestion and never a claim that the profile is complete. The
response therefore separates the two:

- `suggestedSelectors` — what a list stated, filtered through the
  supported-profile schema. A value the program does not allow is dropped and
  explained in `notices`, because a suggestion the profile validator would
  reject is worse than no suggestion.
- `dimensionsRequiringInput` — required dimensions the lists said nothing usable
  about. Grade 2 Turkish lists no anatomy group and Grade 3 Turkish no
  faculty-practice cohort, so both cohorts always leave one field to the student.
- `notices` — why each value was withheld: `ProgramNotOnboardable`,
  `DimensionNotStatedByRoster`, `DimensionNotDeclaredByProgram`,
  `ValueNotSupportedByProgram`, `RosterYearDiffersFromProgram`.
- `someListsUnreadable` — whether a list could not be read when the lookup ran.
  It matters with `NotFound`: "you are not on any list" and "we could not read
  one of the lists" ask the student for different things.

`givenName` and `familyName` are returned for visual confirmation only. They are
never written to the database, copied into the profile, or logged. Two of the
four lists publish them already masked, so a name may arrive as `HAY*******`.

The lists are catalogued in `config/student-rosters.json`, which holds each
list's location, cohort and column layout. The documents are published openly by
the faculty, so the links are in source control; their contents are read at
runtime into memory, refreshed hourly, and never persisted. A roster is not a
schedule source: nothing parses it into canonical records, no snapshot is stored,
and no revision is cut from it.

A list existing does not open a program. The Grade 2 English and Grade 3 English
lists are catalogued and suggest nothing, because those programs are absent from
the supported-profile schema (ADR-084, ADR-098).

Reading a list uses the same `SIRKADIYEN_GOOGLE` source credential the worker
uses, which `common.env` already supplies to both hosts. Without it every list
is reported as unreadable and onboarding continues by hand rather than failing.

## Administrative endpoints

All administration endpoints require the persisted `SuperAdmin` role and a CSRF
token:

- `POST /api/admin/licenses` creates an active code and returns its plaintext
  once.
- `POST /api/admin/licenses/{licenseId}/revoke` records a required reason and
  preserves the affected user's identity and license history.
- `POST /api/admin/users/{userId}/activate` activates an existing
  Google-authenticated user without generating a code. The resulting license
  has explicit kind `Manual`, starts as `Redeemed`, contains no code hash, and
  records the SuperAdmin's required reason.

The actor always comes from the verified local session; it is never accepted in
the request body.
