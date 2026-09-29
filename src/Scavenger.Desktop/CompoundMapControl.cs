using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Desktop;

/// <summary>A projection of simulation state; hit tests produce commands through the window.</summary>
public sealed class CompoundMapControl : Control
{
    private PhysicalReport? _report;
    private CellPosition? _selected;
    private bool _power = true, _water = true, _data = true;
    public PhysicalReport? Report { get => _report; set { _report = value; InvalidateVisual(); } }
    public CellPosition? SelectedCell { get => _selected; set { _selected = value; InvalidateVisual(); } }
    public bool ShowPower { get => _power; set { _power = value; InvalidateVisual(); } }
    public bool ShowWater { get => _water; set { _water = value; InvalidateVisual(); } }
    public bool ShowData { get => _data; set { _data = value; InvalidateVisual(); } }
    public event Action<CellPosition, bool>? CellClicked;
    private double Scale => Report is null ? 1 : Math.Max(1, Math.Min((Bounds.Width - 40) / Report.Width, (Bounds.Height - 40) / Report.Height));
    private Point Offset => Report is null ? default : new((Bounds.Width - Report.Width * Scale) / 2, (Bounds.Height - Report.Height * Scale) / 2);
    public Point CellCenter(CellPosition cell) => new(Offset.X + (cell.X + .5) * Scale, Offset.Y + (cell.Y + .5) * Scale);
    public CellPosition? CellAt(Point point)
    {
        int x = (int)Math.Floor((point.X - Offset.X) / Scale), y = (int)Math.Floor((point.Y - Offset.Y) / Scale);
        return Report is not null && x >= 0 && y >= 0 && x < Report.Width && y < Report.Height ? new CellPosition(x, y) : null;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (CellAt(e.GetPosition(this)) is { } cell)
        {
            CellClicked?.Invoke(cell, e.GetCurrentPoint(this).Properties.IsRightButtonPressed);
            e.Handled = true;
        }
    }
    private Rect CellRect(CellPosition p) => new(Offset.X + p.X * Scale, Offset.Y + p.Y * Scale, Scale, Scale);
    private Rect AreaRect(GridRect area) => new(Offset.X + area.X * Scale, Offset.Y + area.Y * Scale, area.Width * Scale, area.Height * Scale);
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    private static readonly IBrush[] WorkerColors = [Brush("#90DAA0"), Brush("#F1CE77"), Brush("#B69FEA"), Brush("#EEA1AD")];
    private static IBrush WorkerColor(int id) => WorkerColors[(int)((uint)(id - 1) % (uint)WorkerColors.Length)];
    private static void Text(DrawingContext context, string value, Point point, IBrush color, double size = 11)
        => context.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), size, color), point);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brush("#111C24"), new Rect(Bounds.Size));
        if (Report is not { } report) return;
        foreach (var row in report.Tiles)
        {
            string color = row.Tile.Terrain switch
            {
                Terrain.Floor => "#293640", Terrain.Wall => "#62717A", Terrain.OpenDoor => "#81A68A",
                Terrain.ClosedDoor => "#B07C58", Terrain.Water => "#275572", Terrain.Rock => "#4D5359", _ => "#1C2B30"
            };
            context.DrawRectangle(Brush(color), new Pen(Brush("#26373B"), .5), CellRect(row.Cell));
            if (row.Tile.Terrain is Terrain.OpenDoor or Terrain.ClosedDoor)
                Text(context, row.Tile.Terrain == Terrain.OpenDoor ? "·" : "×", CellRect(row.Cell).TopLeft + new Vector(Scale / 3, 1), Brushes.White, Scale * .6);
        }
        foreach (var room in report.Rooms)
        {
            var area = AreaRect(room.Area);
            context.DrawRectangle(null, new Pen(Brush("#7BA1AE"), 1), area.Deflate(1));
            Text(context, room.Name.ToUpperInvariant(), area.TopLeft + new Vector(4, -Scale * .7), Brush("#A0BCC5"), Math.Clamp(Scale * .32, 8, 12));
        }
        foreach (var kind in Enum.GetValues<UtilityKind>())
        {
            if ((kind == UtilityKind.Power && !ShowPower) || (kind == UtilityKind.Water && !ShowWater) || (kind == UtilityKind.Data && !ShowData)) continue;
            var nodes = report.Conduits.Where(n => n.Kind == kind).ToDictionary(n => n.Cell);
            var color = Brush(kind switch { UtilityKind.Power => "#D3A951", UtilityKind.Water => "#58B4CF", _ => "#BB91DB" });
            Vector offset = kind switch { UtilityKind.Power => new(-3, -3), UtilityKind.Water => new(3, 3), _ => new(0, 0) };
            foreach (var node in nodes.Values)
            {
                var center = CellCenter(node.Cell) + offset;
                foreach (var neighbor in new[] { new CellPosition(node.Cell.X + 1, node.Cell.Y), new CellPosition(node.Cell.X, node.Cell.Y + 1) })
                    if (nodes.TryGetValue(neighbor, out var other)) context.DrawLine(new Pen(node.Enabled && other.Enabled ? color : Brush("#6E5A5A"), 2), center, CellCenter(neighbor) + offset);
                context.DrawEllipse(node.Enabled ? color : Brushes.Red, null, center, 2.5, 2.5);
            }
        }
        foreach (var building in report.Buildings.Where(b => b.Kind is not (BuildingKind.Floor or BuildingKind.Wall or BuildingKind.Door or BuildingKind.PowerCable or BuildingKind.WaterPipe or BuildingKind.DataCable)))
        {
            var area = AreaRect(building.Footprint).Deflate(3);
            string color = building.Kind switch { BuildingKind.Stockpile => "#66573A", BuildingKind.Generator => "#5D5937", BuildingKind.Pump => "#2D6276", BuildingKind.Clinic => "#536D56", BuildingKind.Computer => "#65547E", BuildingKind.Battery => "#4A6B61", _ => "#3F536B" };
            context.DrawRectangle(Brush(color), new Pen(building.Operational ? Brush("#8FADB5") : Brush("#E1925F"), 1.5), area, 3, 3);
            string label = building.Kind switch { BuildingKind.Stockpile => "S", BuildingKind.Generator => "GEN", BuildingKind.Pump => "PUMP", BuildingKind.Clinic => "MED", BuildingKind.Computer => "PC", BuildingKind.Battery => "BAT", _ => building.Id switch { "washer" => "WASH", "dryer" => "DRY", "shredder" => "SHRD", "moulder" => "MOULD", "electronics-bench" => "TECH", _ => "BENCH" } };
            Text(context, label, area.TopLeft + new Vector(3, 3), Brushes.White, Math.Clamp(Scale * .32, 8, 12));
            context.DrawEllipse(null, new Pen(Brush("#BDD3D6"), 1), CellCenter(building.InteractionCell), 3, 3);
        }
        foreach (var site in report.Construction.Where(j => j.Status is not (ConstructionStatus.Completed or ConstructionStatus.Cancelled)))
        {
            var area = AreaRect(site.Footprint).Deflate(2);
            context.DrawRectangle(null, new Pen(Brush("#BEA6E9"), 2, DashStyle.Dash), area);
            context.FillRectangle(Brush("#BEA6E9"), new Rect(area.X, area.Bottom - 3, area.Width * (double)(site.CompletedEffort / site.RequiredEffort), 3));
        }
        foreach (var path in report.Paths.Where(p => p.Cells.Count > 1))
            for (int i = 1; i < path.Cells.Count; i++)
                context.DrawLine(new Pen(WorkerColor(path.WorkerId), 1.5, DashStyle.Dot, PenLineCap.Round), CellCenter(path.Cells[i - 1]), CellCenter(path.Cells[i]));
        foreach (var worker in report.Workers)
        {
            var center = CellCenter(worker.Position);
            context.DrawEllipse(WorkerColor(worker.Id), new Pen(Brush("#111C24"), 1.5), center, Scale * .27, Scale * .27);
            Text(context, worker.Id.ToString(CultureInfo.InvariantCulture), center - new Vector(3, 6), Brush("#111C24"), 10);
            if (worker.Carrying) context.DrawRectangle(Brush("#F6DDA2"), new Pen(Brush("#111C24"), 1), new Rect(center.X + 4, center.Y + 2, 7, 7));
        }
        if (SelectedCell is { } selected) context.DrawRectangle(null, new Pen(Brush("#EDF7F9"), 2), CellRect(selected).Deflate(1));
        for (int x = 0; x < report.Width; x += 5) Text(context, x.ToString(CultureInfo.InvariantCulture), new Point(Offset.X + x * Scale + 2, Offset.Y - 16), Brush("#829CA5"), 10);
        for (int y = 0; y < report.Height; y += 5) Text(context, y.ToString(CultureInfo.InvariantCulture), new Point(Offset.X - 15, Offset.Y + y * Scale), Brush("#829CA5"), 10);
    }
}
