using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Region;

/// <summary>Immutable saved geography. Road edges are orthogonal adjacencies, each one cell long.</summary>
public sealed class RegionModule : IPersistentModule
{
    public const string Persist = "scavenger.region";
    private readonly RegionSnapshot _snapshot;
    private readonly HashSet<RegionPoint> _roadPoints;
    private readonly Dictionary<RegionPoint, int> _distance;
    public RegionModule(RegionSnapshot snapshot)
    {
        if ((snapshot.GeneratorVersion == 1 ? snapshot.Width != 64 || snapshot.Height != 64 : snapshot.GeneratorVersion != 2 || snapshot.Width != 142 || snapshot.Height != 142) || snapshot.CellMeters != 80
            || snapshot.Cells.Count != snapshot.Width * snapshot.Height || snapshot.WalkingMetersPerTick is < 1 or > 96000
            || string.IsNullOrWhiteSpace(snapshot.Name)) throw new ArgumentException("Invalid regional geography header.");
        _snapshot = snapshot with { Cells = Array.AsReadOnly(snapshot.Cells.Select(c => c with { Cover = c.Cover is null ? null : Array.AsReadOnly(c.Cover.ToArray()) }).ToArray()), Roads = Array.AsReadOnly(snapshot.Roads.ToArray()), Sites = Array.AsReadOnly(snapshot.Sites.ToArray()), Centers = Array.AsReadOnly((snapshot.Centers ?? []).ToArray()) };
        _roadPoints = snapshot.Roads.Select(r => r.Point).ToHashSet();
        Require(snapshot.Roads.Count == _roadPoints.Count && snapshot.Roads.Count > 0 && snapshot.Roads.All(r => Contains(r.Point) && Enum.IsDefined(r.Kind)), "Duplicate or invalid regional road.");
        Require(snapshot.Cells.All(c => Enum.IsDefined(c.Terrain) && Enum.IsDefined(c.District)), "Unknown regional terrain/district.");
        foreach (var cell in Cells)
            Require(cell.ElevationMeters is >= 0 and <= 1000 && cell.Moisture is >= 0 and <= 1 && cell.FloodRisk is >= 0 and <= 1 && cell.Roughness is >= 0 and <= 1
                && (cell.Cover is null ? snapshot.GeneratorVersion == 1 : cell.Cover.Count is > 0 and <= 10 && cell.Cover.Sum(s => s.Fraction) == 1
                    && cell.Cover.Select(s => s.Kind).Distinct().Count() == cell.Cover.Count && cell.Cover.All(s => Enum.IsDefined(s.Kind) && s.Fraction is > 0 and <= 1)), "Invalid regional terrain composition or field.");
        if (snapshot.GeneratorVersion == 2)
            foreach (var cell in Cells)
            {
                var dominant = cell.Cover!.OrderByDescending(s => s.Fraction).ThenBy(s => s.Kind).First().Kind;
                var expected = dominant is TerrainKind.River or TerrainKind.Lake ? RegionalTerrain.River : dominant == TerrainKind.Woodland ? RegionalTerrain.Woods : RegionalTerrain.Land;
                Require(cell.Terrain == expected, "Dominant terrain disagrees with fractional cover.");
            }
        Require(Enum.IsDefined(snapshot.Motif), "Unknown settlement motif.");
        if (snapshot.GeneratorVersion == 2)
            Require(Centers.Count(c => c.Role == SettlementRole.MainTown) == 1 && Centers.Count(c => c.Role == SettlementRole.Suburb) is >= 2 and <= 3
                && Centers.Count(c => c.Role == SettlementRole.SecondaryCenter) == (Motif == SettlementMotif.TwinCenters ? 1 : 0)
                && Centers.All(c => Contains(c.Point) && Enum.IsDefined(c.Role) && !string.IsNullOrWhiteSpace(c.Name))
                && Centers.Select(c => c.Point).Distinct().Count() == Centers.Count, "Invalid settlement centers.");
        Require(Contains(Compound) && Cell(Compound).CanHostSite && !_roadPoints.Contains(Compound)
            && _roadPoints.Contains(snapshot.CompoundAccess) && Compound.Manhattan(snapshot.CompoundAccess) == 1, "Compound needs a land parcel and adjacent road.");
        Require(snapshot.Roads.All(r => Cell(r.Point).NeedsBridge == (r.Kind == RoadKind.Bridge)), "River roads must be explicit bridges; bridges must cross water.");
        _distance = Distances(snapshot.CompoundAccess);
        Require(_distance.Count == snapshot.Roads.Count, "Regional roads must form one connected network.");
        var occupied = new HashSet<RegionPoint> { Compound }; var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in Sites)
            Require(ids.Add(site.LocationId) && !string.IsNullOrWhiteSpace(site.Variant) && Contains(site.Parcel)
                && occupied.Add(site.Parcel) && !_roadPoints.Contains(site.Parcel) && Cell(site.Parcel).CanHostSite
                && _distance.ContainsKey(site.Access) && site.Parcel.Manhattan(site.Access) == 1
                && RegionalContent.Type(site.TypeId) is { } type && type.Districts.Contains(Cell(site.Parcel).District)
                && site.RoadDistanceMeters == (_distance[site.Access] + 2) * CellMeters, "Invalid regional site, district, access or distance.");
        Require(Sites.Count is >= 20 and <= 256 && Sites.Select(s => s.TypeId).Distinct().Count() == RegionalContent.Types.Count, "Region must contain the twenty baseline location types.");
    }
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    public string Name => _snapshot.Name;
    public ulong Seed => _snapshot.Seed;
    public int GeneratorVersion => _snapshot.GeneratorVersion;
    public SettlementMotif Motif => _snapshot.Motif;
    public IReadOnlyList<SettlementCenter> Centers => _snapshot.Centers!;
    public int Width => _snapshot.Width;
    public int Height => _snapshot.Height;
    public int CellMeters => _snapshot.CellMeters;
    public int WalkingMetersPerTick => _snapshot.WalkingMetersPerTick;
    public decimal SquareMiles => Width * Height * CellMeters * CellMeters / 2589988.110336m;
    public RegionPoint Compound => _snapshot.Compound;
    public IReadOnlyList<RegionCell> Cells => _snapshot.Cells;
    public IReadOnlyList<RoadCell> Roads => _snapshot.Roads;
    public IReadOnlyList<RegionalSite> Sites => _snapshot.Sites;
    public bool Contains(RegionPoint p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
    public RegionCell Cell(RegionPoint p) => Contains(p) ? Cells[p.Y * Width + p.X] : throw new ArgumentOutOfRangeException(nameof(p));
    public RegionalSite? Site(string id) => Sites.FirstOrDefault(s => s.LocationId == id);
    public int TravelTicks(RegionalSite site) => Math.Max(1, (site.RoadDistanceMeters + WalkingMetersPerTick - 1) / WalkingMetersPerTick);
    public IReadOnlyList<RegionPoint> Route(string locationId)
    {
        var site = Site(locationId) ?? throw new ArgumentException("Unknown regional site.", nameof(locationId));
        var path = new List<RegionPoint> { site.Parcel, site.Access }; var current = site.Access;
        while (_distance[current] > 0)
        { current = Neighbors(current).First(p => _distance.TryGetValue(p, out int d) && d == _distance[current] - 1); path.Add(current); }
        path.Add(Compound); path.Reverse(); return path.AsReadOnly();
    }
    internal static IEnumerable<RegionPoint> Neighbors(RegionPoint p)
    { yield return new(p.X, p.Y - 1); yield return new(p.X - 1, p.Y); yield return new(p.X + 1, p.Y); yield return new(p.X, p.Y + 1); }
    private Dictionary<RegionPoint, int> Distances(RegionPoint start)
    {
        var distances = new Dictionary<RegionPoint, int> { [start] = 0 }; var pending = new Queue<RegionPoint>(); pending.Enqueue(start);
        while (pending.TryDequeue(out var p))
            foreach (var next in Neighbors(p)) if (_roadPoints.Contains(next) && distances.TryAdd(next, distances[p] + 1)) pending.Enqueue(next);
        return distances;
    }
    public object ToSnapshot() => _snapshot;
    public static void Register(SaveRegistry registry) => registry.RegisterModule<RegionSnapshot>(Persist, (s, v) => v == 1 ? new RegionModule(s) : throw new NotSupportedException($"Unsupported region schema {v}."));
    public static void Validate(World world)
    {
        var geography = world.Module<RegionModule>();
        Require(world.Has<ScavengingModule>(), "Geography requires regional scavenging.");
        Require(geography.Seed == world.Rng.MasterSeed, "Geography seed disagrees with world seed.");
        var scavenging = world.Module<ScavengingModule>();
        Require(scavenging.Locations.Count == geography.Sites.Count, "Geography and salvage locations differ.");
        foreach (var site in geography.Sites)
        {
            var d = scavenging.Location(site.LocationId)?.Definition;
            Require(d is not null && d.X == site.Parcel.X && d.Y == site.Parcel.Y && d.TravelTicks == geography.TravelTicks(site), "Salvage coordinates/travel disagree with saved geography.");
        }
        Require(geography.WalkingMetersPerTick == Math.Max(1, 4000 * 24 / world.Clock.Time.TicksPerDay), "Regional walking speed disagrees with simulation clock.");
    }
    private static void Require(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); }
}
