# Generated regions — playable baseline

Scavenging is committed and merged into `main`. World generation is developed on `feature/regional-world-generation`. The independent Haldane checkout and engine pin are unchanged. This document describes provisional content and actual implemented behavior, not a finalized geography or balancing spec.

## Run and explore

```sh
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/region.png --ticks 0 --view region
dotnet run --project src/Scavenger.Cli -- --world --seed 42 --json --map artifacts/world-42.svg
dotnet run --project src/Scavenger.Cli -- --world --seed 42 --walkthrough --roundtrip --ticks 400
```

The desktop starts a generated region and the lean compound with Mira ill. Under **Region**, click a marker: the inspector shows its former use, surviving objects, condition and road distance. Gold highlights the shortest route from the compound. **Plan expedition here** opens that destination under **Compound → Scavenging**, where the existing team/manifest/protection/dispatch controls operate. The type selector highlights specific facilities. Enter an unsigned 64-bit seed and use **New world from seed** to start a fresh compound and region. Reset repeats the current seed; loading adopts the saved scenario and seed.

CLI `--world` defaults to zero simulation ticks, suitable for inspecting an untouched map. `--seed` also applies to earlier scenario flags. `--map` exports a standalone SVG with a legend and per-site tooltips. `--json` includes `geography`, finite-stock `region`, `physical` and `industry`. `--walkthrough` chooses viable pharmacy/hardware sites, physically recovers inputs, prepares the fictional clinic treatment and treats Mira. The original five-site fixture remains available with `--scavenging`; the earlier physical, industrial and timed fixtures retain their flags. All disease and treatment content remains fictional.

## Scale and arrangement

- 64 × 64 regional cells, 80 metres per cell: 5.12 × 5.12 km, **10.1214 square miles**.
- A cell represents an abstract regional parcel/frontage. It is not an 80-metre-wide building, a compound tile, or a fully simulated interior. Colored background cells stand in for unmodeled blocks, fields and woodland. Marked facilities are the recoverable sites.
- The center's position, river offset/phase, east-west orientation, grid spacing, town name, site counts, parcel choices and site contents vary with the seed.
- Dense commercial center, civic district, surrounding residential ellipse, industrial corridor between the town and river, agricultural outskirts and woodland. An explicit riverbank district accommodates waterworks.
- Two through arterials and a rural connector cross the river at three bridges. The central grid terminates before the river. Rural tracks connect outlying parcels. All road cells form one network; orthogonal adjacency defines an edge. A site and the compound occupy distinct non-road land parcels with adjacent road access. Sites never occupy water or another site's parcel.
- Placement follows authored district eligibility. Warehouses, workshops, recycling and the data center cluster in the industrial corridor; hospital stays in the civic district; farms and greenhouses stay agricultural. Domestic plots can be roadside neighborhoods rather than uniformly distributed points.

This is one settlement grammar with randomized parameters, not arbitrary continental generation. The river and axes provide shared structure. More town morphologies, coastlines, elevation, named streets, proper road hierarchy/curves and irregular building footprints are future extensions.

## Twenty location types

Each type has three former-use variants, then per-instance quantities, stripping, moisture, condition, faults and source identities. Counts are sampled inclusively from the following ranges, for **50–88 sites**. Every type appears in the experimental baseline. Numbers describe recoverable lots, not the full mass of the former building or contents.

| Type | Count | Placement | Variant / material rationale |
|---|---:|---|---|
| Houses | 14–22 | Residential | Family homes, renovation street, flood damage. Bottle glass, detergent containers, shelves and difficult pipe recovery. |
| Apartments | 5–9 | Center, residential | Walk-ups, maintenance annex, stripped flats. Shared recycling, cloth, copper risers; pipe access is harder than loose goods. |
| Pharmacy | 2–4 | Center, residential | Intact dispensary, picked-over shop, water-damaged stock. Sealed fictional precursor, formulation stock, labelled sterile water, opened alternatives and bottle glass. Picked-over shops lack sealed precursor and sterile water. |
| Hardware | 2–4 | Center, industrial | Plumbing supplier, garden/filter stock, stripped contractor counter. Sorbent, identified HDPE, fittings, pipe, timber, labelled diesel and tools. Stripped counters lack sorbent, diesel and copper pipe. |
| Laboratory | 1–2 | Civic, industrial | Materials testing, teaching lab, collapsed archive. Sorted borosilicate versus ordinary windows, buried contaminated glass and instrument interfaces. Teaching labs lack some instrument stock; collapsed archives lack accessible clean glass. |
| Computer repair | 2–4 | Center, residential | Customer bench, legacy spares, cleared retail floor. Individual compatible parts, incompatible memory, failed silicon, defective coolers and offline media; legacy shops lack the complete modern parts set. |
| Grocery | 3–6 | Center, residential | Dry goods, failed cold chain, wholesale pantry. Sealed dry food only, ordinary jars, pallet timber and cleaning containers. Refrigerated goods are discarded. |
| Clinic | 2–3 | Civic, residential | Outpatient clinic, emergency overflow, evacuated practice. Sterile-labelled water, protective packs, nonsterile borosilicate and furniture steel. |
| Hospital | 1 | Civic | District hospital, isolation ward, evacuation. Larger but condition-sensitive medical supply lots, pathology glass, dismantled frames and a telemetry interface. |
| School | 2–4 | Civic, residential | Secondary, primary, technical college. Furniture, ordinary glazing, cloth, chemistry-room glass or incompatible/compatible loose ICT memory. |
| Library | 1–2 | Civic, center | Reading room, digital archive, shelter. Shelving, radiators and portable collection media. Only the digital-archive variant carries the complete authored offline bundle. |
| Warehouse | 3–5 | Industrial | Packaging, metal stock, cleared dispatch shed. Pallets, racking, HDPE crates and aluminium offcuts in different proportions. |
| Mechanical workshop | 2–4 | Industrial | Vehicle yard, machine repair, stripped tools. Sorted offcuts, rubber hose, service diesel and copper cable. |
| Substation | 1–2 | Industrial, farmland | Distribution yard, rural feeder, damaged switchhouse. Difficult busbar recovery, frames, conductor stock and control hardware. No recovered electricity supply is created automatically. |
| Fuel depot | 1–2 | Industrial | Maintenance store, empty bulk depot, contaminated yard. Closed service cans vary drastically; wet fuel fails generator purity requirements. Bulk tanks are outside recoverable inventory. |
| Water treatment | 1 | Riverbank | Filter works, pump station, flooded plant. Dry filter sorbent, process water, PVC pipe and sensor. Process water is not sterile; PVC is not HDPE. |
| Recycling | 1–2 | Industrial | Sorted recovery, mixed scrap, plastics baler. Soiled HDPE, ordinary bottle glass, steel and aluminium. No laboratory or pharmaceutical stock. |
| Farm | 4–7 | Farmland | Grain farm, equipment barn, abandoned smallholding. Barn timber, labelled fertilizer kept separate from food, maintenance diesel, sealed grain or fittings. |
| Greenhouse | 1–3 | Farmland | Glass nursery, polyhouse, damaged beds. Ordinary glazing, frames, labelled fertilizer and identified containers. Polyhouse variants yield less glass and more pots. |
| Data center | 1 | Industrial | Small hosting facility, cleared racks, water-damaged rooms. Rack steel, cable copper, SATA media and office spares. Server-specific parts are not magically compatible desktop components. |

