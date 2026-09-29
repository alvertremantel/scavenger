# Scavenging expeditions: current slice

Implemented on `feature/scavenging-expeditions`. Design remains provisional; the disease,
treatment chemistry, geography, quantities, effort and risks are authored game fiction.
The engine revision is unchanged. The reusable Haldane inventory, claims, clock,
random streams and serialization support the game-owned expedition module.

## Play the early loop

The desktop starts the regional scenario with Mira ill and modest compound reserves.
Use **Scavenging**, select a destination, multiple workers and multiple source objects,
then **Dispatch team**. The quantity control applies to each selected bulk object and
caps it at remaining stock. Whole computer parts ignore that quantity and recover as
one unit. Every accepted action advances one tick. **Until decision** stops at field
incidents and returns; **Assess / wait** resolves a structural/rival encounter with
extra time at the site. **Recall / retreat** brings back whatever is already recovered.

1. Pharmacy: Ivo and Anya can recover sealed fictional treatment stock, excipient,
   and sterile water. One recipe batch needs 0.1 kg stock, 0.12 kg excipient and 0.8 kg
   water. Selecting those three with the default 1 kg bulk amount brings extra stock
   when available; the API/CLI can request exact per-object amounts.
2. Hardware: Ivo and Anya can recover labelled dry carbon sorbent. Set 0.1 kg for the
   first recipe batch. Anya supplies chemistry; Ivo carries compound deliveries later.
3. At home, **Refine fever stock** delivers inputs to the clinic, consumes 0.2 kg,
   produces 0.08 kg refined stock, and captures 0.12 kg spent sorbent/rejects.
4. **Formulate clinic dose** combines 0.08 kg refined stock, 0.12 kg excipient and
   0.8 kg intact labelled sterile water into a 1 kg batch of fictional clinic treatment.
5. **Treat patient** waits for a released dose at the clinic, then claims a medic and
   clinic equipment. Patient and medic walk there. Working power/water/equipment and
   four effort are required. A 0.1 kg dose restores the patient's availability; its
   mass enters the spent-supplies ledger. **Cancel patient care** releases claims and
   preserves unused stock and the patient's illness.

Both recipes require named grades, minimum purity and no known contamination.
Opened stock, the flooded pharmacy's low-purity stock, industrial process water, or a
batch with a good label but known contaminants cannot bypass these gates. The recipe's
release grade is a gameplay convention, not a claim of actual sterility or clinical
safety. This implementation does not supply a real-world medication recipe.

```sh
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Cli -- --scavenging --roundtrip --ticks 400
dotnet run --project src/Scavenger.Cli -- --scavenging --json --ticks 400
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/scavenging.png --ticks 0 --view scavenging
dotnet test Scavenger.slnx -m:1
```

`--industry` and `--legacy` retain the prior CLI scenarios. The desktop's industrial
screenshot walkthrough still uses the prior complete electronics cache fixture.
Old physical/industrial saves load without adding a region, illness or new supplies.
Reset starts the scenario configured for the window.

## Location distribution and salvage rationale

The region defines five locations and their object inventories independently of the
compound grid. A generator can later place/choose these definitions without changing
the expedition rules. Coordinates are regional labels in this slice; travel ticks are
authored route costs. A world need not contain every archetype or every resource.

| Location | Why go there | Recoverable stock and constraints |
| --- | --- | --- |
| Morrow Pharmacy | Early fictional treatment; formulation supplies | 0.6 kg sealed fictional stock, 1.2 kg excipient, 4 kg intact sterile water, 0.4 kg protective supplies. Wet back-room stock is a separate 2 kg batch at 86% purity with unknown contamination. Empty dispensing bottles provide only 2 kg ordinary glass. No usable refrigeration/cold-chain rewards. |
| Harker Hardware | Sorbent, common repair/building materials, spare tool and fuel | 3 kg labelled carbon refill stock, 8 kg identified soiled HDPE containers, 12 kg sorted steel fittings, 4 kg recoverable copper pipe, 16 kg usable shelving timber, one 1 kg prybar and 10 kg closed diesel cans. Locked pipe racks and rear timber need tool access; ordinary industrial goods provide no sterile or medicinal input. |
| County Materials Laboratory | Substantial glass; instrumentation interfaces | 14 kg dry identified borosilicate vessels, 18 kg ordinary window fragments, 20 kg buried/residue-bearing borosilicate, 6 kg removable steel stands and two whole telemetry/controller units. Buried vessels start inaccessible even to the careful starter team. Window glass and lab glass remain distinct. Dry bench glass is not automatically sterile. |
| Willow Terrace | Nearby small domestic reserves | 3 kg bottle glass, 2 kg identified containers, 5 kg reusable timber and 0.7 kg copper pipe. Dismantling shelves/service cupboards needs tools, skills and a sound enough structure. Modest yields cannot replace a specialist site. |
| Vector Computer Repair | Compatible/incompatible individual parts and archived knowledge | A board, CPU, two DDR4 modules, SSD, PSU/cables, cooler, locked 12 GB RTX 3060, USB archive, older DDR3 module and rejected CPU/SATA drive. No working PC appears. Cooler has a broken clip; rejects remain faulty; only recovered units can be diagnosed or installed. |

Numbers are recoverable fractions already separated by material identity, not whole
building weights. Heavy demolition equipment, unrecoverable masonry, packaging,
fixtures' full bill of materials and unmodeled refuse are outside this ledger. Effort
represents dismantling/separation. Do not add all the mass of a source object to several
alternative salvage entries: future dismantling recipes should use explicit shared
source-object ownership if they offer mutually exclusive recoveries.

