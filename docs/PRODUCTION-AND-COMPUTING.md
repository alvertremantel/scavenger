# Production and scavenged computing

This branch turns the physical compound into a playable recovery-to-automation experiment.
Its content and tuning remain provisional. The apocalypse is still deliberately unsettled.
The researched intentions and acceptance criteria are in
[the implementation plan](PRODUCTION-AND-COMPUTING-PLAN.md).

## Play the slice

```sh
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Cli -- --industry --roundtrip --ticks 2400
dotnet run --project src/Scavenger.Cli -- --industry --json --ticks 2400
```

The desktop starts the industrial scenario. **Industry** contains Production, Hardware,
Archives, Compute, Energy and Transfers. The map still supports room designation,
construction, repairs, doors and isolated power/water networks; it now also builds and
displays data connections. Actions advance one tick, which is fifteen game minutes.
**+1 day** advances 96 ticks; **Until decision** stops for delivery/completion/blocker events.
Scroll the operational pane to see the remaining rows and controls.

1. Choose **Recovery orders** on Production. Anya washes identified HDPE with process water;
   Noor dries, shreds and moulds it; Ivo hauls inputs, product and explicit byproducts. The
   orders maintain intermediate stocks and 1.6 kg of clips in the workshop. Initial stock
   requests can exceed the exact target by one indivisible batch.
2. On Hardware, keep Mira selected. Select `board-a`, `cpu-a`, `ram-a`, `ram-b`, `ssd-a`,
   `psu-a`, `gpu-3060`, `fan-a`, `usb-archive`, `sensor-a` and `controller-a`; diagnose them.
   Use Ctrl/Shift selection for multiple individual units. The cooler has a broken clip. Select only
   `fan-a` and repair it using 0.1 kg of recovered clips.
3. Select the eight core units (the preceding list without USB/sensor/controller), assemble,
   wait for delivery and work, then power on. The resulting AM4/DDR4/NVMe PC has 32 GiB RAM,
   a 12 GiB GPU, a condition-adjusted PSU capacity check, and a compatible cooler.
4. On Archives, copy these assets in dependency order: `offline-os`, `python-runtime`,
   `gpu-driver`, `quant-runtime`, `model-4b`, `model-tokenizer`, `training-textbook`,
   `telemetry-tools`, `rule-control`. Mira studies the copied textbook. A visible corrupt
   archive demonstrates integrity failure; no generic repair command fixes its bits.
5. Select the diagnosed sensor and controller, choose the dryer on Compute, and fit the
   interface. Start a rule monitor. It records telemetry, warns below 80% observed condition,
   and permits automatic drying through the installed actuator and physical data connection.
   Choose Dry HDPE on Production and **Use installed automation** to queue an operator-free
   batch or maintain automated dried stock. Washing, shredding, moulding and hauling still
   need their people. Avoid competing manual/automatic orders on the same dryer while learning.
6. Collect at least 48 samples, then label telemetry. Train the 4B adapter, select the resulting
   adapter, validate on independent held-out samples, and deploy a model monitor. The authored
   workload needs 16 GiB system RAM and 10 GiB VRAM; an 8 GiB 3060 cannot run it. Training
   releases Mira after physical setup but keeps the computer's resource claims. The model
   monitor adds an authored six-hour trend forecast that can warn before the fixed threshold.
7. Energy shows real fuel depletion and electrical receipts. Stop the generator to draw down
   the battery, isolate the battery to remove that bridge, or deliver diesel from salvage.
   A power cut pauses computing and automatic equipment immediately. Training returns to its
   last checkpoint, counts the restart, and resumes after service returns.
8. Transfers moves any available batch between physical stores, including leftovers from
   cancelled jobs. **Whole component** uses the selected unit's full mass. Deliver steel to
   the workshop, damage the dryer using the prototype inspector, then repair it: the repair
   embeds 0.2 kg of steel and 0.1 kg of clips. Cancel an unloaded haul to release its claims;
   already carried goods finish their trip.

The CLI industrial walkthrough issues the same persisted player commands without granting
resources. The earlier physical CLI remains the default, and `--legacy` retains timed transport.
Existing physical saves load without silently creating the industrial modules or supplies;
reset starts the new scenario.

## Resource and scheduling rules

`Haldane.SupplyChain.Conversions.Complete` is the only new engine primitive. It preflights
all stock/capacity claims, known materials, unique owners and exact input/output mass before
mutation, then consumes inputs and receives outputs as one serial operation. Six engine
tests exercise both success and rejected transactions. Game content and UI stay above Haldane.

