# Shaft Stock Distributor

Shaft Stock Distributor plans which returnable-crate article belongs in which shaft of a flow-rack aisle, then simulates the two shuttles that pull those shafts. The same operations core drives the animated demo and the scenario study, and it is the surface a future PLC adapter would call.

The desktop app is Avalonia 11 (11.3.22) on .NET 10. Headless rendering and the chart library both work, so the UI stayed on Avalonia.

All copy in the app, the sample, the logs, and these docs is English.

## What the demo shows

Start demo loads the example warehouse, runs a short simulated-annealing pass (180 iterations), and plays the aisle:

- Shafts fill in the article color. A pull drains the shaft.
- Shuttles move with the acceleration profile, horizontally and between levels at the same time.
- The shared middle locks, and a shuttle that has to wait is drawn with a WAIT marker.
- Playback is play, pause, step, and reset, from 1x to 400x.
- The live dashboard shows pallets per hour, offered pallets per hour, utilization, pull wait, blocked time, and lost crates.

The other pages edit the layout (including per-shuttle, per-axis speed), the crate catalog, the optimizer weights, and the scenario study. The operation page shows the pull queue, the event log, and shuttle state. Switching manual and automatic assignment asks for confirmation first.

## Screenshots

Headless Skia captures of the main window. The live frame is the example demo after the shuttles have started to move. The study frame is a short sweep of the three example scenarios.

![Live demo with the example aisle running](docs/screenshots/live-demo.png)

![Scenario study chart](docs/screenshots/scenario-study.png)

## Projects

| Project | Role |
| --- | --- |
| `ShaftStockDistributor.Core` | Layout, articles, zones, travel time, validation, JSON |
| `ShaftStockDistributor.Optimizer` | Shaft counts, constructive heuristic, simulated annealing |
| `ShaftStockDistributor.Operations` | Pull controller, aisle interlock, automatic reassignment, hardware port |
| `ShaftStockDistributor.Simulation` | Seeded discrete-event simulation and the scenario study |
| `ShaftStockDistributor.Cli` | `shaftstock` sample, optimize, simulate, study |
| `ShaftStockDistributor.Desktop` | Avalonia UI, MVVM, animated aisle, charts |
| `ShaftStockDistributor.Tests` | Domain, optimizer, controller, simulation |
| `ShaftStockDistributor.Desktop.Tests` | View-model tests and headless window renders |

View models use CommunityToolkit.Mvvm (`ObservableProperty`, `RelayCommand`, `ObservableValidator`) and a messenger for project and simulation events. Services (storage, file dialogs, optimizer, study, clock) sit behind interfaces. Bindings are compiled. Strings live in `Resources/Strings.resx`.

## Run

Install the .NET 10 SDK (the repo pins 10.0.402, rolling forward on the 10.0 feature band).

```bash
dotnet run --project src/ShaftStockDistributor.Desktop
```

Published builds are framework-dependent. The machine needs the .NET 10 desktop runtime.

```bash
dotnet publish src/ShaftStockDistributor.Desktop -c Release -r linux-x64 --self-contained false
dotnet publish src/ShaftStockDistributor.Desktop -c Release -r win-x64 --self-contained false
```

Command line:

```bash
dotnet run --project src/ShaftStockDistributor.Cli -- sample --out samples/example-warehouse.json
dotnet run --project src/ShaftStockDistributor.Cli -- optimize --project samples/example-warehouse.json
dotnet run --project src/ShaftStockDistributor.Cli -- simulate --project samples/example-warehouse.json --hours 1 --warmup 0.1 --seed 1 --mode Manual
dotnet run --project src/ShaftStockDistributor.Cli -- study --project samples/example-warehouse.json --min 600 --max 3600 --step 600 --reps 2 --hours 0.5 --compare-modes --csv samples/example-study.csv
```

`--no-optimize` keeps the assignment already stored in the file. The study re-optimizes once per scenario unless you pass that flag.

## Example data

`samples/example-warehouse.json` is hypothetical and labeled EXAMPLE. It is a 12 × 5 × 9 aisle, shaft length 20 m, pitch 0.45 m, level height 0.55 m, segment gap 0.35 m, transfer 12 s, safety distance 1.5 m. Shuttle speed is 0.3 m/s and acceleration is 0.2 m/s² on both axes. Three scenarios each total 1800 crates/h: balanced 70/20/10, fast movers 85/10/5, and even mix 40/35/25.

`EX-C-OVERSIZE` is 900 × 800 × 800 mm. It is in the catalog so the layout warns that a pull can never trigger and that the crate does not fit the optional 0.42 m × 0.40 m cross-section. Its class weight is 0, so it receives no inflow and does not trip saturation by itself.

## Project file

