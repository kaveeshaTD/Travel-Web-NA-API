# Ceylon Roads API

.NET 10 Web API for the Ceylon Roads website and admin portal. Clean architecture with the repository pattern,
PostgreSQL (EF Core + Npgsql), role-based JWT auth with a rotating refresh cookie, SendGrid email (coming later; until then emails are only recorded in `email_logs`).

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

## Sign in from Swagger or Scalar

1. Run the API (Development) and open `http://localhost:5115/swagger` or `/scalar/v1`.
2. Call `POST /api/auth/login` with `{ "email": "admin@ceylonroads.example", "password": "Admin@12345" }`.
3. Copy the `token` from the response, then use **Authorize** (Swagger) or the **Auth** section (Scalar) and paste it as a Bearer token. Calls to protected routes such as `GET /api/auth/me` now work.

The access token lasts about 15 minutes. The browser also receives an httpOnly `refresh_token` cookie; `POST /api/auth/refresh`
exchanges it for a new token. The cookie only exists for browsers (it is never in a response body).

With curl:

```powershell
curl.exe -s -X POST http://localhost:5115/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"admin@ceylonroads.example\",\"password\":\"Admin@12345\"}"
```

Five wrong passwords lock an account for 15 minutes (HTTP 429). Clear it by waiting, or with a password reset
(`POST /api/auth/forgot`, then `POST /api/auth/reset`). Until SendGrid is set up the reset email is not sent. In Development the full email, including the reset link, is written
to the API console (look for `Email not sent (no provider configured)`), so copy the link from there. The `email_logs` table
deliberately shows `token=[REDACTED]`: reset tokens are never stored or logged in clear, and outside Development the log
does not contain email bodies (`Email:LogBodies` is only on in `appsettings.Development.json`).

## Content and uploads

The website content (packages, destinations, vehicles, services, FAQs, gallery), the site settings and the travel tips are
read without signing in and managed by an admin (`/api/packages`, `/api/destinations`, `/api/vehicles`, `/api/services`,
`/api/faqs`, `/api/gallery`, `/api/settings`, `/api/tips`). All prices are in LKR.

To try an upload from Scalar or Swagger, sign in as the admin (see above), then call `POST /api/uploads` with a
`multipart/form-data` body that has one file in the field `file` (JPEG, PNG or WebP, up to 5 MB). The answer is
`{ "url": "/uploads/<name>" }`; open that path on the API address to see the image, and use the same path as an image
value in a package, destination, vehicle, service or gallery record.

Uploaded files are kept in the folder set by `Storage:UploadsPath` (default `uploads` next to the app, ignored by git).
**In production, point it at storage that survives restarts and redeploys** (a mounted volume or disk); otherwise uploaded
images disappear with the container. The API refuses to start when that folder cannot be created or written to.

## Exchange rates

Prices are in LKR. The server fetches USD, EUR and GBP rates about 10 seconds after it starts and then whenever the stored
rates are more than 24 hours old (it checks every hour), keeps every fetch in the `exchange_rates` table and serves the
newest ones in `GET /api/settings` (`currencyRates` is LKR per 1 unit, with `currencyRatesAsOf` and `currencyRatesSource`).
If both free sources are unreachable, the last stored rates stay in use and a warning is written to the log; on a brand-new
database the `ExchangeRates:Fallback` numbers are shown, marked `fallback`. Set `ExchangeRates:Enabled` to `false` to stop all
outgoing requests.

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
| `Jwt:Key` | Token signing key, at least 32 characters. Development has its own key in `appsettings.Development.json`; Production must set it (environment variable `Jwt__Key`) |
| `App:WebBaseUrl` | Website address used in password reset links. Must be `https` in Production |
| `Auth:*` | `MaxFailedAttempts` (5), `LockoutMinutes` (15), `RefreshDays` (1), `RefreshDaysRemember` (30), `RefreshGraceSeconds` (10), `ResetTokenMinutes` (60) |
| `Bookings:MaxPerUserPerHour` | The most bookings one customer can create in an hour (10). Beyond that the API answers `429` |
| `Email:OwnerEmail` | Where the "new booking" notice goes. When empty the business email from the settings is used; when neither exists the notice is skipped |
| `RateLimiting:SubmitPermitLimit`, `RateLimiting:SubmitWindowSeconds` | Requests per window per client address on the public forms, a review and a contact message sharing one counter (5 per 600 s) |
| `RateLimiting:AuthPermitLimit`, `RateLimiting:AuthWindowSeconds` | Requests per window per client address on register, login, forgot, reset and refresh (10 per 60 s) |
| `Hosting:UseForwardedHeaders`, `Hosting:KnownProxies` | Turn on only behind a reverse proxy, and list its addresses, so the real client address is used for rate limiting |
| `Seed:Enabled`, `Seed:IncludeSampleActivity` | Seed at startup, and whether to include sample bookings |
| `Limits:MaxRequestBodyBytes` | Maximum request body (default 1 MB). `POST /api/uploads` has its own limit (5 MB plus the form envelope) |
| `ExchangeRates:*` | Automatic exchange rates. `Enabled` (true), `RefreshHours` (24), `StartupDelaySeconds` (10), `MaxChangePercent` (25), `RequestTimeoutSeconds` (10), `PrimaryUrl`, `FallbackUrl` and `Fallback:{Usd,Eur,Gbp}` (rates used until the first fetch). The API needs outbound HTTPS to `open.er-api.com` and `cdn.jsdelivr.net`. Rates cannot be edited through the API |
| `Storage:UploadsPath` | Folder for uploaded images (default `uploads`). Must be persistent in Production |
| `Email:*` | SendGrid settings. Empty for now: emails are recorded in `email_logs` (marked failed, never resent) instead of being sent. `Email:LogBodies` (true only in Development) also writes email bodies to the console |
| `Serilog:*` | Log levels and sinks (JSON to the console by default) |

In Production the API refuses to start when the connection string, the JWT key or the allowed origins are missing,
too short or still a placeholder, and the message names the setting. Demo accounts are never created in Production
unless seeding is requested explicitly (`--seed` or `Seed:Enabled`).

## Errors

Every error is `{ "error": { "status": 400, "message": "...", "fields": { "email": "..." } } }` with the matching
status code (`400, 401, 403, 404, 405, 409, 413`, and a generic `500` that never shows internals). The
`X-Correlation-Id` response header matches the id in the server log.
