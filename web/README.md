# Sirkadiyen frontend (`web/`)

Next.js (App Router, TypeScript) student-facing app. It consumes the same-site
backend cookie session (ADR-023, ADR-036, ADR-052) and never parses schedules or
touches Google Calendar itself (AI_GUIDELINE §5).

## Why there is no CORS or SameSite change

Local dev uses an **HTTPS edge + same-origin proxy**, so the browser only ever
talks to one origin and there is no cross-origin request to configure:

```
Browser --HTTPS--> https://localhost:3000  (Next dev server)
                       |
                       +-- /            React pages
                       +-- /api/:path*  -> BACKEND_ORIGIN (Kestrel)   [next.config.mjs]
```

- **Same-origin** → no CORS, and the backend's `SameSite=Lax`/`Strict` cookies
  work unchanged.
- **HTTPS edge** (`next dev --experimental-https`) → the backend's `Secure` +
  `__Host-` session and antiforgery cookies are accepted and stored exactly as in
  production. Kestrel can stay on plain HTTP locally; only this edge terminates
  TLS, mirroring the production reverse proxy in front of Kestrel.

This keeps dev faithful to the hardened production cookie config instead of
weakening it — the failure mode ADR-052 explicitly warned against.

## One-time setup

### 1. Google Cloud Console — add the dev origin

Both Google flows are popup / `postMessage`, so **no redirect URI is needed**. You
only add the dev frontend origin to the OAuth client's
**Authorized JavaScript origins**:

```
https://localhost:3000
```

Keep your existing `https://sirkadiyen.com` origin for production. Do this for the
sign-in client and, if separate, the Calendar client.

### 2. Trust the local dev certificates

- Backend (only needed if you point `BACKEND_ORIGIN` at the HTTPS Kestrel URL):
  ```bash
  dotnet dev-certs https --trust
  ```
- Frontend: `next dev --experimental-https` generates and trusts a local cert on
  first run automatically.

### 3. Frontend env

```bash
cp .env.local.example .env.local
```

Set `NEXT_PUBLIC_GOOGLE_AUTH_CLIENT_ID` to the **same** value as the backend's
`SIRKADIYEN_GOOGLE__AUTH_CLIENT_ID` (the backend validates the ID token audience
against it). `BACKEND_ORIGIN` defaults to `http://localhost:5080`, the HTTP Kestrel
URL from `src/Sirkadiyen.Api/Properties/launchSettings.json`.

### 4. Install

```bash
npm install
```

## Running

Two processes:

```bash
# terminal 1 — backend (from repo root)
dotnet run --project src/Sirkadiyen.Api --launch-profile https

# terminal 2 — frontend (from web/)
npm run dev
```

Open **https://localhost:3000**. The Python parser and .NET worker are only needed
to actually populate calendars end-to-end; sign-in, licensing, profile and Calendar
authorization work against the API alone.

> The worker performs the calendar writes. For the initial-sync step to progress
> past `InProgress`, run the worker too:
> `dotnet run --project src/Sirkadiyen.Worker`.

## Testing the OAuth flows on localhost

1. **Sign-in** — the Google button on `/sign-in` returns an ID token to the
   browser; the app posts it to `POST /api/auth/google` (with CSRF) and the
   backend issues the `__Host-Sirkadiyen.Session` cookie.
2. **Calendar** — `/onboarding/calendar` reads the client ID + scope from
   `GET /api/calendar/authorization/options`, opens Google's popup **code** flow,
   and posts the one-time code to `POST /api/calendar/authorization`. The backend
   exchanges it server-side with `redirect_uri=postmessage`.

If sign-in fails with an origin error, the dev origin is not registered (step 1).
If the cookie is not stored, confirm the frontend is on **https** (not http).

## Structure

```
src/
  app/
    layout.tsx                 root layout + SessionProvider
    page.tsx                   routes to the correct onboarding step
    sign-in/page.tsx           Google Identity Services sign-in
    onboarding/
      license/page.tsx         redeem a single-use license
      profile/page.tsx         academic profile (dynamic cohort dimensions)
      calendar/page.tsx        Calendar consent (popup code flow)
      sync/page.tsx            start + poll initial sync
      suspended/page.tsx       revoked-license terminal state
    dashboard/page.tsx         Active account summary
  components/
    SessionProvider.tsx        loads /api/auth/me, exposes user + refresh
    OnboardingGate.tsx         routes each step by authoritative backend state
  lib/
    api.ts                     typed fetch client + CSRF handling
    types.ts                   TS mirrors of backend contracts
    google.ts                  GIS loaders (ID token + Calendar code flow)
    onboarding.ts              onboarding-state -> route map
```
