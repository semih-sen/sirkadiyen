# Product Context

## The problem

The faculty publishes academic schedules through many Google Sheets. These sheets vary by class year, language, curriculum group, practice group, semester, and lesson type.

The tables are designed for human reading, not software integration. They may contain:

- merged cells
- repeated or omitted dates
- inconsistent time formats
- color-coded meaning
- multi-line cells
- instructor and lesson names in the same cell
- group labels spread across columns
- hidden or decorative rows
- manually edited corrections
- weekly room assignments separate from the annual program

Students must repeatedly inspect several documents to understand their personal schedule.

## Product value

Sirkadiyen gives each student one personalized calendar that stays aligned with the faculty's source schedules.

The value is not merely convenience. The product reduces:

- missed lessons
- confusion between groups
- uncertainty after schedule changes
- repetitive manual calendar entry
- dependence on informal announcements

## User lifecycle

### Unauthenticated visitor

Can:

- see product explanation
- sign in with Google

Cannot:

- access protected schedule data
- redeem licenses
- start synchronization

### Authenticated but inactive user

Has a valid Google identity but has not redeemed a license.

Can:

- access license activation flow
- view limited account information

Cannot:

- complete schedule setup
- start synchronization

### Activated but incomplete user

Has redeemed a license but has not completed academic profile or permissions.

Can:

- enter or edit supported academic profile data
- grant required Google Calendar permission
- resume onboarding

Cannot:

- be considered fully synchronized

### Active user

Has:

- Google-authenticated account
- valid activation
- valid student profile
- required Calendar authorization
- selected target calendar or accepted the Sirkadiyen calendar strategy

Can:

- start initial sync
- view sync state
- request reconciliation
- update permitted academic profile data
- revoke or reconnect Google access

### Administrator

Can:

- create and revoke licenses
- inspect license redemption
- manage source definitions
- trigger source polls and reparsing
- review parser warnings
- publish or reject revisions
- inspect synchronization failures
- inspect audit history
- manage supported student profile options

## Onboarding state model

Suggested states:

```text
GoogleAuthenticated
LicenseRequired
LicenseActivated
ProfileRequired
CalendarAuthorizationRequired
ReadyForInitialSync
InitialSyncInProgress
Active
ActionRequired
Suspended
```

State must be derived from authoritative backend data where possible.

## License behavior

Initial product assumption:

- license codes are issued by administrators
- a code activates a user account
- codes may be single-use
- codes may optionally include expiration, cohort restrictions, or notes
- a redeemed code remains auditable
- revocation behavior must be explicit

Open product decision:

- whether revoking a license disables future sync only, removes calendar events, or suspends access while preserving events

Until decided, revocation must not automatically delete calendar events.

## Student profile

The profile must be modeled dynamically enough to support different requirements by class year.

Common fields:

- academic year
- class year
- program language

Conditional fields may include:

- curriculum group
- general practice group
- anatomy group
- vertical corridor group
- bedside group
- faculty-member practice group
- elective selections

Anatomy group is a separate profile dimension from the general practice group.
For the confirmed first- and second-year model it uses values `1`, `2`, and `3`.
The same anatomy and vertical-corridor source schedules may apply to both Turkish
and English programs.

The frontend should request only fields applicable to the selected class year and program.

The backend must validate every combination against supported options.

## Initial synchronization experience

The initial sync may involve many events and should be asynchronous.

The UI should show:

- queued
- reading schedule
- resolving personal lessons
- creating calendar events
- completed
- completed with warnings
- action required
- failed with retry option

The frontend must not remain blocked on a single long HTTP request.

## Change synchronization experience

Routine schedule changes should be invisible when successful.

When user action is required, the system should clearly distinguish:

- Google authorization revoked
- profile incomplete
- source under review
- temporary synchronization failure
- unsupported profile combination
- calendar deleted or unavailable

## Calendar ownership strategy

Preferred initial approach:

- create or use a dedicated Sirkadiyen calendar for managed events
- keep Sirkadiyen events separate from the user's personal events
- store Google calendar and event identifiers in the backend
- mark events with private extended properties

The final calendar creation policy must be recorded in `decisionLog.md`.

## Trust principles

- Never claim the calendar is current when source parsing is under review.
- Never hide a failed sync behind a green status.
- Never destructively reconcile on uncertain parser output.
- Let admins inspect source evidence.
- Let users understand what action they must take.