A location loses integrity once per elapsed day. Each source has a minimum access
integrity. A careful team's construction skill lowers that threshold by up to 0.2;
a prybar is an additional gate, not a replacement for skill. The lab's collapsed store
needs a future access-restoration system, stronger equipment or different authored
condition before it can be harvested. Integrity zero does not delete remaining stock;
inaccessible material stays accounted for in the ruin.

Rival crews remove accessible stock using an authored daily mass budget and source
priority order. Whole parts are taken only when the budget fits the complete unit.
Rival pressure separately controls arrival encounters. This is a pressure/depletion
model; diplomacy, violence and named rival agents remain future work. Rival removals
are permanently recorded. Stock is never regenerated.

## Travel, work and ownership

Dispatch preflight is read-only. It checks qualified healthy team members, worker
availability, manifest mass, finite current stock, whole items, structural access,
walkable receiving/exit paths, local tools/protection and unclaimed receiving capacity.
Each worker adds 12 kg return capacity, including reusable tools. Search effort uses the strongest relevant
specialist; additional carriers increase cargo capacity rather than magically
multiplying extraction work.

Reusable tools remain reserved in the receiving inventory while away; field tool
movement/durability is currently represented by that exclusive claim.
Accepted plans reserve all workers, the reusable tool and each manifest entry's
receiving space. The team gathers at the receiving store and walks to the regional
exit `(0,4)`. Protective stock is consumed only at departure. Regional travel takes a
fixed number of ticks each way; quick travel takes three quarters of the careful time.
Away workers are absent from the compound map and remain exclusively claimed.

Arrival rolls one incident on `scavenging.incidents`. Search/wait ticks accumulate
fictional exposure. Quick work increases exposure, while protection reduces it to
one fifth. Protection covers this abstract expedition; item durability and saturation
are deferred. At home, exposure ≥1 makes the worker ill/unavailable. No contagion,
incubation, natural recovery, mortality or immune memory is modeled yet.

The manifest is ordered. Each object takes work proportional to recovered mass,
approach and specialist skill. At extraction the system rechecks access and finite
stock. Rival depletion can shrink a recovery; collapse/depletion can skip it entirely.
It shrinks/releases receiving claims accordingly. Two player teams cannot duplicate
one object even though both may have planned to recover it.

Cargo exists with the expedition throughout return. Recall during outbound travel
returns over the distance actually travelled. Recall before departure releases claims;
a field recall preserves cargo. On return, workers walk from the regional exit to the
receiving store. Closed doors can block unloading; claims/cargo persist until a path
opens. Only actual arrival creates compound batches or individually owned components.
Archive contents are part of the source object definition; files are registered when
their physical carrier arrives. The subsequent diagnosis,
compatibility, repair, assembly and offline-computing systems are unchanged.

## Persistence and mass

The new module persists definitions, remaining object stock, rival removals, jobs,
phase/search/travel progress, pending decisions, cargo, return receipts, kit claims,
health, clinic jobs and spent supplies. Commands persist plans, field responses,
recalls and clinic start/cancel actions. Saves reconstruct scheduler systems based on
the modules present; RNG state and command history continue without fresh rolls.

Validation checks location stock against initial stock, rival removals and all cargo /
return receipts; whole-unit model/identity/ownership; manifest qualification and phases;
team/tool/capacity claims and orphan claims; clinic dose/release/medic/equipment claims;
and consumed-protection/dose accounting. Production save validation also enforces the
new quality gates. Existing inventory and physical/industry validation remain active.

`PhysicalReport.TotalTrackedKg` now includes remaining regional stock, expedition
cargo, rival removals and spent supplies alongside compound stock, haul cargo,
installed/embedded resources and spent fuel. Rival and spent-supply quantities are
accounting receipts, not usable stock. Historical expedition return receipts are not
added again; those materials are already in compound stock or downstream receipts.

## Verification and next design questions

See the implementation tests in `ScavengingTests` and the actual Avalonia expedition
input test in `CompoundWindowTests`. The tests exercise stock differentiation,
read-only preflight, skill/tool/capacity/labor gating, partial recovery, daily decay,
field decisions, recall, physical blocked unloading, all expedition save phases,
component/archive recovery and diagnosis, competing player teams, medicine purity /
grade / contamination / sterile-water gates, clinic waiting/cancellation/continuation,
exposure, rejected corrupt saves and deterministic command replay.

Open questions remain explicit: authored versus generated regional geography; what
players know before scouting; restoring structural access; faction behavior; handheld
equipment and vehicles; chemistry assays versus trustworthy labels; propagation and
recovery of disease; medic redundancy and emergency/self care. The current UI exposes
all object inventories and underlying risk values to make experiments inspectable.

Final acceptance run: `dotnet build Scavenger.slnx --no-restore -m:1` completed with
zero warnings/errors; `dotnet test Scavenger.slnx --no-build --no-restore -m:1` passed
97 simulation and 5 desktop tests. The 400-tick CLI JSON save/load walkthrough returned
both expeditions, completed Mira's treatment, left all four residents well, and
preserved total tracked mass at 202.25 kg. `artifacts/scavenging.png` was rendered from
the actual Avalonia/Skia window and visually inspected. `git diff --check` passed.
The independent Haldane checkout has no changes.
