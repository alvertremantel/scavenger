# Map-centered management UI

Baseline: `b092ced`, the verified multi-day playability loop. Simulation rules remain
unchanged; this pass changes how the player discovers and manages those actions.

## Workspace model

Exactly four global destinations: Dashboard, Region, Compound, Registers. One
horizontal navigation row, with a persistent clock, time controls, save/load and
status message. No separate Expeditions or Industry workspace, secondary workspace
tabs, workstation rail, or duplicated direct-access navigation.

Region keeps the regional map visible while the player inspects a site, searches
known stock, plans cargo/crew, manages teams, or configures a new world. Inspection
and planning share the selected destination and route. Supply suggestions populate
reviewable plans; dispatch and incident responses live in the sidebar. The search
is scouting existing revealed intelligence, not a new fog-of-war mechanic.

Compound keeps the physical map visible during building, clinic care, production,
power management, transfers, computer work, crew inspection and task management.
Selecting a fixture opens its relevant operations. Building and repair remain real
physical orders. All recipe, hardware, archive and automation controls remain
available through compact forms, with readable job cards instead of compressed tables.

Sidebars use a common layout: context selector, scrollable records/forms, and visible
primary actions. They can be collapsed for map exploration and reopened without
losing plans, selections, zoom or queued work. Minimum window: 1050 × 780. Both maps
should occupy most of their workspace; navigation must not consume a tall page intro.

## Acceptance

- Four destinations are reachable by mouse and Ctrl+1–4 / Ctrl+Tab.
- Both maps stay visible while all their management contexts are used.
- Site clicks, stock search and supply plans agree on the inspected/planned route.
- Field decisions are visible and actionable; dispatch enters team management.
- Clinic, manufacturing, power, transfer, hardware, archive and automation operations
  remain accessible, and cancellation/release controls stay available.
- Collapsing/reopening sidebars and switching workspaces preserves UI state and time.
- Real mouse tests complete the multi-day recovery/cure/build/production/fuel/save loop.
- Render and inspect all major contexts at minimum and standard window sizes.

The baseline guide will be rewritten around these flows. No new geography, actual
scouting expedition rules, hunger systems or engine save-format changes are required.
