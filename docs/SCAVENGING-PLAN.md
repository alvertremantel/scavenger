# Scavenging expeditions — provisional design and implementation plan

This slice builds on physical logistics and production. The first region is authored;
its location and salvage definitions are reusable by a future generator. Geography,
yields, travel times, hazard rates, and the disease are experimental game data.

## Intended play

Choose a destination for a specific shortage, assemble a qualified team, choose a
careful or quick approach, and select an ordered recovery manifest. Workers walk to
the compound receiving point, leave on a timed regional journey, extract finite
objects, carry them home, and unload into reserved space. They remain unavailable
for compound work until return. A recall returns actual cargo without refunds or
teleportation. Door closures can block departure or unloading.

The pharmacy has sealed treatment stock, formulation supplies and sterile water;
its wet back room has damaged stock. Hardware has carbon sorbent, HDPE containers,
metal fittings, timber and tools, but no drug stock. The laboratory is the efficient
source of glassware, with buried and contaminated alternatives. Homes have modest
mixed domestic salvage. A repair shop has individual compatible and incompatible
computer parts; offline files travel with their physical archive. No universal loot
roll or renewable cache supplies every resource.

Each salvage entry records the source object/room, recoverable material, initial and
remaining mass, quality, work effort, qualification, tool requirement, structural
access threshold and whole-item metadata. Quantities describe recoverable fractions,
not the total mass of an entire building. Unmodeled packaging/building mass lies
outside the resource ledger. Dismantling costs effort rather than creating raw
materials for free; glass composition remains distinct from borosilicate glass.

## Obstacles and uncertainty

Building integrity decays with elapsed days. Unsafe access requires relevant team
skills, a reserved prybar, and a careful approach. Arrival can surface a structural
incident or rival encounter requiring a decision. Resolve by taking extra assessment /
waiting time, or retreat. Rival scavengers deplete actual accessible stock on a daily
schedule; their removals go to an external mass ledger. A dispatch promises space,
not ownership of the distant loot. Depletion during travel is an intended risk.

Disease exposure accumulates while searching and waiting. Protective consumables
reduce exposure and are consumed once upon departure. Persistent symptoms make a
returned worker unavailable until a qualified medic treats them at a powered clinic.
These are deliberately fictional disease mechanics, not an epidemiological model.
The early treatment is also fictional: recover a sealed fictional treatment precursor
at the pharmacy and sorbent at hardware, refine with explicit rejects, then formulate
with pharmacy excipient and sterile water. Generic process water, industrial solvent,
or a high purity number alone cannot become medicine. Only the named recipe release
and a suitable dose batch qualify. Production stages conserve mass; used doses and
protective consumables remain in the accounted spent-supplies ledger.

## Implementation boundaries

- New game-owned persistent expedition module, commands, system, reports and authored
  region; no new Haldane primitive is needed unless inspection reveals a gap.
- Separate expedition scenario is the new desktop default. Existing physical and
  industrial fixtures and their saves stay reproducible.
- Regional stock is outside compound inventory. In-transit recovered cargo is owned
  by its expedition. Unloading transfers each batch or indivisible computer unit
  into the receiving store; computer assets appear with their recovered carrier.
- Explicit worker/tool/receiving-space claims, deterministic stream names, finite
  manifests, event-driven decisions, cancellation/recall, detailed preflight errors.
- Desktop destination/yield inspection, team/manifest dispatch, decision/recall,
  medicine preparation and treatment. CLI expedition walkthrough with save/load.
- Saves must validate stock/cargo/removal conservation, whole items, identities,
  claims, team states, progression, health and treatment references. Old saves add
  no regional resources.

## Acceptance evidence

Tests cover differentiated finite yields; qualification/tools/capacity preflight
without mutations; rival depletion and building decay; event decisions and recall;
physical departure/unloading; worker exclusivity; batch quality and component archive
recovery; medicine purity/sterile-water gates and symptoms/treatment; mass across
recovery/consumption/removal; continuation from travel/search/decision/return/clinic;
and command replay. Run the existing game/desktop suite, a scripted expedition
roundtrip and an actual Avalonia input/rendering test for the new controls.

Deferred: generated geography, vehicles, combat/faction diplomacy, injuries/death,
individual protective equipment durability, contagion between residents, general
chemical reactions, analytical assays and pathogen-specific medical realism.
