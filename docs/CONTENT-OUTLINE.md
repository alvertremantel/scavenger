# Compound survival — working content outline

Status: a design direction, not a settled specification. Names, quantities, apocalypse,
and progression pacing remain provisional. Revise this document when playing a small
simulation teaches us something; do not treat its examples as a promised content list.

## 1. The game we are trying to make

You lead a compound after an apocalypse. You organize survivors, dispatch scavenging
teams, preserve useful equipment, recover materials, repair infrastructure, and develop
the ability to make selected essentials. The player coordinates work rather than
personally gathering objects or operating machinery.

The central fantasy is reconstructing industrial capability from the remains of
civilization. Early survival depends on inherited stocks. Later survival depends on
people, maintained equipment, repeatable processes, and supply relationships.

This is a resource and spreadsheet game with a physical compound. Base-building is
part of the intended game, including a rugged 2D presentation if that serves it well.
Layout should matter to hauling, access, storage, utilities, and hazards. Its visual
style, use of multiple floors, and degree of individual movement simulation are open.

## 2. Working commitments

- The player manages teams, priorities, reserves, and projects.
- Chemistry and materials knowledge open alternative ways to meet needs.
- Physical identity matters: an intact pump is not merely a quantity of metal.
- Goods must reach the location where they are used. Capacity, travel, and labor matter.
- Processes have requirements, yields, byproducts, waste, and maintenance costs.
- Quality and contamination affect suitability. More purity alone does not certify safety.
- Information must explain shortages, competing reservations, and feasible alternatives.
- The simulation runs headlessly and deterministically beneath its interfaces.

## 3. The management loop

Assess demand and reserves; identify a shortage or opportunity; compare acquisition,
repair, substitution, and production options; assign people and equipment; arrange
transport; advance time until a report or decision needs attention; revise the plan.

Standing orders should eventually cover routine work: maintain water reserves, service
critical machinery, retain medical materials, and sort salvage. An order is bounded by
available resources and can explain why it is blocked. Automation should preserve
meaningful choices while reducing repeated clicks.

Example: a pump needs a seal. Salvage a spare, cannibalize another pump, fabricate a
compatible substitute, or redesign the plumbing. Fabrication may compete with the
clinic for clean water, specialist time, and power. Cannibalization sacrifices the
other pump's future use. The best route depends on the particular compound.

## 4. Resources and capabilities

### Objects and components

Objects include pumps, vehicles, refrigerators, instruments, containers, and machines.
Track condition, meaningful components, compatibility, and location. Inspection and
dismantling take work. Sometimes recovering an object intact is much more valuable
than extracting its materials; transport capacity should make that a real choice.

Components include seals, bearings, motors, filters, valves, electronics, and fittings.
Choose compatibility families that produce useful decisions without cataloguing every
real-world part number. Precision and tolerances are potential capability gates.

### Bulk materials and batches

Materials include identifiable metals, polymer families, biomass, water, chemical
feedstocks, and mixtures. Use explicit units. Preserve batch identity when sources or
quality differ. Relevant attributes include composition, contamination, concentration,
condition, and verified suitability. Avoid a universal quality ladder: industrial,
food-contact, and medical suitability are different claims.

The first implementation measures bulk quantity and store capacity in decimal kilograms.
It represents nominal purity, identified contaminants, and a named grade; grade names
are labels, not inferred safety guarantees. Volume, temperature, concentrations,
uncertain assays, discrete components, and incompatible storage await concrete consumers.

### Capabilities

Capabilities are combinations of equipment, utilities, trained people, and facilities:
machining, reliable heating, separation, filtration, fermentation, analysis,
refrigeration, and controlled storage. A recipe and its ingredients do not establish
that the compound can execute it at a useful scale.

## 5. Chemistry and production

Author researched process networks rather than attempting a universal reaction solver.
Separate genuine physical constraints, plausible improvisation, and deliberate fictional
departures in content notes. A process defines inputs and acceptable specifications,
equipment, utility demands, qualifications, labor, elapsed time, throughput, outputs,
losses, byproducts, waste, and quality checks.

Networks branch and loop. Recovery of water or solvent can feed another process, but
each loop pays in loss, time, energy, and equipment wear. Validate references and
quantities mechanically; checking conservation requires a later process-specific model
that includes all relevant feeds and wastes, not merely comparing useful output mass.

