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
log — deferred), `GET /api/notifications`, `POST /api/contact`, and the bulk-event /
user-warning domains.

---

## Update — finance administration completed (2026-08-05)

The finance backend (ADR-093) and SuperAdmin workspace (ADR-094) are complete:
`/api/admin/finance/*` (holders, accounts, transactions incl. edit/hard-delete/history, the
ten-figure summary, monthly trend, CSV export, the module's own audit query),
`/api/admin/finance/obligations/*` (receivables/debts, settle, cancel settlement, write off,
cancel), and `/api/admin/finance/distributions/*` (the six-step preview/execute/reverse profit
distribution flow). All are SuperAdmin-only and every mutating route is CSRF-protected. The
`/admin/finance` workspace now exposes reporting, ledger CRUD/export, obligations, accounts,
holders/shares, binding distribution preview/execute/reverse and finance-audit inspection.

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
| Admin · freeze | `GET/POST /api/operations/freeze`, `GET/POST /api/operations/freeze/scopes` |
| Admin · document upload | `GET /api/sources/uploadable`, `POST /api/sources/{id}/document`, `GET .../uploads` |
| Admin · revision review | `GET /api/revisions`, `GET /api/revisions/{id}`, `POST /api/revisions/{id}/approve`, `POST /api/revisions/{id}/reject` |
| Admin · diff queues | `GET /api/diffs?state=Held`, `GET /api/diffs?dispatchState=Failed`, `GET /api/diffs/{id}`, `POST /api/diffs/{id}/release`, `POST /api/diffs/{id}/retry` |
| Admin · self-activation | `POST /api/admin/users/{id}/activate` |
| Admin · department colors | `GET/PUT/POST /api/admin/calendar-colors` |
| Admin · license create/revoke | `POST /api/admin/licenses`, `POST /api/admin/licenses/{id}/revoke` |
| Dashboard · schedule/status/repair | `GET /api/schedule/upcoming`, `GET /api/schedule/changes`, `GET /api/calendar/sync/progress`, `POST /api/calendar/reconcile`, `GET /api/licenses/status` |
| Admin · users/licenses | `GET /api/admin/users`, `GET /api/admin/licenses` and detail routes |
| Admin · source status | `GET /api/admin/sources` and `GET /api/admin/sources/{id}` |
| Admin · access/audit | `GET /api/admin/access-logs`, `POST .../unmask`, `GET /api/admin/audit` |
| Admin · health/metrics | `/health/live`, `/health/ready`, `GET /api/admin/metrics`, `GET /api/admin/services/health` |

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
| Toplu takvim etkinliği | `admin-bulk-event.html` | Audience resolution → estimated recipients → dedup campaign key → queued delivery tracking (plan §4.4, §5.11). |
| Tek kullanıcı uyarısı | `admin-user-warning.html` | Idempotent `warning-key` per user+template+date (plan §4.5, §5.12). |

### 3.2 Endpoint exists, UI not built (cheapest next steps)

**All three entries in this section were closed on 2026-08-15** — see the update at the end of
this document. Nothing currently sits in this category.

### 3.3 Wired read-only administration views

- `/admin/users`: paged user and license lists, user/license detail, license audit and selected-license revocation.
- `/admin/sources`: source pipeline status, the latest persisted parser warning/evidence details and retained snapshot evidence beside administrative upload.
- `/admin/access-logs`: masked sign-in log, audited transient IP reveal and cross-category audit query.
- `/admin/server`: API liveness/readiness, internal worker `/health/ready`, parser `/health` probe and database-backed point-in-time counts. CPU/RAM/Redis metrics remain unavailable and are not fabricated.
- `/admin/finance`: ten-figure summary/trends, transaction CRUD/export/history, obligation lifecycle,
  account/holder/share management, binding profit distribution and append-only finance audit.
- `/admin/diffs`: the held and failed-dispatch diff queues with their changed lessons, plus the
  reason-required release and retry actions (these two are write surfaces, not read-only).

---

## 4. Routing notes

- Public routes use the plan's Turkish slugs: `/` (landing), `/gizlilik`, `/kosullar`, `/iletisim`.
- Existing app routes were **kept as-is** to avoid breaking working integration: `/sign-in`, `/onboarding/{license,profile,calendar,sync,suspended}`, `/dashboard`, `/admin`. The plan's `/kurulum`, `/panel`, `/yonetim/*` slugs were **not** adopted; renaming live routes is out of scope for a re-skin. If Turkish app-route slugs are desired, that is a separate, coordinated change (also update `src/lib/onboarding.ts` `ROUTES`).
- The prototype's `?state=` variant switch (a design-review affordance) is intentionally **not** ported: production screens derive state from the authoritative backend, not a query param (plan §8.2, constraint K5).

---

---

## Update — sync gating and operator recovery (2026-08-05)

ADR-095 through ADR-097 added backend behaviour with frontend consequences:

- **A revoked student now really stops synchronizing** (ADR-095). Their calendar and its events are
  preserved, but nothing is written to them again. The suspended screen should say so plainly; it
  currently does not mention the calendar at all.
- **A profile change now re-synchronizes the calendar** (ADR-096). `PUT /api/profile` reports
  `calendarResyncRequested` when the change altered the audience, so the profile screen can tell the
  student their calendar is being updated rather than leaving them to wonder. The work is the
  worker's; the response only says it was requested.
- **Two new operator routes have no UI**, both listed in §3.2: revision rejection and failed-diff
  retry. **Both were wired on 2026-08-15**; see the update below.

---

---

## Update — the three operator UIs are wired (2026-08-15)

The whole of §3.2 is closed. Every backend-supported operator action now has a surface, so no
state the pipeline can enter is left without a way out of it.

- **`/admin/diffs`** (new route, `DiffQueues.tsx`) carries **two** queues, deliberately as
  separate tabs rather than one merged list:
  - *Bekletilen diff'ler* reads `GET /api/diffs/?state=Held` and releases through
    `POST /api/diffs/{id}/release`.
  - *Başarısız dağıtım* reads `GET /api/diffs/?dispatchState=Failed` and retries through
    `POST /api/diffs/{id}/retry`.

  They are separate because the two axes are orthogonal: a terminally failed diff is still
  `Ready`/`Released` in its review state, so the held queue can never show it. Merging them would
  hide that a *released* diff can still fail its fan-out.
- **An ambiguity hold renders as a stated refusal, not a disabled button.** `isReleasable` false
  replaces the whole reason field and action with the explanation that releasing it would leave
  the previous lesson in every affected calendar and never write its replacement — so the source
  has to state which lesson is which (ADR-042). Same shape for a non-retriable dispatch state.
- **The changed lessons are shown before either action is offered.** Expanding a row loads
  `GET /api/diffs/{id}`, listing each actionable entry with its previous and current lesson,
  and says how many of `actionableEntryCount` are displayed. Releasing without seeing which
  lessons disappear is rubber-stamping, which is the whole point of the hold.
- **The retry count is surfaced, not just the failure.** `dispatchRetryCount`, the last retrying
  operator and their reason are rendered, because a diff retried repeatedly is the real signal
  that the failure is not transient. Nothing alerts on it yet — that remains the unbuilt alerting
  work, not a UI gap.
- **Revision rejection** is in the existing review screen (`RevisionReview.tsx`), behind a
  confirmation step with its own required reason, never reusing the approval field. The
  confirmation states in words that the action is terminal and that the correction is a newer
  revision published over it, never a rollback (ADR-033).
- **The review screen gained a queue selector** (`ReviewRequired` / `Rejected`). This was
  required by rejection being terminal: once rejected, the revision leaves the review queue, and
  without a rejected queue the recorded reason would be unreadable from any operator surface.

### One backend change was needed

`ScheduleRevisionDetail` did not project `RejectedBy` / `RejectionReason` / `RejectedAtUtc`, so
`POST /api/revisions/{id}/reject` wrote an audit record no read path could return. Three fields
added to the application contract and the persistence projection; no migration, no behaviour
change, and the approval fields stay separate so the trail can never state the opposite of what
happened.

---

## 5. Suggested build order for closing gaps

1. Add a per-user sync activity log before exposing `GET /api/calendar/sync/history`.
2. Decide and implement notification and contact contracts.
3. Add exporter/host telemetry only if CPU/RAM/Redis visibility becomes a requirement; worker/parser process health is already wired.
4. Remaining new product domains without a backend: bulk event and user warning.
5. Alerting on `DispatchRetryCount` — the value is now readable in the UI, but nothing watches it.
