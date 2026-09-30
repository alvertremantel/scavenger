# Playing through the four workspaces

Run `dotnet run --project src/Scavenger.Desktop`. Start a fresh game and keep time
paused while planning. The global clock, time controls and save/load stay visible.
Dashboard, Region, Compound and Registers are the only top-level destinations;
use the navigation row, Ctrl+1–4, or Ctrl+Tab / Ctrl+Shift+Tab.

## Dashboard

The opening priorities link straight to pharmacy and sorbent recovery, clinic care,
fuel recovery, production and building. Alerts link to the relevant map sidebar.
Crew health, stock, field teams and delivered power are live. The footer keeps fit
crew and tank fuel visible even while another workspace is open.

## Region: inspect, recover and manage teams

The map stays visible during every sidebar context. Drag to pan, wheel to zoom
around the cursor, and use + / − / Fit as needed. Choose location highlights and
terrain/district/elevation/moisture/flood overlays from the toolbar.

Click a recovery site to see its remaining stock, road route, access integrity,
rivals and mixed terrain. **Plan expedition here** enters the three-step planner:

1. **Site** chooses the destination and offers Pharmacy, Sorbent, Fuel and Materials
   supply shortcuts. Each shortcut checks real stock, quality, skills, kit, paths
   and storage; it prepares a plan and opens Cargo for review.
2. **Team** toggles workers, then sets the approach and protective supplies.
3. **Cargo** toggles objects and edits each amount independently. Components travel
   whole. Expand quality/access requirements for details, or set all bulk quantities.

The site/crew summary, readiness and **Dispatch team** remain visible. Dispatch
opens Field teams. Active cards show crew, phase, cargo and travel progress. Team
markers on the map open that team's controls and highlight its route. Their positions
project the timed regional trip onto the road route; they are not physical field pawns.
Packing/unloading is at the compound and search/decisions are at the destination.

Use **Assess / wait** for a field decision or **Recall / retreat** to bring the crew
home. Normal time controls pause at pending decisions. The selected team and its
route stay linked, including when a newly waiting team takes focus.

**Find supplies** searches revealed locations by material, object or site name. A
search for `glass.borosilicate` finds the relevant known stock; clicking a result
focuses its map parcel and inspection, then you can plan recovery. This is a search
of existing intelligence: fog of war and scouting expedition rules are not yet added.
**World setup** creates a fresh seeded world and replaces the current compound.

## Compound: build and operate

The map stays visible while the sidebar manages Inspect/repair, Build/utilities,
Clinic, Production, Power, Transfers, Computers, Crew or Tasks. Toolbar shortcuts
reach the most common contexts. Clicking the clinic, generator/battery, computer,
store or production fixture opens its relevant operations. Selecting a production
fixture also selects its authored recipe; computer fixtures select their machine.
Right-click to inspect repair, access and utility connections instead.

**Build** offers the existing plans. Choose one and click a clear map cell. A room
zone uses two corners. Noor builds and Ivo delivers materials. Utilities and access
remain real constraints. The overlays can be toggled in the Build sidebar.

**Clinic** shows health, required inputs and batch progress. Recover pharmacy stock
and sorbent, refine, wait for the refined stock to arrive, formulate, wait for the
released dose, then select Mira and treat her. The care controls remain visible while
records scroll. Inputs still need the right purity, grade and contamination status.
The process and treatment remain fictional; this is not real medical instruction.

**Production** queues authored batches or maintains stock targets. Recovery orders
start the HDPE wash → dry → shred → mould chain. Recipe details show actual inputs
and products. Standing orders can be paused and batches cancelled. Build extra storage
and repair worn fixtures after recovery; repair inputs come from the depot.

**Power** separates tank and depot fuel. **Refuel from depot (up to 6 kg)** sends Ivo
on a physical delivery and supports partial batches. Recover more fuel through Region
before supplies run out. Normal time stops when the tank drops below 3 kg.

**Transfers** moves batches, complete computer parts and worksite leftovers. Carrier,
receiving store and quantity remain explicit. Cancellation applies to unloaded hauls.

**Computers** has Hardware, Archives and Automation views. Select components before
Diagnosis/Assembly. An empty hardware view links directly to a computer-parts search
in Region. Maintenance and detailed diagnostics expand when needed. Expand
Computer & technician to change machine or operator. Existing repair, servicing,
software copying, textbook study, telemetry, rule control, adapter training, validation
and model deployment remain simulated workloads with real in-game resource claims.

**Crew** shows sixteen independent skills per member. Choose a member and specialty
for their current level, studied theory, practical XP and remaining gate. **Find
next-level material** searches Region for that precise manual. Recover the whole
volume, return to Crew and study it. After the theory completes, productive work or
**Start hands-on practice** earns practical XP. A qualified mentor speeds practice
while committing their own labor. Learning walks to the physical book or exercise
fixture. Cancellation keeps partial progress and frees the worker, book and mentor.
Duties and project releases remain in the expandable assignment section.

**Tasks** shows construction, production, transfers, computer work and learning
together, with cancellation controls. Long-lived stock orders may queue another batch after cancellation; pause
those orders in Production when freeing crew for other work.

## Registers and view state

Registers keeps detailed Materials, Stores, Workers, Jobs, Services and Events tables,
with the existing case-insensitive filter. Use it for cross-compound audits rather
than managing every action through a table. Recipe and component details also remain
available in their contextual sidebars.

Collapse a sidebar with × to expand the map. **Manage →** restores the same context
and plan. Switching the four global workspaces preserves selections and map view;
navigation does not advance time. Explicit recovery shortcuts replace the planner
with their suggested manifest. Save/load preserves the simulation and pauses time;
UI selections are session-local and are not a new save-format promise.

## Verification

The previous UI baseline passed 131 simulation and 17 Desktop tests. The skills
extension also adds CLI and learning regression coverage; see the current counts
and playthrough evidence in [Skills and learning](SKILLS-AND-LEARNING.md). Rendered views were checked at 1050 × 780 and 1440 × 1000.

Desktop tests exercise the four destinations, minimum-window map dimensions, mouse
map navigation, sidebar collapse/reopen, plan retention, stock search → site → planner,
fixture selection, clickable field teams and task cancellation. The multi-day actual
mouse playthrough and the complete industrial/computing progression also run through
these sidebars. Screenshots are real Avalonia/Skia renders, not mockups.

To generate the test captures:

```sh
SCAVENGER_UI_ARTIFACT_DIR="$PWD/artifacts/ui-redesign" \
  dotnet test tests/Scavenger.Desktop.Tests
```

See [the UI plan](MAP-CENTERED-UI-PLAN.md) for scope and acceptance, and
[the baseline playability notes](BASIC-PLAYABILITY.md) for the previous slice's audit.
