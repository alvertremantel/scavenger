using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Desktop;

public sealed record MapTool(string Label, string? DefinitionId = null)
{ public override string ToString() => Label; }

public sealed class MainWindow : Window
{
    private readonly TextBlock _clock = Label("", 17), _mass = Label(""), _status = Label(""), _inspector = Label("");
    private readonly WrapPanel _inspectionActions = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, StackPanel> _tables = new();
    private readonly DispatcherTimer _timer;
    private readonly Button _run;
    private CellPosition? _roomCorner;
    private int _roomId;
    public GameSession Session { get; } = new();
    public IndustryPanel Industry { get; }
    public CompoundMapControl Map { get; } = new() { Name = "CompoundMap", MinHeight = 320 };
    public ComboBox ToolSelector { get; } = new() { Name = "MapTool", MinWidth = 165 };
    public string SavePath { get; set; } = Path.Combine(Environment.CurrentDirectory, "saves", "compound.json");
    public MainWindow()
    {
        Title = "Scavenger · Compound operations";
        Industry = new IndustryPanel(Session);
        Width = 1440; Height = 1000; MinWidth = 1050; MinHeight = 780;
        Background = new SolidColorBrush(Color.Parse("#18232D"));
        var shell = new Grid { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = 12 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = new StackPanel { Spacing = 4 };
        title.Children.Add(Label("COMPOUND OPERATIONS", 24));
        title.Children.Add(Label("Prototype / recovery chains, scavenged computing & compound construction", 12, "#9DB2BD"));
        heading.Children.Add(title);
        var figures = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Spacing = 4 };
        figures.Children.Add(_clock); figures.Children.Add(_mass); Add(heading, figures, 0, 1);
        Add(shell, heading, 0);

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Action("Step", () => Session.Advance(), "StepButton"));
        toolbar.Children.Add(Action("+12 ticks", () => Session.Advance(12), "AdvanceButton"));
        toolbar.Children.Add(Action("+1 day", () => Session.Advance(Session.World.Clock.Time.TicksPerDay), "DayButton"));
        toolbar.Children.Add(Action("Until decision", () => Session.Advance(1000, true), "DecisionButton"));
        _run = Action("Run", ToggleRun, "RunButton"); toolbar.Children.Add(_run);
        toolbar.Children.Add(Action("Demo orders", Session.BeginDemo, "DemoButton"));
        toolbar.Children.Add(Action("Deliver HDPE", () => Session.DispatchSupplies(), "HaulButton"));
        toolbar.Children.Add(Action("Inspect pump", () => Session.StartInspection(), "InspectButton"));
        toolbar.Children.Add(Action("Prepare clinic", () => Session.StartClinic(), "ClinicButton"));
        toolbar.Children.Add(Action("Save", SaveSlot, "SaveButton"));
        toolbar.Children.Add(Action("Load", LoadSlot, "LoadButton"));
        toolbar.Children.Add(Action("Reset", () => { Pause(); Session.Reset(); }, "ResetButton"));
        Add(shell, toolbar, 1);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        ToolSelector.ItemsSource = new MapTool[] { new("Inspect"), new("Floor", "floor"), new("Wall", "wall"), new("Door", "door"),
            new("Storage zone", "stockpile"), new("Workbench", "workbench"), new("Power cable", "power-cable"), new("Water pipe", "water-pipe"), new("Data cable", "data-cable"), new("Room zone") };
        ToolSelector.SelectedIndex = 0;
        ToolSelector.SelectionChanged += (_, _) => { _roomCorner = null; UpdateInspector(); };
        tools.Children.Add(Label("MAP TOOL", 11, "#9DB2BD")); tools.Children.Add(ToolSelector);
        var power = new CheckBox { Content = "Power", IsChecked = true, Name = "PowerOverlay" };
        power.IsCheckedChanged += (_, _) => Map.ShowPower = power.IsChecked == true;
        var water = new CheckBox { Content = "Water", IsChecked = true, Name = "WaterOverlay" };
        water.IsCheckedChanged += (_, _) => Map.ShowWater = water.IsChecked == true;
        var data = new CheckBox { Content = "Data", IsChecked = true, Name = "DataOverlay" };
        data.IsCheckedChanged += (_, _) => Map.ShowData = data.IsChecked == true;
        tools.Children.Add(power); tools.Children.Add(water); tools.Children.Add(data);
        tools.Children.Add(Label("Click to place · right-click to inspect · room zones use two corners", 11, "#9DB2BD"));
        Add(shell, tools, 2);

