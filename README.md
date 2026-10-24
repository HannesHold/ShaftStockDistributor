# ShaftStockDistributor

Prototype for planning and checking the assignment of returnable beverage crates
(Leergut / Mehrweg-Bierkisten) to shafts in a sorting warehouse.

A row of flow racks (Durchlaufregale) is divided into segments. Each segment has
levels (Ebenen) and each level has shafts (Schächte). Crates of one article enter
a shaft at the front and flow to the back. When a shaft holds a full pallet plus a
configurable offset, it is pulled (abgezogen): a shuttle moves to that shaft and the
contents run out onto a rear conveyor.

The tool does three things:

1. Decide how many shafts each article needs.
2. Place those shafts so two shuttles share the work, keeping fast movers near each shuttle's home.
3. Simulate arrivals and pulls, including a lock so the shuttles cannot occupy the same part of the aisle.

It does not drive live warehouse PLCs. It is a planning and what-if prototype.

## Projects

| Project | Role |
| --- | --- |
| `ShaftStockDistributor.Core` | Layout, articles, assignment, zones, JSON, validation, example data |
| `ShaftStockDistributor.Optimizer` | Shaft-count formula, constructive placement, simulated annealing |
| `ShaftStockDistributor.Simulation` | Seeded discrete-event simulation and KPI text |
| `ShaftStockDistributor.Cli` | `sample`, `optimize`, `simulate`, `compare`, `validate` |
| `ShaftStockDistributor.Web` | Blazor Server page for editing, optimizing, and simulating |
| `ShaftStockDistributor.Tests` | xUnit tests |

`ShaftStockDistributor.sln` targets `net10.0`. Build with warnings as errors:

```bash
dotnet build ShaftStockDistributor.sln -warnaserror
dotnet test ShaftStockDistributor.sln
```

## Domain model

- One segment is one flow rack. Default example: 12 segments, 5 levels, 9 shafts (540 shafts).
- A level has at most 5 levels per segment and at most 9 shafts per level. Larger values fail validation.
- A shaft holds a single article. An article may own many shafts. Empty shafts are allowed.
- Zones are thirds of the segment row, by segment index. The remainder stays in the middle:
  12 segments is 4 / 4 / 4, 10 is 3 / 4 / 3, 11 is 3 / 5 / 3.
  - First third: shuttle 1 only
  - Middle third: both shuttles
  - Last third: shuttle 2 only
- Articles are A / B / C movers (Dreher). A arrives often, C rarely.
- A shaft requests a pull when its crate count reaches `cratesPerPallet + offset`.
- JSON indexes are **one-based** (segment 1 is the left end, level 1 is the bottom). The UI uses the same numbers.

## Assumptions

These are defaults so the prototype runs without warehouse measurements. Change them in the JSON files or in the web page. Values shipped by `sample` are marked `"exampleData": true`.

