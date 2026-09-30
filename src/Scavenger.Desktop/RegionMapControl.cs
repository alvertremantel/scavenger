using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scavenger.Sim.Region;

namespace Scavenger.Desktop;

public sealed class RegionMapControl : Control
{
    public RegionMapControl() => Cursor = new Cursor(StandardCursorType.Hand);
    private RegionModule? _region;
    private string? _selected;
    private MapLayer _layer;
    private IBrush[]? _cellColors;
    private double _zoom = 1;
    private Point? _center;
    private IPointer? _dragPointer;
    private Point _press, _pressCenter;
    private bool _dragging;
    public double ZoomLevel => _zoom;
    public RegionPoint? SelectedCell { get; private set; }
    public MapLayer Layer { get => _layer; set { _layer = value; _cellColors = null; InvalidateVisual(); } }
    public void Zoom(bool fit = false)
    {
        if (fit) { _zoom = 1; _center = null; }
        else
        {
            _zoom = Math.Min(16, _zoom * 2);
            _center = SelectedCell is { } p ? new Point(p.X + .5, p.Y + .5) : Center;
        }
        InvalidateVisual();
    }
    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), .5);
    public RegionModule? Region { get => _region; set { if (_region != value) { EndDrag(); _cellColors = null; _zoom = 1; _center = null; SelectedCell = null; SelectedSite = null; } _region = value; InvalidateVisual(); } }
    public string? SelectedSite { get => _selected; set { _selected = value; InvalidateVisual(); } }
    public string? HighlightType { get; set; }
    public event Action<string>? SiteClicked;
    public event Action<RegionPoint>? CellClicked;
    private double Scale => Region is null ? 1 : Math.Max(.1, Math.Min((Bounds.Width - 36) / Region.Width, (Bounds.Height - 64) / Region.Height)) * _zoom;
    private Point Center => _center ?? new Point((Region?.Width ?? 0) / 2.0, (Region?.Height ?? 0) / 2.0);
    private Point Offset => new(Bounds.Width / 2 - Center.X * Scale, Bounds.Height / 2 - Center.Y * Scale);
    public Point ParcelCenter(RegionPoint p) => Offset + new Vector((p.X + .5) * Scale, (p.Y + .5) * Scale);
    private void ZoomAt(Point anchor, double factor)
    {
        if (Region is null || _dragPointer is not null) return;
        var world = new Point((anchor.X - Offset.X) / Scale, (anchor.Y - Offset.Y) / Scale);
        _zoom = Math.Clamp(_zoom * factor, 1, 16);
        _center = _zoom == 1 ? null : new Point(world.X - (anchor.X - Bounds.Width / 2) / Scale,
            world.Y - (anchor.Y - Bounds.Height / 2) / Scale);
        InvalidateVisual();
    }
    private void EndDrag()
    {
        var pointer = _dragPointer; _dragPointer = null; _dragging = false;
        pointer?.Capture(null); Cursor = new Cursor(StandardCursorType.Hand);
    }
    private static IBrush Brush(string s) => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(s));
    private static readonly IBrush Ink = Brush("#161D21"), Roads = Brush("#7F8C86"), Gold = Brush("#C8AA7C");
    private static void Text(DrawingContext c, string value, Point p, IBrush brush, double size)
        => c.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, brush), p);
    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        if (Region is null || !properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;
        _dragPointer = e.Pointer; _press = e.GetPosition(this); _pressCenter = Center;
        _dragging = properties.IsMiddleButtonPressed;
        e.Pointer.Capture(this); Cursor = new Cursor(StandardCursorType.SizeAll); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragPointer != e.Pointer) return;
        var current = e.GetPosition(this);
        var delta = new Vector(current.X - _press.X, current.Y - _press.Y);
        if (!_dragging && delta.Length < 4) return;
        _dragging = true;
        // Keep some of the region on screen even after a long captured drag.
        _center = new Point(Math.Clamp(_pressCenter.X - delta.X / Scale, 0, Region!.Width),
            Math.Clamp(_pressCenter.Y - delta.Y / Scale, 0, Region.Height));
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragPointer != e.Pointer) return;
        var p = e.GetPosition(this);
        bool select = !_dragging && e.InitialPressMouseButton == MouseButton.Left
            && Distance(_press, p) < 4 && new Rect(Bounds.Size).Contains(p);
        EndDrag(); e.Handled = true;
        if (!select || Region is null) return;
        var cell = new RegionPoint((int)Math.Floor((p.X - Offset.X) / Scale), (int)Math.Floor((p.Y - Offset.Y) / Scale));
        if (!Region.Contains(cell)) return;
        SelectedCell = cell;
        var selected = Region.Sites.OrderBy(s => Distance(ParcelCenter(s.Parcel), p)).FirstOrDefault();
        if (selected is not null && Distance(ParcelCenter(selected.Parcel), p) <= Math.Max(3, Scale * .6))
        { SelectedCell = selected.Parcel; SelectedSite = selected.LocationId; SiteClicked?.Invoke(selected.LocationId); }
        else { SelectedSite = null; CellClicked?.Invoke(cell); }
        InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragPointer = null; _dragging = false; Cursor = new Cursor(StandardCursorType.Hand);
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Region is null || e.Delta.Y == 0) return;
        ZoomAt(e.GetPosition(this), Math.Pow(1.2, e.Delta.Y)); e.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); using var clip = context.PushClip(new Rect(Bounds.Size)); context.FillRectangle(Ink, new Rect(Bounds.Size));
        if (Region is not { } region) { Text(context, "This save has no generated geography.", new(24, 24), Brushes.White, 13); return; }
        _cellColors ??= region.Cells.Select(c =>
        {
            var color = Color.Parse(RegionPalette.Color(c, Layer));
            // Keep terrain differentiation while bringing the survey into the dark console palette.
            return (IBrush)new SolidColorBrush(Color.FromRgb((byte)(22 + color.R * .40), (byte)(29 + color.G * .40), (byte)(33 + color.B * .40)));
        }).ToArray();
        for (int y = 0; y < region.Height; y++) for (int x = 0; x < region.Width; x++)
        {
            context.FillRectangle(_cellColors[y * region.Width + x], new Rect(Offset.X + x * Scale, Offset.Y + y * Scale, Scale + .1, Scale + .1));
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
            context.DrawRectangle(selected ? Gold : Ink, new Pen(highlighted ? ConsoleUi.Brush("#BAC5BF") : Roads, selected ? 2 : .6), rect);
            if (highlighted && (Scale >= 6 || HighlightType is not null)) Text(context, type.Symbol, rect.TopLeft + new Vector(.5, -.5), selected ? Ink : Brushes.White, Math.Max(5, Scale * .62));
        }
        foreach (var center in region.Centers)
        {
            var p = ParcelCenter(center.Point);
            Text(context, center.Name, p + new Vector(5, -14), Brushes.White, center.Role == SettlementRole.Suburb ? 9 : 11);
        }
        if (SelectedCell is { } selectedCell)
        { var p = ParcelCenter(selectedCell); context.DrawRectangle(null, new Pen(Gold, 1.5), new Rect(p.X - Scale / 2, p.Y - Scale / 2, Scale, Scale)); }
        context.DrawEllipse(Gold, new Pen(Ink, 2), ParcelCenter(region.Compound), 6, 6);
        Text(context, "N ↑", new(12, 8), Brushes.White, 12);
        var start = new Point(12, Bounds.Height - 24);
        context.DrawLine(new Pen(Brushes.White, 2), start, start + new Vector((_zoom > 2 ? 200.0 : 1000.0) / region.CellMeters * Scale, 0));
        Text(context, (_zoom > 2 ? "200 m" : "1 km") + "   ·   gold: compound, bridges and selected route", start + new Vector(0, 3), Brushes.White, 10);
    }
}
