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
| Profile edit (`/profile`) | `GET /api/profile`, `GET /api/profile/options`, `PUT /api/profile` (reports `calendarResyncRequested`, ADR-096/105) |
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
| Admin · bulk event / user warning | `GET /api/admin/announcements/options`, `POST .../preview`, `POST .../`, `GET .../`, `GET .../{id}`, `GET .../{id}/deliveries`, `PUT .../{id}`, `POST .../{id}/cancel` |

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

**This section is empty as of 2026-08-17.** Both entries were built full-stack (ADR-107); see the
update at the end of this document. No prototype admin screen is left without a backend.

### 3.2 Endpoint exists, UI not built (cheapest next steps)

**All three entries in this section were closed on 2026-08-15** — see the update at the end of
this document. Nothing currently sits in this category for the **admin** application.

A fourth entry of the same kind was found in the **student** application on 2026-08-16 and closed
the same day: `PUT /api/profile` and the whole ADR-096 calendar-resynchronization path were live,
but the only academic-profile screen was the onboarding step, gated to the `ProfileRequired` state,
so an active student could never reach it. See the update at the end of this document.

### 3.3 Wired read-only administration views

- `/admin/users`: paged user and license lists, user/license detail, license audit and selected-license revocation.
- `/admin/sources`: source pipeline status, the latest persisted parser warning/evidence details and retained snapshot evidence beside administrative upload.
- `/admin/access-logs`: masked sign-in log, audited transient IP reveal and cross-category audit query.
- `/admin/server`: API liveness/readiness, internal worker `/health/ready`, parser `/health` probe and database-backed point-in-time counts. CPU/RAM/Redis metrics remain unavailable and are not fabricated.
- `/admin/finance`: ten-figure summary/trends, transaction CRUD/export/history, obligation lifecycle,
  account/holder/share management, binding profit distribution and append-only finance audit.
- `/admin/diffs`: the held and failed-dispatch diff queues with their changed lessons, plus the
  reason-required release and retry actions (these two are write surfaces, not read-only).
- `/admin/bulk-event` and `/admin/user-warning`: server-resolved audience, exclusions with reasons,
  binding plan-hash confirmation, delivery ledger, content edit and cancellation (write surfaces).

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

## Update — the profile edit surface is wired (2026-08-16)

ADR-105. `PUT /api/profile` had been live since ADR-055 and ADR-096 made an audience-changing write
converge the student's calendar, but the only screen rendering that form was
`/onboarding/profile`, gated by `OnboardingGate` to the single state `ProfileRequired`. A student
who had finished onboarding was never in that state again, so **the feature was unreachable from
the product** — the dashboard showed the profile read-only and offered no way to change it.

- **`/profile`** (new route) renders the shared `AcademicProfileForm`, prefilled from
  `GET /api/profile`, for every state in which a profile already exists
  (`CalendarAuthorizationRequired`, `ReadyForInitialSync`, `InitialSyncInProgress`, `Active`,
  `ActionRequired`). The dashboard's academic-profile card links to it.
- `ProfileRequired` stays with the onboarding step, and `Suspended` is excluded because the backend
  refuses the write for an unactivated account — offering the form there would be a promise the API
  cannot keep.
- **`calendarResyncRequested` was missing from the typed contract entirely.** The backend had been
  returning it since ADR-096; `SaveStudentProfileResponse` in `src/lib/types.ts` never declared it,
  so no screen could have rendered it even if one had wanted to. Added, with the caveat in the type
  itself: it says the work was *requested*, not that it happened.
- **What a save may claim is a component,** `ProfileSaveNotice`, not a string at the call site. A
  requested re-synchronization is described as background work the worker will perform; a false
  flag claims nothing about the calendar, because false has more than one cause (an unchanged
  audience, or a changed audience on an account with no completed connection).
- **The admin audit category filter was extended** — it had been missing both finance categories
  since ADR-093, and now also carries `ProfileUpdated`.

### One backend change accompanied it

