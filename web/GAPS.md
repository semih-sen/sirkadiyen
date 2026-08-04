# Frontend integration gaps

This is the honest inventory of every screen/module from the prototype
(`web-design/`) that is **not** wired to a backend route in this migration, plus
the reason. It follows the design plan §2.2 split between "backend exists today"
and "target UI, no backend yet", and AI_GUIDELINE §21 (report unresolved risks).

Nothing below fabricates production data. Where a screen has no backend, the UI is
either omitted or shown as an explicit "Yakında" placeholder — never with invented
metrics (plan constraint K8).

Legend:

- **No endpoint** — the backend route does not exist yet.
- **Endpoint exists, UI not built** — the route is live; only the React surface is missing. These are the cheapest to finish next.
- **Contract gap** — a route exists but does not expose the fields the screen needs.

---

## Update — ADR-089 frontend integration completed (2026-08-04)

The ADR-089 routes now have typed browser contracts and live React surfaces:

- **Dashboard:** `GET /api/schedule/upcoming`, `GET /api/schedule/changes`,
  `GET /api/calendar/sync/progress` (partial — total mapped + patched counts, no per-stage
  unchanged/failed), `POST /api/calendar/reconcile`, `GET /api/licenses/status` (activation
  state + date; there is no "kalan süre", access does not lapse after activation).
- **Admin:** `GET /api/admin/users` (+`/{id}`), `GET /api/admin/licenses` (+`/{id}`),
  `GET /api/admin/sources` (+`/{id}`), `GET /api/admin/audit`, `GET /api/admin/access-logs`
  (masked IP) + `POST /api/admin/access-logs/{id}/unmask`, `GET /api/admin/metrics`,
  `/health/live` + `/health/ready`.

The health routes are also proxied through the same Next.js edge. Vitest and React Testing
Library cover the request layer and the privacy-sensitive UI paths.

Still **No endpoint** (unchanged): `GET /api/calendar/sync/history` (needs a per-user activity
log — deferred), `GET /api/notifications`, `POST /api/contact`, and the finance / bulk-event /
user-warning domains.

---

## Wired in this migration (for reference)

These screens are connected to real routes and were re-skinned, not stubbed:

| Screen | Routes |
| --- | --- |
| Sign-in | `POST /api/auth/google`, `GET /api/auth/me`, `GET /api/auth/csrf` |
| License step | `POST /api/licenses/redeem` |
| Profile step | `GET /api/profile/options`, `PUT /api/profile` |
| Calendar step | `GET/POST /api/calendar/authorization`, `.../options` |
| Initial-sync step | `GET/POST /api/calendar/sync` |
| Dashboard (real modules) | `GET /api/calendar/sync`, `GET /api/profile` |
| Admin · freeze | `GET/POST /api/operations/freeze` |
| Admin · document upload | `GET /api/sources/uploadable`, `POST /api/sources/{id}/document`, `GET .../uploads` |
| Admin · revision review | `GET /api/revisions`, `GET /api/revisions/{id}`, `POST /api/revisions/{id}/approve` |
| Admin · self-activation | `POST /api/admin/users/{id}/activate` |
| Admin · department colors | `GET/PUT/POST /api/admin/calendar-colors` |
| Admin · license create/revoke | `POST /api/admin/licenses`, `POST /api/admin/licenses/{id}/revoke` |
| Dashboard · schedule/status/repair | `GET /api/schedule/upcoming`, `GET /api/schedule/changes`, `GET /api/calendar/sync/progress`, `POST /api/calendar/reconcile`, `GET /api/licenses/status` |
| Admin · users/licenses | `GET /api/admin/users`, `GET /api/admin/licenses` and detail routes |
| Admin · source status | `GET /api/admin/sources` and `GET /api/admin/sources/{id}` |
| Admin · access/audit | `GET /api/admin/access-logs`, `POST .../unmask`, `GET /api/admin/audit` |
| Admin · health/metrics | `/health/live`, `/health/ready`, `GET /api/admin/metrics` |

---

## 1. Public site

