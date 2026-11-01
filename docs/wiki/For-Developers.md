# For Developers

## Project layout

```
MCSimulator/
├─ MonteCarlo.Backend/         .NET 10, clean architecture
│  └─ src/
│     ├─ MonteCarlo.Domain          entities, reconciliation rules, simulation engine (references nothing)
│     ├─ MonteCarlo.Application     use cases and ports (interfaces for the outside world)
│     ├─ MonteCarlo.Infrastructure  EF Core + migrations, extractors, Jira / GitHub / Graph adapters
│     └─ MonteCarlo.Api             minimal API, webhooks, background workers (composition root)
├─ MonteCarlo.UI/              React 19 + Vite + TypeScript + react-three-fiber
├─ docs/                       screenshots and wiki sources
└─ start.ps1                   starts both
```

**Dependencies point inwards only:** Domain → Application → Infrastructure / Api. The Application layer talks to the outside world through ports such as `IKnowledgeStore`, `ILlmExtractor`, `ITicketSystem`, `ITranscriptSource`, `ICodeActivitySource`, `IInsightNarrator` and `IIngestionQueue`. Swapping Jira for the local board, or the AI for the rule-based extractor, is a one-line change in `AddInfrastructure`.

## Database and migrations

- SQL Server **LocalDB**, database `MonteCarloTwin`, connection string in `appsettings.json` (`ConnectionStrings:Default`).
- `Program.cs` calls `Database.MigrateAsync()` on every start, then the demo-data seeder.
- Enums are stored as strings.

```powershell
# add a migration after changing entities
dotnet ef migrations add <Name> --project src/MonteCarlo.Infrastructure --startup-project src/MonteCarlo.Api --output-dir Persistence/Migrations
```

## HTTP API

Swagger UI at `http://localhost:5080/swagger`. Main groups under `/api`:

| Group | Routes |
|---|---|
| Health, projects | `GET /health`, `/projects`, `/projects/dashboard`, `/projects/digest` |
| People, work items | `/people`, `/workitems` (+ `/{id}/status`, `/sync`) |
| Meetings | `/meetings` (+ `/{id}/reprocess`, `/sample`) |
| Suggestions | `/suggestions` (+ `/{id}/accept`, `/{id}/reject`), `POST /reconcile` |
| Code activity | `/code-events` (+ `/pull`) |
| Search | `GET /search?q=` |
| **Simulation** | `GET /simulation/model`, `POST /simulation/run`, **`POST /simulation/montecarlo`**, **`POST /simulation/landscape`**, `/simulation/runs` |
| Settings | `/settings` (+ `/purge`), `GET /audit` |
| Webhooks | `POST /webhooks/graph`, `POST /webhooks/github` |

## The Monte Carlo engine (`MonteCarlo.Domain/Simulation`)

Pure C#, no I/O, no framework. Key points:

- **Discrete working days.** Each iteration plays the project forward one working day at a time.
- **Common random numbers.** Random draws are keyed by `(seed, iteration, task or person, day)` using SplitMix-style hashing (`Crn.cs`). Baseline and scenario therefore see the *same* luck in iteration *i*, so the difference between them is the scenario's effect.
- **Calibrated estimate error.** Each task's duration is `estimate × lognormal`. The mean and spread come from finished tickets per role (`Calibration.FromHistory`).
- **Random absences** with a configurable daily rate.
- **People model:** capacity, role match (cross-role work is slower), new-hire ramp-up and mentoring drag, knowledge loss when someone leaves.
- **Iterations run in parallel** (`Parallel.For`); results are deterministic for a given seed.

### Entry points

| Method | Used by | Returns |
|---|---|---|
| `MonteCarloEngine.Compare` | What-if simulator | Baseline vs scenario, histogram, bottleneck role, a typical-run replay (day-by-day frames) |
| `MonteCarloEngine.Forecast` | Mitigation check, landscape cells | One forecast summary |
| **`MonteCarloEngine.Explore`** | **3D Monte Carlo, Run by run** | **Every iteration** (finish day, on-time flag, sampled work, absences, critical chain), burn-down paths for the first N runs, the exact finish-day distribution, **convergence** of P50/P80/P95 as iterations accumulate, and a per-task **criticality index** |
| `SimulationService.LandscapeAsync` | 3D Monte Carlo, Risk landscape | A grid of `Forecast` calls over two levers (Capacity, Scope, Hires, Absence), each cell run in parallel with the same seed |

### Criticality index (`Explore`)

For each iteration, start at the task that finished last and repeatedly step to the dependency that finished latest. Every task visited is on that run's critical chain. The index is the share of iterations in which a task was visited.

## The 3D views (`MonteCarlo.UI/src/components`)

| File | Draws |
|---|---|
| `MonteCarlo3D.tsx` | Burn-down paths (one `LineSegments` geometry revealed by draw range), finish-day balls (one `InstancedMesh`), deadline wall, sliding P50/P80/P95 poles |
| `Landscape3D.tsx` | The risk surface (indexed `BufferGeometry` with vertex colours), deadline plane, hover picking |
| `Sim3D.tsx` | The task "city" replay on the What-if simulator page |

The UI contains **no simulation logic**. It fetches the results of `/simulation/montecarlo` and `/simulation/landscape` and animates them.

## Useful commands

```powershell
dotnet build MonteCarlo.Backend/MonteCarlo.slnx
cd MonteCarlo.UI; npm run build      # type-check + production build
```

No automated tests are included yet.
