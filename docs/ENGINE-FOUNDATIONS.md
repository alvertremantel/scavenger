# First engine slice

Implemented in the local Haldane checkout and composed by `Scavenger.Sim`.
This documents the reusable first engine slice. The subsequent physical compound and
Avalonia integration are described in [Physical compound](PHYSICAL-COMPOUND.md).

## Boundaries

`Haldane.Kernel` owns clock, scheduler, commands, events, RNG, and saves. Its scheduler
can now retain a reconstructed command history when continuing a loaded game; otherwise
saving again after loading would discard earlier commands. Pass `LoadResult.CommandLog`
to the scheduler's optional third constructor argument.

`Haldane.CareerSim` owns the shared roster and project shapes. Workers have immutable,
named `SkillProfile`s alongside the existing general `Skill` rating. Missing specialties
have rating zero. A project's optional `SkillRequirement` gates assignment and determines
its work rate. Generic projects keep their former daily progress model.

Assigning a worker to a new project removes their previous project assignment.
`UnassignStaffCommand` frees them for a different activity. External systems can reserve
labor through `WorkforceModule`; project assignment rejects reserved workers and work
does not accrue while they are reserved. A reservation is released only by its owner.
Transport acceptance also checks active project assignments. Callers of the low-level
labor reservation API must account for those assignments when accepting another task.

Workforce and project snapshots are schema version 2; missing new fields in version-1
snapshots default to empty specialties/reservations and no skill requirement. A legacy
save that assigns one worker to multiple active projects is rejected explicitly.
Work processes projects and workers in stable order to preserve RNG draws after loading.

`Haldane.SupplyChain` is an optional library above CareerSim. It defines located bulk
inventory, storage/stock reservations, and tick-based transport. It has no game or UI
dependency. `Scavenger.Sim` supplies the compound scenario, system composition, and
read-only store, batch, worker, and haul report rows.

## Material and storage rules

- All bulk quantities and storage capacities are decimal kilograms. Other dimensions
  and discrete components need separate models before being introduced.
- Definitions, stores, and batches have stable unique ids. Optional cell coordinates
  anchor stores without making a grid or a pathfinder part of inventory.
- Quality records retain nominal purity, known contaminant mass fractions, and a grade
  label. Known impurity fractions cannot exceed the remaining mass. Unidentified
  impurity can remain. Grade labels do not establish food or medical suitability.
- Different batches remain separate after delivery; no automatic blending occurs.
- Available source stock excludes reservations. Free receiving capacity excludes both
  physical stock and promised incoming cargo. Incoming capacity stays reserved after loading.
- Loading returns a cargo record and removes the corresponding mass from storage.
  Delivery creates a new batch with the same quality and releases receiving space.
- Queries expose stored, available, incoming, and in-transit quantities separately.

## Transport rules

A directed `TransportRoute` identifies source and destination stores, positive travel
ticks, per-trip carrying limit, optional qualification, and open/closed state. The
first implementation uses authored durations; it does not infer travel from coordinates.

`QueueHaulCommand` checks source stock, destination capacity, worker presence and
qualification, project assignment, route access, and unique job identity. Acceptance
claims stock, receiving capacity, and the worker. The scheduler rechecks queued commands
when executing them, so two individually plausible plans cannot overbook resources.
`LogisticsModule.ExplainRequest` supplies a readable rejection reason.

An accepted job is `Reserved`, then `InTransit`, then `Delivered`. The loading tick counts
as the first travel tick. Transport progresses on every simulation tick, while existing
project work remains daily. A closed route or absent worker pauses a job without freeing
its claims or deleting cargo. A changed blocking reason emits one warning; recovery
emits a resumption event. Completion and blockage can interrupt fast-forward via the
registered event catalogue.

An unloaded job can be cancelled through `CancelHaulCommand`, releasing every claim.
Loaded cargo cannot be cancelled: a future return/rerouting model must account for its
physical movement. A trip does not automatically release its worker while blocked.
Completed job ids remain occupied so reports and replay references stay unambiguous.

Save/load persists routes, jobs, cargo, progress, quality, and all resource claims.
`SupplyChainSave.ValidateWorld` checks cross-module route/job references and claims after
loading. `CompoundScenario.Load` performs that validation and restores scheduler history.
Saves should be taken between ticks after queued commands have executed: the existing
kernel save envelope does not persist the unexecuted command queue.

## Authoring

Existing CareerSim content types now accept `skills` on staff archetypes and
`requiredSkill` on project templates. For example:

```json
{
  "staffId": 1,
  "name": "Mira",
  "skill": 55,
  "skills": { "mechanical": 75, "hauling": 45 }
}
```

```json
{
  "name": "Inspect pump",
  "effortRequired": 40,
  "requiredSkill": { "skillId": "mechanical", "minimumRating": 40 }
}
```

Malformed ratings and qualifications produce authoring diagnostics. Material and route
content currently use typed C# scenario setup; SupplyChain JSON content types are a
future extension, rather than an unvalidated JSON loader hidden in this slice.

## Verification

Engine tests cover skill gates, reassignment, legacy snapshot defaults, immutable
profiles/quality, fractional mass, stock and space competition, cancellation, route
and absence blocking, and cross-module claim validation. Compound tests cover
deterministic commands, saved continuation at several stages, replay of deserialized
history, report projections, fast-forward interruption, and assembly boundaries.

## Physical compound extension

`Scavenger.Sim.Physical` now owns the grid, worker positions, pathfinding, placement,
room designations, construction/repair sites, and utility networks. `IWorkContext` lets
the game gate project effort by station access and service. `ITransportDriver` lets the
game move workers to stores and carry cargo cell by cell; a route with
`UsesWorldMovement` cannot fall back to timed arrival. These optional seams retain the
legacy engine behavior when omitted.

Physical movement includes the empty approach to each subsequent pickup. Loaded movement
steps may exceed the authored route estimate; saved spatial jobs validate claims and
cargo state rather than requiring an exact timed duration. Production recipes, fatigue,
injury, vehicles, and congestion still need explicit consumers and models.
