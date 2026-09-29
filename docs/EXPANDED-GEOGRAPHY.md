# Expanded regions and fractional terrain — provisional implementation

Version-one world generation is committed (`7001b0f`) and merged into `main` (`f756f89`). This expansion lives on `feature/expanded-regional-geography`. Haldane and its revision pin remain unchanged.

## Explore

```sh
dotnet run --project src/Scavenger.Desktop
dotnet run --project src/Scavenger.Cli -- --world --seed 42 --layout avenue --json --map artifacts/avenue.svg
dotnet run --project src/Scavenger.Cli -- --world --seed 42 --layout twin-centers --walkthrough --roundtrip --ticks 400
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/expanded-compact.png --ticks 0 --view region --seed 42 --layout compact
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/expanded-avenue.png --ticks 0 --view region --seed 42 --layout avenue
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/expanded-twin.png --ticks 0 --view region --seed 42 --layout twin-centers
dotnet run --project src/Scavenger.Desktop -- --screenshot artifacts/expanded-flood.png --ticks 0 --view region --seed 42 --layout avenue --layer FloodRisk
```

Desktop **Region** offers a seed and layout choice, facility highlighting, five map layers and zoom/fit. Click any cell to see terrain percentages and environmental fields. Click a site to inspect actual finite stock and its road route; **Plan expedition here** opens the existing scavenging controls. Zoom centers on the selected cell, or the compound if none is selected; fit restores the whole region. At full regional scale markers are small, so zoom or type highlighting exposes facility symbols.

`--layout` accepts `compact`, `avenue` or `twin-centers`; omission lets the seed choose. `--layer` accepts Terrain, Districts, Elevation, Moisture or FloodRisk. Both screenshot and CLI map export support these choices. SVG cell tooltips report percentages and fields; site tooltips identify individual source inventories. JSON saves/reports include actual fractional cells and settlement centers.

## Fifty square miles and town structure

The new grid is **142 × 142 × 80 m**, covering 11.36 × 11.36 km, **49.8263 square miles**. Cells remain regional parcels/frontage, distinct from compound tiles and building interiors. There is one main town, two or three detached suburban clusters, surrounding industrial land and countryside. The compound begins in a suburban residential cluster. Every suburb receives a housing salvage site rather than existing only as colored background.

| Motif | Organization |
|---|---|
| Compact | Irregular commercial/civic core and broad surrounding neighborhoods. |
| Avenue | Long commercial spine along the main avenue, narrower adjacent residential districts and parallel/local streets. It can orient horizontally or vertically. |
| Twin centers | Main town plus an old junction within the same town, with separate commercial cores and overlapping/connected neighborhoods. The secondary center is not a second independent regional city. |

District shapes are evaluated against the actual motif and center positions. Correlated boundary variation makes edges uneven. Civic uses remain near the main core; an industrial corridor follows the river side of town. Detached suburbs get smaller grids and connectors. Rural roads join agricultural frontage, outlying parcels and crossings. Roads are connected orthogonal cells, with shortest-path travel still used by actual expeditions. Reflections and rotations vary orientation without changing the meaning of locations or road distance.

