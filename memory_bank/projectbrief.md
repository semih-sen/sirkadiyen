# Project Brief

## Project name

Sirkadiyen

## Purpose

Sirkadiyen converts the complex and frequently changing academic schedules of Istanbul Faculty of Medicine into personalized Google Calendar events.

The source schedules are maintained as multiple online Google Sheets with irregular formatting. Students should not need to inspect all source tables manually or repeatedly rebuild their calendars.

## Current historical context

The first production version has operated for approximately one year.

The original implementation:

- used n8n
- supported second-year students
- synchronized schedules into Google Calendar
- handled changes by deleting and recreating a broad time window

The new implementation is a ground-up rewrite designed for greater scale, safer incremental synchronization, stronger observability, and support for multiple class years and programs.

## Target users

Initially:

- first-year Turkish program students
- first-year English program students
- second-year Turkish program students
- second-year English program students
- third-year Turkish A and B curriculum groups
- third-year English curriculum groups supported by source data
- administrators who manage licenses, schedule sources, parser state, and synchronization health

## Source schedule inventory

### First year

- Turkish annual program
- Turkish practice program
- English annual program
- English practice program
- anatomy practice program

### Second year

- Turkish annual program
- Turkish practice program
- English annual program
- English practice program
- anatomy autumn program
- anatomy spring program
- vertical corridor practice program

The anatomy and vertical-corridor sources are shared by Turkish and English
students. Anatomy groups are numbered `1`, `2`, and `3` and are independent from
the normal practice group. Anatomy entries appear as `Diseksiyon` in the annual
program; vertical-corridor and other practice entries appear as `Uygulama`.

The first-year anatomy grouping follows the same or a very similar `1`/`2`/`3`
model and is also independent from the normal practice group.

### Third year

For each supported Turkish or English curriculum group:

- annual program
- bedside practice program
- faculty-member practice program

Known grouping includes Turkish A and Turkish B. Exact English group combinations must be confirmed from fixtures and source configuration.

### Shared source

- weekly amphitheatre schedule that enriches theoretical lessons with room information

## Core product behavior

1. User registers and signs in using Google only.
2. User enters a license code issued by an administrator.
3. The backend activates the account after successful license redemption.
4. User enters academic profile and group information.
5. User starts initial synchronization.
6. The backend resolves applicable canonical lessons.
7. Calendar jobs create managed Google Calendar events.
8. Source tables are polled regularly.
9. Changed sources are reparsed and validated.
10. Only affected lessons and users are synchronized.
11. Existing Google events are updated in place whenever possible.

## Non-goals for the initial release

- password authentication
- public self-service license purchase
- arbitrary university support
- editing source schedules from Sirkadiyen
- full bidirectional calendar editing
- machine-learning-based parsing
- allowing users to rewrite canonical lesson data
- direct spreadsheet-to-calendar coupling

## Success criteria

- No broad calendar wipe during routine synchronization.
- Same input and parser version produce identical parsed output.
- Every calendar event can be traced back to a canonical record and source evidence.
- A changed source is reflected in affected calendars within the defined operational target.
- A parser anomaly does not trigger destructive synchronization.
- Initial synchronization can be retried safely.
- Duplicate event creation is prevented by design.
- Admins can understand why a source or user is out of sync.
