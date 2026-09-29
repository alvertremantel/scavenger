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
    private readonly TextBlock _heading = Text("", 15), _inspection = Text(""), _notice = Text("");
    private readonly TextBox _seed = new() { Name = "RegionSeed", Width = 200 };
    private readonly ComboBox _motif = new() { Name = "RegionMotif", Width = 180 };
    private readonly ComboBox _layer = new() { Name = "RegionLayer", Width = 155, ItemsSource = Enum.GetValues<MapLayer>(), SelectedIndex = 0 };
    private readonly ComboBox _type = new() { Name = "RegionType", Width = 240 };
    private readonly StackPanel _legend = new() { Spacing = 3 };
    private bool _refreshing;
    private readonly Button _plan = new() { Name = "PlanRegionalExpeditionButton", Content = "Plan expedition here", Padding = new Thickness(10, 6) };
    public RegionMapControl Map { get; } = new() { Name = "RegionMap", MinHeight = 360 };
    public event Action<string>? PlanExpedition;
    public RegionPanel(GameSession session)
    {
        _session = session;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 10 };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_seed);
        var generate = new Button { Name = "GenerateRegionButton", Content = "New world from seed", Margin = new Thickness(8, 0), Padding = new Thickness(10, 6) };
        generate.Click += (_, _) => { if (ulong.TryParse(_seed.Text, out ulong seed)) { _session.NewRegion(seed, _motif.SelectedItem is Choice c && Enum.TryParse<SettlementMotif>(c.Id, out var motif) ? motif : null); _notice.Text = ""; } else _notice.Text = "Enter an unsigned whole-number seed."; };
        bar.Children.Add(generate);
        _motif.ItemsSource = new[] { new Choice("random", "Seed-selected layout") }.Concat(Enum.GetValues<SettlementMotif>().Select(m => new Choice(m.ToString(), m.ToString()))).ToArray(); _motif.SelectedIndex = 0;
        bar.Children.Add(_motif); bar.Children.Add(_type); bar.Children.Add(_layer);
        var zoom = new Button { Name = "RegionZoom", Content = "Zoom +" }; zoom.Click += (_, _) => Map.Zoom(); bar.Children.Add(zoom);
        var fit = new Button { Name = "RegionFit", Content = "Fit" }; fit.Click += (_, _) => Map.Zoom(true); bar.Children.Add(fit); bar.Children.Add(_notice); root.Children.Add(bar);
        Grid.SetRow(_heading, 1); root.Children.Add(_heading);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,310"), ColumnSpacing = 16 };
        body.Children.Add(Map);
        var inspector = new StackPanel { Spacing = 10 };
        inspector.Children.Add(Text("Click any cell for terrain percentages; click a site for salvage. Zoom around the selected cell. The highlighted route uses the same road distance as expedition travel."));
        inspector.Children.Add(_inspection);
        _plan.Click += (_, _) => { if (Map.SelectedSite is { } id) PlanExpedition?.Invoke(id); };
        inspector.Children.Add(_plan); inspector.Children.Add(Text("LOCATION TYPES", 13)); inspector.Children.Add(_legend);
        inspector.Children.Add(Text("Terrain blends river/lake blue, woodland green, meadow pale green, hills/scrub tan, marsh teal, rock gray and cropland gold. Use Districts, Elevation, Moisture or FloodRisk to inspect other layers. Gold crossings are bridges. Regional parcels represent frontage and recoverable sites, not building interiors."));
        var scroll = new ScrollViewer { Content = inspector }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        Grid.SetRow(body, 2); root.Children.Add(body); Content = root;
        Map.SiteClicked += _ => Refresh(); Map.CellClicked += _ => Refresh();
        _layer.SelectionChanged += (_, _) => { if (_layer.SelectedItem is MapLayer layer) Map.Layer = layer; };
        _type.SelectionChanged += (_, _) => { if (_refreshing) return; Map.HighlightType = (_type.SelectedItem as Choice)?.Id is { } id && id != "all" ? id : null; Map.InvalidateVisual(); };
        Refresh();
    }
    private static TextBlock Text(string text, double size = 12) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#e4edf0")) };
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