A profile change now writes an `AuditEvent` (`ProfileUpdated`). AI_GUIDELINE §19 lists it as
auditable and ADR-096 lets it delete calendar events, so the trail for "why did these lessons
disappear" was previously only the mapping ledger and the worker log. The metadata records the
resolved audience and **both** outcome flags separately — an audience change that queued nothing is
a real, different case — and deliberately never the student number.

---

---

## Update — the announcement domain is built, and §3.1 is empty (2026-08-17)

ADR-107. The bulk calendar event and the single-user warning were the last two prototype screens
with no backend at all. They are now one backend domain behind two screens, because the recipient
set was the only thing that differed between them — everything else (idempotent write, delivery
ledger, freeze gate, licence gate, edit-patches-not-duplicates, cancel-removes) is identical, and
duplicating it would have meant two copies of high-risk calendar code.

- **`/admin/bulk-event`** (`BulkEventComposer.tsx`) is a three-step wizard over the six-step
  high-risk pattern: audience → content → review and confirm. The server resolves the audience,
  lists the exclusions with their reasons, and returns a **plan hash** covering the recipient
  *identities*, not just their count. The confirmation carries the hash back and the write is
  refused if the audience moved, so an approved preview can never queue a different set of people.
- **`/admin/user-warning`** (`UserWarningComposer.tsx`) searches a user, shows the account state the
  warning is usually *about* before offering a template, then previews and confirms the same way.
  The warning key is user + template + local date, so a second send of the same template on the
  same day is a replay that writes nothing.
- **Delivery is tracked, never claimed.** The confirmation says the announcement was *queued*; the
  worker writes it. The counters (written / pending / skipped / removed / failed) come from the
  per-recipient ledger and are never stored on the announcement, so the number shown cannot
  disagree with the rows behind it.

### Three things the screens deliberately do not offer

- **No "hesap durumu / lisans durumu / senkronizasyon uygunluğu" filters**, though plan §5.11 lists
  them as audience dimensions. They are not choices: a revoked licence has already stopped
  synchronization (ADR-095) and an account with no completed initial sync has no calendar to write
  to. They appear instead as exclusion reasons the operator reads *before* confirming — which is
  the honest place for them, and is what §4.4's "hariç bırakılanlar ve gerekçeleri" asks for.
- **No "deneme bitiyor" warning template**, though plan §5.12 lists one. Sirkadiyen access does not
  lapse after activation and `GET /api/licenses/status` reports no time remaining (ADR-089), so the
  template would have an operator send students a deadline the product does not have. The four
  templates that ship each name a state the system can actually be in.
- **No re-addressing.** Editing an announcement changes what it says and patches every copy already
  written. Changing *who* receives it is a new announcement with its own confirmation, because the
  recipients were frozen when the count on the confirmation screen was approved.

### One inventory change this required

`CalendarInventoryReconciliationService` groups Sirkadiyen-marked Google events by their
`stableIdentity`. An announcement is marked but has no stable identity, so it would have been
counted as an unexpected marked event and reported as a conflict on **every** inventory pass —
making the signal useless exactly when a real conflict appears. Announcement events now carry
`sirkadiyenKind=announcement` and inventory skips them. The marker is on the new kind only, so no
already-written lesson had to be rewritten to gain one.

### Still no endpoint (unchanged)

`GET /api/calendar/sync/history` (needs a per-user activity log), `GET /api/notifications`,
`POST /api/contact`, and the articles/podcast content system.

---

## 5. Suggested build order for closing gaps

1. Add a per-user sync activity log before exposing `GET /api/calendar/sync/history`.
2. Decide and implement notification and contact contracts.
3. Add exporter/host telemetry only if CPU/RAM/Redis visibility becomes a requirement; worker/parser process health is already wired.
4. ~~Remaining new product domains without a backend: bulk event and user warning.~~ **Done
   2026-08-17** (ADR-107).
5. Alerting on `DispatchRetryCount` — the value is now readable in the UI, but nothing watches it.
   The same gap now exists for an announcement that reached its delivery attempt cap: it is visible
   in `/admin/bulk-event`, but nothing alerts on it.
