using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scavenger.Sim.Region;

namespace Scavenger.Desktop;

public sealed class RegionMapControl : Control
{
    private RegionModule? _region;
    private string? _selected;
    public RegionModule? Region { get => _region; set { _region = value; InvalidateVisual(); } }
    public string? SelectedSite { get => _selected; set { _selected = value; InvalidateVisual(); } }
    public string? HighlightType { get; set; }
    public event Action<string>? SiteClicked;
    private double Scale => Region is null ? 1 : Math.Max(.1, Math.Min((Bounds.Width - 36) / Region.Width, (Bounds.Height - 64) / Region.Height));
    private Point Offset => Region is null ? default : new((Bounds.Width - Region.Width * Scale) / 2, (Bounds.Height - Region.Height * Scale) / 2);
    public Point ParcelCenter(RegionPoint p) => Offset + new Vector((p.X + .5) * Scale, (p.Y + .5) * Scale);
    private static IBrush Brush(string s) => new SolidColorBrush(Color.Parse(s));
    private static readonly Dictionary<LandUse, IBrush> Districts = Enum.GetValues<LandUse>().ToDictionary(d => d, d => Brush(RegionMapExport.DistrictColor(d)));
    private static readonly IBrush River = Brush("#3184a5"), Ink = Brush("#111c24"), Roads = Brush("#9da496"), Gold = Brush("#efc56d");
    private static void Text(DrawingContext c, string value, Point p, IBrush brush, double size)
        => c.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, brush), p);
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Region is null) return;
        var p = e.GetPosition(this);
        var selected = Region.Sites.OrderBy(s => Distance(ParcelCenter(s.Parcel), p)).FirstOrDefault();
        if (selected is not null && Distance(ParcelCenter(selected.Parcel), p) <= Math.Max(8, Scale))
        { SelectedSite = selected.LocationId; SiteClicked?.Invoke(selected.LocationId); e.Handled = true; }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Ink, new Rect(Bounds.Size));
        if (Region is not { } region) { Text(context, "This save has no generated geography.", new(24, 24), Brushes.White, 13); return; }
        for (int y = 0; y < region.Height; y++) for (int x = 0; x < region.Width; x++)
        {
            var cell = region.Cell(new(x, y));
            context.FillRectangle(cell.Terrain == RegionalTerrain.River ? River : Districts[cell.District], new Rect(Offset.X + x * Scale, Offset.Y + y * Scale, Scale + .1, Scale + .1));
        }
        var roadPoints = region.Roads.Select(r => r.Point).ToHashSet();
        foreach (var road in region.Roads)
        {
            var center = ParcelCenter(road.Point); var pen = new Pen(road.Kind == RoadKind.Bridge ? Gold : Roads, road.Kind == RoadKind.Arterial ? 2.4 : 1.3);
            foreach (var next in new[] { new RegionPoint(road.Point.X + 1, road.Point.Y), new RegionPoint(road.Point.X, road.Point.Y + 1) })
                if (roadPoints.Contains(next)) context.DrawLine(pen, center, ParcelCenter(next));
            if (road.Kind == RoadKind.Bridge) context.DrawEllipse(Gold, null, center, 3, 3);
        }
        if (SelectedSite is { } selectedId && region.Site(selectedId) is not null)
        {
            var route = region.Route(selectedId); var pen = new Pen(Gold, 2.5);
            for (int i = 1; i < route.Count; i++) context.DrawLine(pen, ParcelCenter(route[i - 1]), ParcelCenter(route[i]));
        }
        foreach (var site in region.Sites)
        {
            var center = ParcelCenter(site.Parcel); bool selected = site.LocationId == SelectedSite;
            var type = RegionalContent.Type(site.TypeId)!;
            bool highlighted = HighlightType is null || HighlightType == site.TypeId;
            double size = Math.Max(4, Scale * .85);
            var rect = new Rect(center.X - size / 2, center.Y - size / 2, size, size);
            context.DrawRectangle(selected ? Gold : Ink, new Pen(highlighted ? Brushes.White : Roads, selected ? 2 : .6), rect);
            if (highlighted && Scale >= 5) Text(context, type.Symbol, rect.TopLeft + new Vector(.5, -.5), selected ? Ink : Brushes.White, Math.Max(5, Scale * .62));
        }
        context.DrawEllipse(Gold, new Pen(Ink, 2), ParcelCenter(region.Compound), 6, 6);
        Text(context, "N ↑", new(Offset.X, Math.Max(0, Offset.Y - 20)), Brushes.White, 12);
        var start = new Point(Offset.X, Math.Min(Bounds.Height - 17, Offset.Y + region.Height * Scale + 12));
        context.DrawLine(new Pen(Brushes.White, 2), start, start + new Vector(1000.0 / region.CellMeters * Scale, 0));
        Text(context, "1 km   ·   gold: compound, bridges and selected route", start + new Vector(0, 3), Brushes.White, 10);
    }
}