### Contact form — `/iletisim`
- **Status:** No endpoint.
- **Target:** `POST /api/contact` (category, subject, description, optional student number, email) → ticket id.
- **Current behaviour:** Client-side validation runs; **submit opens a prefilled `mailto:destek@sirkadiyen.app`** instead of posting. No fake ticket number is shown (the prototype's simulated `SRK-DSK-48213` success panel was intentionally dropped).

---

## 2. Student dashboard — `/dashboard`

Schedule, sync status, academic profile, calendar connection, repair and license
modules are wired. The table distinguishes those live modules from the remaining
honest "Yakında" placeholders:

| Module | Status | Target endpoint (suggested) |
| --- | --- | --- |
| Sıradaki dersler (upcoming lessons) | Wired | `GET /api/schedule/upcoming` — the student's next N managed events. |
| Son program değişiklikleri (recent changes) | Wired | `GET /api/schedule/changes` — creations and updates currently represented in the ledger. |
| Senkronizasyon geçmişi (sync history) | No endpoint | `GET /api/calendar/sync/history`. |
| Onarım / mutabakat talebi (repair request) | Wired | `POST /api/calendar/reconcile` (audited and rate limited). |
| Bildirimler (notifications) | No endpoint | `GET /api/notifications`. |
| Makaleler / podcast | No endpoint | Content system; "Yakında" by design (plan open question #8). |
| Lisans durumu | Wired | `GET /api/licenses/status` reports activation/suspension dates; access is not a timed subscription. |

### Sync progress
- **Status:** Wired to `GET /api/calendar/sync/progress`.
- The UI shows mapped, first-written and later-patched event counts plus write timestamps. It explicitly does not present unavailable unchanged/failed/per-run totals.

---

## 3. Admin application (`/admin`)

The admin information architecture now has dedicated routes. **Genel bakış** is
an orientation screen only; live operations are separated into `/admin/sources`,
`/admin/revisions`, `/admin/colors`, `/admin/operations`, and `/admin/users`.
Areas without a backend have explicit, navigable empty-state panels and never
fabricate records or metrics. The sections below separate remaining gaps from wired reads:

### 3.1 No endpoint (new product surface)

| Prototype screen | File | Notes |
| --- | --- | --- |
| Finans (gelir/gider/kâr dağıtımı) | `admin-finance.html` | Entirely new domain (plan §5.10). Needs revenue/expense models, profit-distribution + audit. Uses the §4.3 high-risk 6-step pattern. |
| Toplu takvim etkinliği | `admin-bulk-event.html` | Audience resolution → estimated recipients → dedup campaign key → queued delivery tracking (plan §4.4, §5.11). |
| Tek kullanıcı uyarısı | `admin-user-warning.html` | Idempotent `warning-key` per user+template+date (plan §4.5, §5.12). |

### 3.2 Endpoint exists, UI not built (cheapest next steps)

| Screen / module | Existing routes | Notes |
| --- | --- | --- |
| Diff serbest bırakma (held-diff release) | `GET /api/diffs`, `GET /api/diffs/{id}`, `POST /api/diffs/{id}/release` | Backend live (ADR-042). An ambiguity hold is **not** releasable and must be shown as such. Only the UI is missing. |

### 3.3 Wired read-only administration views

- `/admin/users`: paged user and license lists, user/license detail, license audit and selected-license revocation.
- `/admin/sources`: source pipeline status and retained snapshot evidence beside administrative upload.
- `/admin/access-logs`: masked sign-in log, audited transient IP reveal and cross-category audit query.
- `/admin/server`: liveness, readiness and database-backed point-in-time counts. CPU/RAM/Worker/Parser/Redis metrics remain unavailable and are not fabricated.

---

## 4. Routing notes

- Public routes use the plan's Turkish slugs: `/` (landing), `/gizlilik`, `/kosullar`, `/iletisim`.
- Existing app routes were **kept as-is** to avoid breaking working integration: `/sign-in`, `/onboarding/{license,profile,calendar,sync,suspended}`, `/dashboard`, `/admin`. The plan's `/kurulum`, `/panel`, `/yonetim/*` slugs were **not** adopted; renaming live routes is out of scope for a re-skin. If Turkish app-route slugs are desired, that is a separate, coordinated change (also update `src/lib/onboarding.ts` `ROUTES`).
- The prototype's `?state=` variant switch (a design-review affordance) is intentionally **not** ported: production screens derive state from the authoritative backend, not a query param (plan §8.2, constraint K5).

---

## 5. Suggested build order for closing gaps

1. Wire **held-diff release** (backend already exists — §3.2).
2. Add a per-user sync activity log before exposing `GET /api/calendar/sync/history`.
3. Decide and implement notification and contact contracts.
4. Add exporter/host telemetry only if CPU/RAM/Worker/Parser/Redis visibility becomes a requirement.
5. New product domains last: bulk event, user warning, finance.
