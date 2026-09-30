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
    public RegionPanel(GameSession session)
    {
        _session = session;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 14 };
        var title = new StackPanel { Spacing = 4 }; title.Children.Add(ConsoleUi.PageHeading("Regional survey", "Drag to pan / wheel to zoom at the cursor / click a parcel or recovery site to inspect.")); title.Children.Add(_heading); root.Children.Add(title);
        var survey = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 14 };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(ConsoleUi.Field("Location filter", _type)); bar.Children.Add(ConsoleUi.Field("Survey layer", _layer));
        var zoom = new Button { Name = "RegionZoom", Content = "Zoom +" }; zoom.Click += (_, _) => Map.Zoom(); bar.Children.Add(zoom);
        var zoomOut = new Button { Name = "RegionZoomOut", Content = "Zoom −" }; zoomOut.Click += (_, _) => Map.ZoomOut(); bar.Children.Add(zoomOut);
        var fit = new Button { Name = "RegionFit", Content = "Fit region" }; fit.Click += (_, _) => Map.Zoom(true); bar.Children.Add(fit);
        foreach (var c in bar.Children) c.Margin = new Thickness(0, 0, 12, 0);
        var settings = ConsoleUi.Frame(bar, new Thickness(14, 10)); survey.Children.Add(settings);
        _motif.ItemsSource = new[] { new Choice("random", "Seed-selected layout") }.Concat(Enum.GetValues<SettlementMotif>().Select(m => new Choice(m.ToString(), m.ToString()))).ToArray(); _motif.SelectedIndex = 0;
        var generate = ConsoleUi.Button("Generate new world", () =>
        { if (ulong.TryParse(_seed.Text, out ulong seed)) { _session.NewRegion(seed, _motif.SelectedItem is Choice c && Enum.TryParse<SettlementMotif>(c.Id, out var motif) ? motif : null); _notice.Text = ""; Tabs.SelectedIndex = 0; } else _notice.Text = "Enter an unsigned whole-number seed."; }, "GenerateRegionButton");
        var config = new StackPanel { Spacing = 16, Margin = new Thickness(0, 14, 16, 0) };
        config.Children.Add(ConsoleUi.Section("World configuration", new StackPanel { Spacing = 14, Children =
        { ConsoleUi.Text("Generate a new region and start a fresh compound. Save your current session first if you want to return to it.", 13, ConsoleUi.Warning),
          ConsoleUi.Bar(ConsoleUi.Field("World seed", _seed), ConsoleUi.Field("Town layout", _motif), generate), _notice }}));
        config.Children.Add(ConsoleUi.Section("Regional parameters", ConsoleUi.Text("49.83 square miles / 142 × 142 parcels\n80 metres per parcel / 20 recovery site types\n\nOne main town with detached suburbs. Layout may be compact, avenue-led or formed around twin centers. Each seed reproduces its terrain, road network and recovery sites.", 13, ConsoleUi.Muted)));
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,290"), ColumnSpacing = 14 };
        body.Children.Add(ConsoleUi.Frame(Map, new Thickness(0)));
        var inspector = new StackPanel { Spacing = 14 };
        inspector.Children.Add(ConsoleUi.Text("Select a cell for terrain coverage, or a recovery site for stock and route distance.", 12, ConsoleUi.Muted));
        inspector.Children.Add(_inspection);
        _plan.Classes.Add("primary"); _plan.Click += (_, _) => { if (Map.SelectedSite is { } id) PlanExpedition?.Invoke(id); };

        var tabs = new TabControl();
        var inspectionPage = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12, Margin = new Thickness(0, 10, 0, 0) };
        inspectionPage.Children.Add(new ScrollViewer { Content = ConsoleUi.Section("Survey record", inspector), Padding = new Thickness(0, 0, 12, 0) });
        Grid.SetRow(_plan, 1); _plan.HorizontalAlignment = HorizontalAlignment.Stretch; inspectionPage.Children.Add(_plan);
        tabs.Items.Add(new TabItem { Header = "Inspection", Content = inspectionPage });
        tabs.Items.Add(new TabItem { Header = "Legend", Content = new ScrollViewer { Content = new StackPanel { Spacing = 14, Children =
        { ConsoleUi.Section("Location types", _legend), ConsoleUi.Section("Survey notation", ConsoleUi.Text("Blue / river and lake\nGreen / woodland and meadow\nTan / hills and scrub\nTeal / marsh\nGray / rock\nGold / cropland\n\nGold lines / selected route\nGold crossings / bridges\nGold marker / compound\n\nParcels represent frontage and recovery sites, rather than building interiors.", 12, ConsoleUi.Muted)) } }, Margin = new Thickness(0, 10, 0, 0) } });
        Grid.SetColumn(tabs, 1); body.Children.Add(tabs);
        Grid.SetRow(body, 1); survey.Children.Add(body);
        Tabs.Items.Add(new TabItem { Header = "Survey", Content = survey, Name = "SurveyTab" });
        Tabs.Items.Add(new TabItem { Header = "World setup", Content = new ScrollViewer { Content = config }, Name = "WorldSetupTab" });
        Grid.SetRow(Tabs, 1); root.Children.Add(Tabs); Content = root;
        Map.SiteClicked += _ => Refresh(); Map.CellClicked += _ => Refresh();
        _layer.SelectionChanged += (_, _) => { if (_layer.SelectedItem is MapLayer layer) Map.Layer = layer; };
        _type.SelectionChanged += (_, _) => { if (_refreshing) return; Map.HighlightType = (_type.SelectedItem as Choice)?.Id is { } id && id != "all" ? id : null; Map.InvalidateVisual(); };
        Refresh();
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
        _heading.Text = $"{region.Name} · seed {region.Seed} · {region.Width * region.CellMeters / 1000m:0.00} × {region.Height * region.CellMeters / 1000m:0.00} km / {region.SquareMiles:0.00} square miles · {region.Motif} · {region.Sites.Count} sites / 20 types";
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
        if (Map.SelectedSite is null && Map.SelectedCell is { } point) _inspection.Text = TerrainDescription(region, point);
        if (Map.SelectedSite is { } id && region.Site(id) is { } site)
        {
            var location = _session.World.Module<ScavengingModule>().Location(id)!; var d = location.Definition;
            _inspection.Text = $"{d.Name}\n{site.LocationId} / {site.Variant}\n{region.Cell(site.Parcel).District} · parcel ({site.Parcel.X},{site.Parcel.Y})\n{site.RoadDistanceMeters / 1000m:0.00} km by road · {d.TravelTicks} ticks each way\nIntegrity {location.Integrity:P0} · rivals {d.RivalPressure:P0}\n\n{TerrainDescription(region, site.Parcel)}\n\nRecoverable stock:\n"
                + string.Join("\n", d.Salvage.Select(s => $"{location.Remaining(s.Id):0.###} kg {s.MaterialId}\n  {s.Object}"));
        }
    }
}
