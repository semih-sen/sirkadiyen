# Technical Context

## Status

This file defines the intended baseline technology stack. Versions should be pinned when the repository is initialized.

## Backend

### Runtime

- .NET 10
- C#
- ASP.NET Core
- .NET Worker Service

### Architectural style

- modular monolith
- clean layer boundaries
- command/query-oriented application layer
- background job processing
- transactional outbox for reliable internal events

### Data access

- Entity Framework Core
- PostgreSQL
- Npgsql
- version-controlled EF Core migrations

### Validation

Preferred:

- FluentValidation or explicit application validators

Choose one consistent validation style and record the decision.

### Authentication

- Google OpenID Connect for sign-in
- backend-issued application session
- no password authentication

The exact frontend/backend session mechanism must be decided and documented:

- secure HTTP-only cookie session, preferred for web
- or a carefully designed token-based alternative

Do not expose Google refresh tokens to the frontend.

### Authorization

- role- and policy-based authorization
- explicit admin policies
- active-license and completed-profile requirements enforced server-side

### Background processing

Initial preference:

- Hangfire
- PostgreSQL-backed storage where appropriate

Redis may be used for:

- distributed locks
- cache
- coordination
- rate limiting support

If a different queue is adopted, record the decision.

### Observability

Preferred baseline:

- OpenTelemetry
- structured logging
- Serilog
- Seq for initial deployment or OpenTelemetry-compatible backend
- health checks
- metrics endpoint

## Python parser service

### Runtime

- Python 3.13
- FastAPI 0.139.2
- Pydantic 2.13.4
- Uvicorn 0.51.0

### Supporting libraries

Use only as justified:

- `google-api-python-client` is not required if .NET acquires snapshots
- `openpyxl` for local `.xlsx` fixture analysis
- `pandas` for limited tabular operations
- `python-dateutil`
- `regex`
- `rapidfuzz` only for controlled deterministic matching where needed

The parser should generally receive a normalized snapshot contract from .NET rather than independently authenticating to Google.

### Quality tooling

- Ruff 0.15.22
- MyPy 2.3.0
- Pytest 9.1.1
- HTTPX2 2.7.0 for ASGI test clients
- coverage
- golden-file tests

## Frontend

Recommended baseline:

- Next.js
- TypeScript
- React
- a typed API client
- server-compatible Google sign-in flow
- accessible component library selected later

The frontend is not yet fully specified. Record the chosen UI stack before broad implementation.

## Database

PostgreSQL stores:

- users
- Google connection metadata
- encrypted refresh tokens
- licenses
- student profiles
- source configurations
- immutable snapshots
- revisions
- canonical records and versions
- schedule diffs
- event mappings
- background workflow state
- audit logs
- outbox messages

Use JSONB selectively for:

- immutable raw snapshot payloads
- parser evidence
- source-specific metadata
- warning details

Do not use JSONB as a substitute for core relational modeling.

## External integrations

### Google Identity

Used for:

- registration
- login
- verified Google identity

### Google Sheets API

Used by the .NET ingestion layer for:

- cell values
- sheet metadata
- merged ranges
- relevant formatting or grid information

### Google Calendar API

Used by .NET for:

- creating or selecting the managed calendar
- inserting events
- patching changed events
- deleting confirmed removed events
- reconciliation
- token refresh and authorization recovery

## Deployment

Initial target:

- Docker Compose
- Linux VPS or mini server
- reverse proxy using Caddy or Nginx
- PostgreSQL
- Redis
- API container
- worker container
- parser container
- frontend container
- observability container as resources permit

## Repository quality gates

Recommended CI checks:

### .NET

```text
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```

### Python

```text
ruff check
ruff format --check
mypy
pytest
```

### Frontend

```text
npm ci
npm run lint
npm run typecheck
npm run test
npm run build
```

### Additional

- dependency vulnerability scan
- secret scan
- Docker build verification
- migration validation
- architecture tests

## Configuration conventions

Use environment variables for deployment configuration.

Suggested prefixes:

```text
SIRKADIYEN_DATABASE__
SIRKADIYEN_REDIS__
SIRKADIYEN_GOOGLE__
SIRKADIYEN_PARSER__
SIRKADIYEN_JOBS__
SIRKADIYEN_SECURITY__
```

Local development secrets must not be committed.

Provide `.env.example` with placeholders only.

## API contract principles

- version public APIs
- use problem details for errors
- use correlation IDs
- use explicit pagination
- do not expose EF Core entities directly
- use typed request and response contracts
- use optimistic concurrency where user edits can conflict
