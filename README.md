# Ceylon Roads API

.NET 10 Web API for the Ceylon Roads website and admin portal. Clean architecture with the repository pattern,
PostgreSQL (EF Core + Npgsql), role-based JWT auth (coming in the auth change), SendGrid email (coming later).

```
TravelWebAPI.Domain          entities, enums, exceptions (no dependencies)
TravelWebAPI.Application     use-case ports: repositories, unit of work, clock, hasher, email sender
TravelWebAPI.Infrastructure  EF Core, migrations, seeder, clock, password hasher, log-only email sender
TravelWebAPI.Api             controllers, middleware, error contract, Program.cs
TravelWebAPI.Tests           unit and integration tests
```

Naming: C# is PascalCase, JSON is camelCase, PostgreSQL is snake_case (EF Core converts between them).

## Prerequisites

- .NET SDK 10
- Docker (the local database container, and the integration tests)
- A PostgreSQL server. The project uses the `my-postgres` Docker container on port 5432

## First-time setup

```powershell
# 1. Start PostgreSQL and create the project database (once)
docker start my-postgres
docker exec my-postgres psql -U postgres -c "CREATE DATABASE ceylon_roads"

# 2. Store the connection string outside git (adjust the password to your container)
cd TravelWebAPI.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=ceylon_roads;Username=postgres;Password=<your password>"
cd ..

# 3. Create the tables and load the demo data
dotnet run --project TravelWebAPI.Api --launch-profile http -- --seed
```

`--seed` applies the migrations, loads the seed data and exits. It only fills empty tables, so running it again
changes nothing. `--migrate` applies only the migrations and exits.

Demo logins created by the seed (Development only): admin `admin@ceylonroads.example` / `Admin@12345`,
customer `emma.wilson@example.com` / `Customer123`. Seeded prices are placeholders (a fixed 300 LKR per USD,
30 LKR per km for every vehicle); edit them in the admin portal later.

On the first run against a brand-new database the log shows one `ERR Failed executing DbCommand` line about
`__EFMigrationsHistory`. It is EF Core probing for its history table before creating it and is expected.

## Run

```powershell
dotnet run --project TravelWebAPI.Api --launch-profile http
```

- API: http://localhost:5115 (health: `GET /api/health`)
- API docs (Development only), both read the same OpenAPI document (`/openapi/v1.json`):
  - Swagger UI: http://localhost:5115/swagger
  - Scalar: http://localhost:5115/scalar/v1
- In Development the app applies pending migrations at start and seeds when `Seed:Enabled` is true
  (set in `appsettings.Development.json`).

## Test

```powershell
dotnet test
```

Integration tests start a throw-away PostgreSQL container with Testcontainers (Docker must be running). They never
touch the `ceylon_roads` database.

## Migrations

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project TravelWebAPI.Infrastructure --startup-project TravelWebAPI.Api --output-dir Persistence/Migrations
```

## Configuration

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | PostgreSQL connection string (user-secrets or environment variable `ConnectionStrings__Default`) |
| `Cors:AllowedOrigins` | Exact origins of the website and admin portal (no wildcard) |
| `Jwt:Key` | Token signing key, at least 32 characters (used by the auth change) |
| `Seed:Enabled`, `Seed:IncludeSampleActivity` | Seed at startup, and whether to include sample bookings |
| `Limits:MaxRequestBodyBytes` | Maximum request body (default 1 MB) |
| `Email:*` | SendGrid settings. Empty for now: emails are written to the log and `email_logs` instead of being sent |
| `Serilog:*` | Log levels and sinks (JSON to the console by default) |

In Production the API refuses to start when the connection string, the JWT key or the allowed origins are missing,
too short or still a placeholder, and the message names the setting. Demo accounts are never created in Production
unless seeding is requested explicitly (`--seed` or `Seed:Enabled`).

## Errors

Every error is `{ "error": { "status": 400, "message": "...", "fields": { "email": "..." } } }` with the matching
status code (`400, 401, 403, 404, 405, 409, 413`, and a generic `500` that never shows internals). The
`X-Correlation-Id` response header matches the id in the server log.