| Topic | Assumption |
| --- | --- |
| Shaft capacity | Depends on crate type. Per-article `shaftCapacity`, else the layout default (48). A per-shaft override replaces both. |
| Pull quantity | The whole shaft content leaves when the shuttle finishes the transfer, not a counted pallet layer. Crates that arrived while the shaft was waiting leave with that pull. |
| Offset | Extra crates above a full pallet required before the pull is requested. It is not left behind. |
| Inflow | Crates per hour, Poisson arrivals. If `cratesPerHour` is omitted, that article gets an equal split of `totalCratesPerHour * classShare` among articles of the same class that also omit a rate. Explicit rates do not consume the pool. Example shares: A 70%, B 20%, C 10%. |
| Shaft count | See below. `expectedPullWaitSeconds` (default 300) is only a planning input. The simulation measures the real wait. |
| Geometry | Shaft pitch 0.45 m, level height 0.50 m. Horizontal position is `(segment * shaftsPerLevel + shaft) * pitch`, so shafts along a level are in one line. |
| Shuttle motion | Axes move together. Travel time is the slower axis (Chebyshev). Each axis uses a symmetric trapezoidal speed profile. Defaults: horizontal 1.5 m/s and 0.5 m/s², vertical 0.6 m/s and 0.3 m/s². Transfer is a fixed 30 s, not proportional to crate count. |
| Homes | Shuttle 1 starts at the left end (segment 1, level 1, first shaft). Shuttle 2 starts at the right end, level 1, last shaft. The cost function treats a pull as a one-way trip from that home. The simulation travels from the shuttle's real position and returns home when idle. |
| Collision | Horizontal only. Shuttles can meet on different levels because they share the rear of the racks. The shared zone plus `safetyDistanceSegments` (default 1) is a keep-out that only one shuttle may enter. A safety distance of 1 also forbids adjacent segments. Each exclusive zone must be wider than the safety distance so a shuttle can wait outside the keep-out. |
| Routing | Crates of an article are dealt round-robin across its shafts that are not already waiting for a pull, so the others keep receiving. This matches the even split assumed by the shaft-count formula. If every shaft is waiting, crates continue round-robin onto shafts that still have free space. Shafts that are transferring, or already full, are skipped. If none can take it, the crate is rejected. |
| Shared pulls | Dispatched to the shuttle with the smaller estimated ready time (backlog plus travel). Exclusive zones stay with their shuttle. |
| Optimizer | Pins are fixed. By default every unpinned shaft is rebuilt. "Keep current shafts" only fills missing demand and then searches. Annealing swaps, relocates, and will not move a pin. |
| Example crate mix | Names, inflows, pallet sizes, and capacities in `sample` are invented so the screen is not empty. They are not a survey of a real empties hall. |

### How many shafts

Exposure is the expected pull wait plus the transfer duration.

- If the shaft has spare room above the pull threshold (`buffer = capacity - pallet - offset`), the article needs at least `inflow * exposure / buffer` shafts so a waiting shaft does not overflow, and at least `inflow * exposure / threshold` shafts so fills are staggered.
- The result is the ceiling of the larger of those two terms, and at least 1.
- Zero inflow or zero exposure yields 1 shaft, because validation still requires every article to have a shaft.
- If `buffer <= 0`, the formula uses the whole capacity instead. Validation still reports that the shaft cannot hold a pallet plus the offset.

### Cost function

Local search minimizes:

`travelWeight * travel + imbalanceWeight * |load1 - load2| + middleWeight * middle + penalties`

- `travel` sums, over shafts, pulls per hour times the one-way home-to-shaft time.
- Shared shafts contribute the trip from the nearer home, and half of that pull-time to each shuttle's load.
- `middle` is the pull-time of shared shafts. It stands in for conflict and travel into the middle.
- A hard violation (unknown article, no shaft, capacity below the threshold) costs `violation` (default 1e6).
- Each shaft short of the calculated demand costs `shortfall` (default 10000).
- Default imbalance weight is 2.5. A packing that leaves one shuttle idle can have very short trips and would otherwise look cheaper than a balanced plan.

The constructive heuristic places A articles near a shuttle home in that shuttle's exclusive zone, split by pull rate. An A article with two or more shafts keeps one shaft in the shared middle. B and C articles prefer the middle, because they are pulled rarely. Simulated annealing then swaps and moves unpinned shafts.

## CLI

From the repository root:

```bash
dotnet run --project src/ShaftStockDistributor.Cli -- sample --out samples

dotnet run --project src/ShaftStockDistributor.Cli -- optimize \
  --layout samples/layout.json \
  --articles samples/articles.json \
  --parameters samples/parameters.json \
  --out samples/assignment.json \
  --naive-out samples/naive-assignment.json

dotnet run --project src/ShaftStockDistributor.Cli -- simulate \
  --layout samples/layout.json \
  --articles samples/articles.json \
  --assignment samples/assignment.json \
  --hours 8 --seed 1

dotnet run --project src/ShaftStockDistributor.Cli -- compare \
  --layout samples/layout.json \
  --articles samples/articles.json \
  --first samples/assignment.json \
  --second samples/naive-assignment.json \
  --hours 8 --seed 1 \
  --first-name optimized --second-name naive
```

