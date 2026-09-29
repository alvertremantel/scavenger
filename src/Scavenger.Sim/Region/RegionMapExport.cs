using System.Globalization;
using System.Security;
using System.Text;

namespace Scavenger.Sim.Region;

/// <summary>Standalone inspectable SVG, with coordinates and source identity in each site's tooltip.</summary>
public static class RegionMapExport
{
    public static string DistrictColor(LandUse d) => d switch { LandUse.TownCenter => "#67717c", LandUse.Civic => "#536878", LandUse.Residential => "#43595c",
        LandUse.Industrial => "#705e50", LandUse.Farmland => "#667347", LandUse.Woodland => "#304c3e", _ => "#3a6965" };
    public static string Svg(RegionModule region)
    {
        const int cell = 10, pad = 24; var b = new StringBuilder();
        b.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 970 740\"><rect width=\"970\" height=\"740\" fill=\"#18232d\"/><g font-family=\"sans-serif\" fill=\"#e4edf0\">");
        b.Append($"<text x=\"24\" y=\"28\" font-size=\"20\">{Escape(region.Name)} · seed {region.Seed}</text><text x=\"24\" y=\"50\" font-size=\"12\">5.12 × 5.12 km · {region.SquareMiles.ToString("0.00", CultureInfo.InvariantCulture)} square miles · {region.Sites.Count} sites · north ↑</text>");
        int X(RegionPoint p) => pad + p.X * cell + cell / 2; int Y(RegionPoint p) => 70 + p.Y * cell + cell / 2;
        for (int y = 0; y < region.Height; y++) for (int x = 0; x < region.Width; x++)
        {
            var c = region.Cell(new(x, y)); b.Append($"<rect x=\"{pad + x * cell}\" y=\"{70 + y * cell}\" width=\"10\" height=\"10\" fill=\"{(c.Terrain == RegionalTerrain.River ? "#3184a5" : DistrictColor(c.District))}\"/>");
        }
        var roadPoints = region.Roads.Select(r => r.Point).ToHashSet();
        foreach (var road in region.Roads)
        {
            string color = road.Kind == RoadKind.Bridge ? "#f5d490" : road.Kind == RoadKind.Arterial ? "#b4ac8a" : "#819091";
            foreach (var next in new[] { new RegionPoint(road.Point.X + 1, road.Point.Y), new RegionPoint(road.Point.X, road.Point.Y + 1) })
                if (roadPoints.Contains(next)) b.Append($"<path d=\"M {X(road.Point)} {Y(road.Point)} L {X(next)} {Y(next)}\" stroke=\"{color}\" stroke-width=\"2\"/>");
            if (road.Kind == RoadKind.Bridge) b.Append($"<circle cx=\"{X(road.Point)}\" cy=\"{Y(road.Point)}\" r=\"3\" fill=\"#f5d490\"/>");
        }
        foreach (var site in region.Sites)
        {
            var type = RegionalContent.Type(site.TypeId)!;
            b.Append($"<g><title>{Escape(type.Name + " / " + site.LocationId + " / " + site.Variant)} · {site.RoadDistanceMeters} m by road</title><rect x=\"{X(site.Parcel) - 4}\" y=\"{Y(site.Parcel) - 4}\" width=\"8\" height=\"8\" fill=\"#17212a\" stroke=\"#c4d7dc\" stroke-width=\".5\"/><text x=\"{X(site.Parcel)}\" y=\"{Y(site.Parcel) + 2}\" text-anchor=\"middle\" font-size=\"5\">{Escape(type.Symbol)}</text></g>");
        }
        b.Append($"<circle cx=\"{X(region.Compound)}\" cy=\"{Y(region.Compound)}\" r=\"6\" fill=\"#efc56d\"/><text x=\"690\" y=\"90\" font-size=\"14\">LOCATION TYPES</text>");
        int line = 113;
        foreach (var type in RegionalContent.Types)
        { b.Append($"<text x=\"690\" y=\"{line}\" font-size=\"12\">{Escape(type.Symbol + "  " + type.Name)} ({region.Sites.Count(s => s.TypeId == type.Id)})</text>"); line += 20; }
        line += 15;
        foreach (var district in Enum.GetValues<LandUse>())
        { b.Append($"<rect x=\"690\" y=\"{line - 9}\" width=\"10\" height=\"10\" fill=\"{DistrictColor(district)}\"/><text x=\"708\" y=\"{line}\" font-size=\"12\">{district}</text>"); line += 19; }
        b.Append("<text x=\"24\" y=\"732\" font-size=\"12\">Gold circle: compound · gold river cells: bridges · 80 m per parcel · tooltips identify sites</text></g></svg>");
        return b.ToString();
    }
    private static string Escape(string s) => SecurityElement.Escape(s)!;
}
