using Haldane.Kernel.Randomness;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Region;

/// <summary>Version 1: district-first town, river crossings, street grid, then roadside parcels and authored objects.</summary>
internal static class LegacyRegionGenerator
{
    public const int Version = 1;
    public const int Size = 64;
    public const int CellMeters = 80;
    public static GeneratedRegion Generate(ulong seed, int ticksPerDay = 96)
    {
        if (ticksPerDay is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        var streams = new RngStreams(seed); var terrainRng = streams.Stream("region.v1.terrain");
        var settlementRng = streams.Stream("region.v1.settlement"); var placementRng = streams.Stream("region.v1.placement");
        int cx = terrainRng.NextInt(25, 31), cy = terrainRng.NextInt(23, 30), riverX = terrainRng.NextInt(43, 48);
        bool mirror = terrainRng.Chance(.5); int phase = terrainRng.NextInt(0, 12);
        RegionPoint Transform(RegionPoint p) => mirror ? new(Size - 1 - p.X, p.Y) : p;
        int River(int y) { int t = (y + phase) % 32; return riverX + (t <= 16 ? t : 32 - t) / 4 - 2; }
        var cells = new RegionCell[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            int river = River(y), waterDistance = Math.Abs(x - river); int ellipse = (x - cx) * (x - cx) * 256 + (y - cy) * (y - cy) * 324;
            LandUse district = waterDistance <= 2 ? LandUse.Floodplain
                : x >= cx + 8 && x < river - 2 && y >= cy - 8 && y <= cy + 17 ? LandUse.Industrial
                : Math.Abs(x - cx) <= 7 && y >= cy - 11 && y <= cy - 5 ? LandUse.Civic
                : Math.Abs(x - cx) <= 6 && Math.Abs(y - cy) <= 5 ? LandUse.TownCenter
                : ellipse <= 324 * 256 ? LandUse.Residential
                : y >= cy + 12 || x > river ? LandUse.Farmland : LandUse.Woodland;
            var p = Transform(new(x, y));
            cells[p.Y * Size + p.X] = new(waterDistance == 0 ? RegionalTerrain.River : district == LandUse.Woodland ? RegionalTerrain.Woods : RegionalTerrain.Land, district);
        }
        var roads = new Dictionary<RegionPoint, RoadKind>();
        void Road(int x, int y, RoadKind kind)
        {
            var p = Transform(new(x, y));
            if (cells[p.Y * Size + p.X].Terrain == RegionalTerrain.River) kind = RoadKind.Bridge;
            if (!roads.TryGetValue(p, out var prior) || kind == RoadKind.Bridge || kind == RoadKind.Arterial || prior == RoadKind.Track) roads[p] = kind;
        }
        void Horizontal(int y, int from, int to, RoadKind kind) { for (int x = from; x <= to; x++) Road(x, y, kind); }
        void Vertical(int x, int from, int to, RoadKind kind) { for (int y = from; y <= to; y++) Road(x, y, kind); }
        // Through roads and three deliberate crossings. River cells elsewhere remain impassable.
        Horizontal(cy, 3, 60, RoadKind.Arterial); Horizontal(cy + 14, 3, 60, RoadKind.Arterial);
        Vertical(cx, 3, 60, RoadKind.Arterial); Vertical(riverX + 8, 3, 60, RoadKind.Track);
        Vertical(6, cy, 60, RoadKind.Track); Horizontal(cy + 24, 6, 60, RoadKind.Track);
        // Town grid stops short of the river. Grid offset and density vary without fragmenting roads.
        int spacing = settlementRng.NextInt(4, 6); int left = cx - 15, right = riverX - 5;
        for (int y = cy - 12; y <= cy + 12; y += spacing) Horizontal(y, left, right, RoadKind.Street);
        for (int x = left; x <= right; x += spacing) Vertical(x, cy - 12, cy + 14, RoadKind.Street);
        // Make the last horizontal street connect to the arterial even at the short eastern grid edge.
        Vertical(right, cy - 12, cy + 14, RoadKind.Street);
        var roadSet = roads.Keys.ToHashSet(); var distance = new Dictionary<RegionPoint, int>();
        var parcels = new HashSet<RegionPoint>();
        IEnumerable<RegionPoint> Candidates(LocationType type) => roads.Keys.OrderBy(p => p.Y).ThenBy(p => p.X)
            .SelectMany(RegionModule.Neighbors).Distinct().Where(p => p.X > 1 && p.Y > 1 && p.X < Size - 2 && p.Y < Size - 2
                && !roadSet.Contains(p) && !parcels.Contains(p) && cells[p.Y * Size + p.X].Terrain != RegionalTerrain.River
                && type.Districts.Contains(cells[p.Y * Size + p.X].District));
        var compoundCandidates = Candidates(RegionalContent.Type("homes")!).OrderBy(p => p.Manhattan(Transform(new(cx - 13, cy + 9)))).ThenBy(p => p.Y).ThenBy(p => p.X).Take(8).ToArray();
        var compound = compoundCandidates[placementRng.NextInt(compoundCandidates.Length)]; parcels.Add(compound);
        var compoundAccess = RegionModule.Neighbors(compound).First(roadSet.Contains);
        var queue = new Queue<RegionPoint>(); queue.Enqueue(compoundAccess); distance.Add(compoundAccess, 0);
        while (queue.TryDequeue(out var p)) foreach (var next in RegionModule.Neighbors(p)) if (roadSet.Contains(next) && distance.TryAdd(next, distance[p] + 1)) queue.Enqueue(next);
        var sites = new List<RegionalSite>(); var locations = new List<LocationDefinition>();
        int metersPerTick = Math.Max(1, 4000 * 24 / ticksPerDay);
        foreach (var type in RegionalContent.Types)
        {
            int count = placementRng.NextInt(type.MinimumCount, type.MaximumCount + 1);
            for (int n = 1; n <= count; n++)
            {
                var candidates = Candidates(type).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException($"No roadside parcel in {type.Id}'s districts.");
                bool starter = n == 1 && type.Id is "pharmacy" or "hardware";
                if (starter) candidates = candidates.OrderBy(p => RegionModule.Neighbors(p).Where(roadSet.Contains).Min(a => distance[a])).ThenBy(p => p.Y).ThenBy(p => p.X).Take(6).ToArray();
                var parcel = candidates[placementRng.NextInt(candidates.Length)]; parcels.Add(parcel);
                var access = RegionModule.Neighbors(parcel).Where(roadSet.Contains).OrderBy(p => distance[p]).First();
                string id = $"{type.Id}-{n:000}"; var contentRng = streams.Stream($"region.v1.content/{id}");
                int variant = starter ? 0 : contentRng.NextInt(type.Variants.Count);
                int meters = (distance[access] + 2) * CellMeters;
                var site = new RegionalSite(id, type.Id, type.Variants[variant], parcel, access, meters); sites.Add(site);
                locations.Add(Skills.EducationLoot.Add(RegionalContent.Instantiate(type, id, variant, parcel, Math.Max(1, (meters + metersPerTick - 1) / metersPerTick), cells[parcel.Y * Size + parcel.X], contentRng, starter), type.Id, streams.Stream($"skills.loot/{id}")));
            }
        }
        string[] prefixes = ["Willow", "Ash", "Morrow", "Cedar", "Birch", "Alder", "Mill", "Oak"];
        string name = $"{prefixes[settlementRng.NextInt(prefixes.Length)]} {new[] { "Vale", "Crossing", "Reach", "Bend" }[settlementRng.NextInt(4)]}";
        var geography = new RegionModule(new(Version, seed, name, Size, Size, CellMeters, compound, compoundAccess, metersPerTick, cells,
            roads.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => new RoadCell(p.Key, p.Value)).ToArray(), sites));
        return new(geography, locations.AsReadOnly());
    }
}
