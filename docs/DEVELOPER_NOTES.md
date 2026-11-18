# Benchline developer notes

## Source layout

| Project | Responsibility |
| --- | --- |
| Domain | Immutable schedule, booking rules, recovery matching |
| Application | Use cases and repository interface |
| Infrastructure | EF Core schema, database provider and change persistence |
| Api | HTTP and SignalR host, authentication and demo data |
| Contracts | HTTP payloads shared by API and client |
| Client | Blazor routes, board reducer, HTTP and hub connection |
| Components | Reusable Razor components |
| Showcase | Route assembly loaded when visitors open the component library |

`Directory.Build.props` sets C# 12, nullable analysis and warnings as errors. Builds and tests use .NET 10. The test projects use temporary SQLite databases or test doubles, so they leave the developer database alone.

## Host configuration

| Key | Purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Demo` enables sample data and allows HTTP for a loopback deployment. Other environments use HTTPS and secure cookies. |
| `Workshop__DemoPassword` | Password assigned when a demo account is first created. |
| `Workshop__DemoClockUtc` | Optional fixed clock for Demo verification, for example `2024-01-08T10:30:00Z`. The host uses the system clock when the override is absent. |
| `Workshop__Provider` | The value `PostgreSQL` selects Npgsql. SQLite is the default. |
| `ConnectionStrings__Workshop` | Provider connection string. The default is `Data Source=workshop.db`. |
| `Workshop__KeyPath` | Persistent Data Protection key directory. The application user requires write access. The host protects the directory with filesystem permissions. |

The startup migration creates a portable schema. A local PostgreSQL demo uses an empty database, the PostgreSQL provider and connection string, and a loopback address. Provisioning production accounts and station schedules requires an administrative database or identity integration workflow.

In Demo, the injected clock sets the session window relative to the current UTC hour for storage. Startup checks future availability, both primary laser reservations, the printer reservation and the actual recovery planner against both laser deadlines. When the scenario is no longer ready, one transaction renews the four demo stations' sessions and bookings, advances the revision and appends an audit event. Usable data stays in place across restarts. Outside Demo, startup runs schema migrations only. The `docker compose down -v` command deletes the demo volume.

`DemoCatalog` defines the demo display names and campus spaces. Startup updates these fields in Demo even if the scenario is usable. The account IDs and password hashes, station IDs, existing usable reservations and earlier audit events are retained.

| Station | Campus space |
| --- | --- |
| Desktop CNC | FabLab |
| Laser Cutter 01 | Coworking Zone |
| Laser Cutter 02 | Open Space |
| Prusa MK4 | Library |

`AstanaTime` uses a fixed +05:00 offset for display and calendar-day grouping. At 19:00 UTC, the display rolls over to midnight the next day. Booking comparisons and timestamps in storage and JSON remain UTC. The CSV appends **Recorded Astana time** as ISO 8601 and retains the original columns.

In the expiry check, we started a separate Compose project with an old `Workshop__DemoClockUtc` value. We removed the override and recreated the service under the same project name to retain the volume. The audit log showed **Demo schedule renewed**. The browser case runs from `tests/browser` with `BENCHLINE_OLD_VOLUME=1 npx playwright test demo-renewal.spec.js`. `BENCHLINE_URL` specifies an alternate address.

## Publishing and deployment

```sh
dotnet publish src/Benchline.Api -c Release -o publish
```

The published host serves the browser client, API and hub from the origin root. Production uses HTTPS directly or through a trusted reverse proxy with forwarded headers. The database and key directory hold state across restarts. Both contain data used by a migration backup. `/health` exposes datastore readiness.

## Deployment limits

A single site revision serializes schedule writes. Database reads use a consistent snapshot and fixed queries. An indexed cursor bounds audit pages. The verified deployment covers one workshop and one host. A multi-host deployment depends on a shared database, shared Data Protection keys, coordinated migrations and a shared SignalR transport.

## Client views and limits

The shared Razor library contains the timetable, schedule cells, filter, connection tag and notices host. Workshop-specific header, booking, recovery and audit views live under `Benchline.Client/Pages`. The page keeps its effects and dispatches updates to `BoardStore`.

`AuditQuery.DefaultPageSize` keeps the client cursor aligned with the server default of 25 entries. The application caps requests and CSV export at 100 entries. `ScheduleRules` limits the acceptable replacement finish to seven days after the original slot. Existing sign-in, polling and recovery limits preserve the published demo behavior.

## Design tokens

The `--paper`, `--ink`, `--muted`, `--hairline` and `--hatch` tokens in `styles.css` define both monochrome themes. IBM Plex Sans handles prose and headings. IBM Plex Mono displays Astana times, revisions and summary numbers. Controls use a 2px radius and a 44px minimum height. Cells and tags have square corners. The schedule uses a native table on wider displays and stacks station rows on phones. Buttons with dashed borders link to visible reasons using `aria-describedby`. Reduced motion disables both the 120ms dialog fade and the brief highlight on changed station rows.

Latin WOFF2 files and their OFL licenses live under `wwwroot/fonts`. The text-only startup shell uses the same local styles.

The session response advertises demo account choices only in Demo. The client displays the public stock password when it matches the configured demo password. A session check in Demo resolves the current display name even if a browser cookie was issued by the previous release. A custom password stays in configuration.

## Publish performance

The host registers publish asset endpoints with `MapStaticAssets`. Negotiated Brotli and gzip use content-specific metadata, and fingerprinted resources can be cached immutably. The shell, service worker and asset list require revalidation. The client schedules offline asset preparation after the initial session check or showcase render. The client keeps invariant globalization, Release trimming and the separate showcase route assembly. The HTTP transport uses Polly Core directly. Safe reads have transient retry and circuit-breaker policies, and writes have an attempt timeout.

## Verification

Unit tests cover deterministic matching and pure transition rules. Integration tests use real cookie and antiforgery flows, the migrated database, and competing requests. Component tests check callbacks, templating and reducer behavior. Playwright exercises the published host, UI, cross-session updates and offline shell. See [QA](QA_REPORT.md) for the observed results.
