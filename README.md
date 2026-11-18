# Benchline

Benchline is a workshop scheduling and outage-recovery application for a university makerspace. Members reserve equipment, technicians report outages, and coordinators review and publish replacement plans. The recovery planner matches displaced bookings to compatible future slots while respecting each member's deadline.

## Technology

- **Backend:** C# 12, .NET 10, ASP.NET Core
- **Frontend:** Blazor WebAssembly (PWA)
- **Data:** Entity Framework Core, SQLite for the demo, PostgreSQL provider available
- **Live updates:** SignalR
- **Deployment:** Docker Compose
- **Tests:** .NET tests and Playwright browser tests

## Run the demo

Install Docker with Compose, then run from the repository root:

```bash
docker compose up --build
```

Open [http://localhost:8080](http://localhost:8080). The demo uses a local SQLite volume.

| Role | Username | Demo password |
|---|---|---|
| Member | `member` | `Benchline-Demo!26` |
| Member | `member2` | `Benchline-Demo!26` |
| Technician | `tech` | `Benchline-Demo!26` |
| Coordinator | `coord` | `Benchline-Demo!26` |

These credentials are for the local demo only; do not reuse them in production. Use separate browser profiles or private windows to test different roles. To remove the demo database and its audit history, run `docker compose down -v`.

## Project structure

- `src/Benchline.Domain` — immutable domain records, scheduling rules and the recovery planner.
- `src/Benchline.Application` — workshop use cases and orchestration.
- `src/Benchline.Infrastructure` — EF Core database and repository implementation.
- `src/Benchline.Api` — HTTP endpoints, authentication, authorization and SignalR hub.
- `src/Benchline.Client` — Blazor application and `BoardStore` state management.
- `src/Benchline.Components` — reusable Razor UI components.
- `src/Benchline.Contracts` — shared API contracts and Astana time formatting.
- `tests` — unit, integration, component and browser tests.

## Design notes

The solution follows Clean Architecture. The domain layer is kept independent of the UI, API and database. It applies Functional Programming techniques including immutable records and collections, pure functions (`ScheduleRules.Execute` and `RecoveryPlanner.Build`), typed `Result<T>`/`Fault` outcomes, and pattern matching. The client uses a Flux-style unidirectional state flow: actions are dispatched to `BoardStore`, reduced to a new state, and then reflected by UI components.

The recovery planner uses maximum-cardinality bipartite matching with augmenting paths. A coordinator previews a plan before publishing it. The API rechecks the schedule revision and exact assignments before committing changes, while a database constraint prevents duplicate active bookings for a slot.

## Run tests

.NET tests:

```bash
dotnet test Benchline.slnx -c Release --nologo
```

Browser tests (start a fresh demo with Docker Compose first):

```bash
cd tests/browser
npm ci --omit=optional
npx playwright install chromium firefox webkit
npx playwright test
```

## Further documentation

- [Architecture and Functional Programming](docs/ARCHITECTURE.md)
- [API reference and examples](docs/API.md)
- [Developer and deployment notes](docs/DEVELOPER_NOTES.md)
- [User guide](docs/USER_GUIDE.md)
- [Quality assurance report](docs/QA_REPORT.md)
- [Implementation backlog](docs/BACKLOG.md)

## Scope and known limitations

The current release targets one workshop with fixed one-hour sessions and Astana time (UTC+5). Offline support caches the application shell; server-side booking changes require a connection. Physical iOS/Android installation, native macOS execution and manual screen-reader testing have not been verified. Production account and station provisioning still require deployment integration.
