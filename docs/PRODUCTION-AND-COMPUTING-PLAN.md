# Production, scavenged computing, and automation

Status: working design, written before implementation on
`feature/production-and-scavenged-computing`. The objective is a substantial playable
resource-chain slice with a computing subgame, not an actual ML training application.
Treat quantities, time, yields, and workload profiles as authored balance data unless
explicitly identified as real hardware facts. Preserve the previous physical scenario
as a compatibility fixture and add an industrial scenario for the new default UI.

## Research and consequences

Precious Plastic's practitioner documentation describes collecting/sorting, washing
where needed, shredding, moulds, and injection as distinct requirements. Its washing
documentation stresses separating plastic types to control stream quality. This argues
for identifiable polymer streams, multiple stations, testing/specification gates,
maintained equipment, and explicit rejects rather than universal "plastic" currency.
[Injection workspace](https://community.preciousplastic.com/library/set-up-an-injection-workspace),
[washing system](https://community.preciousplastic.com/library/ws---10).

The maker's published basic injection workspace includes an electrically heated,
hand-operated injection machine and a separate shredder. Electrical heating does not
remove the operator's physical work. Implement one researched HDPE recovery route, with
provisional game-scale yields and limited noncritical products (clips/covers/spacers).
Do not turn recovered polymer into semiconductor dies, precision bearings, food-contact
certification, pressure seals, or arbitrary medical supplies.
[Maker's machine/workspace specification](https://www.preciousplastic.com/starterkits/showcase/injection).

Dell's service manual treats memory, processor, cooling, power supply, and storage as
separate replaceable assemblies. Model recoverable individual units, compatibility,
diagnosis, replacement, and finite repair specifications. A dead chip is not restored
by spending a generic electronics token; a recoverable fan/connector fault can be.
[Manufacturer service manual](https://www.dell.com/support/manuals/en-us/optiplex-3090-desktop/optiplex3090_mt_sm/working-inside-your-computer).

NVIDIA lists RTX 3060 variants with 12 GB and 8 GB VRAM, and reference card power of
170 W. Do not infer VRAM from the family name or treat card power as total wall draw.
PSU capacity, connectors, CPU/socket, memory type, storage interfaces, cooling and
condition constrain assembly; PSU efficiency affects whole-system consumption.
[NVIDIA reference specifications](https://www.nvidia.com/en-us/geforce/graphics-cards/30-series/rtx-3060-3060ti/).

Qwen's model card gives a concrete 4.0B-parameter example. Raw four-bit weights for
4B parameters are about 2 GB before metadata, runtime state, cache, and activations;
16-bit weights are about 8 GB. These are arithmetic estimates, not measured working
budgets. Author explicit RAM/VRAM/storage profiles, context/batch constraints, and
runtime dependencies, so memory exhaustion is an explainable gameplay blocker.
[Publisher's model card](https://huggingface.co/Qwen/Qwen3-4B).

The QLoRA paper freezes quantized base weights and trains low-rank adapters, reducing
memory demands. It does not establish that every 4B workload fits every 3060. Represent
an explicitly bounded adapter-training workload, not full-model training or an assertion
of real throughput. Preserve model/dataset provenance and require held-out validation
before deployment. Game task durations and scores are simulation abstractions.
[Original QLoRA research](https://arxiv.org/abs/2305.14314).

Transformers' offline documentation requires already-downloaded files and dependencies.
Separate weights, tokenizer/config, compatible runtime/driver, operating system,
manuals, datasets, and derived adapters. Recovery, integrity, installation, storage
space, and backups all matter. A USB drive is a physical carrier, not a magical unlock.
[Official offline documentation](https://huggingface.co/docs/transformers/main/installation).

SMART monitoring provides ordinary scheduled disk-health checks without an LLM.
Use deterministic rule monitoring as the first automation tier. Later models can
interpret telemetry and maintenance histories; sensors, interfaces, actuation, data,
and testing remain necessary. Inference cannot move a crate or replace a bearing.
[Packaged upstream smartd manual](https://manpages.debian.org/bookworm/smartmontools/smartd.8.en.html).

Energy is power multiplied by elapsed time. Keep kW allocation, kWh accounting, fuel
mass, battery capacity, discharge limits, and conversion losses separate. The industrial
scenario uses 96 ticks/day (15 minutes/tick); worker cell steps and process effort are
gameplay abstractions. Idle and workload demands differ; avoid perpetually assigning
the full nameplate draw to an unused workbench.
[Extension engineers' energy accounting](https://ext.vt.edu/content/pubs_ext_vt_edu/en/2901/2901-9014/2901-9014.html).

## Production transaction and physical flow

Add a reusable, atomic bulk conversion operation in Haldane.SupplyChain. Validate all
input claims, all output-capacity claims, known material references, unique owners,
and exact total mass before mutating. Return consumed and produced batch receipts.
Reject invalid conversions without deleting inputs or releasing claims. The engine
does not own polymer recipes, stations, or apocalypse content.

Game-owned process definitions specify inputs/purity, station capability, skill,
effort, active utility demand, outputs/byproducts, output quality policy, wear, and
automation eligibility. Jobs persist source stores, local staging/output store,
operator/carrier, station, claims, deliveries, progress, receipts, and blockers.

1. Accept a plan and designate local staging at the station's interaction cell.
2. Physically haul suitable inputs to that store, preserving batch quality.
3. Claim the station, operator, local input batches, local output capacity and promised
   product/waste destination capacity. Claim nothing partially on failed acquisition.
4. Walk the operator to the station and perform work only while qualified, present,
   powered, and correctly equipped. Account for active demand and equipment wear.
5. Commit the mass-balanced conversion at completion. Outputs exist at the station.
6. Physically deliver products and wastes, handing destination capacity promises to
   haul claims without an opportunity for overbooking. Release labor/equipment after
   processing; mark the job complete after output shipments finish.

Before conversion, cancellation retains all actual input material and finishes loaded
inbound shipments. After conversion, cancellation cannot undo products or refund raw
materials; produced batches remain at the site or finish loaded shipments. Model inputs
as unconsumed until the completion transaction in this slice. Intermediate chemistry,
spoiled hot batches, and irreversible partial reactions need an explicit future WIP model.

Implement recovery stages: wash/sort identified HDPE, dry/capture moisture, shred,
and hand-operated moulding. Products are noncritical polymer repair pieces; use them
in a specific equipment/computer maintenance operation. Product and waste mass must
equal input mass, and quality improvements must be authored as separation/yield
tradeoffs. No blanket material safety certification follows from purity or grade.

## Standing orders and scheduling

Support persistent "maintain target stock" orders with recipe, station, sources,
destination/waste store, operator/carrier, enabled state, and optional automation.
Count available stock plus promised unfinished output once, never double-counting
output already handed to a haul. Stable ordering prevents duplicate jobs and starvation
caused by incidental dictionary order. Missing inputs or capacity produce readable
blockers instead of queue floods. Disabling an order stops new work and lets accepted
jobs finish. Cap outstanding jobs per order and make the pending quantity visible.

Share explicit equipment claims across process and computer tasks. Workforce claims
must remain exclusive across construction, projects, hauling, processing, and electronics
work. Autonomous system-created jobs are deterministic consequences of persisted orders;
player history records commands, not every internal scheduling step.

## Individual components and recoverable archives

Each component has a stable item id, model definition, mass, compatibility attributes,
condition, diagnosed fault, known/unknown diagnosis, and installation owner. Loose units
are represented by whole-item material batches with unique material ids; this reuses
physical hauling, storage capacity, and mass claims. Reject fractional item hauling,
duplicated ownership, and contradictory installed/loose state. Installed component mass
remains part of the world's tracked mass.

Seed a remote electronics cache, not a preassembled working PC. Recover motherboard,
CPU, RAM, storage, PSU, GPU, cooling, and portable media individually. Include useful
incompatible or faulty alternatives. Assembly consumes claimed whole units into a
machine instance at a physical computer fixture. Enforce socket, memory/interface,
slot, power/connector and cooling compatibility before installation. Allow diagnosis,
specific repair/maintenance, component replacement and uninstallation where supported.
Keep difficult chip-level failures as replacement requirements.

Separate digital definitions from physical source media and installed copies. Track
integrity, size, dependencies, machine copies, knowledge prerequisites, and derived
assets. Copying bits is allowed; duplicate copies consume storage, not extra component
mass. Recover OS/runtime, manuals, control software, model weights and tokenizer from
media. Manuals require study by a qualified person; they do not confer general skill.

Computer tasks include diagnosis/repair, assembly, copying/installing software,
studying a manual, telemetry collection/labelling, bounded adapter training, validation,
and monitoring. Human tasks claim technicians and walk to access cells. Machine tasks
claim a compatible powered computer, CPU/RAM/VRAM/disk budget, software/knowledge/data,
and simulated work time. Completion never executes recovered software on this host.

## Electricity, failures, and automation

Add finite generator fuel and network-local batteries. Generation is limited by fixture
condition, fuel availability, and connected capacity. Batteries have kWh, charge and
discharge kW limits, and explicit efficiency/losses. Charge from unused generation;
do not generate energy by cycling. Meter per-tick/cumulative generation, demand,
consumption, charging/discharging, and loss. Track spent fuel in a mass ledger rather
than silently reducing conserved resource mass; it is an accounting abstraction, not
a combustion chemistry/oxygen model.

Allocate idle+requested workload demand in stable priorities. Critical water/clinic
service may outrank optional GPU training. Keep PSU input demand distinct from GPU
nameplate power. Interruption pauses checkpointable work; unsafe shutdown invalidates
uncommitted machine work according to explicit task rules. Installed hardware wear
and specific faults gate operation; repair does not automatically repair file integrity.

Physical data connections, installed sensors/controllers, working endpoints, and
verified software gate telemetry/automation. A failed PC, broken connection, empty
battery, missing runtime, or unvalidated adapter removes automation immediately.
Rule control may run eligible timed equipment; hand-operated moulding and all hauling
still require humans. Monitoring emits maintenance alerts and can free routine
observation work, never perform a physical repair by itself.

Late-game path: recover a compatible 12 GB RTX 3060, enough RAM/storage/cooling and
power; recover a complete 4B model/runtime bundle and training textbook; collect and
label relevant telemetry; train a bounded adapter; validate against held-out data;
deploy monitoring with provenance and clear limitations. A cheaper rule monitor should
remain useful throughout, so the LLM is an interesting capability rather than mandatory
magic. User-facing workload estimates must identify authored profiles versus measured facts.

## UI and persistence

Add process/standing-order, component/assembly, computer task/archive, and energy views
to the current map/table interface. Provide concrete buttons/selectors for the complete
recovery/processing/computing progression, cancellation, power controls, and inspection.
Show blockers, locations, promised output, memory budgets, installed assets, fuel/kWh,
and automation availability. Keep tables usable at the existing minimum window size.

Register new module/command schemas; preserve old physical saves without inventing new
resources. Validate all cross-module claims, component ownership, task/equipment claims,
destination promises, compatibility, asset storage and provenance on load. Derive new power allocations and paths each tick. Preserve completed electrical interval
receipts in saves so a reload does not replace the last metered service with a new, unpaid
allocation; the next preparation calculates fresh budgets from fuel and stored charge. Record workload
and order choices in player history so replay is deterministic.

## Acceptance audit before completion

| Requirement | Required evidence |
| --- | --- |
| Atomic production | Invalid mass/capacity conversions leave inventory and claims unchanged |
| Physical processing | Inputs and products travel; no remote output creation; local qualified work |
| Resource conservation | Every recovery stage, fuel use, component install/repair, and cancellation balances mass |
| Standing orders | No duplicate/pending output overcount; targets converge; pause/shortage/resume |
| Scheduling | Labor/equipment/receiving space cannot be double booked across systems |
| Computing salvage | Whole-item movement, identity, compatibility, diagnosis, assembly and repair tests |
| Offline tasks | Missing/corrupt assets, runtime, RAM/VRAM/storage/knowledge prevent work clearly |
| Model progression | Dataset/provenance/train/held-out-validation/deployment gates and useful monitoring |
| Electricity | Finite fuel, network separation, battery bounds, kW/kWh/loss accounting, workload interruption |
| Automation | Sensors/data/power/software required; eligible work runs without operator; hands-on tasks still claim labor |
| Persistence | Mid-haul/process/assembly/training save continuation and deterministic command replay |
| UI | Actual input, visible new tables/actions, and inspected Skia frame for the industrial scenario |
| Regression | Existing engine/physical behavior remains tested; lower layers have no game/UI dependency |

## Explicit follow-on scope

Full expeditions, arbitrary board-level repair, detailed chemistry/speciation, thermal
fluid simulation, voltage/frequency/surge physics, realistic CPU/GPU benchmarks, real ML
execution, robot manipulators, detailed cables/interfaces, and multiple floors remain
future expansions. The implemented slice must have actual consumers and consequences
for its component/software/energy abstractions; avoid empty schemas labelled as features.
Revise this plan when implementation reveals a better boundary, and record any omitted
planned mechanics honestly in the implementation audit rather than silently narrowing it.


## Decisions from implementation

- A dryer placed immediately inside the old door made the workshop inaccessible. It now
  sits at `(14,6)` with access at `(14,7)`. The industrial scenario validates its initial world.
- Finished output needs a local stock claim as well as a destination capacity promise until
  the carrier takes over. Otherwise another task could consume a product owed to a shipment.
- Source media travels between installation worksites. Installed copies, telemetry and adapters
  belong to a particular physical disk, so removing that disk actually removes those capabilities.
- Rule monitoring remains the controller prerequisite. A validated model unlocks an authored
  six-hour condition-trend forecast; it cannot operate a hand press or physically repair equipment.
- Routine component service is separate from specified fault repair. It consumes process water,
  captures the same mass as local effluent, and restores at most ten condition points, capped
  at 95%. This is provisional maintenance content, not electronics cleaning advice.
- The tutorial starts with 80 kg of tank fuel and 40 kg in salvage. Earlier provision ran out
  during serial parts hauling before the player could finish learning the new mechanics.
- Add general physical store transfers now: otherwise clips in the workshop and steel in salvage
  could not meet a single-source fixture repair order from the desktop.
- World time is 15 minutes per tick; map movement remains one cell per tick. Neither cell size,
  human work effort nor the resulting logistics duration claims literal real-world calibration.

The implemented mechanics and remaining boundaries are recorded in
[PRODUCTION-AND-COMPUTING.md](PRODUCTION-AND-COMPUTING.md).
