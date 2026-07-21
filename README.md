# Sirkadiyen

Sirkadiyen is a personal academic schedule synchronization platform for Istanbul Faculty of Medicine students.

It parses several irregular Google Sheets schedules, converts them into a canonical internal schedule model, determines which lessons apply to each student, and synchronizes only the changed events with the student's Google Calendar.

## Core goals

- Support first-, second-, and third-year students.
- Support Turkish and English programs.
- Parse irregular and frequently changing Google Sheets.
- Detect source changes quickly.
- Update only affected Google Calendar events.
- Preserve a complete audit trail for every parsed lesson and synchronization action.
- Keep the parser isolated from authentication, user management, and calendar synchronization.

## Main components

- Frontend application
- ASP.NET Core backend API
- .NET background worker
- Python parsing service
- PostgreSQL database
- Redis for caching, locks, and short-lived coordination
- Google OAuth, Google Sheets API, and Google Calendar API

## Repository conventions

The AI agent must read the following files before making architectural or implementation changes:

1. `AI_GUIDELINE.md`
2. `memory_bank/projectbrief.md`
3. `memory_bank/productContext.md`
4. `memory_bank/systemPatterns.md`
5. `memory_bank/techContext.md`
6. `memory_bank/activeContext.md`
7. `memory_bank/progress.md`
8. `memory_bank/decisionLog.md`

Source spreadsheet fixtures must be placed under `sheets/`.

## Initial directory proposal

```text
/
├── AI_GUIDELINE.md
├── README.md
├── memory_bank/
├── sheets/
├── src/
│   ├── Sirkadiyen.Api/
│   ├── Sirkadiyen.Application/
│   ├── Sirkadiyen.Domain/
│   ├── Sirkadiyen.Infrastructure/
│   ├── Sirkadiyen.Worker/
│   ├── Sirkadiyen.Contracts/
│   └── parser/
├── tests/
│   ├── Sirkadiyen.UnitTests/
│   ├── Sirkadiyen.IntegrationTests/
│   ├── Sirkadiyen.ArchitectureTests/
│   └── parser/
└── docker/
```

This structure may evolve only through an explicit architectural decision recorded in `memory_bank/decisionLog.md`.

## Current implementation status

The .NET 10 solution foundation is initialized with the following projects:

- `Sirkadiyen.Domain`
- `Sirkadiyen.Application`
- `Sirkadiyen.Contracts`
- `Sirkadiyen.Infrastructure`
- `Sirkadiyen.Api`
- `Sirkadiyen.Worker`

The API currently exposes `GET /health`. Business capabilities, persistence,
Google integrations, and the Python parser have not been implemented yet.

## Local development

Prerequisite: the .NET SDK version selected by `global.json`.

Restore and build the solution:

```powershell
dotnet restore Sirkadiyen.slnx
dotnet build Sirkadiyen.slnx --configuration Release --no-restore
```

Run the API or worker:

```powershell
dotnet run --project src/Sirkadiyen.Api
dotnet run --project src/Sirkadiyen.Worker
```

Copy `.env.example` to a local untracked environment file when configuration is
introduced. Never commit real credentials or tokens.