`validate` checks a layout, an article file, and an optional assignment. Exit codes: 0 ok, 1 usage, 2 validation error, 3 bad file, 4 the simulation recorded a shuttle collision.

Pinned shafts already present in `--current assignment.json` are kept. Add `--keep-current` to start the search from the other placements instead of rebuilding them.

## Web UI

```bash
dotnet run --project src/ShaftStockDistributor.Web --urls http://localhost:5088
```

Open `http://localhost:5088`. The page loads the example warehouse.

- Edit ABC class, inflow, pallet size, offset, and shaft capacity. The shaft column shows assigned / calculated demand.
- Select an article and click a shaft to assign it, or drag the article onto a shaft. Drag a shaft onto another to move or swap. Pin a shaft in the inspector; optimize will not move it.
- Run the optimizer or the simulation. The rack can switch to a pull-count heatmap.
- Export `assignment.json`, or import layout, articles, assignment, and parameters.
- "Apply size" rebuilds the rack (at most 5 levels and 9 shafts) and clears the assignment. Homes stay at the two outer ends, level 1.

The page keeps one plan in memory for the browser circuit. Nothing is written to disk until you export.

## File formats

All files are JSON, camelCase, `formatVersion` 1. Indexes are one-based. `exampleData` and `notes` are informational.

`layout.json` — `segmentCount`, `levelsPerSegment`, `shaftsPerLevel`, `defaultShaftCapacity`, `shaftPitchMeters`, `levelHeightMeters`, `safetyDistanceSegments`, `kinematics` (speeds, accelerations, `transferDurationSeconds`), `shuttle1Home` / `shuttle2Home` (`segment`, `level`, `shaft`), optional `shaftCapacityOverrides`.

`articles.json` — `abcDefaults` (`totalCratesPerHour`, `shareA`, `shareB`, `shareC`), `defaultCratesPerPallet`, `defaultOffset`, and `articles` (`id`, `name`, `abcClass` of `A`/`B`/`C`, optional `cratesPerHour`, `cratesPerPallet`, `offset`, `shaftCapacity`).

`assignment.json` — `shafts`: `segment`, `level`, `shaft`, `articleId`, `pinned`.

`parameters.json` — `expectedPullWaitSeconds`, `weights` (`travel`, `imbalance`, `middleTraffic`, `violation`, `shortfall`), `annealing` (`seed`, `iterations`, `initialTemperature`, `cooling`).

Omitted homes mean shuttle 1 at segment 1 / level 1 / shaft 1 and shuttle 2 at the opposite end.

## Tests

`dotnet test` covers zone thirds, the shaft-count formula, travel time, JSON round-trip, validation, optimizer cost against the left-to-right baseline, pinned shafts, the A/B/C placement rules, simulation determinism for a seed, exclusive-zone dispatch, and a trace assertion that shuttle segment ranges never come within the safety distance.

## Next steps and open questions

- Replace the example inflows, pallet sizes, and shaft capacities with measured data. Crate dimensions change how many crates fit in a shaft and on a pallet.
- Confirm shuttle speeds, accelerations, and whether both axes really move at once. The numbers above are placeholders.
- Confirm the handover / home position. The default is the outer end of the aisle at level 1, and the shuttle returns there when idle. A real system may hand over at a different segment or stay parked at the last pull.
- Confirm what a pull removes. This prototype empties the shaft. If the line must discharge exactly one pallet and leave the offset behind, the fill and overflow numbers change.
- Decide whether the shared-zone rule should be a whole-zone lock (what this prototype does) or a moving safety window. The lock is deadlock-free and conservative; it can make one shuttle wait while the other works anywhere in the middle. There is no fairness timeout.
- Decide whether live pull control is in scope. This prototype plans an assignment and replays it. It does not choose the next pull for a running PLC, and it does not talk to sensors.
- Calibrate ABC shares and the expected pull wait against a real shift before trusting the shaft counts.
- The surrogate cost uses trips from home. The simulation's job-to-job travel is the number to compare when two plans are close.
