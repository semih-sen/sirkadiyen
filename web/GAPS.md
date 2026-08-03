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

---

## 1. Public site

### Contact form — `/iletisim`
- **Status:** No endpoint.
- **Target:** `POST /api/contact` (category, subject, description, optional student number, email) → ticket id.
- **Current behaviour:** Client-side validation runs; **submit opens a prefilled `mailto:destek@sirkadiyen.app`** instead of posting. No fake ticket number is shown (the prototype's simulated `SRK-DSK-48213` success panel was intentionally dropped).

---

## 2. Student dashboard — `/dashboard`

Real modules (sync status, academic profile, calendar-connection state, mapped
event count) are wired. The following prototype modules have **no backend** and are
rendered as honest "Yakında" placeholders, not fabricated data:

| Module | Status | Target endpoint (suggested) |
| --- | --- | --- |
| Sıradaki dersler (upcoming lessons) | No endpoint | `GET /api/schedule/upcoming` — the student's next N managed events. |
| Son program değişiklikleri (recent changes) | No endpoint | `GET /api/schedule/changes` — diffs applied to this user's calendar. |
| Senkronizasyon geçmişi (sync history) | No endpoint | `GET /api/calendar/sync/history`. |
| Onarım / mutabakat talebi (repair request) | No endpoint | `POST /api/calendar/reconcile` (audited, per plan §5.8). Button rendered disabled. |
| Bildirimler (notifications) | No endpoint | `GET /api/notifications`. |
| Makaleler / podcast | No endpoint | Content system; "Yakında" by design (plan open question #8). |
| Lisans/deneme kalan süre | Contract gap | `GET /api/onboarding` returns `hasActiveLicense` only — no expiry/trial detail. A license-status endpoint is needed to show "14 gün kaldı". |

### Sync-progress per-stage counters
- **Status:** Contract gap.
- `GET /api/calendar/sync` returns `initialSyncState` + `mappedEventCount` only. The prototype's 8-stage timeline with created/updated/unchanged/failed counters (plan §4.2, from the `UserCalendarEventMapping` ledger) is **not** exposed. The migrated screen therefore drives timeline status from the single authoritative state and shows only the real mapped-event count. A richer `GET /api/calendar/sync/progress` would be needed for true per-stage counters.

---

## 3. Admin application (`/admin`)

The admin information architecture now has dedicated routes. **Genel bakış** is
an orientation screen only; live operations are separated into `/admin/sources`,
`/admin/revisions`, `/admin/colors`, `/admin/operations`, and `/admin/users`.
Areas without a backend have explicit, navigable empty-state panels and never
fabricate records or metrics. The following still need backend work:

### 3.1 No endpoint (new product surface)

| Prototype screen | File | Notes |
| --- | --- | --- |
| Finans (gelir/gider/kâr dağıtımı) | `admin-finance.html` | Entirely new domain (plan §5.10). Needs revenue/expense models, profit-distribution + audit. Uses the §4.3 high-risk 6-step pattern. |
| Toplu takvim etkinliği | `admin-bulk-event.html` | Audience resolution → estimated recipients → dedup campaign key → queued delivery tracking (plan §4.4, §5.11). |
| Tek kullanıcı uyarısı | `admin-user-warning.html` | Idempotent `warning-key` per user+template+date (plan §4.5, §5.12). |
| Sunucu izleme | `admin-server.html` | Health/metrics endpoints (CPU/RAM/queue depth/API/worker/parser/PostgreSQL/Redis). Progress.md Phase 10 "Health checks / Metrics" = not started. |
| Erişim kayıtları | `admin-access-logs.html` | Login audit with **masked IP by default**; unmask is a separate audited action (plan §5.14, constraint K7). No audit-event model yet (progress.md Phase 1 "audit event model" = not started). |

### 3.2 Endpoint exists, UI not built (cheapest next steps)

| Screen / module | Existing routes | Notes |
| --- | --- | --- |
| Diff serbest bırakma (held-diff release) | `GET /api/diffs`, `GET /api/diffs/{id}`, `POST /api/diffs/{id}/release` | Backend live (ADR-042). An ambiguity hold is **not** releasable and must be shown as such. Only the UI is missing. |

### 3.3 No endpoint (admin data views)

| Prototype screen | File | Missing backend |
| --- | --- | --- |
| Kullanıcı listesi + detay | `admin-users.html` | `GET /api/admin/users`, `GET /api/admin/users/{id}` (identity, profile, license history, login/sync history, managed-event count, audit). progress.md "User sync status" = not started. |
| Kaynak durum panosu | `admin-sources.html` | Source poll status, snapshot inspection, parser warnings/metrics, revision validation findings. progress.md Phase 10 "Source status dashboard / Snapshot inspection / Parser warning review / Revision diff viewer" = not started. Note: revision review queue **is** wired; the broader source dashboard is not. |
| Lisans yönetimi listesi + denetim | `admin-users.html` / finance | `GET /api/admin/licenses` (listing) and license audit inspection = not started (progress.md Phase 10). |

---

## 4. Routing notes

- Public routes use the plan's Turkish slugs: `/` (landing), `/gizlilik`, `/kosullar`, `/iletisim`.
- Existing app routes were **kept as-is** to avoid breaking working integration: `/sign-in`, `/onboarding/{license,profile,calendar,sync,suspended}`, `/dashboard`, `/admin`. The plan's `/kurulum`, `/panel`, `/yonetim/*` slugs were **not** adopted; renaming live routes is out of scope for a re-skin. If Turkish app-route slugs are desired, that is a separate, coordinated change (also update `src/lib/onboarding.ts` `ROUTES`).
- The prototype's `?state=` variant switch (a design-review affordance) is intentionally **not** ported: production screens derive state from the authoritative backend, not a query param (plan §8.2, constraint K5).

---

## 5. Suggested build order for closing gaps

1. Wire **held-diff release** and **license create/revoke** UIs (backend already exists — §3.2).
2. Add `GET /api/admin/users` + detail, then build the user list/detail screen.
3. Add sync history + upcoming-lessons read endpoints for the dashboard.
4. Add the audit-event model → access-logs and audit viewers.
5. Health/metrics endpoints → server monitoring.
6. New product domains last: bulk event, user warning, finance.
