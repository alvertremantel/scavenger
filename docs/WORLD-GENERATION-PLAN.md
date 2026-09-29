# Regional world generation — provisional plan

Goal: a deterministic bounded 2D town and its surroundings, approximately ten square miles, twenty authored location types, varied individual inventories, coherent zoning and connected roads, playable through existing expeditions.

## Geography and settlement

Use a 64 × 64 grid of 80 m cells (5.12 km per side, 10.12 square miles). Cells represent regional parcels, not compound tiles or building interiors. Generate a river, a town center, civic and residential districts, an industrial corridor, farmland and woodland. Streets follow settlement density: central grid, suburban connectors, rural arterials. Bridges are explicit crossings. Sites occupy distinct land parcels with adjacent road access. Distance is shortest road travel plus parcel access; expedition ticks convert metres at an explicit walking speed and the simulation clock's tick duration.

## Content

Twenty authored types: houses, apartments, pharmacy, hardware, laboratory, computer repair, grocery, clinic, hospital, school, library, warehouse, workshop, substation, fuel depot, water treatment, recycling, farm, greenhouse and data center. At least one instance of each establishes the experimental baseline; common types repeat more often. Variants alter former use and object selection, then seed-based quantity, condition, stripping and moisture alter each instance. No generic loot bucket. Component and archive identities must be unique across instances; whole components retain exact model mass. Preserve source labels and purity gates, and put specialist materials where the former activity explains them.

## Integration and acceptance

- Generated scenario uses the same expedition/clinic/computing systems; keep the five-site fixture for regression tests and walkthroughs.
- Persist generated cells, access points, roads and content; load does not regenerate or replenish stock. Version generator separately from save schema.
- Desktop map supports selecting a site and inspecting its actual finite salvage through expedition planning. Seed input creates a new world. CLI exports regional JSON and a standalone map.
- Verify multiple seeds, exact repeatability, diversity, zoning, bounds, parcel uniqueness, network reachability, bridge constraints, road-derived timing, content identities, mass conservation, save/load and command replay. Render and inspect real desktop/map output.

Future questions: sparse/rare type availability instead of the baseline guarantee; discovery/fog of war; damaged or repaired bridges; rival territories; vehicles; full building footprints/interiors; terrain elevation. These do not block the first playable regional generator.
