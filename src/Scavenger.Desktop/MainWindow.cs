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
    private readonly TextBlock _clock = Label("", 12), _mass = Label(""), _status = Label(""), _inspector = Label("");
    private readonly WrapPanel _inspectionActions = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, StackPanel> _tables = new();
    private readonly DispatcherTimer _timer;
    private Haldane.Kernel.World? _displayedWorld;
    private readonly StackPanel _staffActions = new() { Spacing = 6 };
    private readonly StackPanel _duties = new();
    private readonly Button _run;
    private CellPosition? _roomCorner;
    private int _roomId;
    private readonly TextBox _recordFilter = new() { Name = "RegisterFilter", PlaceholderText = "Filter records by material, worker, state or location…", MinWidth = 300 };
    private readonly Dictionary<string, (string[] Headers, string Widths, string[][] Rows)> _records = [];
    public GameSession Session { get; }
    public ScavengingPanel Scavenging { get; }
    public RegionPanel Region { get; }
    public TabControl WorkspaceTabs { get; } = new() { Name = "WorkspaceTabs" };
    public IndustryPanel Industry { get; }
    public CompoundMapControl Map { get; } = new() { Name = "CompoundMap", MinHeight = 320 };
    public TabControl OperationsTabs { get; } = new() { Name = "OperationsTabs" };
    public ComboBox ToolSelector { get; } = new() { Name = "MapTool", MinWidth = 165 };
    public string SavePath { get; set; } = Path.Combine(Environment.CurrentDirectory, "saves", "compound.json");
    public HomePanel Home { get; }
    private readonly Dictionary<int, Button> _navigation = [];
    private readonly TextBlock _breadcrumb = ConsoleUi.Eyebrow("Overview / home");
    private readonly TextBlock _runState = ConsoleUi.Text("PAUSED", 10, ConsoleUi.Accent);
    public MainWindow(bool regional = true, bool generated = true)
    {
        Session = new GameSession(regional, generated);
        Region = new RegionPanel(Session); Scavenging = new ScavengingPanel(Session);
        Industry = new IndustryPanel(Session); Home = new HomePanel(Session);
        Title = "Scavenger / Compound operator console";
        Width = 1440; Height = 1000; MinWidth = 1050; MinHeight = 780;
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("168,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        var rail = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = ConsoleUi.Brush("#171C20") };
        var identity = new StackPanel { Spacing = 5, Margin = new Thickness(20, 25, 12, 24) };
        identity.Children.Add(ConsoleUi.Text("SCAVENGER", 15, ConsoleUi.Accent));
        identity.Children.Add(ConsoleUi.Eyebrow("Compound console")); rail.Children.Add(identity);
        var nav = new StackPanel { Spacing = 3 };
        string[] titles = ["Overview", "Region", "Compound", "Expeditions", "Industry", "Registers"];
        nav.Children.Add(new Border { Padding = new Thickness(20, 8), Child = ConsoleUi.Eyebrow("Workspaces") });
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            var button = ConsoleUi.Button($"{i + 1:00}   {titles[i]}", () => WorkspaceTabs.SelectedIndex = index, $"Nav{titles[i]}Button");
            button.Classes.Add("nav"); ToolTip.SetTip(button, $"{titles[i]} · Ctrl+{i + 1}"); _navigation.Add(i, button); nav.Children.Add(button);
        }
        nav.Children.Add(new Border { Padding = new Thickness(20, 20, 12, 8), Child = ConsoleUi.Eyebrow("Direct access") });
        foreach (var (label, route) in new[] { ("Clinic", "clinic"), ("Power & reserves", "industry/4"), ("Hardware", "industry/1"), ("Store transfers", "industry/5") })
        { var b = ConsoleUi.Button(label, () => Navigate(route), "Quick" + route.Replace("/", "")); b.Classes.Add("nav"); nav.Children.Add(b); }
        Add(rail, nav, 1);
        var railFoot = new StackPanel { Margin = new Thickness(20), Spacing = 6 };
        railFoot.Children.Add(ConsoleUi.Eyebrow("Local workstation")); railFoot.Children.Add(ConsoleUi.Text("Offline / operator 01", 11, ConsoleUi.Muted));
        railFoot.Children.Add(ConsoleUi.Text("Ctrl+Tab  switch view", 10, ConsoleUi.Muted)); Add(rail, railFoot, 2);
        shell.Children.Add(new Border { Child = rail, BorderBrush = ConsoleUi.Brush(ConsoleUi.Line), BorderThickness = new Thickness(0, 0, 1, 0) });
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 14, Margin = new Thickness(24, 20, 24, 16) };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(_breadcrumb);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        right.Children.Add(_runState); right.Children.Add(_clock); Add(top, right, 0, 1); main.Children.Add(top);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = ConsoleUi.Brush("#1A2024") };
        var timeControls = new WrapPanel { Margin = new Thickness(10, 8, 0, 4) };
        timeControls.Children.Add(Action("Step", () => { Pause(); Session.Advance(); }, "StepButton"));
        timeControls.Children.Add(Action("+12 ticks", () => { Pause(); Session.Advance(12, true); }, "AdvanceButton"));
        timeControls.Children.Add(Action("+1 day", () => { Pause(); Session.Advance(Session.World.Clock.Time.TicksPerDay, true); }, "DayButton"));
        timeControls.Children.Add(Action("Until decision", () => { Pause(); Session.Advance(1000, true); }, "DecisionButton"));
        _run = Action("Run", ToggleRun, "RunButton"); _run.Classes.Add("primary"); timeControls.Children.Add(_run); toolbar.Children.Add(timeControls);
        var persistence = ConsoleUi.Bar(Action("Save", SaveSlot, "SaveButton"), Action("Load", LoadSlot, "LoadButton"));
        persistence.Margin = new Thickness(12, 8, 0, 0); Add(toolbar, persistence, 0, 1);
        Add(main, new Border { Child = toolbar, BorderThickness = new Thickness(1), BorderBrush = ConsoleUi.Brush(ConsoleUi.Line) }, 1);

        ToolSelector.ItemsSource = new MapTool[] { new("Inspect"), new("Floor", "floor"), new("Wall", "wall"), new("Door", "door"),
            new("Storage zone", "stockpile"), new("Workbench", "workbench"), new("Power cable", "power-cable"), new("Water pipe", "water-pipe"), new("Data cable", "data-cable"), new("Room zone") };
        ToolSelector.SelectedIndex = 0; ToolSelector.SelectionChanged += (_, _) => { _roomCorner = null; UpdateInspector(); };
        var power = new CheckBox { Content = "Power", IsChecked = true, Name = "PowerOverlay" }; power.IsCheckedChanged += (_, _) => Map.ShowPower = power.IsChecked == true;
        var water = new CheckBox { Content = "Water", IsChecked = true, Name = "WaterOverlay" }; water.IsCheckedChanged += (_, _) => Map.ShowWater = water.IsChecked == true;
        var data = new CheckBox { Content = "Data", IsChecked = true, Name = "DataOverlay" }; data.IsCheckedChanged += (_, _) => Map.ShowData = data.IsChecked == true;
        var compound = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 14 };
        compound.Children.Add(ConsoleUi.PageHeading("Compound plan", "Construction, access and utilities / click to place, right-click to inspect."));
        Add(compound, ConsoleUi.Frame(ConsoleUi.Bar(ConsoleUi.Field("Map tool", ToolSelector), power, water, data), new Thickness(14, 10, 14, 2)), 1);
        var mapRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,280"), ColumnSpacing = 14 };
        mapRow.Children.Add(ConsoleUi.Frame(Map, new Thickness(0)));
        var details = new StackPanel { Spacing = 16 };
        _inspector.TextWrapping = TextWrapping.Wrap; _inspector.FontSize = 12; _inspector.LineHeight = 20;
        var inspection = new StackPanel { Spacing = 12 }; inspection.Children.Add(_inspector); inspection.Children.Add(_inspectionActions);
        details.Children.Add(ConsoleUi.Section("Cell inspector", inspection));
        details.Children.Add(ConsoleUi.Section("Operators & duties", _duties));
        details.Children.Add(ConsoleUi.Section("Project assignments", _staffActions, "Cancel batches, hauls and expeditions in their own workspaces; release experimental project assignments here."));
        details.Children.Add(ConsoleUi.Section("Plan notation", ConsoleUi.Text("Gold / power\nBlue / water\nViolet / data\n\nDotted paths / intended movement\nDashed outlines / construction\nNumbered markers / operators", 11, ConsoleUi.Muted)));
        Add(mapRow, new ScrollViewer { Content = details }, 0, 1); Add(compound, mapRow, 2);
        Add(compound, ConsoleUi.Bar(Action("Recovery stock orders", Session.BeginDemo, "DemoButton"), Action("Store transfers →", () => Navigate("industry/5"), "HaulButton"),
            Action("Personnel & duties →", () => Navigate("register/2"), "InspectButton"), Action("Open clinic →", () => Navigate("clinic"), "ClinicButton"),
            Action("Reset scenario", () => { Pause(); Session.Reset(); }, "ResetButton")), 3);
        var registers = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 12 };
        registers.Children.Add(ConsoleUi.PageHeading("Compound registers", "Inventory, workforce and service records / live operational data."));
        foreach (string name in new[] { "Materials", "Stores", "Workers", "Jobs", "Services", "Events" })
        {
            var rows = new StackPanel(); _tables.Add(name, rows);
            OperationsTabs.Items.Add(new TabItem { Header = name, Name = "Register" + name, Content = new ScrollViewer { Content = ConsoleUi.Section(name + " register", rows), Margin = new Thickness(0, 14, 0, 0) } });
        }
        _recordFilter.TextChanged += (_, _) => FilterRecords();
        Add(registers, _recordFilter, 1); Add(registers, OperationsTabs, 2);
        var pages = new Control[] { Home, Region, compound, Scavenging, Industry, registers };
        for (int i = 0; i < pages.Length; i++) WorkspaceTabs.Items.Add(new TabItem { Header = titles[i], Name = "Workspace" + titles[i], Content = pages[i] });
        WorkspaceTabs.SelectedIndex = regional ? 0 : 2;
        WorkspaceTabs.SelectionChanged += (_, _) => UpdateNavigation();
        Region.PlanExpedition += id => { Navigate("scavenging"); Scavenging.SelectLocation(id); };
        Home.Navigate += route =>
        {
            if (route.StartsWith("plan/", StringComparison.Ordinal)) { Navigate("scavenging"); Scavenging.Suggest(route[5..]); }
            else Navigate(route);
        };
        Scavenging.Navigate += Navigate; Industry.Navigate += Navigate;
        Add(main, WorkspaceTabs, 2); Add(shell, main, 0, 1);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(18, 8) };
        _status.TextWrapping = TextWrapping.Wrap; _status.FontSize = 11; footer.Children.Add(_status);
        _mass.FontSize = 10; _mass.Foreground = ConsoleUi.Brush(ConsoleUi.Muted); Add(footer, _mass, 0, 1);
        var footFrame = new Border { Child = footer, BorderBrush = ConsoleUi.Brush(ConsoleUi.Line), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetColumnSpan(footFrame, 2); Add(shell, footFrame, 1); Content = shell;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => { if (Session.Advance(1, true).StoppedEarly) Pause(); };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) return;
            if (e.Key is >= Avalonia.Input.Key.D1 and <= Avalonia.Input.Key.D6) { WorkspaceTabs.SelectedIndex = (int)e.Key - (int)Avalonia.Input.Key.D1; e.Handled = true; }
            if (e.Key == Avalonia.Input.Key.Tab) { WorkspaceTabs.SelectedIndex = (WorkspaceTabs.SelectedIndex + (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? 5 : 1)) % 6; e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Session.Changed += Refresh; Map.CellClicked += OnCell;
        Closed += (_, _) => { Pause(); Session.Changed -= Refresh; };
        Refresh(); UpdateNavigation();
    }
    public void Navigate(string route)
    {
        if (route.StartsWith("industry/", StringComparison.Ordinal) && int.TryParse(route[9..], out int page) && page is >= 0 and <= 6)
        { WorkspaceTabs.SelectedIndex = 4; Industry.Tabs.SelectedIndex = page; }
        else if (route.StartsWith("register/", StringComparison.Ordinal) && int.TryParse(route[9..], out int register) && register is >= 0 and <= 5)
        { WorkspaceTabs.SelectedIndex = 5; OperationsTabs.SelectedIndex = register; }
        else WorkspaceTabs.SelectedIndex = route switch { "region" => 1, "compound" => 2, "scavenging" or "clinic" or "fieldteams" => 3, "industry" => 4, "registers" => 5, _ => 0 };
        if (route == "fieldteams") Scavenging.Tabs.SelectedIndex = 3;
        if (route is "clinic" or "scavenging") Scavenging.Tabs.SelectedIndex = route == "clinic" ? 1 : 0;
        UpdateNavigation();
    }
    private void UpdateNavigation()
    {
        foreach (var (index, button) in _navigation) button.Classes.Set("active", index == WorkspaceTabs.SelectedIndex);
        string[] paths = ["Overview / home", "Field operations / regional survey", "Infrastructure / compound plan", "Field operations / expeditions & clinic", "Systems / industry & computing", "Records / compound registers"];
        if (WorkspaceTabs.SelectedIndex is >= 0 and < 6) _breadcrumb.Text = paths[WorkspaceTabs.SelectedIndex].ToUpperInvariant();
    }
    private static TextBlock Label(string text, double size = 13, string color = ConsoleUi.Ink) => new()
    { Text = text, FontSize = size, Foreground = new SolidColorBrush(Color.Parse(color)), VerticalAlignment = VerticalAlignment.Center };
    private static void Add(Grid grid, Control control, int row, int column = 0)
    { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
    private static Button Action(string title, System.Action action, string? name = null)
    {
        var button = new Button { Content = title, Name = name, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 7) };
        button.Click += (_, _) => action(); return button;
    }
    private void ToggleRun() { if (_timer.IsEnabled) Pause(); else if (Session.PendingDecision is { } reason) { Session.Notify(reason); Navigate("fieldteams"); } else { _timer.Start(); _run.Content = "Pause"; _runState.Text = "RUNNING"; } }
    private void Pause() { _timer.Stop(); _run.Content = "Run"; _runState.Text = "PAUSED"; }
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
        if (_displayedWorld != Session.World || Session.PendingDecision is not null) Pause();
        _displayedWorld = Session.World;
        var report = Session.Report;
        Map.Report = report;
        long interval = report.Tick % Session.World.Clock.Time.TicksPerDay;
        _clock.Text = $"DAY {Session.World.Clock.Day + 1:00}   {interval * 24 / Session.World.Clock.Time.TicksPerDay:00}:{interval * 1440 / Session.World.Clock.Time.TicksPerDay % 60:00}   /   TICK {report.Tick:0000}";
        _mass.Text = $"MASS LEDGER  /  {report.TotalTrackedKg:0.##} kg tracked";
        _status.Text = Session.Status;
        Table("Materials", ["Material / batch", "Store", "kg", "Free kg", "Purity / known contamination"], "3*,2*,*,*,3*",
            report.Inventory.Batches.Select(b => new[] { $"{b.MaterialId} / {b.Id}", b.StockpileId, $"{b.QuantityKg:0.##}", $"{b.AvailableKg:0.##}", $"{b.Quality.Purity:P0} · {b.Quality.Grade}" + string.Concat(b.Quality.Contaminants.Select(c => $" · {c.Key} {c.Value:P0}")) }));
        Table("Stores", ["Store", "Location", "Stored kg", "Incoming kg", "Free / capacity kg"], "3*,*,*,*,2*",
            report.Inventory.Stores.Select(s => new[] { s.Name, $"{s.Position?.X},{s.Position?.Y}", $"{s.StoredKg:0.##}", $"{s.IncomingKg:0.##}", $"{s.FreeKg:0.##} / {s.CapacityKg:0.##}" }));
        Table("Workers", ["Worker", "Cell", "Assignment / cargo", "Skills", "Activity / blocker"], "*,*,2*,3*,3*",
            report.Workers.Select(w => new[] { $"{w.Id} {w.Name}", w.Away ? "Regional expedition" : $"{w.Position.X},{w.Position.Y}", $"{w.Assignment ?? "Idle"}{(w.Carrying ? $" / {w.CargoKg} kg" : "")}", string.Join(", ", w.Skills.Select(s => $"{s.Key} {s.Value}")), w.WorkReason ?? (w.Assignment is null ? "Available" : "Working / travelling") }));
        Table("Jobs", ["Job", "Type", "State", "Progress", "Reason"], "2*,*,*,*,3*",
            report.Construction.Select(j => new[] { j.Id, j.IsRepair ? "Repair" : j.Name, j.Status.ToString(), $"{j.CompletedEffort:0.##} / {j.RequiredEffort}", j.BlockedReason ?? "—" })
                .Concat(report.Inventory.Hauls.Select(h => new[] { h.Id, $"Haul {h.QuantityKg} kg", h.Status.ToString(), $"{h.ElapsedTicks} steps", h.BlockedReason ?? "—" }))
                .Concat(report.Projects.Select(p => new[] { p.Name, $"Work / {p.StationId}", p.Completed ? "Completed" : "Planned", $"{p.CompletedEffort:0.##} / {p.RequiredEffort:0.##}", p.BlockedReason ?? "Working" })));
        Table("Services", ["Fixture / room", "Condition", "Power kW", "Water L/h", "Service"], "2*,*,*,*,3*",
            report.Buildings.Select(b => { var u = report.Utilities.FirstOrDefault(u => u.BuildingId == b.Id); return new[] { b.Name, $"{b.Condition:P0}", $"{u?.PowerAllocatedKw ?? 0} / {u?.PowerRequiredKw ?? 0}", $"{u?.WaterAllocatedLph ?? 0} / {u?.WaterRequiredLph ?? 0}", b.Operational ? "Operational" : b.BlockedReason ?? "Unavailable" }; })
                .Concat(report.Rooms.Select(r => new[] { r.Name, "—", "—", "—", r.Status })));
        Table("Events", ["Activity / newest first"], "*", report.RecentEvents.Reverse().Select(entry => new[] { entry }));
        var workers = Session.World.Module<Haldane.CareerSim.Work.WorkforceModule>();
        var board = Session.World.Module<Haldane.CareerSim.Work.ProjectBoardModule>();
        ConsoleUi.Table(_duties, ["Operator", "Activity"], "*,2*", report.Workers.Select(w => new[] { w.Name, w.WorkReason ?? w.Assignment ?? "Idle / awaiting orders" }));
        _staffActions.Children.Clear();
        foreach (var worker in workers.Staff.Where(w => board.IsAssigned(w.Id)))
        {
            int id = worker.Id;
            _staffActions.Children.Add(Action($"Release {worker.Name} from project", () => Session.ReleaseProject(id), $"ReleaseWorker{id}Button"));
        }
        Home.Refresh();
        Industry.Refresh();
        Scavenging.Refresh();
        Region.Refresh();
        UpdateInspector();
    }
    private void Table(string name, string[] headings, string widths, IEnumerable<string[]> data)
    {
        _records[name] = (headings, widths, data.ToArray());
        RenderRecord(name);
    }
    private void FilterRecords() { foreach (var name in _records.Keys) RenderRecord(name); }
    private void RenderRecord(string name)
    {
        var record = _records[name]; string filter = _recordFilter.Text?.Trim() ?? "";
        ConsoleUi.Table(_tables[name], record.Headers, record.Widths, record.Rows.Where(row => row.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase))));
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
