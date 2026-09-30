using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Scavenger.Desktop;

/// <summary>A persistent map with one contextual drawer. Switching context never reparents the map.</summary>
public sealed class MapWorkspace : UserControl
{
    private readonly Grid _body = new() { ColumnDefinitions = new ColumnDefinitions("*,360"), ColumnSpacing = 10 };
    private readonly Border _drawer;
    private readonly ContentControl _content = new();
    private readonly ComboBox _mode;
    private readonly Button _open;
    private bool _changing;
    public string Mode => (_mode.SelectedItem as Choice)?.Id ?? "";
    public bool SidebarOpen => _drawer.IsVisible;
    public event Action<string>? ModeChanged;
    public MapWorkspace(string name, Control map, Control toolbar, IEnumerable<Choice> modes)
    {
        Name = name + "Workspace";
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        header.Children.Add(toolbar);
        _open = ConsoleUi.Button("Manage →", () => SetSidebar(true), name + "OpenSidebarButton");
        _open.IsVisible = false; Grid.SetColumn(_open, 1); header.Children.Add(_open); root.Children.Add(header);
        _body.Children.Add(ConsoleUi.Frame(map, new Thickness(0)));
        _mode = new ComboBox { Name = name + "Context", ItemsSource = modes.ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        _mode.SelectedIndex = 0;
        var drawerRoot = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        var drawerHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        drawerHeader.Children.Add(_mode);
        var close = ConsoleUi.Button("×", () => SetSidebar(false), name + "CloseSidebarButton");
        ToolTip.SetTip(close, "Collapse management sidebar"); Grid.SetColumn(close, 1); drawerHeader.Children.Add(close);
        drawerRoot.Children.Add(drawerHeader); Grid.SetRow(_content, 1); drawerRoot.Children.Add(_content);
        _drawer = ConsoleUi.Frame(drawerRoot, new Thickness(12)); Grid.SetColumn(_drawer, 1); _body.Children.Add(_drawer);
        Grid.SetRow(_body, 1); root.Children.Add(_body); Content = root;
        _mode.SelectionChanged += (_, _) => { if (!_changing) { SetSidebar(true); ModeChanged?.Invoke(Mode); } };
    }
    public void Show(string mode, Control content)
    {
        _changing = true;
        try { _mode.SelectedItem = _mode.Items.Cast<Choice>().Single(c => c.Id == mode); _content.Content = content; }
        finally { _changing = false; }
        SetSidebar(true);
    }
    public void SetSidebar(bool open)
    {
        _drawer.IsVisible = open; _open.IsVisible = !open;
        _body.ColumnDefinitions[1].Width = new GridLength(open ? 360 : 0);
        _body.ColumnSpacing = open ? 10 : 0;
    }
}
