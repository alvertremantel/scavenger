using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Region;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Desktop;

public sealed class RegionPanel : UserControl
{
    private readonly GameSession _session;
    private readonly TextBlock _heading = Text("", 11), _inspection = Text(""), _notice = Text("");
    private readonly TextBox _seed = new() { Name = "RegionSeed", Width = 200 };
    private readonly ComboBox _motif = new() { Name = "RegionMotif", Width = 180 };
    private readonly ComboBox _layer = new() { Name = "RegionLayer", Width = 155, ItemsSource = Enum.GetValues<MapLayer>(), SelectedIndex = 0 };
    private readonly ComboBox _type = new() { Name = "RegionType", Width = 240 };
    private readonly StackPanel _legend = new() { Spacing = 3 };
    private bool _refreshing;
    private readonly Button _plan = new() { Name = "PlanRegionalExpeditionButton", Content = "Plan expedition here", Padding = new Thickness(10, 6) };
    public RegionMapControl Map { get; } = new() { Name = "RegionMap", MinHeight = 200 };
    public TabControl Tabs { get; } = new() { Name = "SurveyTabs" };
    public event Action<string>? PlanExpedition;
    private ScavengingPanel? _operations;
    private readonly TextBox _search = new() { Name = "ScoutSearch", PlaceholderText = "Find glass, diesel, medicine, a site…" };
    private readonly StackPanel _known = new() { Spacing = 8 };
    public MapWorkspace Workspace { get; }
    public RegionPanel(GameSession session)
    {
        _session = session;
        _type.Width = 180; _layer.Width = 135;
        ToolTip.SetTip(_type, "Highlight location type"); ToolTip.SetTip(_layer, "Survey overlay");
        var bar = ConsoleUi.Bar(_type, _layer,
            ConsoleUi.Button("+", () => Map.Zoom(), "RegionZoom"),
            ConsoleUi.Button("−", Map.ZoomOut, "RegionZoomOut"),
            ConsoleUi.Button("Fit", () => Map.Zoom(true), "RegionFit"),
            ConsoleUi.Button("Find supplies", () => Show("scout"), "RegionScoutButton"),
            ConsoleUi.Button("Field teams", () => Show("teams"), "RegionTeamsButton"));
        var toolbar = new StackPanel { Spacing = 3 }; toolbar.Children.Add(_heading); toolbar.Children.Add(bar);
        Workspace = new MapWorkspace("Region", Map, toolbar, new[] {
            new Choice("inspect", "Site / terrain inspection"), new Choice("plan", "Plan an expedition"),
            new Choice("teams", "Field teams / decisions"), new Choice("scout", "Scouting / find supplies"),
            new Choice("world", "World setup") });
        _motif.ItemsSource = new[] { new Choice("random", "Seed-selected layout") }.Concat(Enum.GetValues<SettlementMotif>().Select(m => new Choice(m.ToString(), m.ToString()))).ToArray(); _motif.SelectedIndex = 0;
        var generate = ConsoleUi.Button("Generate new world", () =>
        { if (ulong.TryParse(_seed.Text, out ulong seed)) { _session.NewRegion(seed, _motif.SelectedItem is Choice c && Enum.TryParse<SettlementMotif>(c.Id, out var motif) ? motif : null); _notice.Text = ""; Tabs.SelectedIndex = 0; Show("inspect"); } else _notice.Text = "Enter an unsigned whole-number seed."; }, "GenerateRegionButton");
        _seed.Width = _motif.Width = double.NaN;
        var config = new StackPanel { Spacing = 14, Children = {
            ConsoleUi.Text("Start a new region", 20),
            ConsoleUi.Text("This replaces your compound. Save first to keep the current game.", 12, ConsoleUi.Warning),
            ConsoleUi.Field("World seed", _seed), ConsoleUi.Field("Town layout", _motif), generate, _notice,
            ConsoleUi.Text("49.83 square miles · 142 × 142 parcels\n80 metres per parcel · 20 site types\n\nCompact, avenue-led or twin-center towns with detached suburbs, mixed terrain and finite stock.", 12, ConsoleUi.Muted)
        } };
        var inspector = new StackPanel { Spacing = 12, Children = { _inspection } };
        inspector.Children.Add(new Expander { Header = "Map key & controls", Content = new StackPanel { Spacing = 8, Children = {
            ConsoleUi.Text("Drag to pan · wheel to zoom\nClick sites or terrain to inspect.\nGold: compound, bridges and selected route.\nKnown stock can be depleted by other scavengers.", 11, ConsoleUi.Muted), _legend } } });
        _plan.Classes.Add("primary"); _plan.HorizontalAlignment = HorizontalAlignment.Stretch;
        _plan.Click += (_, _) => { if (Map.SelectedSite is { } id) { _operations?.SelectLocation(id); Show("plan"); PlanExpedition?.Invoke(id); } };
        var inspection = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
        inspection.Children.Add(new ScrollViewer { Content = inspector }); Grid.SetRow(_plan, 1); inspection.Children.Add(_plan);
        ConsoleUi.HideHeaders(Tabs);
        Tabs.Items.Add(new TabItem { Header = "Inspection", Content = inspection });
        Tabs.Items.Add(new TabItem { Header = "World setup", Content = new ScrollViewer { Content = config } });
        Tabs.Items.Add(new TabItem { Header = "Scouting", Content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 } });
        var scouting = (Grid)((TabItem)Tabs.Items[2]!).Content!;
        scouting.Children.Add(new StackPanel { Spacing = 8, Children = { ConsoleUi.Text("Find supplies", 20),
            ConsoleUi.Text("Search known locations and remaining stock. Select a result to inspect its road route and plan recovery.", 12, ConsoleUi.Muted), _search } });
        var results = new ScrollViewer { Content = _known }; Grid.SetRow(results, 1); scouting.Children.Add(results);
        _search.TextChanged += (_, _) => Search();
        Workspace.ModeChanged += Show;
        Tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, Tabs) && Workspace.Mode is not ("plan" or "teams")) Workspace.Show(Tabs.SelectedIndex == 1 ? "world" : Tabs.SelectedIndex == 2 ? "scout" : "inspect", Tabs); };
        Content = Workspace; Show("inspect");
        Map.SiteClicked += id => { _operations?.SelectLocation(id); if (Workspace.Mode != "plan") Show("inspect"); Refresh(); };
        Map.TeamClicked += id => { Show("teams"); _operations?.SelectTeam(id); Refresh(); };
        Map.CellClicked += _ => { Show("inspect"); Refresh(); };
        _layer.SelectionChanged += (_, _) => { if (_layer.SelectedItem is MapLayer layer) Map.Layer = layer; };
        _type.SelectionChanged += (_, _) => { if (_refreshing) return; Map.HighlightType = (_type.SelectedItem as Choice)?.Id is { } id && id != "all" ? id : null; Map.InvalidateVisual(); };
        Refresh();
    }
    public void AttachOperations(ScavengingPanel operations)
    {
        _operations = operations;
        if (Map.SelectedSite is { } id) operations.SelectLocation(id);
        operations.DestinationChanged += id => { Map.SelectedSite = id; Refresh(); };
        operations.Tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, operations.Tabs) && operations.Tabs.SelectedIndex is 0 or 1) Show(operations.Tabs.SelectedIndex == 1 ? "teams" : "plan"); };
    }
    public void Show(string mode)
    {
        if (mode is "plan" or "teams" && _operations is not null)
        {
            Workspace.Show(mode, _operations);
            _operations.Tabs.SelectedIndex = mode == "teams" ? 1 : 0;
            if (mode == "plan" && _operations.PlannedDestination is { } destination) { Map.SelectedSite = destination; Refresh(); }
        }
        else
        {
            Workspace.Show(mode is "plan" or "teams" ? "inspect" : mode, Tabs);
            Tabs.SelectedIndex = mode == "world" ? 1 : mode == "scout" ? 2 : 0;
            if (mode == "scout") Search();
        }
    }
    public void SearchSupplies(string query) { _search.Text = query; Show("scout"); }
    private void Search()
    {
        _known.Children.Clear();
        if (!_session.World.TryModule<ScavengingModule>(out var scavenging)) return;
        string query = _search.Text?.Trim() ?? "";
        foreach (var location in scavenging.Locations.Where(l => l.Definition.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || l.Definition.Salvage.Any(s => l.Remaining(s.Id) > 0 && ((s.MaterialId + " " + s.Object).Contains(query, StringComparison.OrdinalIgnoreCase) || query.Equals("computer", StringComparison.OrdinalIgnoreCase) && s.ComponentId is not null)))
            .OrderBy(l => l.Definition.TravelTicks))
        {
            string id = location.Definition.Id;
            var card = new StackPanel { Spacing = 5, Children = {
                ConsoleUi.Text(location.Definition.Name, 13, ConsoleUi.Accent),
                ConsoleUi.Text($"{location.Definition.TravelTicks} ticks each way · integrity {location.Integrity:P0}", 11, ConsoleUi.Muted),
                ConsoleUi.Text(string.Join(" · ", location.Definition.Salvage.Where(s => location.Remaining(s.Id) > 0
                    && (query.Length == 0 || (s.MaterialId + " " + s.Object).Contains(query, StringComparison.OrdinalIgnoreCase) || query.Equals("computer", StringComparison.OrdinalIgnoreCase) && s.ComponentId is not null))
                    .Take(4).Select(s => $"{location.Remaining(s.Id):0.##} kg {s.MaterialId}")), 11, ConsoleUi.Muted)
            } };
            var button = ConsoleUi.Button("", () => { Map.SelectedSite = id; if (Map.Region?.Site(id) is { } site) Map.Focus(site.Parcel); _operations?.SelectLocation(id); Show("inspect"); Refresh(); }, "ScoutSite_" + id);
            button.Content = card; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; _known.Children.Add(button);
        }
        if (_known.Children.Count == 0) _known.Children.Add(ConsoleUi.Text("No remaining known stock matches. Try another material or site.", 12, ConsoleUi.Muted));
    }
    private static TextBlock Text(string text, double size = 12) => ConsoleUi.Text(text, size, ConsoleUi.Muted);
    private static string TerrainDescription(RegionModule region, RegionPoint p)
    {
        var cell = region.Cell(p);
        if (cell.Cover is null) return $"Parcel ({p.X},{p.Y}) / {cell.District}\nLegacy terrain: {cell.Terrain}. Elevation, moisture and flood risk were not recorded.";
        string cover = string.Join(" · ", Enum.GetValues<TerrainKind>().Where(k => cell.Fraction(k) > 0).OrderByDescending(k => cell.Fraction(k)).Select(k => $"{cell.Fraction(k):P0} {k}"));
        return $"Parcel ({p.X},{p.Y}) / {cell.District}\n{cover}\nMean elevation {cell.ElevationMeters} m · moisture {cell.Moisture:P0}\nFlood susceptibility {cell.FloodRisk:P0} · roughness {cell.Roughness:P0}";
    }
    public void SetLayer(MapLayer layer) => _layer.SelectedItem = layer;
    public void Refresh()
    {
        if (!_session.World.TryModule<RegionModule>(out var region))
        { Map.Region = null; _plan.IsEnabled = false; _heading.Text = "Authored fixture / no generated regional map. Enter a seed to start a generated world."; _inspection.Text = ""; _legend.Children.Clear(); return; }
        bool changed = Map.Region != region;
        Map.Region = region;
        var scavenging = _session.World.Module<ScavengingModule>();
        var workers = _session.World.Module<Haldane.CareerSim.Work.WorkforceModule>();
        Map.Teams = scavenging.Expeditions.Where(e => e.Active && region.Site(e.Plan.LocationId) is not null).Select(e =>
        {
            var point = e.Status is ExpeditionStatus.Searching or ExpeditionStatus.Decision ? region.Site(e.Plan.LocationId)!.Parcel : region.Compound;
            if (e.Status is ExpeditionStatus.Outbound or ExpeditionStatus.Returning)
            {
                var route = region.Route(e.Plan.LocationId);
                double progress = Math.Clamp((double)e.ElapsedTravel / scavenging.TravelTicksFor(e), 0, 1);
                if (e.Status == ExpeditionStatus.Returning) progress = 1 - progress;
                if (route.Count > 0) point = route[(int)Math.Round(progress * (route.Count - 1))];
            }
            return new RegionTeamMarker(e.Plan.Id, string.Join(", ", e.Plan.Team.Select(id => workers.Find(id)!.Name)), point, e.Status == ExpeditionStatus.Decision);
        }).ToArray();
        if (Workspace.Mode == "teams" && _operations?.ActiveDestination is { } active) Map.SelectedSite = active;
        Map.InvalidateVisual();
        _heading.Text = $"{region.Name.ToUpperInvariant()}  /  {region.SquareMiles:0.0} mi²  /  {region.Sites.Count} known sites";
        if (changed)
        {
            _seed.Text = region.Seed.ToString();
            string motif = _session.RegionMotifPreference?.ToString() ?? "random";
            _motif.SelectedItem = _motif.Items.Cast<Choice>().First(c => c.Id == motif);
            Map.SelectedSite = region.Sites.Where(s => s.TypeId == "pharmacy").OrderBy(s => s.RoadDistanceMeters).First().LocationId;
            _refreshing = true;
            _type.ItemsSource = new[] { new Choice("all", "All location types") }.Concat(RegionalContent.Types.Select(t => new Choice(t.Id, t.Name))).ToArray(); _type.SelectedIndex = 0;
            Map.HighlightType = null; _refreshing = false;
            _legend.Children.Clear();
            foreach (var type in RegionalContent.Types) _legend.Children.Add(Text($"{type.Symbol,-3} {type.Name} · {region.Sites.Count(s => s.TypeId == type.Id)}"));
        }
        _plan.IsEnabled = Map.SelectedSite is not null;
        if (Workspace.Mode == "scout") Search();
        if (Map.SelectedSite is null && Map.SelectedCell is { } point) _inspection.Text = TerrainDescription(region, point);
        if (Map.SelectedSite is { } id && region.Site(id) is { } site)
        {
            var location = _session.World.Module<ScavengingModule>().Location(id)!; var d = location.Definition;
            _inspection.Text = $"{d.Name}\n{site.LocationId} / {site.Variant}\n{region.Cell(site.Parcel).District} · parcel ({site.Parcel.X},{site.Parcel.Y})\n{site.RoadDistanceMeters / 1000m:0.00} km by road · {d.TravelTicks} ticks each way\nIntegrity {location.Integrity:P0} · rivals {d.RivalPressure:P0}\n\n{TerrainDescription(region, site.Parcel)}\n\nRecoverable stock:\n"
                + string.Join("\n", d.Salvage.Select(s => $"{location.Remaining(s.Id):0.###} kg {s.MaterialId}\n  {s.Object}"));
        }
    }
}
