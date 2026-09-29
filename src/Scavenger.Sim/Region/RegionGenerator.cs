using Haldane.Kernel.Randomness;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Region;

public sealed record GeneratedRegion(RegionModule Geography, IReadOnlyList<LocationDefinition> Locations);

public static class RegionGenerator
{
    public const int Version = 2;
    public const int Size = 142;
    public const int CellMeters = 80;
    public static GeneratedRegion GenerateLegacy(ulong seed, int ticksPerDay = 96) => LegacyRegionGenerator.Generate(seed, ticksPerDay);
    public static GeneratedRegion Generate(ulong seed, int ticksPerDay = 96, SettlementMotif? motif = null)
    {
        if (ticksPerDay is < 1 or > 100000 || motif is { } m && !Enum.IsDefined(m)) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        var streams = new RngStreams(seed); var settlement = streams.Stream("region.v2.settlement");
        var placement = streams.Stream("region.v2.placement");
        var rolledMotif = (SettlementMotif)settlement.NextInt(3); var layout = motif ?? rolledMotif;
        int cx = settlement.NextInt(54, 63), cy = settlement.NextInt(58, 68), river = settlement.NextInt(102, 110);
        bool mirror = settlement.Chance(.5), rotate = settlement.Chance(.5);
        RegionPoint Transform(RegionPoint p)
        { if (mirror) p = new(Size - 1 - p.X, p.Y); return rotate ? new(p.Y, Size - 1 - p.X) : p; }
        var centers = new List<SettlementCenter> { new("Main town", new(cx, cy), SettlementRole.MainTown) };
        if (layout == SettlementMotif.TwinCenters) centers.Add(new("Old junction", new(cx + 23, cy - 5), SettlementRole.SecondaryCenter));
        centers.Add(new("South suburb", new(cx - 30 + settlement.NextInt(-3, 4), cy + 34), SettlementRole.Suburb));
        centers.Add(new("Riverside suburb", new(cx + 28, cy + 36), SettlementRole.Suburb));
        if (settlement.Chance(.65)) centers.Add(new("North suburb", new(cx - 18, cy - 37), SettlementRole.Suburb));
        var landscape = new RegionalLandscape(streams.Stream("region.v2.landscape"), cx, cy, river, layout, centers);
        var cells = new RegionCell[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        { var p = Transform(new(x, y)); cells[p.Y * Size + p.X] = landscape.Cell(x, y); }
        var roads = new Dictionary<RegionPoint, RoadKind>();
        void Road(int x, int y, RoadKind kind)
        {
            var p = Transform(new(x, y));
            if (cells[p.Y * Size + p.X].NeedsBridge) kind = RoadKind.Bridge;
            if (!roads.TryGetValue(p, out var prior) || kind == RoadKind.Bridge || kind == RoadKind.Arterial || prior == RoadKind.Track) roads[p] = kind;
        }
        void Horizontal(int y, int from, int to, RoadKind kind) { for (int x = from; x <= to; x++) Road(x, y, kind); }
        void Vertical(int x, int from, int to, RoadKind kind) { for (int y = from; y <= to; y++) Road(x, y, kind); }
        void Connect(RegionPoint center)
        {
            var transformed = Transform(center);
            var nearest = roads.Keys.OrderBy(p => p.Manhattan(transformed)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
            // Find the untransformed coordinate without depending on orientation in road construction.
            int nx = nearest.X, ny = nearest.Y;
            if (rotate) { int old = nx; nx = Size - 1 - ny; ny = old; }
            if (mirror) nx = Size - 1 - nx;
            Horizontal(center.Y, Math.Min(center.X, nx), Math.Max(center.X, nx), RoadKind.Street);
            Vertical(nx, Math.Min(center.Y, ny), Math.Max(center.Y, ny), RoadKind.Street);
        }
        void Grid(RegionPoint center, int rx, int ry, int spacing)
        {
            Connect(center);
            Vertical(center.X, center.Y - ry, center.Y + ry, RoadKind.Street);
            Horizontal(center.Y, center.X - rx, center.X + rx, RoadKind.Street);
            for (int y = center.Y - ry; y <= center.Y + ry; y += spacing) Horizontal(y, center.X - rx, center.X + rx, RoadKind.Street);
            for (int x = center.X - rx; x <= center.X + rx; x += spacing) Vertical(x, center.Y - ry, center.Y + ry, RoadKind.Street);
        }
        Horizontal(cy, 3, Size - 4, RoadKind.Arterial); Horizontal(cy + 30, 3, Size - 4, RoadKind.Arterial);
        Vertical(cx, 3, Size - 4, RoadKind.Arterial); Vertical(river + 17, 3, Size - 4, RoadKind.Track);
        Vertical(10, cy, Size - 4, RoadKind.Track); Horizontal(cy + 52, 10, Size - 4, RoadKind.Track);
        int spacing = settlement.NextInt(4, 7);
        if (layout == SettlementMotif.Avenue) Grid(new(cx, cy), 33, 14, spacing);
        else if (layout == SettlementMotif.TwinCenters) { Grid(new(cx, cy), 18, 18, spacing); Grid(centers[1].Point, 16, 15, spacing); }
        else Grid(new(cx, cy), 23, 21, spacing);
        Grid(new(river - 13, cy + 8), 9, 22, 6);
        foreach (var suburb in centers.Where(c => c.Role == SettlementRole.Suburb)) Grid(suburb.Point, 8, 7, 4);
        var roadSet = roads.Keys.ToHashSet(); var distance = new Dictionary<RegionPoint, int>(); var occupied = new HashSet<RegionPoint>();
        var frontage = roads.Keys.OrderBy(p => p.Y).ThenBy(p => p.X).SelectMany(RegionModule.Neighbors).Distinct()
            .Where(p => p.X > 1 && p.Y > 1 && p.X < Size - 2 && p.Y < Size - 2 && !roadSet.Contains(p) && cells[p.Y * Size + p.X].CanHostSite).ToArray();
        IEnumerable<RegionPoint> Candidates(LocationType type) => frontage.Where(p => !occupied.Contains(p) && type.Districts.Contains(cells[p.Y * Size + p.X].District));
        var compoundCandidates = Candidates(RegionalContent.Type("homes")!).OrderBy(p => p.Manhattan(Transform(centers.First(c => c.Role == SettlementRole.Suburb).Point))).ThenBy(p => p.Y).ThenBy(p => p.X).Take(8).ToArray();
        var compound = compoundCandidates[placement.NextInt(compoundCandidates.Length)]; occupied.Add(compound);
        var compoundAccess = RegionModule.Neighbors(compound).First(roadSet.Contains);
        var queue = new Queue<RegionPoint>(); queue.Enqueue(compoundAccess); distance.Add(compoundAccess, 0);
        while (queue.TryDequeue(out var p)) foreach (var next in RegionModule.Neighbors(p)) if (roadSet.Contains(next) && distance.TryAdd(next, distance[p] + 1)) queue.Enqueue(next);
        var sites = new List<RegionalSite>(); var locations = new List<LocationDefinition>(); int speed = Math.Max(1, 4000 * 24 / ticksPerDay);
        var suburbs = centers.Where(c => c.Role == SettlementRole.Suburb).Select(c => Transform(c.Point)).ToArray();
        foreach (var type in RegionalContent.Types)
        {
            int multiplier = type.Id is "homes" or "apartments" or "farm" or "grocery" or "school" or "warehouse" ? 2 : 1;
            int count = placement.NextInt(type.MinimumCount * multiplier, type.MaximumCount * multiplier + 1);
            for (int n = 1; n <= count; n++)
            {
                var candidates = Candidates(type).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException($"No suitable frontage in {type.Id}'s districts.");
                // Guarantee inhabited outlying clusters rather than only drawing empty suburban zoning.
                if (type.Id == "homes" && n <= suburbs.Length)
                    candidates = candidates.OrderBy(p => p.Manhattan(suburbs[n - 1])).ThenBy(p => p.Y).ThenBy(p => p.X).Take(8).ToArray();
                bool starter = n == 1 && type.Id is "pharmacy" or "hardware";
                if (starter) candidates = candidates.OrderBy(p => RegionModule.Neighbors(p).Where(roadSet.Contains).Min(a => distance[a])).ThenBy(p => p.Y).ThenBy(p => p.X).Take(6).ToArray();
                var parcel = candidates[placement.NextInt(candidates.Length)]; occupied.Add(parcel);
                var access = RegionModule.Neighbors(parcel).Where(roadSet.Contains).OrderBy(p => distance[p]).First();
                string id = $"{type.Id}-{n:000}"; var content = streams.Stream($"region.v2.content/{id}"); int variant = starter ? 0 : content.NextInt(type.Variants.Count);
                int meters = (distance[access] + 2) * CellMeters;
                sites.Add(new(id, type.Id, type.Variants[variant], parcel, access, meters));
                locations.Add(RegionalContent.Instantiate(type, id, variant, parcel, (meters + speed - 1) / speed, cells[parcel.Y * Size + parcel.X], content, starter));
            }
        }
        string[] names = ["Willow", "Ash", "Morrow", "Cedar", "Birch", "Alder", "Mill", "Oak"];
        string name = $"{names[settlement.NextInt(names.Length)]} {new[] { "Vale", "Crossing", "Reach", "Bend" }[settlement.NextInt(4)]}";
        var geography = new RegionModule(new(Version, seed, name, Size, Size, CellMeters, compound, compoundAccess, speed, cells,
            roads.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => new RoadCell(p.Key, p.Value)).ToArray(), sites, layout,
            centers.Select(c => c with { Point = Transform(c.Point) }).ToArray()));
        return new(geography, locations.AsReadOnly());
    }
}
