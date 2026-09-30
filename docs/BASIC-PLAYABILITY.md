# Basic playability baseline

This records the verified `b092ced` baseline. The current four-workspace UI is
covered by [the updated play guide](MAP-CENTERED-UI.md).

Run from the repository root:

```sh
dotnet run --project src/Scavenger.Desktop
```

Start a fresh scenario for this slice. The default generated region is still about
50 square miles; no new geography work is included here. **Overview** now supplies
an opening checklist and direct recovery plans. Time starts paused.

## Restore the crew

1. Click **Plan pharmacy supplies** on Overview, or **Plan pharmacy run** in Clinic.
   This selects a stocked, quality-compatible site and the smallest available
   qualified crew. It fills exact quantities for one medical batch plus protective
   supplies. Review the location, crew and cargo before pressing **Dispatch team**.
2. Dispatch opens **Active teams**. Use **Run** or **+12 ticks** to advance.
   If a field incident stops time, select that team and choose **Assess / wait**
   or **Recall / retreat**. A pending decision prevents normal fast forward from
   silently abandoning the team; manual **Step** deliberately remains possible.
3. Once cargo is unloaded and the team is released, click **Plan recovery**, then
   **Clinic sorbent** and dispatch. Wait for this team to return too.
4. Open **Clinic**. **Refine fever stock** becomes available when the appropriate
   free inputs are in depot/clinic stores. Queue it and advance time. Ivo physically
   delivers ingredients, Anya walks to the station and works, then Ivo carries the
   refined product and captured waste to their stores. Follow **Medical work queue**.
5. Once refined stock reaches the clinic store, **Formulate clinic dose** becomes
   available. Queue and advance until the released treatment reaches the clinic.
6. Select Mira and **Treat patient**. Anya and Mira walk to the clinic; advancing
   finishes care and restores Mira to duty. One batch produces 1 kg of fictional
   treatment, and each treatment consumes 0.1 kg.

The clinic controls stay visible while its records scroll. The health summary,
free/required ingredient quantities, quality gates and process reasons are live.
A medical button disables for missing ingredients or a duplicate active batch.
Treatment disables until a released dose is locally available. Merely opening the
clinic never assigns a worker to a demonstration project.

## Keep the compound running

- **Generator fuel** searches usable diesel across site types, preparing up to
  six kilograms from available cans, including partial remaining stock. Wet,
  contaminated fuel is excluded from suggestions. Returned fuel enters the depot,
  not the generator. **Industry → Energy → Refuel from depot (up to 6 kg)** sends
  Ivo on an actual physical haul to the tank. Refill before the tank is empty; Overview
  warns below 3 kg, and normal time stops when the tank crosses that reserve
  threshold. The energy screen separates free tank/depot fuel. Partial depot batches also work; **Store
  transfers** remains available for custom quantities. Fuel is finite; repeat expeditions as needed.
- **Recovery orders** in Industry can be started on any day. They maintain the
  wash → dry → shred → mould chain, rather than requiring tick-zero demo setup.
  These jobs share Ivo, Anya and Noor with other operations. Pause stock orders or
  cancel batches if you need those workers elsewhere.
- **Building materials** prepares a hardware-store trip for steel, timber and HDPE.
  In Compound, choose **Storage zone** and click a clear cell to build useful extra
  capacity. Noor builds; Ivo brings depot materials. Benches and powered fixtures
  additionally need connected utility lines; check their service records.
- Select a worn fixture on the compound map and **Repair**. Repair inputs now come
  from the depot, avoiding an unannounced prerequisite to move them to the workshop.
  Worker paths, duties and overlapping worker markers are visible on the map.
- Recover computer parts through custom expeditions when ready. Existing diagnosis,
  assembly, archive and automation mechanics remain available in Industry.
- **Save** / **Load** uses `saves/compound.json`. Reload pauses time and retains
  physical cargo, work, reservations, exposure, site depletion and random state.

