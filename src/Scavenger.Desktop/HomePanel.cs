using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Region;
using Scavenger.Sim.Scavenging;
using static Scavenger.Desktop.ConsoleUi;

namespace Scavenger.Desktop;

/// <summary>Live operational summary. Every figure and alert is derived from the current world.</summary>
public sealed class HomePanel : UserControl
{
    private readonly GameSession _session;
    private readonly StackPanel _body = new() { Spacing = 18, Margin = new Thickness(0, 0, 16, 12) };
    public event Action<string>? Navigate;
    public HomePanel(GameSession session) { _session = session; Content = new ScrollViewer { Content = _body }; Refresh(); }
    private Button Link(string label, string route, bool primary = false) => Button(label, () => Navigate?.Invoke(route), "Home" + route.Replace("/", "") + "Button", primary);
    private static Control Metric(string label, string value, string note)
    {
        var p = new StackPanel { Spacing = 6 }; p.Children.Add(Eyebrow(label)); p.Children.Add(Text(value, 28)); p.Children.Add(Text(note, 11, Muted)); return Frame(p);
    }
    private static Control Meter(string label, decimal value, decimal total, string unit)
    {
        var p = new StackPanel { Spacing = 6 }; var h = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        h.Children.Add(Text(label)); var v = Text($"{value:0.##} / {total:0.##} {unit}", 11, Muted); Grid.SetColumn(v, 1); h.Children.Add(v); p.Children.Add(h);
        var track = new Grid { Height = 3, Background = Brush(Line) };
        track.Children.Add(new Border { Background = Brush(Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 });
        track.SizeChanged += (_, _) => ((Border)track.Children[0]).Width = track.Bounds.Width * (double)(total <= 0 ? 0 : Math.Clamp(value / total, 0, 1));
        p.Children.Add(track); return p;
    }
    public void Refresh()
    {
        _body.Children.Clear(); var r = _session.Report; var world = _session.World;
        world.TryModule<ScavengingModule>(out var scavenging); world.TryModule<EnergyModule>(out var energy); world.TryModule<ComputingModule>(out var computing);
        world.TryModule<ProductionModule>(out var production); world.TryModule<RegionModule>(out var region);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(PageHeading("Operations overview", "Your compound, at a glance. Review constraints, assign teams, keep systems running."));
        var tag = Text(region is null ? "LOCAL OPERATIONS" : $"REGION {region.Seed}  /  {region.SquareMiles:0.0} MI²", 10, Accent);
        tag.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(tag, 1); heading.Children.Add(tag); _body.Children.Add(heading);
        int sick = scavenging?.Health.Count(h => h.Sick) ?? 0, away = r.Workers.Count(w => w.Away);
        var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 12 };
        Control[] cards = [Metric("Personnel", $"{r.Workers.Count - sick} / {r.Workers.Count}", $"fit for duty · {away} in the field"),
            Metric("Stock on hand", $"{r.Inventory.StoredKg:0.0} kg", $"{r.Inventory.InTransitKg:0.0} kg moving between stores"),
            Metric("Field teams", $"{scavenging?.Expeditions.Count(e => e.Active) ?? 0:00}", $"{region?.Sites.Count ?? scavenging?.Locations.Count ?? 0} mapped recovery sites"),
            Metric("Power delivered", energy?.LastFrame is { } f ? $"{f.ServedKw:0.00} kW" : "—", energy?.LastFrame is { } frame ? $"{frame.RequestedKw:0.00} kW requested · last interval" : "Awaiting first operating interval")];
        for (int i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i); metrics.Children.Add(cards[i]); } _body.Children.Add(metrics);
        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        var attention = new StackPanel { Spacing = 10 };
        if (scavenging is not null)
        {
            var patients = scavenging.Health.Where(h => h.Sick).ToArray();
            if (patients.Length > 0) attention.Children.Add(Alert("CARE REQUIRED", $"Unavailable for duty: {string.Join(", ", patients.Select(h => r.Workers.Single(w => w.Id == h.WorkerId).Name))}.", "Open clinic →", "clinic"));
            var decisions = scavenging.Expeditions.Where(e => e.Status == ExpeditionStatus.Decision).ToArray();
            if (decisions.Length > 0) attention.Children.Add(Alert("FIELD DECISION", $"{decisions.Length} teams waiting · {decisions[0].Reason}", "Review team →", "scavenging"));
        }
        if (production is not null)
        {
            var blocked = production.Jobs.Where(j => j.Active && j.BlockedReason is not null).ToArray();
            if (blocked.Length > 0) attention.Children.Add(Alert("PRODUCTION CONSTRAINT", $"{blocked.Length} batches waiting · {blocked[0].BlockedReason}", "Review production →", "industry/0"));
        }
        if (computing is not null)
        {
            var waiting = computing.Jobs.Where(j => j.Active && j.BlockedReason is not null).ToArray();
            if (waiting.Length > 0) attention.Children.Add(Alert("COMPUTING CONSTRAINT", $"{waiting.Length} tasks waiting · {waiting[0].BlockedReason}", "Review compute →", "industry/3"));
        }
        var hauls = r.Inventory.Hauls.Where(h => h.Status is not (HaulStatus.Delivered or HaulStatus.Cancelled) && h.BlockedReason is not null).ToArray();
        var builds = r.Construction.Where(b => b.Status is not (ConstructionStatus.Completed or ConstructionStatus.Cancelled) && b.BlockedReason is not null).ToArray();
        if (hauls.Length + builds.Length > 0) attention.Children.Add(Alert("WORK QUEUE CONSTRAINT", $"{hauls.Length} hauls and {builds.Length} build jobs waiting · {hauls.FirstOrDefault()?.BlockedReason ?? builds.First().BlockedReason}", "Review jobs →", "register/3"));
        var issues = r.Buildings.Where(b => b.Kind is not (BuildingKind.Floor or BuildingKind.Wall or BuildingKind.Door or BuildingKind.Stockpile or BuildingKind.PowerCable or BuildingKind.WaterPipe or BuildingKind.DataCable) && !b.Operational).ToArray();
        if (issues.Length > 0) attention.Children.Add(Alert("SERVICE CONSTRAINT", $"{issues.Length} fixtures unavailable · {issues[0].Name}: {issues[0].BlockedReason}", "Inspect services →", "register/4"));
        if (attention.Children.Count == 0) attention.Children.Add(Text("No immediate constraints reported. Review stock targets and plan the next recovery run.", 13, Accent));
        middle.Children.Add(Section("Attention queue", attention, "Current conditions that need an operator."));
        var system = new StackPanel { Spacing = 16 };
        decimal capacity = r.Inventory.Stores.Sum(s => s.CapacityKg);
        system.Children.Add(Meter("Storage occupied", r.Inventory.StoredKg, capacity, "kg"));
        if (energy is not null)
        {
            system.Children.Add(Meter("Battery reserve", energy.Batteries.Sum(b => b.StoredKwh), energy.Batteries.Sum(b => b.Plan.CapacityKwh), "kWh"));
            system.Children.Add(Text($"Diesel available  {IndustryReport.Capture(world).FuelAvailableKg:0.00} kg", 12, Muted));
        }
        system.Children.Add(Bar(Link("Energy ledger →", "industry/4"), Link("Stores →", "register/1")));
        var sys = Section("System reserves", system, "Capacity and energy, measured from live inventories."); Grid.SetColumn(sys, 1); middle.Children.Add(sys); _body.Children.Add(middle);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        var crew = new StackPanel { Spacing = 12 }; var crewRows = new StackPanel();
        Table(crewRows, ["Operator", "Assignment", "Status"], "*,2*,*", r.Workers.Select(w => new[] { w.Name, w.Assignment ?? "Unassigned", scavenging?.HealthOf(w.Id)?.Sick == true ? "Clinic required" : w.Away ? "In the field" : w.Assignment is null ? "Available" : "Assigned" }));
        crew.Children.Add(crewRows); crew.Children.Add(Bar(Link("Personnel register →", "register/2"), Link("Plan expedition →", "scavenging", true)));
        bottom.Children.Add(Section("Duty roster", crew));
        var pipeline = new StackPanel { Spacing = 10 };
        int parts = computing?.Components.Count ?? 0, diagnosed = computing?.Components.Count(c => c.Diagnosed) ?? 0;
        pipeline.Children.Add(Text("OFFLINE COMPUTING", 10, Accent));
        pipeline.Children.Add(Text($"{parts} recovered units   /   {diagnosed} examined", 17));
        pipeline.Children.Add(Text($"{computing?.Computers.Sum(c => c.ComponentIds.Count) ?? 0} mounted components · {computing?.Copies.Count ?? 0} installed archive copies", 12, Muted));
        pipeline.Children.Add(Text($"{computing?.Samples.Count ?? 0} telemetry records · {computing?.Adapters.Count ?? 0} trained adapters", 12, Muted));
        pipeline.Children.Add(Bar(Link("Hardware →", "industry/1"), Link("Archive →", "industry/2"), Link("Compute →", "industry/3")));
        var pipe = Section("Capability development", pipeline, "Recovered parts → functional systems → automation."); Grid.SetColumn(pipe, 1); bottom.Children.Add(pipe); _body.Children.Add(bottom);
        var events = new StackPanel { Spacing = 8 };
        foreach (var entry in r.RecentEvents.TakeLast(5).Reverse()) events.Children.Add(Text(entry, 12, Muted));
        if (events.Children.Count == 0) events.Children.Add(Text("No activity recorded. Dispatch a team or queue work to begin.", 12, Muted));
        events.Children.Add(Link("Full activity log →", "register/5")); _body.Children.Add(Section("Latest activity", events));
    }
    private Control Alert(string label, string description, string button, string route)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        var text = new StackPanel { Spacing = 3 }; text.Children.Add(Text(label, 10, Warning)); text.Children.Add(Text(description)); row.Children.Add(text);
        var link = Button(button, () => Navigate?.Invoke(route), "Attention" + route.Replace("/", "") + "Button"); link.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(link, 1); row.Children.Add(link);
        return new Border { Child = row, Padding = new Thickness(0, 0, 0, 10), BorderBrush = Brush(Line), BorderThickness = new Thickness(0, 0, 0, 1) };
    }
}