Stock scales to 55–150% of each authored object lot, rounded to the nearest gram. Some later objects are missing after stripping. Sealed labels are preserved; damp exposed bulk gains a named unassayed moisture/residue contaminant and lower purity. Components remain indivisible at exact model mass. Their initial condition and faults vary. Variants are intentionally modest: not every object differs between all three variants; generic stripping and quantities add instance diversity.

The first pharmacy and hardware are intact starter variants placed among nearby eligible roadside parcels, with sufficient labelled inputs for the initial fictional clinic chain. This is an explicit accessibility guarantee for the experiment, not an accidental generic-loot fallback. Other sites can lack particular goods, and the generator does **not** guarantee a complete functional computing chain. Specialist glass, components, fuel and clean labels still require selecting appropriate sites.

`food.dry`, cloth, aluminium, PVC, rubber and fertilizer are preserved as differentiated inventory materials. Consumption, agriculture, PVC/plastics processing and recipes using those new materials are not implemented yet. Waterworks process-water lots are unassayed and below the existing wash recipe's purity gate; a treatment chain will be needed. Glass processing also remains future work. No extra conversion chain is implied by the existence of a material.

## Distance, integration and persistence

For each site, breadth-first search finds the shortest orthogonal road distance from compound access to site access, then adds both parcel access steps. Each step is 80 m. Routes drawn by the desktop follow that same graph; SVG roads show connected edges. Careful walking uses 4 km/h: at 96 ticks/day (15 minutes/tick), 1,000 m per tick. Round upward to at least one tick. Existing quick-approach timing, on-compound walking, recall, search, exposure, labor/tool/space claims, daily decay and rival depletion retain their expedition rules.

The geography module is immutable and separate from mutable salvage stock. Its snapshot persists the actual cells, roads, parcels, access points, distances, speed, seed, name and generator version. Loading rebuilds path indexes from the saved graph and validates them, without calling the generator. It rejects disconnected roads, duplicate points/parcels, water sites, false bridges, ineligible districts, invalid distances, missing site counterparts, altered travel times and seed/clock mismatches. Older saves stay on their original geography-free fixture.

The generator uses Haldane's stable named RNG streams in a separate local stream collection: terrain, settlement, placement and a content stream for each stable site ID. It consumes no gameplay RNG. Geography calculations use integer coordinates and an integer river wave. Each site's component IDs and archive asset IDs are prefixed with the site ID. Physical media references change with those identities, while capability dependencies (`os`, `python`, `quant-runtime`, etc.) remain semantic capabilities. Multiple recovered collections can coexist without ID collision or a spurious change in compatibility.

Files: `RegionGenerator.cs` owns terrain/roads/placement, `RegionalContent.cs` owns authored types/objects/variation, `RegionModule.cs` owns immutable geography/pathfinding/validation, `GeneratedScenario.cs` binds the region to the existing compound, and `RegionMapExport.cs` exports maps. Desktop region controls use the same module rather than generating a display-only map.

## Verification and next decisions

The solution's **114 simulation tests and 6 desktop tests pass**. New coverage includes a 128-seed sweep; repeatability including zero and maximum seeds; actual shortest-path comparison; size, bounds, zoning, occupied parcels and bridge checks; instance variance; component/media uniqueness and exact mass; clock-dependent walking speed; corrupted geography and false salvage timing; a three-seed clinic loop with active-trip save/load, mass conservation and command replay; real desktop seed input, marker click, expedition planning/dispatch and saved-state restoration. SVG XML and tooltip identities are checked. The map is also rendered through real Avalonia/Skia and visually inspected.

Next useful work: scouting and imperfect information, road/bridge condition and route disruption, expedition movement shown on regional roads, rival territories and accessibility gates. Content should grow through object-level source stories, richer instance variants and missing/rare facility configurations. Full building footprints and interiors can follow without conflating regional parcels with the compound grid. All roads currently remain traversable, every location is revealed, and this is a single-town grammar with guaranteed type coverage.
