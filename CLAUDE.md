# ExpenseTracker

Offline-first expense tracking for phone, desktop and browser. A .NET MAUI Blazor Hybrid app
and an ASP.NET Core service share the same Razor components and domain model. Devices keep a
local SQLite replica and reconcile with the cloud; the web UI runs next to the database and
has no replica.

Read [README.md](README.md) first for the product view. This file is the working agreement:
what to run, what the layers mean, and the things that have already bitten someone here.

## Architecture

Layered Clean Architecture. Dependencies point inward; the two heads sit on the outside.

```
Domain  <-  Application  <-  Infrastructure  <-  ExpenseTracker       (MAUI head)
                                             <-  ExpenseTracker.Api   (web UI + sync API)
Contracts  --  the sync wire format, referenced by both heads and the tests
```

| Project | Target | Rule |
|---|---|---|
| `ExpenseTracker.Domain` | net10.0 | Entities, repository interfaces, `PredictionEngine`, currency/language catalogues. **References nothing.** |
| `ExpenseTracker.Contracts` | net10.0 | The sync wire format only. Shared by the API, the device client and the tests, so a change is a change on both sides at once. |
| `ExpenseTracker.Application` | net10.0 | Service interfaces and implementations, UI service contracts, translations. No EF, no HTTP. |
| `ExpenseTracker.Infrastructure` | net10.0 | `AppDbContext`, device repositories, **SQLite** migrations, the sync and auth clients. |
| `ExpenseTracker.UI` | net10.0 | Shared Razor components. Compiled into both heads. |
| `ExpenseTracker` | net10.0-android/ios/maccatalyst/windows | MAUI head. **Platform implementations only** - `Preferences`, `SecureStorage`, payment capture. |
| `ExpenseTracker.Api` | net10.0 | Sync API, Blazor Server UI, **SQL Server** migrations. |

Two consequences worth stating outright:

- **Nothing about sync logic lives in the MAUI head.** `SyncService` and `AuthService` are in
  `Infrastructure` and depend on `IPreferenceStore` / `ISecureStore`, not MAUI's statics. That
  is what makes them testable on a CI runner with no device. Keep it that way - moving sync
  code back into the head silently deletes 38 tests' worth of coverage.
- **The web and the device are different deployments, not different features.** Where they
  genuinely differ, express it as a capability flag (`ISyncService.IsSupported`,
  `IPaymentCaptureService.IsAvailable`), not as a page that renders a control which does
  nothing. An inert button is indistinguishable from a broken one.

## Commands

**Build and test the parts CI builds.** Never `dotnet build` or `dotnet test` on the solution
unless you actually want the MAUI Android head packaged - it needs workloads this repository's
CI does not install, and takes minutes for no benefit.

```bash
dotnet build ExpenseTracker.Api/ExpenseTracker.Api.csproj
```

```bash
dotnet test ExpenseTracker.Domain.Tests; dotnet test ExpenseTracker.Infrastructure.Tests; dotnet test ExpenseTracker.Api.Tests
```

Run the API locally. It needs both secrets and refuses to start without a valid `Jwt:Key`,
which is intentional:

```bash
dotnet run --project ExpenseTracker.Api
```

Whole stack including its own SQL Server:

```bash
docker compose up --build
```

MAUI head:

```bash
dotnet build ExpenseTracker/ExpenseTracker.csproj -f net10.0-android -t:Run
```

## Conventions

`.editorconfig` describes the style and is the source of truth - file-scoped namespaces,
primary constructors, `var` only where the type is apparent, `_camelCase` private fields,
expression-bodied one-liners. Rules are `suggestion` severity on purpose: formatting must not
be able to break a build.

- **`TreatWarningsAsErrors` is on** everywhere except the MAUI head, which is excluded because
  Android and iOS bindings emit warnings outside this repository's control. Do not "fix" a
  warning by suppressing it unless the suppression carries a comment saying why.
- **Central package management.** Versions live in `Directory.Packages.props`, pinned exactly.
  A `PackageReference` in a csproj carries no `Version` attribute. MAUI packages are the one
  exception and use `VersionOverride` in the head.
- **Tests are xUnit v2, one `[Fact]` per behaviour, sentence-shaped names**
  (`One_account_cannot_exhaust_anothers_budget`). Each test class carries an XML doc comment
  saying what would break in production if those tests were deleted. Match that.
- **API tests boot the real API** over in-memory SQLite via `ApiFactory` - no mocked pipeline,
  no Testcontainers.
- **Comments explain why, not what.** The existing ones are prose and often several lines.
  Match the density of the file being edited rather than the average of the repository.

## Landmines

Things that have already cost time here. Read before touching the relevant area.

**Blazor SSR form binding.** `ExpenseTracker.Api/Web/Account/Login.razor` is static SSR because
signing in writes a `Set-Cookie` header, which a live circuit cannot do. The `name` attribute is
generated from *the text of the bind expression*, so `@bind-Value="Input.Email"` must reference a
property literally named `Input` decorated with `[SupplyParameterFromForm]`. Routing it through
a computed `Model` property to silence BL0008 breaks that correlation and every field posts
empty. The `#pragma warning disable BL0008` there is deliberate - leave it.

**Two DbContexts, two migration histories.** `AppDbContext` (SQLite, device) and `ApiDbContext`
(SQL Server, cloud) keep separate migration folders. Add a migration to the right project with
the right provider. A migration that only fails on SQL Server is invisible to the unit suite,
which builds its schema with `EnsureCreated` against SQLite - which is why CI runs a real SQL
Server for the smoke test.

**Middleware order.** `UseRateLimiter()` must come *after* `UseAuthentication()`, or the sync
limiter has no identity to partition by and every account shares one bucket.

**Data Protection keys live in the database**, so the database is a startup dependency and
rendering any page needs the key ring. A container that builds and starts is not proof the app
works; `/account/login` returning 200 is.

**Package versions must track the shared framework.** Blazor does not diagnose a mismatch at
compile time: a component built against an older framework emits newer parameters as plain
string attributes, and fails when they are cast at run time.

**`ApiBaseUrl` is a fixed `const`** in `ExpenseTracker.Infrastructure/External/AuthService.cs`.
Production has exactly one server, the user does not choose it, and there is no field for it.

**Secrets are never committed.** user-secrets locally, `.env` under Compose, environment
variables in App Service. The Configuration table in the README lists every one.

## Decisions already made

Do not relitigate these without asking:

- Deletes are soft; tombstones propagate, so a row deleted on one device stays deleted.
- Sync conflicts resolve by the client's own edit time, not arrival order.
- `SyncId` is the cross-device identity; the seven built-in categories use fixed values.
- Access tokens last 24 hours; a 30-day refresh token rotates on every use. Refresh tokens are
  stored SHA-256 hashed, so a database read cannot hand out a usable credential.
- `/account/logout` is POST-only, so a stray link or image tag cannot trigger it.
- Currency, language and theme live on the account and ride the sync payload, so they follow
  the user between devices.

## Known gaps

- Password reset exists end to end but **cannot deliver a token in production**. The only
  `IPasswordResetSender` is `LoggingPasswordResetSender`, which logs the token in Development
  and deliberately delivers nothing anywhere else, rather than writing a live credential into
  a log file. Registering a real sender is all that stands between here and a working flow.
- No email confirmation. `AppUser.EmailConfirmed` is never set and nothing checks it.
- Two-device tombstone propagation (phone to web and back) has not been exercised end to end.
