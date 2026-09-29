namespace Scavenger.Sim.Region;

public enum LandUse { TownCenter, Civic, Residential, Industrial, Farmland, Woodland, Floodplain }
public enum RegionalTerrain { Land, Woods, River }
public enum RoadKind { Street, Arterial, Track, Bridge }
public sealed record RegionPoint(int X, int Y)
{
    public int Manhattan(RegionPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}
public sealed record RegionCell(RegionalTerrain Terrain, LandUse District);
public sealed record RoadCell(RegionPoint Point, RoadKind Kind);
public sealed record RegionalSite(string LocationId, string TypeId, string Variant, RegionPoint Parcel,
    RegionPoint Access, int RoadDistanceMeters);
public sealed record RegionSnapshot(int GeneratorVersion, ulong Seed, string Name, int Width, int Height,
    int CellMeters, RegionPoint Compound, RegionPoint CompoundAccess, int WalkingMetersPerTick,
    IReadOnlyList<RegionCell> Cells, IReadOnlyList<RoadCell> Roads, IReadOnlyList<RegionalSite> Sites);
