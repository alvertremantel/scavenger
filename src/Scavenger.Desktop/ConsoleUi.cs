using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Scavenger.Desktop;

/// <summary>Shared visual vocabulary for the operator console; no simulation state lives here.</summary>
public static class ConsoleUi
{
    public const string Ink = "#CBD1D3", Muted = "#8D9BA1", Line = "#30393E", Surface = "#1A2024", Accent = "#9CB8AD", Warning = "#C8AA7C";
    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    public static TextBlock Text(string text, double size = 12, string color = Ink) => new()
    { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.55 };
    public static TextBlock Eyebrow(string text) => Text(text.ToUpperInvariant(), 10, Muted);
    public static Button Button(string text, Action action, string? name = null, bool primary = false)
    {
        var b = new Button { Content = text, Name = name };
        if (primary) b.Classes.Add("primary"); b.Click += (_, _) => action(); return b;
    }
    public static Border Frame(Control child, Thickness? padding = null) => new()
    { Child = child, BorderBrush = Brush(Line), BorderThickness = new Thickness(1), Background = Brush(Surface), Padding = padding ?? new Thickness(16), CornerRadius = new CornerRadius(0) };
    public static Border Section(string title, Control content, string? subtitle = null)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Eyebrow(title));
        if (subtitle is not null) stack.Children.Add(Text(subtitle, 12, Muted));
        stack.Children.Add(content); return Frame(stack);
    }
    public static Control Field(string label, Control control)
    {
        ToolTip.SetTip(control, label);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        var stack = new StackPanel { Spacing = 5 }; stack.Children.Add(Eyebrow(label)); stack.Children.Add(control); return stack;
    }
    public static Control PageHeading(string title, string subtitle)
    {
        var p = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 0, 6) };
        p.Children.Add(Text(title, 23)); p.Children.Add(Text(subtitle, 12, Muted)); return p;
    }
    public static WrapPanel Bar(params Control[] controls)
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.Margin = new Thickness(0, 0, 10, 8); bar.Children.Add(control); }
        return bar;
    }
    // The deck retains its pages and selection, without a second row of navigation tabs.
    public static void HideHeaders(TabControl tabs) => tabs.Template = new FuncControlTemplate<TabControl>((t, _) =>
        new ContentControl { [!ContentControl.ContentProperty] = new Binding("SelectedItem.Content") { Source = t } });
    public static void Cards(StackPanel panel, string[] headers, IEnumerable<string[]> data)
    {
        panel.Children.Clear();
        foreach (var cells in data)
        {
            var card = new StackPanel { Spacing = 5 };
            card.Children.Add(Text(cells[0], 13, Accent));
            for (int i = 1; i < cells.Length; i++)
                if (cells[i] is not ("" or "—")) card.Children.Add(Text(headers[i] + " · " + cells[i], 11, Muted));
            panel.Children.Add(Frame(card, new Thickness(10)));
        }
        if (panel.Children.Count == 0) panel.Children.Add(Text("Nothing queued here.", 12, Muted));
        panel.Spacing = 8;
    }
    public static void Table(StackPanel panel, string[] headers, string widths, IEnumerable<string[]> data)
    {
        panel.Children.Clear(); var values = data.ToArray();
        void Row(string[] cells, int index)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(widths), ColumnSpacing = 14, Margin = new Thickness(14, 10) };
            for (int i = 0; i < cells.Length; i++)
            {
                var t = index < 0 ? Eyebrow(cells[i]) : Text(cells[i]);
                Grid.SetColumn(t, i); grid.Children.Add(t);
            }
            panel.Children.Add(new Border { Child = grid, Background = Brush(index < 0 ? "#242C30" : index % 2 == 0 ? "#1B2226" : "#192024"),
                BorderBrush = Brush(Line), BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        Row(headers, -1); for (int i = 0; i < values.Length; i++) Row(values[i], i);
        if (values.Length == 0) panel.Children.Add(new Border { Padding = new Thickness(14, 22), Child = Text("No records in this view.", 12, Muted) });
    }
}
