# Operator console UI

The game surface is the workstation a compound operator uses. Flat charcoal layers,
sharp one-pixel outlines, restrained sage and amber accents, and small uppercase
labels distinguish navigation, commands and records without decorative depth.
Inter is the primary typeface. Text remains legible against quiet backgrounds;
large numeric figures are reserved for the overview.

## Information architecture

Six persistent workspaces remain open as document tabs and are also reachable
from the navigation rail. Ctrl+1 through Ctrl+6 selects a workspace; Ctrl+Tab
and Ctrl+Shift+Tab cycle through them. Switching views preserves selections and
never advances the simulation.

- **Overview** is the in-game landing screen. Live attention, duty, inventory,
  energy and computing projections link into their detailed workspaces.
- **Region** gives the survey the full workspace, with separate inspection and
  legend tabs. Generation lives in a separate World setup tab and states
  that it replaces the running world.
- **Compound** provides construction tools, utility overlays and cell inspection.
- **Expeditions** separates the dispatch planner, clinic and site inventory.
- **Industry** separates production, individual hardware, archives, compute,
  energy, transfers and recipe specifications. Machine and technician context stays visible
  in hardware, archives and compute; production exposes its live four-stage stock chain.
- **Registers** contains materials, stores, workers, jobs, services and events, with a persistent
  case-insensitive filter across every column.

Time controls, save/load, operational state and the latest command result stay
visible across the workspaces. Direct rail links and contextual links connect
clinic, power, hardware, transfers and inventory without nesting those screens
inside the compound map. The native tab abstractions retain keyboard focus and
selection behavior.

## Implementation and validation

`ConsoleUi` supplies frames, section labels, form fields, table rows and empty
states; application styles give Fluent controls the same square, dark treatment.
`HomePanel` only projects existing world state, and navigation remains in the
window. UI changes must not consume random streams, submit commands or advance
time on view changes.

Visual review uses real Avalonia/Skia screenshots, including the minimum supported
1050 × 780 window. Dashboard, map, forms, tables and developed computing states
must each be inspected. UI interaction tests exercise cross-navigation, input,
command results and save/load, alongside existing end-to-end workflows.

## Review record

The implementation was refined against real Skia frames: fresh dashboard at both
1440 × 1000 and 1050 × 780, regional survey and world setup, compound plan,
expedition planner, clinic, production, transfers and inventory. Developed hardware,
archive, compute, power and dashboard states were captured from the UI test that
recovers components, assembles the machine, installs dependencies and deploys
validated offline automation.

The review corrected map overflow at minimum size, pinned the map-to-expedition
link, moved world configuration off the survey, removed irrelevant machine context
from production and energy, separated recipe specifications from the process board,
muted default filled buttons and form borders, and promoted clinic health records
and crew availability into explicit data views. Window-level shortcuts use the
input tunnel so nested tabs cannot intercept workspace switching.

Validation commands:

```sh
dotnet build Scavenger.slnx --no-restore -m:1 -v quiet
dotnet test Scavenger.slnx --no-build --no-restore -m:1 -v minimal
SCAVENGER_UI_ARTIFACT_DIR=artifacts/ui-audit dotnet test tests/Scavenger.Desktop.Tests --no-build --no-restore -m:1 -v minimal
```

The optional artifact directory captures developed screens from the tested UI
workflow; it does not alter the simulation. Screenshots are generated artifacts,
not checked-in binary assets. Rendering is headless Avalonia with actual Skia text,
controls and pointer/keyboard input. Native operating-system chrome and high-DPI
monitor behavior require a display session and are outside this headless review.

Final checks: build completed with zero warnings and zero errors; all 123 simulation
and 10 desktop tests passed. The desktop suite was run again after the final clinic,
crew availability, attention-queue and selection-color refinements. The simulation
and nested Haldane checkout were unchanged by this UI pass.
