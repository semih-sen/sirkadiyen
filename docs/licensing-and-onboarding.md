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
