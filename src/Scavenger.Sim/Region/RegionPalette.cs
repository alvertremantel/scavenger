using System.Globalization;

namespace Scavenger.Sim.Region;

/// <summary>Shared desktop/export colors; terrain pixels blend actual coverage rather than dominant labels.</summary>
public static class RegionPalette
{
    public static string TerrainColor(TerrainKind kind) => kind switch {
        TerrainKind.River => "#398FAE", TerrainKind.Lake => "#3E7FAD", TerrainKind.Woodland => "#345944", TerrainKind.Meadow => "#82916A",
        TerrainKind.Hills => "#92896A", TerrainKind.Marsh => "#537E70", TerrainKind.Scrub => "#A09568", TerrainKind.Rock => "#85888A",
        TerrainKind.Cropland => "#A49E5E", _ => "#69757C" };
    private static (int R, int G, int B) Rgb(string hex) => (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));
    private static string Hex(decimal r, decimal g, decimal b) => $"#{Math.Clamp((int)r, 0, 255):X2}{Math.Clamp((int)g, 0, 255):X2}{Math.Clamp((int)b, 0, 255):X2}";
    private static string Blend(string a, string b, decimal weight)
    { var ca = Rgb(a); var cb = Rgb(b); return Hex(ca.R * (1 - weight) + cb.R * weight, ca.G * (1 - weight) + cb.G * weight, ca.B * (1 - weight) + cb.B * weight); }
    public static string Color(RegionCell cell, MapLayer layer)
    {
        if (layer == MapLayer.Elevation) return Blend("#344D5C", "#E4D8B1", Math.Clamp(cell.ElevationMeters / 230m, 0, 1));
        if (layer == MapLayer.Moisture) return Blend("#B5A572", "#327F9E", cell.Moisture);
        if (layer == MapLayer.FloodRisk) return Blend("#3C5550", "#F2A265", cell.FloodRisk);
        decimal r = 0, g = 0, b = 0;
        foreach (var kind in Enum.GetValues<TerrainKind>())
        { var rgb = Rgb(TerrainColor(kind)); decimal f = cell.Fraction(kind); r += rgb.R * f; g += rgb.G * f; b += rgb.B * f; }
        var terrain = Hex(r, g, b);
        if (layer == MapLayer.Districts) return Blend(terrain, RegionMapExport.DistrictColor(cell.District), .65m);
        // Development and natural terrain meet through actual fractional coverage; civic/industrial identity stays legible.
        return Blend(terrain, RegionMapExport.DistrictColor(cell.District), cell.Fraction(TerrainKind.BuiltGround) * .55m);
    }
}
