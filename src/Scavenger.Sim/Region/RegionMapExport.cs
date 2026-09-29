using System.Globalization;
using System.Security;
using System.Text;

namespace Scavenger.Sim.Region;

public static class RegionMapExport
{
    public static string DistrictColor(LandUse d) => d switch { LandUse.TownCenter => "#67717c", LandUse.Civic => "#536878", LandUse.Residential => "#43595c",
        LandUse.Industrial => "#705e50", LandUse.Farmland => "#667347", LandUse.Woodland => "#304c3e", _ => "#3a6965" };
    public static string Svg(RegionModule region, MapLayer layer = MapLayer.Terrain)
    {
        int cell = region.Width > 64 ? 6 : 10, pad = 24, mapWidth = region.Width * cell, mapHeight = region.Height * cell;
        int width = mapWidth + 360, height = Math.Max(mapHeight + 110, 980), legendX = mapWidth + 50;
        var b = new StringBuilder();
        b.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"#18232d\"/><g font-family=\"sans-serif\" fill=\"#e4edf0\">");
        b.Append($"<text x=\"24\" y=\"28\" font-size=\"20\">{Escape(region.Name)} · seed {region.Seed} · {region.Motif}</text><text x=\"24\" y=\"50\" font-size=\"12\">{F(region.Width * region.CellMeters / 1000m)} × {F(region.Height * region.CellMeters / 1000m)} km · {F(region.SquareMiles)} square miles · {region.Sites.Count} sites · {layer} · north ↑</text>");
        int X(RegionPoint p) => pad + p.X * cell + cell / 2; int Y(RegionPoint p) => 70 + p.Y * cell + cell / 2;
        for (int y = 0; y < region.Height; y++) for (int x = 0; x < region.Width; x++)
        {
            var c = region.Cell(new(x, y));
            string cover = string.Join(", ", Enum.GetValues<TerrainKind>().Where(k => c.Fraction(k) > 0).Select(k => $"{F(c.Fraction(k) * 100)}% {k}"));
            b.Append($"<rect x=\"{pad + x * cell}\" y=\"{70 + y * cell}\" width=\"{cell}\" height=\"{cell}\" fill=\"{RegionPalette.Color(c, layer)}\"><title>({x},{y}) / {c.District}: {cover}; elevation {c.ElevationMeters} m; moisture {F(c.Moisture * 100)}%; flood susceptibility {F(c.FloodRisk * 100)}%; roughness {F(c.Roughness * 100)}%</title></rect>");
        }
        var roadPoints = region.Roads.Select(r => r.Point).ToHashSet();
        foreach (var road in region.Roads)
        {
            string color = road.Kind == RoadKind.Bridge ? "#f5d490" : road.Kind == RoadKind.Arterial ? "#b4ac8a" : "#819091";
            foreach (var next in new[] { new RegionPoint(road.Point.X + 1, road.Point.Y), new RegionPoint(road.Point.X, road.Point.Y + 1) })
                if (roadPoints.Contains(next)) b.Append($"<path d=\"M {X(road.Point)} {Y(road.Point)} L {X(next)} {Y(next)}\" stroke=\"{color}\" stroke-width=\"1.5\"/>");
            if (road.Kind == RoadKind.Bridge) b.Append($"<circle cx=\"{X(road.Point)}\" cy=\"{Y(road.Point)}\" r=\"2\" fill=\"#f5d490\"/>");
        }
        foreach (var site in region.Sites)
        {
            var type = RegionalContent.Type(site.TypeId)!;
            b.Append($"<g data-site=\"{Escape(site.LocationId)}\"><title>{Escape(type.Name + " / " + site.LocationId + " / " + site.Variant)} · {site.RoadDistanceMeters} m by road</title><rect x=\"{X(site.Parcel) - 3}\" y=\"{Y(site.Parcel) - 3}\" width=\"6\" height=\"6\" fill=\"#17212a\" stroke=\"#c4d7dc\" stroke-width=\".5\"/><text x=\"{X(site.Parcel)}\" y=\"{Y(site.Parcel) + 1}\" text-anchor=\"middle\" font-size=\"4\">{Escape(type.Symbol)}</text></g>");
        }
        foreach (var center in region.Centers) b.Append($"<text x=\"{X(center.Point) + 4}\" y=\"{Y(center.Point) - 5}\" font-size=\"11\">{Escape(center.Name)}</text>");
        b.Append($"<circle cx=\"{X(region.Compound)}\" cy=\"{Y(region.Compound)}\" r=\"5\" fill=\"#efc56d\"/><text x=\"{legendX}\" y=\"90\" font-size=\"14\">LOCATION TYPES</text>");
        int line = 113;
        foreach (var type in RegionalContent.Types)
        { b.Append($"<text x=\"{legendX}\" y=\"{line}\" font-size=\"12\">{Escape(type.Symbol + "  " + type.Name)} ({region.Sites.Count(s => s.TypeId == type.Id)})</text>"); line += 20; }
        line += 20;
        foreach (var kind in Enum.GetValues<TerrainKind>())
        { b.Append($"<rect x=\"{legendX}\" y=\"{line - 9}\" width=\"10\" height=\"10\" fill=\"{RegionPalette.TerrainColor(kind)}\"/><text x=\"{legendX + 18}\" y=\"{line}\" font-size=\"12\">{kind}</text>"); line += 19; }
        line += 20;
        foreach (var district in Enum.GetValues<LandUse>())
        { b.Append($"<rect x=\"{legendX}\" y=\"{line - 9}\" width=\"10\" height=\"10\" fill=\"{DistrictColor(district)}\"/><text x=\"{legendX + 18}\" y=\"{line}\" font-size=\"12\">{district}</text>"); line += 19; }
        b.Append($"<text x=\"24\" y=\"{height - 12}\" font-size=\"12\">Gold: compound and bridges · 80 m parcels · hover cells for terrain percentages and sites for source identity</text></g></svg>");
        return b.ToString();
    }
    private static string F(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Escape(string s) => SecurityElement.Escape(s)!;
}
