# Getting Started

You will have the app running in a few minutes. Nothing needs to be set up by hand, because the database creates itself the first time you start.

## What you need

| Thing | Why | How to check |
|---|---|---|
| **.NET 10 SDK** | Runs the backend | `dotnet --version` |
| **Node.js 20 or newer** | Runs the web screens | `node -v` |
| **SQL Server LocalDB** | A small database that ships with Visual Studio and SQL Server Express | `sqllocaldb info` should list `MSSQLLocalDB` |

## Start it (easiest way)

Open PowerShell in the project folder and run:

```powershell
.\start.ps1
```

That opens two windows (the backend and the web screens) and then your browser at **http://localhost:5173**.

## Start it by hand

```powershell
# Window 1: the backend  ->  http://localhost:5080  (API docs at /swagger)
cd MonteCarlo.Backend
dotnet run --project src/MonteCarlo.Api

# Window 2: the web screens  ->  http://localhost:5173
cd MonteCarlo.UI
npm install
npm run dev
```

## What happens on the very first start

1. The backend **creates the database** `MonteCarloTwin` on LocalDB and **applies all migrations** automatically.
2. It loads a **demo project** (a fictional "GST Reconciliation" team with 8 people, 12 finished tickets and about 14 open ones).
3. It reads a **sample sprint-call transcript** and turns it into suggestions, so the *Review* screen already has something to look at.

Want an empty app instead? Open `MonteCarlo.Backend/src/MonteCarlo.Api/appsettings.json` and set `"SeedDemoData": false`.

## Starting over

To wipe the database and begin again:

```powershell
cd MonteCarlo.Backend
dotnet ef database drop -f --project src/MonteCarlo.Infrastructure --startup-project src/MonteCarlo.Api
```

The next start recreates it.

## If something goes wrong

| Symptom | Likely cause and fix |
|---|---|
| Red banner *"API offline"* in the sidebar | The backend is not running. Start it (see above). |
| Backend cannot connect to the database | LocalDB is missing. Run `sqllocaldb info`. If it is empty, install *SQL Server Express LocalDB*. |
| Port 5080 or 5173 already in use | Close the other program using it, or change the port (backend: `Properties/launchSettings.json`; screens: `vite.config.ts`). |
| The 3D area is black | Your browser has WebGL turned off. Turn on hardware acceleration, or try Chrome or Edge. |

Next: [A Tour of the App](Tour-of-the-App)
