# Physical compound implementation

Implemented against the [research and acceptance plan](PHYSICAL-COMPOUND-PLAN.md).
The [content outline](CONTENT-OUTLINE.md) remains provisional. This is an operational
physical foundation and a usable inspection/building interface; it is not a completed
survival economy or chemical process simulator.

## State and boundaries

`CompoundModule` owns a bounded single-level grid, terrain/roof flags, stable fixture
definitions and instances, rectangular room zones, work bindings, and worker positions.
Movement uses shortest four-neighbor paths with fixed north/east/south/west tie ordering.
Doors and solid footprints affect every new step; inaccessible jobs retain their claims.
Workers can share a cell. Loading and work use explicit interaction cells.

The initial 26×18 compound contains a depot, workshop, clinic, generator, pump, power
cables, and water pipes. Four workers have distinct skills. Existing fixtures/network
fabric are inherited world objects with no authored salvage mass. The 200 kg tracked
baseline covers resource batches, cargo, and newly embedded construction/repair material.

The Haldane extension consists of optional `IWorkContext` and `ITransportDriver` contracts.
Haldane has no Scavenger or Avalonia dependency. Generic daily work and authored timed
hauls retain their previous behavior. Physical hauling advances every tick; project
effort still advances daily, with four ticks per day in this scenario.

`GameSession` provides a framework-independent command/step/save/load boundary.
`PhysicalReport` projects tiles, paths, workers, batches, stores, jobs, project progress,
fixtures, services, room status, and recent events. Avalonia owns display/input only.
Failed load attempts leave the live session intact. Save files contain authoritative
modules and executed player history; derived paths and service allocation are recomputed.

## Construction and repair

A valid designation reserves its layer/footprint and creates a bounded local staging
store. Construction generates deliveries from the selected source, reserving exact
stock, destination capacity, and one carrier per active haul. Each carrier walks to
the source, loads there, walks to the site, and unloads. Purity requirements select
suitable batches without blending them.

Once all local inputs are available, the job claims the builder and local batches.
The builder walks to the interaction cell, contributes skill-based effort, and installs
the fixture only after placement is rechecked. Input material becomes fixture-embedded
cargo records at completion, preserving mass and quality. New storage has an inventory
store; cable/pipe construction adds a utility node. Different geometry layers can coexist.
Generators and pumps must currently be recovered intact; they are repairable fixtures.

Repair uses the same delivery/staging/labor pipeline at the target's access cell.
Completion restores condition and embeds the replacement material. Damage currently
changes service effectiveness, not material mass or rubble. Floor construction also
provides a roof flag; room enclosure and roof completeness are derived from actual tiles.

Cancellation releases builder/local input claims and cancels unloaded deliveries.
Already-loaded deliveries finish at the retained staging store. Delivered material
stays available there; it is never refunded remotely or silently deleted. Cleanup,
reclaiming site stores, demolition, and loaded-haul return orders need later commands.

## Utility consequences

Enabled cardinal cable/pipe connections form independent power and water components.
Fixture ports attach at their origin cell. Generators contribute condition-scaled kW;
full consumer demands are allocated by priority and then stable id. Powered pumps
contribute condition-scaled L/h; water is allocated after power. Broken fixtures and
disconnected ports cannot operate. The pump, clinic, and workbench have priorities
0, 1, and 2 respectively, so capacity shortages have deterministic consequences.

These are capacity and connectivity models. They do not simulate fuel, batteries,
pressure, voltage, losses, water quality, heat, or chemical reactions. Seeded conduits
can be isolated/reconnected. Constructed conduits also stop connecting when broken.
Outage/restoration events support stopping time for decisions.

## Desktop integration

`Scavenger.Desktop` pins all Avalonia packages to 12.1.2. A custom drawn control uses
Avalonia `DrawingContext` for terrain, fixture/room labels, power/water overlays,
construction ghosts/progress, workers/cargo, intended paths, and selected-cell outlines.
The surrounding controls expose materials with quality/contamination, stores, skills,
work progress, logistics, construction, utility allocation, and events.

Clicking a build tool submits a persistent plan; right-click inspects. Room designations
use two corners. The inspector operates doors, repairs fixtures, cancels sites, and
isolates conduits. The four-person scenario uses Noor as builder and Ivo as carrier;
general team/order editing is a future interface expansion. Prototype damage controls
make outages reproducible. Accepted commands execute at the beginning of one tick.

The desktop loop and simulation clock are separate. Manual stepping, fast-forward,
and an optional timer invoke `GameSession`; rendering never advances world time.
Save writes a temporary file then replaces the slot. Rendering mode starts a real
Avalonia window on the headless platform with Skia drawing enabled and saves its frame.

## Acceptance audit

| Planned requirement | Implemented verification |
| --- | --- |
| Authoritative geometry | Bounds/levels, occupied/overlapping footprints, derived enclosure/roof, room overlap tests |
| Actual worker travel | Before-load positions, one-cell carried steps, source/destination arrival assertions |
| Layout affects hauling | Door interruption, retained loaded cargo, alternate door replanning, wall path changes |
| Local qualified work | No distant/unreachable/broken/unpowered effort; named specialty and presence regressions |
| Delivered construction inputs | Remote steel/timber trips, on-site builder, exact embedded mass; storage/overlay installation |
| Safe cancellation | Before loading, after loading, during on-site work, retained staging stock and released claims |
| Utility consequences | Power/water shortages, stable priority, branch isolation/reconnection, powered pump dependency |
| Persistence and replay | Mid-walk/build/repair continuation, cancelled-site continuation, byte-identical authoritative state and replay |
| Real UI integration | Actual window layout, mouse clicks on buttons/cells/doors, save/load recovery, Skia PNG frame |
| Engine compatibility | Full Haldane regressions and game tests; lower layers retain no game/UI references |

Verification commands are in the root README. Captured example frames live under the
ignored `artifacts/` directory and can be regenerated. Tests use real headless Avalonia
input and rendering rather than substituting a mock map or invoking click handlers directly.

The completed verification run passed 129 Haldane tests, 36 simulation tests, and two
Avalonia input/rendering tests (167 total). The root solution built with zero warnings
and errors. Physical and legacy CLI save/load runs passed; the physical run completed
the extension bench with 192 kg stored and 8 kg embedded, totaling 200 kg. Skia frames
were inspected at ticks 6 and 120. A native desktop startup also stayed running through
a five-second smoke test using the local display.

## Next consumers

Add a process-job model that claims equipment, utilities, qualified labor, input batches,
and receiving capacity, then emits explicit products/byproducts/losses. Those jobs can
reuse physical stores and access checks. Follow with salvage expeditions, machine parts,
needs, fuel/water consumption, storage policies, general team editing, demolition, and
data-driven content. Keep resource dimensions separate: mass, discrete parts, electrical
capacity, water throughput, and labor effort are not interchangeable units.