## Custom expeditions and getting unstuck

In **Region**, drag with the left or middle mouse button to pan, and use the wheel
to zoom around the cursor. Click to inspect; dragging never selects a new site.
**Zoom +**, **Zoom −**, and **Fit region** also work. Zoom is limited to 1–16×; Fit
restores the centered full region. Survey layers and selected routes survive navigation.

**Region → Plan expedition here** opens a custom planner. Plain clicks toggle
crew and cargo selections; no modifier key is required. Individual bulk quantities
can differ. **Set all selected bulk kg** applies a common amount when useful.
Components always travel whole. Crew specialties and object stock/qualifications
are visible. The dispatch preview gives the rejection reason and disables dispatch
until the order is valid. Supply suggestions do not advance time or create resources.
If no complete list survives, recover a partial custom manifest from multiple sites.

A worker who is ill, away, reserved, or assigned cannot take a new expedition.
**Compound → Project assignments** releases old experimental project assignments;
Clinic also has **Release Anya from project** when needed. Production, transfer,
computer and expedition tasks have their own cancel/recall controls. Cancelling a
batch preserves real stock at its current worksite; transfer leftovers if needed.

A warning/completion can stop **+1 day** early. Read the footer and work records,
resolve the constraint, then advance again. Walking and normal input transit no
longer interrupt fast forward solely because their status was emitted. A clinic
batch being finished at the station is distinct from its product reaching storage.
Closed doors and disconnected utilities remain real causes of stalled work.

## What changed and what is still abstract

The missing `refuel-route` was a genuine logistics bug: both the UI and industrial
walkthrough referenced a route never registered by the scenario. It now exists and
uses physical worker travel. Other fixes make already-supported systems accessible:
clear clinic entry, project release, quality-aware supply suggestions, per-object
cargo amounts, local medical progress, later stock-order creation, persistent field
interrupts, an early tank-reserve interrupt, fuel visibility, and depot-sourced repairs.

This remains a small systems game. Illness is fictional exposure plus treatment;
there is no hunger, contagion or death clock yet. Walking is cardinal and discrete,
with no collision avoidance; workers can share a cell, now shown as separate markers.
Regional travel remains timed. Suggestions know revealed stock and existing skills.
Chemistry and computing use authored conversions and workloads. No real medical
instructions or actual machine learning execute. Older saves are not a compatibility
target for this development slice.

## Verification

Final checks: solution build with zero warnings/errors; all 131 simulation tests
and 13 desktop input/rendering tests pass (144 total). Rendered fresh clinic, planner,
active teams, repaired compound, day-14 overview and mouse-navigated region views
were inspected at 1050 × 780.

The new simulation tests exercise the clinic/fuel/production/construction loop in
three generated seeds, quality-aware read-only suggestions, releasing the medic,
late recovery orders, partial/cross-site fuel suggestions, a low-tank interrupt and
stopping an already-pending field decision.

The default-seed desktop acceptance test reaches day 14 with all residents healthy
and the clinic powered. It uses actual Avalonia mouse input for supply plans,
dispatch, field decisions, medicine, cure, active-team save/load, repeated fuel
recovery/delivery, storage construction, clinic repair and polymer production. It
then advances three further days and verifies health, working clinic utilities,
positive electrical service, mass conservation and save/load. A separate input test
checks plain-click selection and individually edited cargo reaching the order.
Mouse map tests check wheel anchoring, captured drags, click/drag separation, zoom
limits, zoom-out and Fit with rendered frames.

Optional real screenshots from the acceptance test:

```sh
SCAVENGER_UI_ARTIFACT_DIR="$PWD/artifacts/playability" \
  dotnet test tests/Scavenger.Desktop.Tests --filter FullyQualifiedName~GeneratedWorldIsPlayable
```

See [the implementation plan](BASIC-PLAYABILITY-PLAN.md) for the original audit.
