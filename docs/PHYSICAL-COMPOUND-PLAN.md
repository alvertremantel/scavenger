# Physical compound — researched implementation plan

Status: approved direction for this experiment by the user's request to implement;
details are working decisions. Written before the implementation below. The goal is
an operational backend compound with basic Avalonia integration, not a painted diagram.

## Evidence and framework choice

The current simulation owns located batches and tick-duration hauling, but has no
walkability, worker positions, construction sites, equipment placement, or utilities.
Haldane's world/module and system contracts allow game-owned physical state without
adding a dependency on Scavenger to the engine. Work and hauling need small extension
points so physical travel can constrain the existing labor and inventory rules.

Avalonia supports a custom `Control.Render(DrawingContext)` implementation, styled
properties, and invalidation. This is sufficient for a tile map and overlays; direct
Skia API leases add complexity without helping this first map. Use ordinary Avalonia
controls around the map for actions, selection details, and tabular reports.
[Official custom-drawn control documentation](https://docs.avaloniaui.net/docs/custom-controls/custom-drawn-controls).

Avalonia's headless platform can run actual controls and simulated mouse events, and
can produce PNG frames through Skia when `UseHeadlessDrawing` is false. This gives us
runtime and visual evidence even when desktop automation is unavailable.
[Official headless platform documentation](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform).

Use Avalonia 12.1.2 consistently, which is already present in this machine's package
cache. The upstream releases include 12.1.3; choosing the locally available patch is
a reproducibility decision, not a claim that it is the latest version.
[Official releases](https://github.com/AvaloniaUI/Avalonia/releases).

For a modest uniform-cost, four-neighbor compound grid, breadth-first search finds
shortest paths with simple deterministic tie breaking. A* becomes useful when larger
maps or terrain costs justify it. Do not introduce diagonal corner cutting or an
unverified heuristic. Recompute from a worker's current location when topology changes.
[Algorithm author's explanation and implementation](https://www.redblobgames.com/pathfinding/a-star/implementation.html).

## 1. World and topology

Implement a bounded, single-level tile grid. Cells describe ground/floor, roof, walls,
and open/closed doors. Reject unsupported levels and out-of-bounds coordinates. Stable
location ids remain separate from geometry. A topology revision changes whenever
passability changes; persistence preserves authoritative state, not a cached path.

Track each roster member's physical position. Solid building footprints block movement;
stockpile zones and utility overlays can be traversed. Reject building over an occupied
cell, another solid footprint, or an existing site's footprint. Work and loading use
explicit walkable interaction cells next to equipment, rather than standing inside it.

Implement named rectangular room designations with purpose and a derived enclosure/roof
report. Boundaries must explain whether a room is enclosed. Doors act as room boundaries
regardless of their current travel state. Room labels do not magically create walls.

## 2. Navigation and physical hauling

Expose shortest paths and single-cell travel from authoritative topology. Fixed neighbor
ordering makes equal-length choices deterministic. Paths include the starting cell;
workers move at most one cell per tick in this model. Occupancy is not a crowd solver:
workers may share a walkable cell. This avoids deadlock masquerading as logistics while
still enforcing physical barriers and construction footprint safety.

Add a transport-driver seam to Haldane.SupplyChain. Legacy timed routes retain their
behavior. Spatial routes require a driver: approach the origin, load only when there,
carry the cargo to the destination, then unload. The game implements this driver using
its grid. No fallback may deliver spatial cargo by counting ticks when a driver is absent.

Door closure and new walls can interrupt a route. Replan from the worker's current cell;
use an alternate path if available, otherwise keep the worker/cargo/stock/space claims
and report a block. Elapsed movement is history, not a guarantee that an outdated path
length still represents remaining travel. Reports distinguish movement from duration.

## 3. Workstations and work access

Persist equipment definitions, footprints, interaction cells, capabilities, condition,
and constructed materials. Physical project bindings name a station and capability.
Workers walk there before contributing. Skill, attendance, labor reservations, station
condition, access, and utility allocation all gate work. Extend the reusable WorkSystem
with an optional eligibility context rather than letting generic projects ignore physical
rules. The default game uses this context.

Station condition can be changed by a command for testing damage and outages. Repair
uses the same material delivery, labor, and persistence pipeline as construction.

## 4. Construction and material conservation

Provide build definitions for floor, wall, door, stockpile, workbench, electrical conduit,
and water pipe. Existing generator and pump fixtures establish a useful initial compound;
their presence does not imply that bulk metal can manufacture those machines.

A construction designation reserves a footprint and creates a local staging store with
an interaction cell. The job tracks builder, delivery worker, source store, requirements,
deliveries, progress, and blockage. The backend dispatches physical delivery jobs from
the selected source in bounded trips. Split source batches only through inventory's
reservation API. Competing jobs cannot claim the same stock or worker twice.

Once inputs are local, reserve those batches and the qualified builder. Walk to the
site before accruing construction effort. At completion, consume the reserved inputs
into the structure's embedded-material records and install the structure or overlay.
Conservation includes store stock, haul cargo, and embedded construction materials.

Cancellation releases the footprint and builder and cancels unloaded deliveries. Loaded
deliveries finish into the retained staging store; materials never vanish. Reserved local
inputs are released. Completed/cancelled ids stay stable. A repair restores condition
and retains its newly embedded material history without overlapping a new structure.

## 5. Utility networks

Persist power and water conduit cells as overlays. Connected components determine which
ports are connected. Structure definitions provide supply and demand; condition affects
available supply. Allocate finite capacity deterministically to consumers, record unmet
demand, and derive operation status. A powered pump can provide water to its connected
water network; a clinic fixture can require both networks.

This is a capacity/access model, not voltage, hydraulic pressure, fuel chemistry, or a
storage-tank solver. Keep flow units explicit (power kW and water L/h) and separate from
material inventories. Disconnected ports and insufficient supply must appear in reports
and affect workstation work. Utility construction and removal update connectivity.

## 6. Commands, state, and interfaces

All player changes go through persistent commands: designate/cancel build or repair,
toggle door, designate room, change fixture condition, assign work, and dispatch hauling.
Autonomous systems can generate delivery jobs deterministically as part of a tick.
Save definitions, tiles, positions, equipment, rooms, construction jobs, and networks;
validate cross-module references and ownership before continuing a loaded world.

Add immutable projections for the map, structures, room state, construction, work access,
utilities, inventories, and workers. A game session facade exposes submit/advance,
save/load, and reports. Avalonia consumes the same facade used in integration tests.
Simulation rules must not live in click handlers or a rendering control.

## 7. Desktop integration

Create Scavenger.Desktop with Avalonia, a dark management interface, and a custom map
control. Show floors/walls/doors, room zones, stores, workstations, conduit overlays,
workers, cargo, and construction ghosts. Provide inspection and build tools, selected
cell details, door toggles, repair/cancel actions, workforce/jobs/stock/utility reports,
step/fast-forward controls, and a save slot. Expose validation failures in the UI.

Support headless screenshot output of the actual application window. Exercise map input
and action buttons with Avalonia's headless event helpers, then inspect a rendered frame.
Keep desktop rendering independent of ticks; only explicit stepping/optional run controls
advance simulation. The CLI should also exercise the physical scenario.

## 8. Acceptance evidence

| Requirement | Evidence required before completion |
| --- | --- |
| Geometry is authoritative | Bounds, footprint, door, roof/enclosure, and overlap tests |
| Workers physically travel | Position assertions before loading, during transit, and on delivery |
| Layout affects logistics | Door interruption, alternate-route replanning, and no-teleport tests |
| Work is local and gated | No effort at a distant, damaged, unpowered, or unreachable station |
| Building consumes delivered goods | Remote input transfer, on-site labor, installation, and embedded-mass tests |
| Cancellation preserves resources | Unloaded and in-transit cancellation tests, retained staging material |
| Utilities have consequences | Connectivity, capacity shortage, pump dependency, and outage tests |
| State persists deterministically | Mid-walk/mid-build/mid-repair save/load continuation and command replay |
| UI runs against the backend | Actual Avalonia window/layout, simulated interaction, and rendered PNG |
| Existing engine stays sound | Haldane and game regression suites; no lower-layer game/UI references |

## 9. Working limits and follow-on work

Do not confuse a broad physical foundation with a complete game. Multiple floors,
vehicle logistics, collision avoidance, thermal/contamination propagation, process
chemistry, needs/morale, and expedition maps need later content and consumers. Keep
the single-level structures, ports, batches, and task ids extensible without pretending
those systems already exist. Record implementation discoveries and verification evidence
alongside this plan; revise a decision when tests or interaction show it is wrong.
