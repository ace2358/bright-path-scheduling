# Bright Path Scheduling

Small ASP.NET Core API for the Bright Path Learning Centre scheduling assessment.

## Prerequisites

- .NET 8 SDK

## Run

```powershell
dotnet restore BrightPathScheduling.sln --configfile NuGet.Config
dotnet run --project BrightPath.Api
```

Swagger is available at the URL printed by the API (normally `/swagger`). SQLite is created automatically as `BrightPath.Api/brightpath.db`.

## Tests

```powershell
dotnet test BrightPathScheduling.sln --no-restore
```

## API surface

- `GET /api/lessons` — lists lessons and their participants.
- `POST /api/lessons` — creates a lesson after conflict validation.
- `PUT /api/lessons/{id}` — updates or reschedules an existing lesson after conflict validation.

The provided CSV exports are held in `data/` and are imported idempotently at startup. Invalid new or rescheduled lessons return `409 Conflict` with structured tutor, room, and student conflict details.