Promising initial networks:

| Network | Decisions and dependencies |
| --- | --- |
| Water | Source identification, appropriate treatment, pumping, storage, testing, maintenance |
| Workshop | Inspection, patching, substitution, cannibalization, fabrication, tooling |
| Plastic recovery | Preserve objects, identify and sort polymers, clean, reprocess suitable streams, select applications |
| Medical supplies | Preserve scavenged stock; develop selected formulation, production, storage, and verification capabilities |
| Biological production | Feedstocks, process control, contamination, downstream separation, storage |
| Solvent recovery | Separate useful fractions, accept losses, allocate equipment, handle residue |

For medicine, distinguish active substance production, formulation, and quality assurance.
Do not make a generic chemistry station sufficient to manufacture any drug. Specific
routes and production scales need research before becoming playable content.

Background references: [NIST on plastics recycling](https://www.nist.gov/el/applied-economics-office/manufacturing/circular-economy/plastic-recycling)
and [FDA on manufacturing quality](https://www.fda.gov/drugs/pharmaceutical-quality-resources/facts-about-current-good-manufacturing-practice-cgmp).
These motivate constraints; they are not evidence that a particular improvised process works.

## 6. People and work

Specialties might include mechanical repair, machining, chemistry, analysis, medicine,
electrical work, agriculture, construction, and field logistics. A general competence
rating can remain useful for generic projects; it must not substitute for a missing
specialty. Distinguish qualifications from productivity.

Teams eventually combine roles, equipment, carrying capacity, leadership, and risk.
Learning, mentorship, fatigue, injury, morale, and institutional knowledge are promising
extensions. The initial implementation establishes named skills and exclusive assignment
or reservation; it does not yet simulate those extensions.

People cannot perform full-time work on several projects simultaneously. A hauling job
must occupy its worker while reserved, moving, or blocked. Absence pauses work and must
not silently release goods or deliver cargo. Job cancellation has explicit consequences.

## 7. Logistics and the physical compound

Inventory belongs to stable locations. Production uses local inputs; compound totals are
an overview, not permission to consume a distant store. Reserve stock and receiving
capacity when a job is accepted. Loading removes stock from its origin; cargo remains
accounted for in transit; unloading creates a batch at the destination.

Start with authored routes and tick durations. A closed route pauses the job and explains
the blockage. Later, routes can be generated from walkable cells and doors; the inventory
and job identities should survive that change. Route cargo limits are per trip, not a
simulation of traffic capacity. Vehicles and transport teams are later extensions.

Base-building should eventually provide rooms, stockpiles, workstations, passages, utility
connections, and construction/repair projects. Placement affects travel and operation.
Construction materials must be delivered; building progress depends on qualified labor.
Candidate constraints include dirty/clean work separation, drainage, ventilation, fire,
heat, refrigeration, and incompatible storage. Introduce these when they create legible
choices rather than requiring all of them in the first grid prototype.

## 8. Apocalypse: desired conditions, cause undecided

Working hypothesis: infrastructure and manufactured goods remain, but utilities,
distribution, governance, and replacement-part production have failed. Local agriculture
and renewable feedstocks remain possible. Hazards vary geographically; useful knowledge
survives unevenly in people, records, and equipment.

This supports scavenging, repair, recovery, and selective reindustrialization. Different
settlements can specialize and trade. The precise cause remains open: evaluate candidates
against these conditions and establish coherent effects before committing to one.

Questions still open: how recently did collapse occur; how densely populated is the
region; which ecosystems and infrastructure survive; why are certain places dangerous;
what limits regional recovery; how much armed conflict serves this management game?

## 9. Progression and pressures

Early: stabilize water, food, shelter, and critical equipment with inherited supplies.
Middle: inspect salvage, maintain machines, establish recovery and selected production.
Later: replace vulnerable supply lines with maintained capabilities and regional trade.

Pressures include consumption, seasonal demand, wear, reserve depletion, contamination,
lost expertise, and transport access. Demand and scale should evolve so a solved early
chain remains useful without producing endless trivial emergencies. Long-term survival
must be possible; avoid a world where all machinery inevitably decays with no replacement.

## 10. Interface direction

Primary screens: compound map, inventory/batches, workforce and teams, jobs and priorities,
process network, equipment/maintenance, expeditions, and reports. Tables need useful
filters, units, reserve/available/in-transit distinctions, forecasts, and drill-down.

Explain causes: "Workshop stock is reserved by the clinic"; "Receiving store has no
capacity"; "Route is closed"; "Assigned worker is absent". A process view should trace
input shortages to the jobs or infrastructure that can resolve them. Preserve batch
quality visibility throughout transfers. Fast-forward stops on meaningful events.

## 11. Implementation sequence and boundaries

1. Foundations: named worker skills; exclusive work; material definitions and batch quality;
   located inventories; source/destination reservations; persistent, deterministic hauling;
   diagnostics; a runnable headless compound example.
2. Physical compound: a small 2D map, stockpile/workstation placement, access/pathfinding,
   construction jobs, and route generation using those locations.
3. First playable networks: water, workshop repair, and one researched polymer route;
   production batches, equipment/utility reservations, input delivery, waste, and testing.
4. Spreadsheet interface and standing orders, informed by the same headless world.
5. Expand personnel, expeditions, medical networks, hazards, and regional exchange where
   the first playable slice demonstrates their value.

Haldane.Kernel remains domain-agnostic. Extend Haldane.CareerSim's reusable work shapes;
put reusable inventory/transport rules in optional Haldane.SupplyChain. Keep apocalypse,
compound needs, scenario content, and eventual map rules in Scavenger.Sim. Do not force
funding or career progression onto a survival scenario.

First acceptance scenario: a qualified worker cannot simultaneously repair and haul;
recover a batch from a salvage location; deliver it to a bounded workshop store; preserve
its quality; pause transparently on a closed route; save/load during transit; continue
to the same result. Conservation counts store stock plus in-transit cargo. The first
slice does not claim to implement chemistry, survival consumption, or a playable UI.


## Disease backdrop and authored scavenging region (September 2026 revision)

The working direction now favors a disease-like collapse with surviving useful goods,
decaying buildings and competition over remaining stocks. Keep the pathogen and cause
unsettled. Early chemistry-for-medicine should combine a short pharmacy/hardware chain
with meaningful purity, contamination and provenance gates. The current fictional
treatment is a prototype for that progression, not a settled real chemical mechanism.

Expeditions now provide location-specific finite salvage, exclusive teams, physical
departure/unloading, regional travel, field decisions, recall and persistent exposure.
The authored initial region separates laboratory borosilicate, ordinary domestic glass,
pharmacy formulations, hardware repair materials and a computer shop's individual
parts/offline carrier. This establishes data/rules reusable by either an authored or
generated world. See [the plan](SCAVENGING-PLAN.md) and
[implementation, yields and unresolved choices](SCAVENGING.md).


## Generated regional geography — provisional baseline

The game now starts a seeded town-and-outskirts region: 64 × 64 parcels of 80 m,
about ten square miles. Commercial, civic, residential and industrial districts
join farms, woods and a river with explicit connected roads and bridges. Twenty
authored facility types instantiate different former uses, quantities, surviving
objects and component faults. Specialist materials remain tied to appropriate
activities; dry labels, purity and contamination survive generation and recovery.

Every type is present for this experiment, with a nearby intact pharmacy/hardware
path for the initial fictional illness. Later distributions can omit rare types or
require structural restoration, scouting or negotiation to access them. The region
is a layer above compound construction, not a substitute for detailed building
interiors. See [generated worlds](WORLD-GENERATION.md) for implementation, authored
yields, explicit accessibility guarantees and deferred systems.


## Expanded landscape and mixed parcels

The current generator covers about fifty square miles with a compact, avenue-led or
twin-center main town and two or three outlying suburbs. Regional cells retain terrain
percentages from 16 m sampling inside 80 m parcels, together with elevation, moisture,
flood susceptibility and roughness. Rivers, lakes, marsh, woodland, meadow, hills, rock,
scrub, cultivation and developed ground provide a basis for later washed-out routes,
off-road scavenging and specialized mobility/kit. Those task rules are still future
work; all current sites remain accessible by connected roads. See
[expanded geography](EXPANDED-GEOGRAPHY.md) for provisional placement and sampling rules.
