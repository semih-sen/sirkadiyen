# Decision Log

This file is append-only. Do not erase historical decisions. Mark decisions as superseded when necessary.

---

## ADR-001: Use a .NET primary backend

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The rewrite requires authentication, licensing, user profiles, background jobs, transactional state changes, Google API integration, incremental synchronization, and administrative workflows.

### Decision

Use .NET 10 and ASP.NET Core for the primary backend, with a separate .NET Worker Service for background operations.

### Consequences

- Strong typing and mature background-service support.
- One primary application ecosystem for business logic.
- Python remains isolated to parsing.
- Domain boundaries must prevent infrastructure leakage.

---

## ADR-002: Keep Python parser-only

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Spreadsheet structures are irregular and Python is suitable for specialized parsing and fixture-driven experimentation.

### Decision

Python will only parse normalized source snapshots and return canonical candidate records, evidence, warnings, metrics, and confidence information.

Python will not manage users, licenses, OAuth, database publication, affected-user resolution, or Google Calendar writes.

### Consequences

- Clear ownership boundaries.
- Parser can be tested deterministically.
- .NET remains authoritative for business state.
- A versioned HTTP contract is required.

---

## ADR-003: Use Google-only authentication

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The product is tightly integrated with Google Calendar and does not require password-based accounts.

### Decision

Registration and login will use Google authentication only.

### Consequences

- No password storage or recovery system.
- Google identity does not imply product activation.
- Local user and onboarding state remain necessary.
- Google Calendar authorization scopes must be managed separately or incrementally as appropriate.

---

## ADR-004: Require administrator-issued license activation

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Access is controlled through codes distributed by administrators.

### Decision

A Google-authenticated user must redeem a valid license code before completing activation and synchronization.

### Consequences

- License redemption must be transactional.
- Codes must be stored as secure hashes.
- Redemption attempts require rate limiting and audit logs.
- Exact expiration, revocation, and reuse policy remains open.

---

## ADR-005: Use canonical schedule records between parsing and calendar sync

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Source spreadsheet formats differ and change. Direct mapping from cells to Google events would tightly couple parsing with synchronization.

### Decision

All parser outputs must be converted into a canonical schedule model and published as versioned revisions before calendar synchronization.

### Consequences

- Parsing, validation, diffing, and synchronization are independently testable.
- Every event can retain provenance.
- Publication and rollback workflows are required.

---

## ADR-006: Use incremental event synchronization

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The old system deleted and recreated a broad two-week event range.

### Decision

Routine synchronization will update only events affected by a published semantic schedule diff.

### Consequences

- Durable user-to-Google-event mapping is required.
- Stable lesson identity and content hashing are required.
- Calendar jobs must be idempotent.
- Full rebuild is reserved for explicit repair operations.

---

## ADR-007: Preserve immutable raw source snapshots

**Status:** Accepted  
**Date:** 2026-07-21

### Context

Parser behavior must be explainable and reproducible.

### Decision

Every changed source acquisition is stored as an immutable snapshot before parsing.

### Consequences

- Historical parser runs can be reproduced.
- Storage use increases.
- Snapshot retention policy may be added later.
- Source evidence can be shown to administrators.

---

## ADR-008: Use parser profiles instead of one universal parser

**Status:** Accepted  
**Date:** 2026-07-21

### Context

The source inventory contains several structurally distinct spreadsheet families.

### Decision

Implement shared parsing primitives and multiple named, versioned parser profiles.

### Consequences

- Changes to one schedule family are less likely to break others.
- Each profile requires fixtures and regression tests.
- Source configuration must map each source to a parser profile.

---

## Open decisions

The following require future ADRs:

- frontend framework and component system
- exact browser session strategy
- managed Google Calendar strategy
- license reuse, expiration, and revocation policy
- background queue implementation
- source polling interval policy
- automatic publication thresholds
- profile schema and supported group combinations
- snapshot retention policy
