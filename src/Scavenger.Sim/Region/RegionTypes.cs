namespace Scavenger.Sim.Region;

public enum LandUse { TownCenter, Civic, Residential, Industrial, Farmland, Woodland, Floodplain }
public enum RegionalTerrain { Land, Woods, River }
public enum SettlementMotif { Compact, Avenue, TwinCenters }
public enum SettlementRole { MainTown, SecondaryCenter, Suburb }
public enum TerrainKind { River, Lake, Woodland, Meadow, Hills, Marsh, Scrub, Rock, Cropland, BuiltGround }
public enum MapLayer { Terrain, Districts, Elevation, Moisture, FloodRisk }
public sealed record TerrainShare(TerrainKind Kind, decimal Fraction);
public sealed record SettlementCenter(string Name, RegionPoint Point, SettlementRole Role);
public enum RoadKind { Street, Arterial, Track, Bridge }
public sealed record RegionPoint(int X, int Y)
{
    public int Manhattan(RegionPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}
public sealed record RegionCell(RegionalTerrain Terrain, LandUse District, IReadOnlyList<TerrainShare>? Cover = null,
    int ElevationMeters = 0, decimal Moisture = 0, decimal FloodRisk = 0, decimal Roughness = 0)
{
    public decimal Fraction(TerrainKind kind) => Cover?.Where(s => s.Kind == kind).Sum(s => s.Fraction)
        ?? (kind == (Terrain == RegionalTerrain.River ? TerrainKind.River : Terrain == RegionalTerrain.Woods ? TerrainKind.Woodland : TerrainKind.Meadow) ? 1 : 0);
    [System.Text.Json.Serialization.JsonIgnore] public decimal WaterFraction => Fraction(TerrainKind.River) + Fraction(TerrainKind.Lake);
    [System.Text.Json.Serialization.JsonIgnore] public bool NeedsBridge => WaterFraction > 0;
    [System.Text.Json.Serialization.JsonIgnore] public bool CanHostSite => WaterFraction <= .16m && Fraction(TerrainKind.Marsh) <= .6m && Roughness <= .85m;
}
public sealed record RoadCell(RegionPoint Point, RoadKind Kind);
public sealed record RegionalSite(string LocationId, string TypeId, string Variant, RegionPoint Parcel,
    RegionPoint Access, int RoadDistanceMeters);
public sealed record RegionSnapshot(int GeneratorVersion, ulong Seed, string Name, int Width, int Height,
    int CellMeters, RegionPoint Compound, RegionPoint CompoundAccess, int WalkingMetersPerTick,
    IReadOnlyList<RegionCell> Cells, IReadOnlyList<RoadCell> Roads, IReadOnlyList<RegionalSite> Sites,
    SettlementMotif Motif = SettlementMotif.Compact, IReadOnlyList<SettlementCenter>? Centers = null);