Each production job owns a physical staging stockpile and input routes. It waits for suitable
purity, local inputs, an available qualified operator, exclusive equipment access, local output
space and promised receiving space. Work begins at the station and stops when utilities or
automation disappear. Inputs are retained until the atomic conversion; products and waste
appear locally, are held for dispatch, and receive independent physical hauls. Completion
means all outputs arrived. Cancellation releases promises and work claims without refunding,
teleporting, or deleting stock. Loaded hauls retain their real cargo.

Standing orders count unreserved destination stock, active inbound cargo and unhanded process
output promises exactly once. Each order limits its active jobs. Pausing prevents further jobs
and leaves accepted jobs intact. A target can be overshot by one batch. Carrier and worksite
selection are stable, deterministic policies rather than an optimization solver; one carrier
can be a deliberate bottleneck.

Production, electronics service, computing setup, fixture repairs, legacy work projects and
hauling share worker reservations. Physical station access also has an exclusive claim.
Hands-off computing releases its technician and access claim after two setup steps; it retains
RAM, VRAM, CPU and disk output promises. Telemetry cannot consume storage promised to a running
installation or adapter. Continuous tasks still consume electricity and hardware condition.

The HDPE chain conserves bulk mass, including process water and wastes:

| Stage | Inputs kg | Product kg | Captured waste/byproduct kg | Active kW |
| --- | --- | --- | --- | --- |
| Wash | 1 HDPE + 2 process water | 0.95 washed HDPE | 2.05 effluent | 0.2 |
| Dry | 0.95 washed HDPE | 0.93 dried HDPE | 0.02 condensate/residue | 0.8 |
| Shred | 0.93 dried HDPE | 0.90 flakes | 0.03 fines | 1.2 |
| Mould | 0.90 flakes | 0.80 clips | 0.10 offcuts | 0.8 |

Wash/dry quality is explicit authored content; shred/mould preserve weighted input quality.
This is bulk accounting, not an elemental/species or medical-grade purity model. Process
water is finite inventory; the older network's water flow remains a capacity abstraction.
The noncritical clips are a repair consumer, not a claim that arbitrary safety-critical
parts can be printed or moulded from unidentified plastic.

## Physical computers and offline knowledge

Each component has a unique material id and exactly one full batch, loaded cargo or installed
owner. Assembly consumes whole units into the machine. Fault repair embeds replacement mass;
removal returns that same unit locally. Routine service conserves its process-water mass as
captured local effluent. Sensor/controller units are installed at their target fixture rather
than hidden in a global capability flag. Larger-than-six-kilogram units require a future
carrier/route extension and are blocked rather than split.

Compatibility checks cover socket, RAM generation/slot count, storage interface, PSU connectors
and peak headroom, cooler rating, diagnosed health, and exactly one required core part. The
fixture supports one motherboard, CPU, PSU, cooler, storage drive and optional GPU. This is a
bounded model, not a universal motherboard/BIOS compatibility database.

The recovered USB has a sized asset catalogue with dependency and manifest/integrity metadata.
Copy tasks haul its physical unit to the PC. Installed software, datasets and adapters belong
to the storage unit; removing that disk removes access, and reinstalling it restores the bits.
Studied knowledge belongs to a worker. Knowledge and file integrity are separate from hardware
skill or condition. Dead silicon and corrupted media require replacement or future recovery
content; a polymer clip cannot repair them.

