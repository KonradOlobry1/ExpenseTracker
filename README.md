# ExpenseTracker

Offline-first expense tracking for phone, desktop and browser, backed by a shared cloud
database.

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

## Projects

| Project | Target | Role |
|---|---|---|
| `ExpenseTracker.Domain` | net10.0 | Entities, repository interfaces, `PredictionEngine`, currency and language catalogues |
| `ExpenseTracker.Contracts` | net10.0 | The sync wire format, shared by the API, the device client and the tests |
| `ExpenseTracker.Application` | net10.0 | Service interfaces and implementations |
| `ExpenseTracker.Infrastructure` | net10.0 | `AppDbContext`, device repositories, SQLite migrations |
| `ExpenseTracker.UI` | net10.0 | Shared Razor components and UI service interfaces |
| `ExpenseTracker` | net10.0-* | MAUI head — platform implementations only |
| `ExpenseTracker.Api` | net10.0 | Sync API, Blazor Server UI, SQL Server migrations |

Everything targets .NET 10. Mixing frameworks previously caused a subtle failure: a
component compiled against ASP.NET Core 9 silently emitted `Router.NotFoundPage` — a .NET 10
API — as a plain string attribute, which then failed to cast at run time.

## Running

**Web / API**

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=expensetracker;Trusted_Connection=True;" --project ExpenseTracker.Api
dotnet user-secrets set "Jwt:Key" "<a random string of at least 32 bytes>" --project ExpenseTracker.Api
dotnet run --project ExpenseTracker.Api
```

The app refuses to start without a valid `Jwt:Key` — that is intentional, not a bug.

**MAUI**

```bash
dotnet build ExpenseTracker/ExpenseTracker.csproj -f net10.0-android -t:Run
```

## Docker

Build from the repository root; the API references three sibling projects.

```bash
docker build -t expensetracker-api .
```

```bash
docker run -p 8080:8080 -e "Jwt__Key=<32+ bytes>" -e "ConnectionStrings__DefaultConnection=<connection string>" expensetracker-api
```

## Tests

```bash
dotnet test ExpenseTracker.Domain.Tests; dotnet test ExpenseTracker.Api.Tests
```

Run them individually — `dotnet test` on the solution drags the MAUI Android head through a
full package build for no benefit.

`ExpenseTracker.Api.Tests` boots the real API against in-memory SQLite and covers auth, sync
push/pull, soft-delete tombstones and conflict resolution.

## Configuration

Never committed. Supply at run time via user-secrets locally, or environment variables in
a container or App Service:

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

## Notes

- Deletes are soft. Tombstones propagate so a row deleted on one device stays deleted
  everywhere; a hard delete could not be communicated.
- Sync conflicts resolve by the client's own edit time, not arrival order.
- `SyncId` is the cross-device identity. The seven built-in categories use fixed `SyncId`
  values so a device and the web agree they are the same categories.
