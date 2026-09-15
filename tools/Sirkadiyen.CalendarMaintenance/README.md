# Sirkadiyen.CalendarMaintenance

One-off maintenance for the incident where a schedule revision's diff was never calculated, so the
deletions it carried never reached the calendars that held them. A bulk **drog → ilaç** rename in
`G3-TR-A-ANNUAL` (revision `01a061eb…`) dropped 8 lessons whose old "drog" events were left stranded
on two students' calendars, because the next revision's diff baselined past the skipped one and never
emitted the deletions.

Both commands are **dry-run by default**. Nothing is written unless you pass `--apply`.

## Configuration (same environment the worker uses)

```
SIRKADIYEN_DATABASE__CONNECTION_STRING     # Postgres
SIRKADIYEN_DATAPROTECTION__KEY_RING_PATH   # the SHARED key ring — refresh tokens are sealed with it
SIRKADIYEN_GOOGLE__CALENDAR_CLIENT_ID
SIRKADIYEN_GOOGLE__CALENDAR_CLIENT_SECRET
```

The key ring path must be the one the API/worker use (ADR-058); otherwise the stored refresh tokens
cannot be unprotected and `remove-strays` cannot reach Google.

## Order of operations

Run this **before** deploying the `CalculatePendingAsync` isolation fix. The current (pre-fix)
worker has not computed these 4 revisions for weeks, so the tombstone wins the race; once the fix
ships, a revived worker would otherwise recompute and dispatch their stale content.

```bash
# 1. Neutralize the 4 skipped revisions (dry-run, then apply).
dotnet run --project tools/Sirkadiyen.CalendarMaintenance -- neutralize
dotnet run --project tools/Sirkadiyen.CalendarMaintenance -- neutralize --apply

# 2. Remove the stranded "drog" events for the affected users (dry-run, then apply).
dotnet run --project tools/Sirkadiyen.CalendarMaintenance -- remove-strays
dotnet run --project tools/Sirkadiyen.CalendarMaintenance -- remove-strays --apply

# 3. Deploy the isolation fix.
```

Save the console output of each `--apply` run: it is the audit record of what was changed.

## What each command does

- **neutralize** — for each skipped revision, records a `Discarded` tombstone `ScheduleDiff` (the
  real, recomputed entries, marked discarded). A discarded diff is excluded from both dispatch and
  replay (`ListPendingDispatchAsync` / `ListDispatchedForReplayAsync` require `Ready`/`Released`),
  and its existence takes the revision out of the pending-diff scan. Skips a revision that already
  has a diff.

- **remove-strays** — for each user, deletes every ledger mapping whose `(SourceId, StableIdentity)`
  is in **no** `Published` revision of its source (the "retired" case the cohort repair refuses to
  touch), removing the Google event and the ledger row. A lesson still published anywhere — even one
  merely no longer applicable to the user — is never touched.

## Overrides

`--revisions <id,id,…>` and `--users <id,id,…>` override the built-in incident defaults.
