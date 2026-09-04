# ExpenseTracker

Offline-first expense tracking for phone, desktop and browser, backed by a shared cloud
database.

[![CI](https://github.com/KonradOlobry1/ExpenseTracker/actions/workflows/ci.yml/badge.svg)](https://github.com/KonradOlobry1/ExpenseTracker/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Platforms](https://img.shields.io/badge/platforms-Android%20%7C%20iOS%20%7C%20Windows%20%7C%20macOS%20%7C%20Web-blue)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## What it is

A .NET MAUI Blazor Hybrid app and an ASP.NET Core service that share the same UI components
and domain model. Devices keep a local SQLite replica so the app works with no network, and
reconcile with the cloud when online. The web UI runs server-side next to the database and
needs no replica.

```
Phone   ── SQLite replica ──┐
                            ├──→  ExpenseTracker.Api  ──→  SQL Server
Desktop ── SQLite replica ──┤     (sync API + web UI)
Browser ────────────────────┘
```

One codebase renders on five platforms. The Razor components in `ExpenseTracker.UI` are
compiled into both heads; only the platform services behind them differ.

## Features

| | |
|---|---|
| **Expenses and income** | Categorised entries with notes, soft-deleted and synced across devices |
| **Subscriptions** | Recurring costs on weekly, monthly, quarterly or yearly cycles, with next-payment dates and a forecast timeline |
| **Dashboard and analytics** | Spending by category and period, monthly-equivalent totals that normalise every billing cycle to one comparable figure |
| **Offline-first** | Full read and write with no network; changes reconcile on the next sync |
| **Accounts** | Registration, sign-in, lockout, per-IP rate limiting, and silent token refresh so a device stays signed in without re-prompting |
| **16 currencies** | Each formatted in its own culture, so symbol placement and separators follow the currency rather than the UI language |
| **English and Polish** | UI language independent of currency |

## Screenshots

<!-- Add real captures here: docs/screenshots/*.png, then link them below.
     Suggested set: dashboard, expenses list, subscription timeline, analytics,
     and one phone-sized shot to show the MAUI head. -->

_Coming soon._

## Projects

| Project | Target | Role |
|---|---|---|
| `ExpenseTracker.Domain` | net10.0 | Entities, repository interfaces, `PredictionEngine`, currency and language catalogues |
| `ExpenseTracker.Contracts` | net10.0 | The sync wire format, shared by the API, the device client and the tests |
| `ExpenseTracker.Application` | net10.0 | Service interfaces and implementations, UI service contracts, translations |
| `ExpenseTracker.Infrastructure` | net10.0 | `AppDbContext`, device repositories, SQLite migrations, the sync and auth clients |
| `ExpenseTracker.UI` | net10.0 | Shared Razor components and UI service interfaces |
| `ExpenseTracker` | net10.0-* | MAUI head — platform implementations only (`Preferences`, `SecureStorage`, payment capture) |
| `ExpenseTracker.Api` | net10.0 | Sync API, Blazor Server UI, SQL Server migrations |

Dependencies point inward: `Domain` references nothing, and the two heads sit at the outside.
The sync client is testable without a device because it depends on `IPreferenceStore` and
`ISecureStore` rather than MAUI's statics — the MAUI head supplies the implementations and
nothing else about sync lives there.

Everything targets .NET 10. Mixing frameworks previously caused a subtle failure: a
component compiled against ASP.NET Core 9 silently emitted `Router.NotFoundPage` — a .NET 10
API — as a plain string attribute, which then failed to cast at run time.

## Running

### Docker Compose — the whole stack, one command

Brings up the API, the web UI and its own SQL Server. No Azure, no local SQL install.

```bash
cp .env.example .env
```

Edit the two values in `.env`, then:

```bash
docker compose up --build
```

The web UI is then on <http://localhost:8080>. Compose waits for SQL Server to accept
connections before starting the app, because migrations run at startup and "container
started" is not the same as "database ready".

### Local .NET

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=expensetracker;Trusted_Connection=True;" --project ExpenseTracker.Api
dotnet user-secrets set "Jwt:Key" "<a random string of at least 32 bytes>" --project ExpenseTracker.Api
dotnet run --project ExpenseTracker.Api
```

The app refuses to start without a valid `Jwt:Key` — that is intentional, not a bug.

### MAUI

```bash
dotnet build ExpenseTracker/ExpenseTracker.csproj -f net10.0-android -t:Run
```

### Container by hand

Build from the repository root; the API references three sibling projects, so the root is the
context even though the Dockerfile is not there.

```bash
docker build -f ExpenseTracker.Api/Dockerfile -t expensetracker-api .
```

```bash
docker run -p 8080:8080 -e "Jwt__Key=<32+ bytes>" -e "ConnectionStrings__DefaultConnection=<connection string>" expensetracker-api
```

## Tests

114 tests across three projects. Run them individually — `dotnet test` on the solution drags
the MAUI Android head through a full package build for no benefit.

```bash
dotnet test ExpenseTracker.Domain.Tests; dotnet test ExpenseTracker.Infrastructure.Tests; dotnet test ExpenseTracker.Api.Tests
```

| Project | Tests | Covers |
|---|---|---|
| `ExpenseTracker.Domain.Tests` | 19 | Billing-cycle arithmetic and forecasting |
| `ExpenseTracker.Infrastructure.Tests` | 38 | The device half of sync: pulled tombstones, the settings merge, what a failed push must not do, silent token refresh, sign-out |
| `ExpenseTracker.Api.Tests` | 57 | The real API on in-memory SQLite: auth, lockout, rate limits, push/pull, conflict resolution, refresh token issuance and rotation |

## CI

[`ci.yml`](.github/workflows/ci.yml) runs on every push and pull request, in two jobs.

**Tests** — the three suites above, on a runner with no database and no secrets.

**Container build and smoke test** — builds the image, then starts it against a real SQL
Server service container and asserts four things over HTTP:

- `/health/live` answers, so the process came up
- `/health/ready` answers, so migrations applied and the database is reachable
- `/account/login` renders, which needs the Data Protection key ring the database holds
- `/api/sync/pull` returns `401` to an anonymous caller

A real SQL Server rather than a stub, because the unit suite builds its schema with
`EnsureCreated` against SQLite — so a migration that only fails on SQL Server would otherwise
reach production unnoticed.

## Configuration

Never committed. Supply at run time via user-secrets locally, `.env` under Compose, or
environment variables in a container or App Service:

| Setting | Purpose |
|---|---|
| `Jwt__Key` | Token signing key, 32 bytes minimum |
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `RunMigrationsAtStartup` | Applies migrations on boot. Default `true`; set `false` once the schema is settled |
| `RateLimit__AuthPermitPerMinute` | Sign-in and registration attempts per IP. Default `10` |
| `RateLimit__SyncPermitPerMinute` | Sync requests per account. Default `30` |

## Health

| Endpoint | Checks | Use |
|---|---|---|
| `/health/live` | process only, no database | Platform probe. Deliberately does not touch SQL, so it cannot keep the auto-paused free-tier database awake |
| `/health/ready` | database reachable | Call before a first real request to warm the database, or when diagnosing |

Both are anonymous.

## Design notes

- Deletes are soft. Tombstones propagate so a row deleted on one device stays deleted
  everywhere; a hard delete could not be communicated.
- Sync conflicts resolve by the client's own edit time, not arrival order.
- `SyncId` is the cross-device identity. The seven built-in categories use fixed `SyncId`
  values so a device and the web agree they are the same categories.
- Access tokens last 24 hours; a 30-day refresh token (rotated on every use) renews one
  silently, so a device stays signed in as long as it syncs at least once a month. A device
  only sees a password prompt again once the refresh token itself has run out or been revoked.
- Device→API HTTP calls (sync, login, refresh) retry transient failures the same way
  `EnableRetryOnFailure` covers the database — Azure SQL's serverless tier auto-pauses, and
  the first request after idle needs a retry to survive the wake-up window.
- Package versions are pinned centrally in `Directory.Packages.props`. They used to float on
  wildcards, so the same commit could restore different binaries months apart.

## License

[MIT](LICENSE).
