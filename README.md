# Compound survival experiment

A playable systems prototype for a compound-management game about scavenging, chemistry,
repair, logistics, and rebuilding industrial capability after an undecided apocalypse.
The prototype combines operational tables with a physical 2D compound in Avalonia.

[Skills and learning](docs/SKILLS-AND-LEARNING.md) · [Headless play guide](docs/CLI-PLAY.md) · [Play guide](docs/MAP-CENTERED-UI.md) · [Map-centered UI plan](docs/MAP-CENTERED-UI-PLAN.md) · [Baseline playability](docs/BASIC-PLAYABILITY.md) · [Playability plan](docs/BASIC-PLAYABILITY-PLAN.md) ·
[Working content outline](docs/CONTENT-OUTLINE.md) · [Engine foundations](docs/ENGINE-FOUNDATIONS.md) ·
[Physical implementation plan](docs/PHYSICAL-COMPOUND-PLAN.md) · [Physical implementation and verification](docs/PHYSICAL-COMPOUND.md) ·
[Production/computing plan](docs/PRODUCTION-AND-COMPUTING-PLAN.md) ·
[Industrial walkthrough and audit](docs/PRODUCTION-AND-COMPUTING.md) ·
[Scavenging plan](docs/SCAVENGING-PLAN.md) · [Expeditions and location yields](docs/SCAVENGING.md) ·
[World-generation plan](docs/WORLD-GENERATION-PLAN.md) · [Original generated worlds](docs/WORLD-GENERATION.md) ·
[Expanded geography plan](docs/EXPANDED-GEOGRAPHY-PLAN.md) · [Fifty-square-mile worlds and terrain](docs/EXPANDED-GEOGRAPHY.md)

## Layout

- `docs/`: provisional design and implementation notes.
- `src/Scavenger.Sim/`: physical world, construction, utilities, scenario, commands, and reports.
- `src/Scavenger.Cli/`: interactive headless gameplay, command files, scripted demonstrations and JSON reports.
- `src/Scavenger.Desktop/`: Avalonia operator console, live dashboard, persistent workspaces, maps, searchable registers and commands.
- `tests/`: simulation integration/replay and real Avalonia input/rendering tests.
- `haldane/haldane/`: existing independent Haldane engine checkout. It is deliberately
  ignored by this repository; engine changes and tests live in that checkout's own Git repository.

The project references that local engine checkout directly. It must be available at
`haldane/haldane/`. The compatible engine commit is recorded in
[haldane-revision.txt](haldane-revision.txt); the engine remains a separate repository
and is not yet packaged for distribution. The pinned engine revision includes the
`Haldane.SupplyChain/Conversions.cs` primitive required by the industrial slice.
Scavenging, expanded geography and the operator console are merged into `main`.
The verified playability baseline is committed as `b092ced` on `feature/basic-playability`.
The four-workspace UI is on `feature/map-centered-management`; the independent engine checkout remains
on `feature/production-and-scavenged-computing` at the pinned revision.

## Run

Requires the .NET 10 SDK.

```sh
dotnet run --project src/Scavenger.Cli -- --play
dotnet run --project src/Scavenger.Cli -- --script docs/playthroughs/skills.cli
dotnet run --project src/Scavenger.Cli -- --roundtrip
dotnet run --project src/Scavenger.Cli -- --json
dotnet run --project src/Scavenger.Cli -- --world --seed 42 --json --map artifacts/world-42.svg
dotnet run --project src/Scavenger.Cli -- --world --seed 42 --walkthrough --roundtrip --ticks 400
dotnet run --project src/Scavenger.Cli -- --scavenging --roundtrip --ticks 400
dotnet run --project src/Scavenger.Cli -- --industry --roundtrip --ticks 2400
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/region.png --ticks 0 --view region
dotnet test Scavenger.slnx
dotnet test haldane/haldane/Haldane.slnx
```

The playable CLI starts with time paused and runs no demonstration commands. Use `help`,
`skills`, `find`, `site`, `expedition`, `study`, `practice`, `produce`, `until last`, and
`save` / `load` to manage the same simulation as Desktop. `--script path` executes a
command file and stops on rejected actions; `--play --load path` resumes a save.
See [the CLI guide](docs/CLI-PLAY.md) for syntax and a tested progression.

The CLI demonstration assigns a mechanic to pump inspection, delivers recovered HDPE,
and builds an extension bench using physically delivered steel and timber. Closing the
workshop door interrupts hauling; reopening it resumes travel. `--roundtrip` saves and
reloads while blocked. `--json` emits the full physical report; `--ticks N` controls the
run length. `--legacy` runs the earlier timed transport fixture.

