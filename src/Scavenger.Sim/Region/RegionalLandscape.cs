using Haldane.Kernel.Randomness;

namespace Scavenger.Sim.Region;

/// <summary>Correlated fields sampled at 16 m resolution, aggregated into 80 m parcels.</summary>
internal sealed class RegionalLandscape
{
    private const int Samples = 5;
    private readonly NoiseField _height, _detail, _wet, _vegetation, _edge;
    private readonly int[] _river;
    private readonly int _riverBase, _cx, _cy;
    private readonly SettlementMotif _motif;
    private readonly IReadOnlyList<SettlementCenter> _centers;
    private readonly (int X, int Y, int Rx, int Ry)[] _ponds;
    public RegionalLandscape(Rng rng, int cx, int cy, int riverBase, SettlementMotif motif, IReadOnlyList<SettlementCenter> centers)
    {
        _cx = cx; _cy = cy; _riverBase = riverBase; _motif = motif; _centers = centers;
        _height = new(rng, 42); _detail = new(rng, 12); _wet = new(rng, 30); _vegetation = new(rng, 19); _edge = new(rng, 8);
        _river = Enumerable.Range(0, RegionGenerator.Size / 12 + 2).Select(_ => riverBase * Samples + rng.NextInt(-30, 31)).ToArray();
        _ponds = [(rng.NextInt(15, 30) * Samples, rng.NextInt(20, 38) * Samples, 17, 12),
            (rng.NextInt(120, 133) * Samples, rng.NextInt(95, 124) * Samples, 12, 20)];
    }
    private int RiverX(int y)
    {
        int step = 12 * Samples, index = y / step, fraction = y % step;
        return (_river[index] * (step - fraction) + _river[index + 1] * fraction) / step;
    }
    private static bool Ellipse(int dx, int dy, int rx, int ry) => dx * dx * ry * ry + dy * dy * rx * rx <= rx * rx * ry * ry;
    private LandUse District(int x, int y, int riverDistance, int vegetation, int elevation, int moisture)
    {
        int edge = (_edge.Value(x, y) - 500) / 100, px = x / Samples, py = y / Samples;
        if (riverDistance < (4 + Math.Max(0, edge)) * Samples) return LandUse.Floodplain;
        if (Ellipse(px - (_cx - 3), py - (_cy - 11), 9 + edge, 6 + edge / 2)) return LandUse.Civic;
        foreach (var center in _centers)
        {
            if (center.Role == SettlementRole.Suburb) continue;
            int dx = px - center.Point.X, dy = py - center.Point.Y;
            bool core = center.Role == SettlementRole.MainTown && _motif == SettlementMotif.Avenue
                ? Ellipse(Math.Max(0, Math.Abs(dx) - 25), dy, 6 + edge, 5 + edge / 2)
                : Ellipse(dx, dy, 8 + edge, 7 + edge / 2);
            if (core) return LandUse.TownCenter;
        }
        if (Ellipse(px - (_riverBase - 13), py - (_cy + 8), 10 + edge, 22 + edge)) return LandUse.Industrial;
        foreach (var center in _centers)
        {
            int dx = px - center.Point.X, dy = py - center.Point.Y;
            int radius = center.Role == SettlementRole.Suburb ? 9 : 21;
            if (center.Role == SettlementRole.MainTown && _motif == SettlementMotif.Avenue)
            { if (Ellipse(Math.Max(0, Math.Abs(dx) - 26), dy, 13 + edge, 15 + edge)) return LandUse.Residential; }
            else if (Ellipse(dx, dy, radius + edge, radius - 2 + edge)) return LandUse.Residential;
        }
        return elevation < 160 && moisture is > 260 and < 830 && vegetation < 660 ? LandUse.Farmland : LandUse.Woodland;
    }
    public RegionCell Cell(int x, int y)
    {
        var covers = new int[Enum.GetValues<TerrainKind>().Length]; var districts = new int[Enum.GetValues<LandUse>().Length];
        int heights = 0, wetness = 0, risk = 0, roughness = 0;
        for (int sy = 0; sy < Samples; sy++) for (int sx = 0; sx < Samples; sx++)
        {
            int px = x * Samples + sx, py = y * Samples + sy;
            int distance = Math.Abs(px - RiverX(py)), vegetation = _vegetation.Value(px, py), moisture = Math.Clamp(_wet.Value(px, py) + Math.Max(0, 300 - distance * 12), 0, 1000);
            int elevation = 25 + _height.Value(px, py) * 140 / 1000 + _detail.Value(px, py) * 40 / 1000 + (RegionGenerator.Size - y) / 5;
            int riverElevation = 32 - y / 8;
            if (distance < 80) elevation = (elevation * distance + (riverElevation + distance / 2) * (80 - distance)) / 80;
            bool river = distance <= 3 + _edge.Value(px, py) / 300;
            bool lake = false;
            foreach (var pond in _ponds) if (Ellipse(px - pond.X, py - pond.Y, pond.Rx, pond.Ry)) { lake = true; break; }
            var district = District(px, py, distance, vegetation, elevation, moisture);
            int rough = Math.Clamp((elevation - 90) * 5 + _detail.Value(px, py) / 3, 0, 1000);
            TerrainKind kind = river ? TerrainKind.River : lake ? TerrainKind.Lake
                : district is LandUse.TownCenter or LandUse.Civic or LandUse.Residential or LandUse.Industrial ? TerrainKind.BuiltGround
                : moisture > 800 || distance < 20 && moisture > 650 ? TerrainKind.Marsh
                : elevation > 175 && _detail.Value(px, py) > 600 ? TerrainKind.Rock
                : district == LandUse.Farmland && vegetation < 610 ? TerrainKind.Cropland
                : elevation > 145 && vegetation < 480 ? TerrainKind.Hills
                : moisture < 350 ? TerrainKind.Scrub
                : vegetation > 440 ? TerrainKind.Woodland : TerrainKind.Meadow;
            int flood = river || lake ? 1000 : Math.Clamp(600 - distance * 14 + (100 - elevation) * 2 + (kind == TerrainKind.Marsh ? 200 : 0), 0, 950);
            covers[(int)kind]++; districts[(int)district]++;
            heights += river ? riverElevation : elevation; wetness += river || lake ? 1000 : moisture; risk += flood; roughness += river || lake ? 0 : rough;
        }
        var shares = covers.Select((count, kind) => new TerrainShare((TerrainKind)kind, count / 25m)).Where(s => s.Fraction > 0).ToArray();
        var dominant = shares.OrderByDescending(s => s.Fraction).ThenBy(s => s.Kind).First().Kind;
        var landUse = (LandUse)Enumerable.Range(0, districts.Length).OrderByDescending(i => districts[i]).ThenBy(i => i).First();
        return new(dominant is TerrainKind.River or TerrainKind.Lake ? RegionalTerrain.River : dominant == TerrainKind.Woodland ? RegionalTerrain.Woods : RegionalTerrain.Land,
            landUse, Array.AsReadOnly(shares), heights / 25, wetness / 25000m, risk / 25000m, roughness / 25000m);
    }
    private sealed class NoiseField
    {
        private readonly int _step, _width;
        private readonly int[] _values;
        public NoiseField(Rng rng, int cells)
        {
            _step = cells * Samples; _width = RegionGenerator.Size / cells + 2;
            _values = Enumerable.Range(0, _width * _width).Select(_ => rng.NextInt(0, 1001)).ToArray();
        }
        public int Value(int x, int y)
        {
            int ix = x / _step, iy = y / _step, fx = x % _step, fy = y % _step;
            // Smoothstep keeps field derivatives from changing sharply at lattice boundaries.
            int wx = fx * fx * (3 * _step - 2 * fx) / (_step * _step);
            int wy = fy * fy * (3 * _step - 2 * fy) / (_step * _step);
            int a = (_values[iy * _width + ix] * (_step - wx) + _values[iy * _width + ix + 1] * wx) / _step;
            int b = (_values[(iy + 1) * _width + ix] * (_step - wx) + _values[(iy + 1) * _width + ix + 1] * wx) / _step;
            return (a * (_step - wy) + b * wy) / _step;
        }
    }
}