The 4B/3060 path is grounded in the distinction between quantized weights and total runtime
memory. NVIDIA documents both 12 GB and 8 GB RTX 3060 variants, and QLoRA demonstrates a frozen
quantized base with trained adapters. Neither establishes that every 4B training configuration
fits this GPU. Our RAM/VRAM/time/power/context budgets and score thresholds are game profiles,
not benchmarks. See the [NVIDIA specification](https://www.nvidia.com/en-us/geforce/graphics-cards/30-series/rtx-3060-3060ti/),
[publisher model card](https://huggingface.co/Qwen/Qwen3-4B), and
[QLoRA paper](https://arxiv.org/abs/2305.14314).

No recovered code executes on the host. No actual LLM is loaded, trained or evaluated. Training
freezes labelled, non-held-out record IDs; validation requires labelled held-out IDs absent
from training. The explicitly authored validation score depends on sample counts. Deployment
requires that provenance and a passing score. Model-assisted forecasts use a deterministic
condition trend over up to sixteen distinct observed ticks with a 24-tick horizon; they are
a game consequence for the simulated adapter, not measured model predictions.

## Electricity and persistence

Generator supply is limited by connected nameplate capacity, fixture condition and reserved
fuel. The current provisional conversion is 0.3 kg diesel per generated kWh. The battery stores
2 kWh, starts at 1.5 kWh, charges at up to 0.5 kW, discharges at up to 1 kW, and has 90% efficiency
each way. There is no simultaneous charge/discharge cycle. Loads on disconnected networks cannot
borrow its charge. Priority resolves critical pump/clinic capacity before optional GPU loads.

Energy is power multiplied by the tick's hours. Metered generation, delivered use, retained
charge/discharge and losses balance. A powered healthy PC requests its authored DC idle plus
workload demand divided by PSU efficiency; GPU nameplate wattage is a separate peak compatibility
check. Fuel is reserved before allocation and only actual generation consumes it. Spent fuel
goes into a mass ledger; this is not a combustion/oxygen chemistry simulation. Tracked mass covers
recoverable inventory, carried goods, embedded additions, installed components and spent fuel.
The mass of initial map fixtures, air and terrain is outside that ledger.

Saves capture completed electrical interval receipts rather than inventing a new allocation at
load time. The next tick derives fresh supply and workload demand. New module and command schemas
are registered alongside the old ones. Validation rejects orphaned claims, inconsistent output
promises, duplicate/fractional component ownership, malformed job references, overbooked machine
resources, invalid archive storage, dataset provenance, and unbalanced energy. Command histories
replay the recovery-to-model walkthrough and manual transfer/repair path deterministically.

## Verification and boundaries

The audit exercises rejected atomic conversions, physical inputs/products/waste, cancellation
at three stages, stock targets, missing waste space, diagnosed incompatibilities, dead/corrupt
parts, installed component service, removed-disk capabilities, software and knowledge gates,
RAM/VRAM/disk contention, finite fuel and isolated batteries, operator-free work with interrupted
data/power, training checkpoints, and mid-process/parts/assembly/training save continuation.
The desktop test drives real mouse buttons through recovery, assembly, copies, study, telemetry,
training, validation and deployment, and captures actual Skia rendering. Earlier physical and
engine tests remain in the regression suites.

Final audit on 2026-09-29 (.NET 10.0.302):

| Check | Result |
| --- | --- |
| Game simulation suite | 65 passed |
| Desktop input/render suite | 4 passed, including the full progression and minimum-size transfers |
| Haldane regressions | 135 passed (25 Authoring, 33 CareerSim, 36 Kernel, 11 Meteo, 30 SupplyChain) |
| Total | 204 passed, no skipped tests |
| Solution build | Zero warnings/errors |
| Native Avalonia startup | Window stayed alive for the five-second smoke run |
| Industrial CLI with save/reload | 2,400 ticks; adapter trained/validated; model monitor running; automatic drying completed |
| Conservation in that run | 345.55 kg tracked, unchanged from initial state |
| Energy receipt in that run | 228.23056 kWh generated; 227.675 kWh used; 0.05556 kWh lost; battery 2 kWh; 51.53083 kg fuel remains |
| Repository checks | Both branches confirmed; whitespace checks clean; engine has no game/Avalonia dependency |

Rendered local review artifacts (ignored by Git) include
[production](../artifacts/industry-production.png),
[hardware](../artifacts/industry-hardware.png),
[model monitoring](../artifacts/industry-compute.png), and
[energy accounting](../artifacts/industry-energy.png). The production, hardware, computing
and energy frames were visually inspected. Controls and tables use the same real Avalonia/Skia
rendering path as the native window. The operational area scrolls at the minimum size.

Reproduce a late-game rendered view:

```sh
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/industry-compute.png --ticks 2400 --view compute --walkthrough
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/industry-energy.png --ticks 2400 --view energy --walkthrough
```

The final suite was run with `--no-restore --disable-build-servers -m:1
-p:UseSharedCompilation=false`; these flags avoid unnecessary restore/build-server work in
this environment. Test runner IPC and the native display smoke check required the environment's
standard reviewed execution permission.

Full expeditions, population needs, arbitrary board-level repair, storage/media recovery,
telemetry archive pruning/export, detailed chemistry, voltage/frequency/surge physics, cooling
fluid/thermal simulation, literal map-distance calibration, robots, multiple floors and real ML
execution remain future work. Recovered stations are seeded fixtures; generic construction and
data cables remain buildable, but manufacturing a new GPU or computer factory is not implemented.
The UI targets one recovered machine; the module supports more machines, while multi-machine
data migration, imported custom content and additional computer fixtures still need design work.

Both repositories use `feature/production-and-scavenged-computing`. The engine additions remain
in the independent Haldane checkout. `haldane-revision.txt` pins engine commit
`93c960a8504ecb7d102868b7684fb9c6655667d2`, which includes the atomic conversion primitive
and its tests. Both feature branches remain unmerged.