JSON schema version 1, camelCase, enums as strings, shaft addresses 1-based (`segment`, `level`, `shaft`). `ProjectSerializer` loads and saves it. Assignment export headers are `Segment,Level,Shaft,Zone,ArticleId,ArticleName,Pinned`. Study CSV headers are `Scenario,AssignmentMode,LoadCratesPerHour,Replication,OfferedPalletsPerHour,AchievedPalletsPerHour,Shuttle1Utilization,Shuttle2Utilization,AveragePullWaitSeconds,MaxPullWaitSeconds,BlockedSeconds,OverflowingShafts,LostCrates`.

## Assignment strategy

The full rationale, with the worked shaft-count example and sketches, is in [docs/assignment-strategy.md](docs/assignment-strategy.md). Short version:

- A segment is one rack. The first third of segments belongs to shuttle 1, the last third to shuttle 2, and the middle is shared.
- Shaft capacity is `floor(length / (flow dimension + gap))`. The number of shafts is the crates that arrive during the expected pull wait, divided by the headroom above the pull trigger.
- **Example numbers:** 20 m / 0.40 m = 50 crates, trigger 40 + 5 = 45, headroom 5, 600 crates/h over 90 s is 15 crates, `ceil(15 / 5) = 3` shafts.
- A movers are split across the two exclusive ends, near each shuttle's home, with one shared shaft when they need three or more. B and C movers prefer the middle.
- Travel time is the slower axis. The planner scores trips from home. A return to a separate handover pose is not applied, because that pose is still unknown.
- Annealing swaps unpinned shafts. The cost is travel hours, shuttle imbalance, shared-zone pull fraction, and a penalty for missing shafts. Default weights are 1, 1.2, 0.8, and 4.
- Manual mode keeps the stored assignment. Automatic mode may retarget an empty, unpinned shaft, with hysteresis, a cooldown, and a cap of 24 changes per hour. A shaft that still holds crates never changes article.
- The saturation chart plots offered and achieved pallets/h. A load is saturated when achieved stays under 98% of offered, when any crate is lost, or when utilization crosses an optional ceiling. Read the curves as well as the marker: a short window, or a few lost crates, marks the first point and reports sustainable throughput as 0.

## Sample run

Optimizer, 400 iterations, example warehouse:

| Plan | Total cost | Travel hours | Imbalance | Middle traffic |
| --- | --- | --- | --- | --- |
| Naive, articles in order from the left | 1.460 | 0.260 | 1.000 | 0 |
| Optimized | 0.815 | 0.584 | 0.082 | 0.165 |

One simulated hour, seed 1, 0.1 h warm-up excluded, offered 52.8 pallets/h at the example 1800 crates/h:

| Assignment | Mode | Pallets/h | Pulls (shuttle 1 / 2) | Utilization | Lost crates |
| --- | --- | --- | --- | --- | --- |
| Optimized | Manual | 42.4 | 12 / 21 | 18.5% / 27.1% | 6 |
| Optimized | Automatic | 21.7 | 4 / 13 | 7.0% / 18.6% | 390 |
| Naive | Manual | 41.4 | 33 / 0 | 20.2% / 0% | 8 |

The optimized plan costs more travel because it sends work to shuttle 2, and the naive plan never pulls with shuttle 2. Achieved pallets are close on this particular hour; the balance shows up in who does the pulls. Automatic mode retargets empty shafts during the hour and, on this hypothetical inflow, loses a lot of crates. The [strategy note](docs/assignment-strategy.md) explains when to use each mode, and how to read a sweep where the strict saturation rule fires at 600 crates/h. The raw sweep is `samples/example-study.csv` (0.5 h, 0.05 h warm-up, 2 replications, re-optimized once per scenario, both modes).

## Assumptions

- A pull discharges the whole shaft. Pallets credited equal discharged crates divided by crates per pallet, so the credit can be fractional.
- Offered pallets/h is inflow divided by crates per pallet, summed over articles.
- Arrivals are Poisson, from one `Random` seeded per run. Modern .NET uses the same algorithm on every platform.
- All shafts share one length. Home and handover default to the aisle ends on level 1.
- Acceleration default is 0.2 m/s². Transfer default is 12 s. Expected pull wait default is 90 s.
- The hardware port is a simulated adapter. `PlcHardwareStub.SendPullJob` throws `NotSupportedException` until OPC UA, S7, or TCP is chosen.

## Tests and CI

```bash
dotnet test ShaftStockDistributor.slnx -c Release -warnaserror
```

GitHub Actions on `ubuntu-latest` builds with warnings as errors, runs the tests (including the headless window), and publishes `win-x64` and `linux-x64` desktop artifacts.

## Open questions

Home and handover poses, whether 0.3 m/s and 0.2 m/s² are right, the real transfer time, whether a pull should discharge the whole shaft, real inflow, and the PLC protocol. Detail is in the [strategy note](docs/assignment-strategy.md).

## What this environment did not verify

The screenshots are Skia frames from Avalonia headless, committed under `docs/screenshots/`. They were not taken from an interactive window manager, and the tests do not compare pixels across machines. No PLC was connected, and no measured inflow was used.
