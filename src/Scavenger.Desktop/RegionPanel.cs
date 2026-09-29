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
    private readonly ComboBox _type = new() { Name = "RegionType", Width = 240 };
    private readonly StackPanel _legend = new() { Spacing = 3 };
    private bool _refreshing;
    public RegionMapControl Map { get; } = new() { Name = "RegionMap", MinHeight = 360 };
    public event Action<string>? PlanExpedition;
    public RegionPanel(GameSession session)
    {
        _session = session;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 10 };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_seed);
        var generate = new Button { Name = "GenerateRegionButton", Content = "New world from seed", Margin = new Thickness(8, 0), Padding = new Thickness(10, 6) };
        generate.Click += (_, _) => { if (ulong.TryParse(_seed.Text, out ulong seed)) { _session.NewRegion(seed); _notice.Text = ""; } else _notice.Text = "Enter an unsigned whole-number seed."; };
        bar.Children.Add(generate); bar.Children.Add(_type); bar.Children.Add(_notice); root.Children.Add(bar);
        Grid.SetRow(_heading, 1); root.Children.Add(_heading);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,310"), ColumnSpacing = 16 };
        body.Children.Add(Map);
        var inspector = new StackPanel { Spacing = 10 };
        inspector.Children.Add(Text("Click a site to inspect it. The highlighted route uses the same road distance as expedition travel."));
        inspector.Children.Add(_inspection);
        var plan = new Button { Name = "PlanRegionalExpeditionButton", Content = "Plan expedition here", Padding = new Thickness(10, 6) };
        plan.Click += (_, _) => { if (Map.SelectedSite is { } id) PlanExpedition?.Invoke(id); };
        inspector.Children.Add(plan); inspector.Children.Add(Text("LOCATION TYPES", 13)); inspector.Children.Add(_legend);
        inspector.Children.Add(Text("Land use: gray center · blue-gray civic · teal residential · brown industrial · olive farms · dark green woods · blue river. Gold crossings are bridges. Regional parcels represent frontage and recoverable sites, not building interiors."));
        var scroll = new ScrollViewer { Content = inspector }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        Grid.SetRow(body, 2); root.Children.Add(body); Content = root;
        Map.SiteClicked += _ => Refresh();
        _type.SelectionChanged += (_, _) => { if (_refreshing) return; Map.HighlightType = (_type.SelectedItem as Choice)?.Id is { } id && id != "all" ? id : null; Map.InvalidateVisual(); };
        Refresh();
    }
    private static TextBlock Text(string text, double size = 12) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#e4edf0")) };
    public void Refresh()
    {
        if (!_session.World.TryModule<RegionModule>(out var region))
        { Map.Region = null; _heading.Text = "Authored fixture / no generated regional map. Enter a seed to start a generated world."; _inspection.Text = ""; _legend.Children.Clear(); return; }
        bool changed = Map.Region != region;
        Map.Region = region;
        _heading.Text = $"{region.Name} · seed {region.Seed} · 5.12 × 5.12 km / {region.SquareMiles:0.00} square miles · {region.Sites.Count} sites / 20 types";
        if (changed)
        {
            _seed.Text = region.Seed.ToString();
            Map.SelectedSite = region.Sites.Where(s => s.TypeId == "pharmacy").OrderBy(s => s.RoadDistanceMeters).First().LocationId;
            _refreshing = true;
            _type.ItemsSource = new[] { new Choice("all", "All location types") }.Concat(RegionalContent.Types.Select(t => new Choice(t.Id, t.Name))).ToArray(); _type.SelectedIndex = 0;
            Map.HighlightType = null; _refreshing = false;
            _legend.Children.Clear();
            foreach (var type in RegionalContent.Types) _legend.Children.Add(Text($"{type.Symbol,-3} {type.Name} · {region.Sites.Count(s => s.TypeId == type.Id)}"));
        }
        if (Map.SelectedSite is { } id && region.Site(id) is { } site)
        {
            var location = _session.World.Module<ScavengingModule>().Location(id)!; var d = location.Definition;
            _inspection.Text = $"{d.Name}\n{site.LocationId} / {site.Variant}\n{region.Cell(site.Parcel).District} · parcel ({site.Parcel.X},{site.Parcel.Y})\n{site.RoadDistanceMeters / 1000m:0.00} km by road · {d.TravelTicks} ticks each way\nIntegrity {location.Integrity:P0} · rivals {d.RivalPressure:P0}\n\nRecoverable stock:\n"
                + string.Join("\n", d.Salvage.Select(s => $"{location.Remaining(s.Id):0.###} kg {s.MaterialId}\n  {s.Object}"));
        }
    }
}