Common housing, apartments, farms, groceries, schools and warehouses have twice the previous count range; specialist facilities retain their counts. Regions contain **81–141 authored salvage sites across twenty types**. The [original object/content rationale](WORLD-GENERATION.md#twenty-location-types) still applies: sites vary in former use, surviving objects, amounts, labels, contamination and component faults. A nearby intact pharmacy and hardware still provide the initial fictional treatment path. Working computing chains are not guaranteed. Moisture and flood susceptibility now also influence damp salvage, without erasing sealed provenance or creating purity.

## Cells describe mixtures

Each 80 m square is sampled on a **5 × 5 grid of 16 m squares**. Counts become exact coverage fractions totaling 1, in 4% increments. For example, 44% river, 32% hills and 24% meadow is a valid cell representation. Generated riverbanks generally mix water with marsh, meadow or developed ground because this first hydrology model describes a lowland river. Terrain classes are:

- River and lake/open water.
- Woodland and meadow.
- Hills, exposed rock and dry scrub.
- Marsh/wet ground.
- Cropland and built ground.

These are area-cover categories, not a full ecological taxonomy. Hills represent exposed hill terrain; wooded slopes can instead have woodland cover and elevated roughness. Elevation and roughness are separate from coverage, so a forest can still be difficult ground. Neither coverage nor dominant labels assert that an entire parcel has a single ecological/physical property.

The generator uses stable named RNG streams and integer smoothstep interpolation over correlated fields at several scales. Broad elevation, smaller terrain detail, moisture, vegetation and district-edge fields shape patches. A meandering river runs across the region, changes width and influences bank elevation, moisture and flooding. Two small lakes create additional shoreline. Cultivation follows suitable rural ground; forest, scrub, marsh, meadow, rock and hills form irregular patches rather than one straight woodland/farmland division.

Cells retain mean elevation in metres, normalized moisture, static flood susceptibility and ground roughness. Terrain colors mix the actual area percentages. Other layers display district identity or continuous environmental fields. Values are synthetic gameplay descriptors; flood susceptibility is not a forecast, rainfall simulation or real engineering assessment. Subcell sample arrangements are aggregated, not retained as a second walkable map. Sampling can miss features below sixteen metres and percentages carry no survey accuracy claim.

## Constraints now, equipment choices later

Sites and compound parcels require at least 84% non-water ground, at most 60% marsh and roughness at most 85%; these are explicit experimental placement gates. A site in a partly wet cell means its recoverable frontage fits the dry part, not that the whole building footprint is simulated. Every road cell with any river/lake coverage is marked as a bridge, including cells where water is not dominant. This is a conservative cell-level crossing abstraction: no exact bridge span or road alignment within the cell is modeled.

Current roads remain traversable and all salvage sites remain road-served. Coverage, moisture, roughness, elevation and flood susceptibility are now persisted inputs for future washed-out roads, marsh bypasses, climbing, boats, protective equipment, cargo penalties or off-road routes. No damaged-road or off-road task/equipment rule is active yet. A sensible next implementation can assess a route's water exposure, wet ground, ascent and roughness separately, then compare alternative teams and kit instead of deriving all difficulty from one terrain label.

## Persistence and versioning

The generator is version two; the geography save module keeps schema one through optional appended fields. Version-one 64 × 64 saves load with their original geography and categorical terrain. Their missing environmental data is shown as unrecorded, not treated as a new terrain survey. `LegacyRegionGenerator` preserves the previous algorithm. Resetting a loaded old region repeats the old size and seed; explicitly making a new world uses version two. Forced motif choice survives save/load/reset.

Loading never regenerates salvage or geography. It validates fractional totals, unique known cover kinds, environmental ranges, dominant-label consistency, settlement roles, parcels, road connectivity, partial-water bridge requirements, shortest distances and clock/seed consistency. Nested cover collections are defensively copied. Component/media identities remain scoped to individual site IDs, with capabilities independent from asset identity. Generation consumes no gameplay RNG.

## Validation and remaining design work

The final build has zero warnings/errors, and **123 simulation tests plus 7 desktop tests pass (130 total)**. The audit includes all original simulation/desktop regressions, 48 naturally selected seeds, twelve seeds per forced motif, repeatability including extreme seeds, real shortest-path verification, populated suburbs and actual layout footprint differences, rich mixed cover and smooth elevation, partial-water bridges, invalid composition rejection, immutable snapshot collections, old save compatibility, forced-layout reset, generated medicine/archives recovery, mass conservation and command replay. Real desktop input exercises layout selection, arbitrary-cell inspection, layers, zoom/fit and expedition planning. Representative compact, avenue, twin-center and flood maps are rendered with Avalonia/Skia and inspected.

The next design choices remain open: different ecology/climate profiles; roads that follow terrain rather than generally straight survey grids; exact bridge placement/durability; discovered versus unknown environmental information; faction territory; route-specific mobility and equipment; terrain-informed farming and resource processes; more elaborate town histories. This remains one bounded region with a main settlement and surrounding suburbs, not a full national geography generator.

For the seed-42 twin-center audit, the map has 121 sites, 2,220 mixed-cover cells and
20,164 cell tooltips in the exported SVG. Tracked mass stays exactly 4,115.549 kg
through the clinic/save-load walkthrough, and all four residents finish healthy.
