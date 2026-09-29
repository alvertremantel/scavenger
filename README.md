# Compound survival experiment

A playable systems prototype for a compound-management game about scavenging, chemistry,
repair, logistics, and rebuilding industrial capability after an undecided apocalypse.
The prototype combines operational tables with a physical 2D compound in Avalonia.

[Working content outline](docs/CONTENT-OUTLINE.md) · [Engine foundations](docs/ENGINE-FOUNDATIONS.md) ·
[Physical implementation plan](docs/PHYSICAL-COMPOUND-PLAN.md) · [Implementation and verification](docs/PHYSICAL-COMPOUND.md)

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
and is not yet packaged for distribution.

## Run

Requires the .NET 10 SDK.

```sh
dotnet run --project src/Scavenger.Cli -- --roundtrip
dotnet run --project src/Scavenger.Cli -- --json
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

In the desktop app, use **Demo orders** to begin that supply/construction scenario, or
choose a map tool and click a site. Room zones use two interior corners. Right-click
inspects without placing. The inspector supports door operation, repair, cancellation,
and conduit isolation; **Damage** is a prototype testing control. Accepted actions advance
one tick. **Step**, **+12 ticks**, **Until decision**, and **Run** control time explicitly.
Save/load uses `saves/compound.json`. The screenshot mode uses actual Avalonia controls
and Skia rendering without requiring a display server.

Current scope includes named skills, quality-preserving material batches, stock/capacity
claims, actual walking and cargo movement, bounded placement, rooms/roofs, construction,
repair, power/water connectivity and allocation, and deterministic continuation/replay.
Chemistry execution, scavenging expeditions, needs, multiple floors, vehicles, demolition,
and crowd avoidance remain future systems. Building costs are experimental gameplay data.
