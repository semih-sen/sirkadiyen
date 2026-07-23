# Authentication

Sirkadiyen uses Google-only sign-in and a backend-managed browser session
(ADR-003, ADR-023, ADR-045, ADR-052). It stores no password and never turns a
Google login into an activated product account. A new account reports
`LicenseRequired`; successful license redemption advances it to
`ProfileRequired`. See `docs/licensing-and-onboarding.md`.

## Browser flow

The frontend and API are currently expected to be served over HTTPS in a
same-site deployment.

1. The browser calls `GET /api/auth/csrf` with credentials enabled.
2. The frontend initializes Google Identity Services with
   `SIRKADIYEN_GOOGLE__AUTH_CLIENT_ID`.
3. Google returns a short-lived ID credential to the browser.
4. The browser posts `{"credential":"..."}` to `POST /api/auth/google`, includes
   the CSRF response token in the returned header name, and includes cookies.
5. The backend validates the token signature, issuer, audience, expiry and
   `email_verified`; creates or refreshes the local user; and emits the
   `__Host-Sirkadiyen.Session` cookie.
6. The browser reads the local account with `GET /api/auth/me` and ends it with
   the CSRF-protected `POST /api/auth/logout`.

The Google ID credential is not stored. Google access tokens, refresh tokens and
Calendar permissions are not part of this flow and will use a separate,
incremental authorization path.

## Session guarantees

- Session cookie: `HttpOnly`, `Secure`, `SameSite=Lax`, path `/`, eight-hour
  sliding expiry.
- Anti-forgery cookie: `HttpOnly`, `Secure`, `SameSite=Strict`; the request token
  is returned in the JSON response and sent in `X-CSRF-TOKEN`.
- Every authenticated request reloads the local user, so a deleted user or
  changed role invalidates or rotates the principal.
- API authentication failures are `401`/`403`; cookie middleware never redirects
  an API request to an HTML login page.
- Google sign-in is limited to ten attempts per remote address per minute. Proxy
  deployment must configure trusted forwarded headers before that address is used
  as an internet-client identity.
- License redemption is limited to five attempts per authenticated user and
  remote address per minute.

Before a containerized or multi-instance production deployment, configure a
shared persistent ASP.NET Core Data Protection key ring. The current host default
is suitable only for this single-instance foundation; otherwise restarts can
invalidate every cookie and different instances cannot decrypt one another's
sessions.
- The cookie holds only backend-owned local user ID, verified email, display name
  and role claims. It never holds a Google credential.

## User persistence

`sirkadiyen.users` identifies Google accounts by the immutable `sub` claim.
`GoogleSubject` and case-normalized email have independent unique indexes. A
verified email already linked to another Google subject is rejected rather than
silently account-linked.

The backend-owned ADR-045 bootstrap email is granted `SuperAdmin` on first or
later verified sign-in. Authorization reads the explicit `users.Role` value;
the email literal seeds the role and is not itself the authorization policy.

Administrative revision approval and held-diff release now derive their actor
from this verified session. Their request bodies contain only the reason.
License creation/revocation and operational freeze/unfreeze use the same policy
and derive their actors the same way.