        var workspace = new Grid { RowDefinitions = new RowDefinitions("3*,2*"), RowSpacing = 14 };
        var mapRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300"), ColumnSpacing = 16 };
        var mapPanel = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = Map };
        mapRow.Children.Add(mapPanel);
        var details = new StackPanel { Spacing = 12 };
        details.Children.Add(Label("CELL INSPECTOR", 13, "#ABC5CE"));
        _inspector.TextWrapping = TextWrapping.Wrap; _inspector.FontSize = 12; _inspector.LineHeight = 20;
        details.Children.Add(_inspector);
        details.Children.Add(_inspectionActions);
        details.Children.Add(Label("PEOPLE & LAYERS", 11, "#ABC5CE"));
        details.Children.Add(Label("1 Mira / mechanics, electronics, computing\n2 Ivo / hauling\n3 Noor / construction\n4 Anya / medicine, chemistry\n\nGold: power · blue: water · violet: data\nDotted lines: intended walking paths\nDashed footprints: construction sites\nSmall box: carried cargo", 12, "#A8BCC6"));
        Add(mapRow, new ScrollViewer { Content = details }, 0, 1);
        workspace.Children.Add(mapRow);
        var tabs = new TabControl { Name = "OperationsTabs" };
        tabs.Items.Add(new TabItem { Header = "Industry", Name = "IndustryTab", Content = Industry });
        foreach (string name in new[] { "Materials", "Stores", "Workers", "Jobs", "Services", "Events" })
        {
            var rows = new StackPanel { Spacing = 2 };
            _tables.Add(name, rows);
            tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = rows, Margin = new Thickness(0, 8, 0, 0) } });
        }
        Add(workspace, tabs, 1); Add(shell, workspace, 3);
        _status.TextWrapping = TextWrapping.Wrap; _status.Foreground = new SolidColorBrush(Color.Parse("#C4D7DC"));
        Add(shell, _status, 4);
        Content = shell;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => { if (Session.Advance(1, true).StoppedEarly) Pause(); };
        Session.Changed += Refresh;
        Map.CellClicked += OnCell;
        Closed += (_, _) => { Pause(); Session.Changed -= Refresh; };
        Refresh();
    }
    private static TextBlock Label(string text, double size = 13, string color = "#E4EDF0") => new()
    { Text = text, FontSize = size, Foreground = new SolidColorBrush(Color.Parse(color)), VerticalAlignment = VerticalAlignment.Center };
    private static void Add(Grid grid, Control control, int row, int column = 0)
    { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
    private static Button Action(string title, System.Action action, string? name = null)
    {
        var button = new Button { Content = title, Name = name, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 7) };
        button.Click += (_, _) => action(); return button;
    }
    private void ToggleRun() { if (_timer.IsEnabled) Pause(); else { _timer.Start(); _run.Content = "Pause"; } }
    private void Pause() { _timer.Stop(); _run.Content = "Run"; }
    private void OnCell(CellPosition cell, bool inspectOnly)
    {
        Map.SelectedCell = cell;
        if (!inspectOnly && ToolSelector.SelectedItem is MapTool tool)
        {
            if (tool.DefinitionId is { } definition) { Pause(); Session.Build(definition, cell); }
            else if (tool.Label == "Room zone")
            {
                if (_roomCorner is null) { _roomCorner = cell; _status.Text = "Choose the opposite corner of the room interior."; }
                else
                {
                    var corner = _roomCorner; _roomCorner = null;
                    var area = new GridRect(Math.Min(cell.X, corner.X), Math.Min(cell.Y, corner.Y), Math.Abs(cell.X - corner.X) + 1, Math.Abs(cell.Y - corner.Y) + 1);
                    string id;
                    do { id = $"room-{++_roomId}"; } while (Session.Report.Rooms.Any(r => r.Id == id));
                    Session.Submit(new DesignateRoomCommand(new RoomDesignation(id, $"Room {_roomId}", "Unassigned", area)));
                }
            }
        }
        UpdateInspector();
    }
    private void Refresh()
    {
        var report = Session.Report;
        Map.Report = report;
        _clock.Text = $"Tick {report.Tick} · day {Session.World.Clock.Day + 1} · {24m / Session.World.Clock.Time.TicksPerDay * 60:0} min/tick";
        _mass.Text = $"{report.Inventory.StoredKg:0.##} kg stored   /   {report.Inventory.InTransitKg:0.##} kg carried   /   {report.TotalTrackedKg:0.##} kg tracked";
        _status.Text = Session.Status;
        Table("Materials", ["Material / batch", "Store", "kg", "Free kg", "Purity / known contamination"], "3*,2*,*,*,3*",
            report.Inventory.Batches.Select(b => new[] { $"{b.MaterialId} / {b.Id}", b.StockpileId, $"{b.QuantityKg:0.##}", $"{b.AvailableKg:0.##}", $"{b.Quality.Purity:P0} · {b.Quality.Grade}" + string.Concat(b.Quality.Contaminants.Select(c => $" · {c.Key} {c.Value:P0}")) }));
        Table("Stores", ["Store", "Location", "Stored kg", "Incoming kg", "Free / capacity kg"], "3*,*,*,*,2*",
            report.Inventory.Stores.Select(s => new[] { s.Name, $"{s.Position?.X},{s.Position?.Y}", $"{s.StoredKg:0.##}", $"{s.IncomingKg:0.##}", $"{s.FreeKg:0.##} / {s.CapacityKg:0.##}" }));
        Table("Workers", ["Worker", "Cell", "Assignment / cargo", "Skills", "Activity / blocker"], "*,*,2*,3*,3*",
            report.Workers.Select(w => new[] { $"{w.Id} {w.Name}", $"{w.Position.X},{w.Position.Y}", $"{w.Assignment ?? "Idle"}{(w.Carrying ? $" / {w.CargoKg} kg" : "")}", string.Join(", ", w.Skills.Select(s => $"{s.Key} {s.Value}")), w.WorkReason ?? (w.Assignment is null ? "Available" : "Working / travelling") }));
        Table("Jobs", ["Job", "Type", "State", "Progress", "Reason"], "2*,*,*,*,3*",
            report.Construction.Select(j => new[] { j.Id, j.IsRepair ? "Repair" : j.Name, j.Status.ToString(), $"{j.CompletedEffort:0.##} / {j.RequiredEffort}", j.BlockedReason ?? "—" })
                .Concat(report.Inventory.Hauls.Select(h => new[] { h.Id, $"Haul {h.QuantityKg} kg", h.Status.ToString(), $"{h.ElapsedTicks} steps", h.BlockedReason ?? "—" }))
                .Concat(report.Projects.Select(p => new[] { p.Name, $"Work / {p.StationId}", p.Completed ? "Completed" : "Planned", $"{p.CompletedEffort:0.##} / {p.RequiredEffort:0.##}", p.BlockedReason ?? "Working" })));
        Table("Services", ["Fixture / room", "Condition", "Power kW", "Water L/h", "Service"], "2*,*,*,*,3*",
            report.Buildings.Select(b => { var u = report.Utilities.FirstOrDefault(u => u.BuildingId == b.Id); return new[] { b.Name, $"{b.Condition:P0}", $"{u?.PowerAllocatedKw ?? 0} / {u?.PowerRequiredKw ?? 0}", $"{u?.WaterAllocatedLph ?? 0} / {u?.WaterRequiredLph ?? 0}", b.Operational ? "Operational" : b.BlockedReason ?? "Unavailable" }; })
                .Concat(report.Rooms.Select(r => new[] { r.Name, "—", "—", "—", r.Status })));
        var events = _tables["Events"]; events.Children.Clear();
        foreach (string entry in report.RecentEvents.Reverse()) events.Children.Add(Label(entry, 12));
        Industry.Refresh();
        UpdateInspector();
    }
    private void Table(string name, string[] headings, string widths, IEnumerable<string[]> data)
    {
        var panel = _tables[name]; panel.Children.Clear();
        void Row(string[] cells, bool header)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(widths), ColumnSpacing = 12, Margin = new Thickness(8, 5) };
            for (int i = 0; i < cells.Length; i++)
            {
                var text = Label(cells[i], header ? 11 : 12, header ? "#9DB2BD" : "#E4EDF0"); text.TextWrapping = TextWrapping.Wrap;
                if (header) text.FontWeight = FontWeight.SemiBold;
                Add(grid, text, 0, i);
            }
            panel.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse(header ? "#273844" : "#1D2B36")), Child = grid });
        }
        Row(headings, true); foreach (var cells in data) Row(cells, false);
    }
    private void UpdateInspector()
    {
        _inspectionActions.Children.Clear();
        if (Map.SelectedCell is not { } cell || Map.Report is not { } report)
        { _inspector.Text = "Select a map cell to inspect fixtures, construction, doors and conduit connections.\n\nBuild plans use Noor as builder and Ivo as carrier. Salvage is delivered to a local staging store before work begins.\n\nEvery accepted action advances one tick."; return; }
        var tile = report.Tiles.First(t => t.Cell == cell).Tile;
        var building = report.Buildings.FirstOrDefault(b => b.Footprint.Contains(cell) && b.Kind is not (BuildingKind.Floor or BuildingKind.PowerCable or BuildingKind.WaterPipe or BuildingKind.DataCable));
        var site = report.Construction.FirstOrDefault(j => j.Footprint.Contains(cell) && j.Status is not (ConstructionStatus.Completed or ConstructionStatus.Cancelled));
        var lines = new List<string> { $"CELL {cell.X}, {cell.Y}", $"{tile.Terrain} · {(tile.Roofed ? "roofed" : "open sky")}" };
        if (building is not null)
        {
            lines.Add($"\n{building.Name} / {building.Id}\nCondition {building.Condition:P0}\nAccess {building.InteractionCell.X},{building.InteractionCell.Y}\nEmbedded {building.EmbeddedKg:0.##} kg\n{building.BlockedReason ?? "Operational"}");
            _inspectionActions.Children.Add(Action("Repair", () => Session.Repair(building.Id)));
            _inspectionActions.Children.Add(Action("Damage", () => Session.Submit(new SetBuildingConditionCommand(building.Id, .3m))));
        }
        if (site is not null)
        {
            lines.Add($"\n{site.Name} / {site.Status}\n{site.CompletedEffort:0.##} / {site.RequiredEffort} effort\n{site.BlockedReason}");
            _inspectionActions.Children.Add(Action("Cancel", () => Session.Submit(new CancelBuildCommand(site.Id))));
        }
        if (tile.Terrain is Terrain.OpenDoor or Terrain.ClosedDoor)
            _inspectionActions.Children.Add(Action(tile.Terrain == Terrain.OpenDoor ? "Close door" : "Open door", () => Session.Submit(new SetDoorOpenCommand(cell, tile.Terrain != Terrain.OpenDoor)), "DoorButton"));
        foreach (var node in report.Conduits.Where(n => n.Cell == cell))
            _inspectionActions.Children.Add(Action($"{(node.Enabled ? "Isolate" : "Connect")} {node.Kind}", () => Session.Submit(new SetConduitEnabledCommand(node.Kind, cell, !node.Enabled))));
        foreach (var worker in report.Workers.Where(w => w.Position == cell)) lines.Add($"\n{worker.Name} · {worker.Assignment ?? "idle"}{(worker.Carrying ? $" · {worker.CargoKg} kg carried" : "")}");
        foreach (var room in report.Rooms.Where(r => r.Area.Contains(cell))) lines.Add($"\n{room.Name}: {room.Status}");
        _inspector.Text = string.Join("\n", lines);
    }
    public void SaveSlot()
    {
        Pause();
        try
        {
            string path = Path.GetFullPath(SavePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Session.SaveJson()); File.Move(temp, path, true);
            _status.Text = $"Saved tick {Session.Report.Tick} to {path}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { _status.Text = $"Save failed: {error.Message}"; }
    }
    public void LoadSlot()
    {
        Pause();
        try { Session.LoadJson(File.ReadAllText(SavePath)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or NotSupportedException)
        { _status.Text = $"Load failed: {error.Message}"; }
    }
}
