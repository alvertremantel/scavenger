# Compound survival experiment

A playable systems prototype for a compound-management game about scavenging, chemistry,
repair, logistics, and rebuilding industrial capability after an undecided apocalypse.
The prototype combines operational tables with a physical 2D compound in Avalonia.

[Working content outline](docs/CONTENT-OUTLINE.md) · [Engine foundations](docs/ENGINE-FOUNDATIONS.md) ·
[Physical implementation plan](docs/PHYSICAL-COMPOUND-PLAN.md) · [Physical implementation and verification](docs/PHYSICAL-COMPOUND.md) ·
[Production/computing plan](docs/PRODUCTION-AND-COMPUTING-PLAN.md) ·
[Industrial walkthrough and audit](docs/PRODUCTION-AND-COMPUTING.md)

## Layout

- `docs/`: provisional design and implementation notes.
- `src/Scavenger.Sim/`: physical world, construction, utilities, scenario, commands, and reports.
- `src/Scavenger.Cli/`: scripted simulation, save/load demonstration, and JSON reports.
- `src/Scavenger.Desktop/`: Avalonia map, inspector, operational tables, and save/load controls.
- `tests/`: simulation integration/replay and real Avalonia input/rendering tests.
- `haldane/haldane/`: existing independent Haldane engine checkout. It is deliberately
  ignored by this repository; engine changes and tests live in that checkout's own Git repository.

The project references that local engine checkout directly. It must be available at
`haldane/haldane/`. The compatible engine commit is recorded in
[haldane-revision.txt](haldane-revision.txt); the engine remains a separate repository
and is not yet packaged for distribution. The pinned engine revision includes the
`Haldane.SupplyChain/Conversions.cs` primitive required by the industrial slice.
Both checkouts are on `feature/production-and-scavenged-computing`.

## Run

Requires the .NET 10 SDK.

```sh
dotnet run --project src/Scavenger.Cli -- --roundtrip
dotnet run --project src/Scavenger.Cli -- --json
dotnet run --project src/Scavenger.Cli -- --industry --roundtrip --ticks 2400
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/compound.png --ticks 6
dotnet test Scavenger.slnx
dotnet test haldane/haldane/Haldane.slnx
```

The CLI demonstration assigns a mechanic to pump inspection, delivers recovered HDPE,
and builds an extension bench using physically delivered steel and timber. Closing the
workshop door interrupts hauling; reopening it resumes travel. `--roundtrip` saves and
reloads while blocked. `--json` emits the full physical report; `--ticks N` controls the
run length. `--legacy` runs the earlier timed transport fixture.

The desktop now starts the industrial scenario: use **Recovery orders** under **Industry**
to wash, dry, shred and mould recovered HDPE into repair clips. Diagnose individual computer
parts, repair the cooler with those clips, assemble a PC, copy recovered offline software,
study the textbook, and build telemetry/automation. Finite diesel, batteries, electrical demand,
memory, archive integrity and physical delivery constrain that progression. See the
[step-by-step walkthrough](docs/PRODUCTION-AND-COMPUTING.md) for the complete 4B/3060 path.
`--industry` runs a deterministic command-driven demonstration of this path in the CLI.
Model training/evaluation are explicitly simulated game workloads; no actual ML code executes.

In the desktop app, choose a map tool and click a site, or
inspect an existing fixture. Room zones use two interior corners. Right-click
inspects without placing. The inspector supports door operation, repair, cancellation,
and power/water/data conduit isolation; **Damage** is a prototype testing control. Accepted actions advance
one tick. **Step**, **+12 ticks**, **+1 day**, **Until decision**, and **Run** control time explicitly.
Save/load uses `saves/compound.json`. The screenshot mode uses actual Avalonia controls
and Skia rendering without requiring a display server.

Current scope includes skills, quality-bearing batches, atomic mass-balanced conversions,
standing stock orders, physical logistics, construction/repair, individually owned computer
parts, offline assets and knowledge, CPU/RAM/VRAM/storage claims, telemetry and eligible
operator-free processing, finite electrical energy/fuel, and deterministic saves/replay.
Chemistry is a provisional HDPE recovery chain, not general reaction/species simulation.
Expeditions, population needs, multiple floors, vehicles, detailed thermal/electrical physics,
board-level repair, actual ML execution and crowd avoidance remain future systems.
Costs, yields, work rates and workload profiles are experimental gameplay data.