The desktop starts a seeded **49.83-square-mile region** with twenty authored location types,
compact, avenue-led or twin-center town layouts, outlying suburbs, connected roads and varied finite sites, Mira ill and modest starting stores.
Under **Region**, enter a seed to generate a fresh world, click a site to inspect its stock
and road route, then use **Plan expedition here**. Click any cell for mixed terrain
percentages, elevation, moisture, flood susceptibility and roughness; switch map layers
and drag to pan or wheel to zoom around the cursor. Zoom-out and Fit controls are also
available. Layout can be selected or chosen by the seed. **Dashboard** offers guided supply plans.
In the **Region** sidebar, select a site, team and cargo; dispatch, respond to field incidents, recall, and receive actual cargo. Recover
pharmacy supplies and hardware sorbent, then refine/formulate the fictional treatment
at the clinic. See [the expedition walkthrough](docs/SCAVENGING.md) for quantities,
location rationale, risk/quality rules and current abstractions.

The industrial systems remain available: use **Recovery orders** in **Compound → Production**
to wash, dry, shred and mould recovered HDPE into repair clips. Diagnose individual computer
parts, repair the cooler with those clips, assemble a PC, copy recovered offline software,
study the textbook, and build telemetry/automation. Finite diesel, batteries, electrical demand,
memory, archive integrity and physical delivery constrain that progression. See the
[step-by-step walkthrough](docs/PRODUCTION-AND-COMPUTING.md) for the complete 4B/3060 path.
`--industry` runs a deterministic command-driven demonstration of this path in the CLI.
Model training/evaluation are explicitly simulated game workloads; no actual ML code executes.

The desktop has four workspaces: **Dashboard**, **Region**, **Compound** and
**Registers**. Ctrl+1–4 selects one; Ctrl+Tab / Ctrl+Shift+Tab cycles them. Region
keeps the map visible while inspecting stock, finding supplies, planning expeditions
or managing teams. Compound keeps its map visible while building, treating patients,
managing production/power/transfers, servicing computers or reviewing tasks. Sidebars
collapse without losing plans. Registers retains searchable detailed tables. See the
[current play guide](docs/MAP-CENTERED-UI.md) for the complete loop.

In **Compound → Crew**, each member has sixteen independent skills. Recover matching
manuals through **Find next-level material**, study them, then earn practical XP by
working or practising (optionally with a qualified mentor). Books are reusable physical
items, and learning competes for labor and fixture access. Both gates are required for
each of ten levels. See [the progression design](docs/SKILLS-AND-LEARNING.md).

In **Compound**, choose a map tool and click a site, or
inspect an existing fixture. Room zones use two interior corners. Right-click
inspects without placing. The inspector supports door operation, repair, cancellation,
and power/water/data conduit isolation. Accepted orders advance
one tick; navigation and supply suggestions do not. **Step**, **+12 ticks**, **+1 day**, **Until event**, and **Run** control time explicitly.
Normal time advances stop at pending field decisions, new warnings and completions.
Save/load uses `saves/compound.json`. The screenshot mode uses actual Avalonia controls
and Skia rendering without requiring a display server. For example:

```sh
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/ui-home.png --view home --ticks 0
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/ui-region.png --view region --seed 42 --width 1050 --height 780
```

Screenshot views include home, compound, crew, region, region-setup, scavenging, clinic,
yields, production, recipes, hardware, archives, compute, energy, transfers and
all six register views. `--ticks` defaults to zero so captures do not silently
start demonstration orders.

Current scope includes regional expeditions with decay/rivals/exposure, finite object
yields, recovered parts/media, fictional medicine and clinic care, skills, quality-bearing batches, atomic mass-balanced conversions,
standing stock orders, physical logistics, construction/repair, individually owned computer
parts, offline assets and knowledge, CPU/RAM/VRAM/storage claims, telemetry and eligible
operator-free processing, finite electrical energy/fuel, and deterministic saves/replay.
Chemistry currently covers HDPE recovery and a deliberately fictional purity/grade-gated
clinic chain; general reaction/species simulation remains future work.
Population needs, scouting/fog of war, road disruption, faction combat, multiple floors, vehicles, detailed thermal/electrical physics,
board-level repair, actual ML execution and crowd avoidance remain future systems.
Costs, yields, work rates and workload profiles are experimental gameplay data.
