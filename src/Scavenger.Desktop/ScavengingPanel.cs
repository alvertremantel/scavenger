using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haldane.CareerSim.Work;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Desktop;

/// <summary>Destination inspection, manifest planning, team dispatch, field decisions and early clinic loop.</summary>
public sealed class ScavengingPanel : UserControl
{
    private readonly GameSession _session;
    private readonly ComboBox _location = new() { Name = "ExpeditionLocation", MinWidth = 210 };
    private readonly ComboBox _active = new() { Name = "ActiveExpedition", MinWidth = 180 };
    private readonly ComboBox _patient = new() { Name = "ClinicPatient", MinWidth = 180 };
    private readonly ComboBox _approach = new() { Name = "ExpeditionApproach", ItemsSource = new[] { ExpeditionApproach.Careful, ExpeditionApproach.Quick }, SelectedIndex = 0 };
    private readonly CheckBox _protection = new() { Name = "ExpeditionProtection", Content = "Use protective supplies (0.02 kg/person)", IsChecked = true };
    private readonly NumericUpDown _quantity = new() { Name = "SalvageQuantity", Minimum = .01m, Maximum = 48, Increment = .1m, Value = 1, Width = 110, FormatString = "0.00" };
    private readonly TextBlock _description = Text(""), _preview = Text("");
    private readonly StackPanel _health = new();
    private readonly StackPanel _yields = new() { Spacing = 4 }, _jobs = new() { Spacing = 4 };
    public ListBox TeamSelector { get; } = new() { Name = "ExpeditionTeam", SelectionMode = SelectionMode.Multiple, Height = 140, MinWidth = 170 };
    public ListBox SalvageSelector { get; } = new() { Name = "SalvageManifest", SelectionMode = SelectionMode.Multiple, Height = 140, MinWidth = 260 };
    private string? _shownLocation;
    private bool _refreshing;
    public TabControl Tabs { get; } = new() { Name = "FieldTabs" };
    public event Action<string>? Navigate;
    public ScavengingPanel(GameSession session)
    {
        _session = session;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        root.Children.Add(ConsoleUi.PageHeading("Field operations", "Recover specific supplies, coordinate teams and keep the compound healthy."));
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(ConsoleUi.Section("Destination", new StackPanel { Spacing = 12, Children =
        {
            Bar(ConsoleUi.Field("Recovery site", _location), ConsoleUi.Field("Approach", _approach), _protection,
                ConsoleUi.Button("Regional survey →", () => Navigate?.Invoke("region"))), _description
        }}));
        var selectors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2*"), ColumnSpacing = 14 };
        selectors.Children.Add(ConsoleUi.Section("Field team", TeamSelector, "Select multiple workers to form a team."));
        var cargo = ConsoleUi.Section("Recovery manifest", SalvageSelector, "Select objects. Individual computer units travel whole."); Grid.SetColumn(cargo, 1); selectors.Children.Add(cargo);
        panel.Children.Add(selectors);
        panel.Children.Add(ConsoleUi.Section("Dispatch authorization", new StackPanel { Spacing = 12, Children =
        { Bar(ConsoleUi.Field("kg per bulk object", _quantity), Button("Dispatch team", Dispatch, "DispatchExpeditionButton")), _preview }}));
        panel.Children.Add(ConsoleUi.Section("Active expeditions", new StackPanel { Spacing = 12, Children =
        { Bar(ConsoleUi.Field("Field team", _active), Button("Assess / wait", () => SelectedJob(e => new RespondToExpeditionCommand(e, ExpeditionResponse.Wait)), "WaitExpeditionButton"),
            Button("Recall / retreat", () => SelectedJob(e => new RecallExpeditionCommand(e)), "RecallExpeditionButton")), _jobs }}));
        var clinic = new StackPanel { Spacing = 16 };
        clinic.Children.Add(ConsoleUi.Section("Treatment supply chain", new StackPanel { Spacing = 14, Children =
        {
            Text("Fever care requires pharmacy treatment stock, excipient and intact sterile water, plus labelled carbon sorbent from hardware. Refine recovered stock and capture rejects before formulating at the clinic. Opened stock and generic process water fail purity and grade requirements."),
            Bar(Button("Refine fever stock", () => Medicine("refine-fever-stock"), "RefineMedicineButton"),
                Button("Formulate clinic dose", () => Medicine("formulate-fever-dose"), "FormulateMedicineButton"),
                ConsoleUi.Button("Inspect ingredients →", () => Navigate?.Invoke("register/0"))),
            Text("Clinical compounds and treatments in this scenario are fictional.", 11)
        }}));
        clinic.Children.Add(ConsoleUi.Section("Patient care", Bar(ConsoleUi.Field("Patient", _patient), Button("Treat patient", Treat, "TreatPatientButton"),
            Button("Cancel patient care", CancelCare, "CancelTreatmentButton"))));
        clinic.Children.Add(ConsoleUi.Section("Compound health", _health));
        // Keep care status above supply-chain documentation in the clinic workspace.
        var health = clinic.Children[2]; clinic.Children.RemoveAt(2); clinic.Children.Insert(0, health);
        var yields = new StackPanel { Spacing = 16 }; yields.Children.Add(Text("Stock reflects the selected expedition destination. Recovery depends on worker skills, access, kit and building integrity."));
        yields.Children.Add(ConsoleUi.Section("Recoverable objects", _yields));
        Tabs.Items.Add(new TabItem { Header = "Expedition planner", Content = new ScrollViewer { Content = panel, Margin = new Thickness(0, 14, 0, 0) } });
        Tabs.Items.Add(new TabItem { Header = "Clinic", Content = new ScrollViewer { Content = clinic, Margin = new Thickness(0, 14, 0, 0) } });
        Tabs.Items.Add(new TabItem { Header = "Site inventory", Content = new ScrollViewer { Content = yields, Margin = new Thickness(0, 14, 0, 0) } });
        Grid.SetRow(Tabs, 1); root.Children.Add(Tabs); Content = root;
        _location.SelectionChanged += (_, _) => { _shownLocation = null; Refresh(); };
        TeamSelector.SelectionChanged += (_, _) => Preview(); SalvageSelector.SelectionChanged += (_, _) => Preview();
        _quantity.ValueChanged += (_, _) => Preview(); _approach.SelectionChanged += (_, _) => Preview(); _protection.IsCheckedChanged += (_, _) => Preview();
        Refresh();
    }
    private static TextBlock Text(string value, double size = 12) => ConsoleUi.Text(value, size, ConsoleUi.Muted);
    private static Button Button(string text, Action action, string name) => ConsoleUi.Button(text, action, name, name is "DispatchExpeditionButton" or "TreatPatientButton");
    private static WrapPanel Bar(params Control[] controls) => ConsoleUi.Bar(controls);
    private static string? Selected(ComboBox selector) => (selector.SelectedItem as Choice)?.Id;
    private static void Choices(ComboBox selector, IEnumerable<Choice> values)
    {
        string? id = Selected(selector); var choices = values.ToArray();
        selector.ItemsSource = choices; selector.SelectedItem = choices.FirstOrDefault(c => c.Id == id) ?? choices.FirstOrDefault();
    }
    private ExpeditionPlan? Plan()
    {
        if (!_session.World.TryModule<ScavengingModule>(out var module) || Selected(_location) is not { } locationId) return null;
        var location = module.Location(locationId)!;
        var manifest = SalvageSelector.SelectedItems!.Cast<Choice>().Select(c =>
        {
            var s = location.Definition.Salvage.Single(s => s.Id == c.Id);
            return new SalvageRequest(s.Id, s.ComponentId is not null ? s.InitialKg : Math.Min(_quantity.Value ?? 1, location.Remaining(s.Id)));
        }).ToArray();
        return new(_session.NextId("expedition"), locationId, TeamSelector.SelectedItems!.Cast<Choice>().Select(c => int.Parse(c.Id)).Order().ToArray(), manifest,
            Approach: _approach.SelectedItem is ExpeditionApproach approach ? approach : ExpeditionApproach.Careful, UseProtection: _protection.IsChecked == true);
    }
    private void Preview()
    {
        if (_refreshing || Plan() is not { } plan) return;
        var m = _session.World.Module<ScavengingModule>();
        _preview.Text = $"Manifest {plan.Manifest.Sum(s => s.QuantityKg):0.##} / {plan.Team.Count * 12} kg carrying capacity. "
            + (m.Explain(_session.World, plan) ?? "Ready: team, kit and receiving space will be reserved; distant stock can still be taken by rivals.");
    }
    private void Dispatch() { if (Plan() is { } p) _session.Submit(new DispatchExpeditionCommand(p)); }
    private void SelectedJob(Func<string, Haldane.Kernel.Simulation.ICommand> command)
    { if (Selected(_active) is { } id) _session.Submit(command(id)); }
    private void Medicine(string recipe)
    { if (_session.World.Has<ScavengingModule>()) _session.Submit(new QueueProductionCommand(ScavengingScenario.MedicinePlan(_session.NextId("medicine"), recipe))); }
    private void Treat()
    { if (Selected(_patient) is { } id) _session.Submit(new QueueTreatmentCommand(new(_session.NextId("clinic"), int.Parse(id)))); }
    private void CancelCare()
    {
        if (_session.World.TryModule<ScavengingModule>(out var module) && Selected(_patient) is { } id
            && module.Treatments.FirstOrDefault(t => t.Active && t.Plan.PatientId == int.Parse(id)) is { } treatment)
            _session.Submit(new CancelTreatmentCommand(treatment.Plan.Id));
    }
    public void SelectLocation(string id)
    { _location.SelectedItem = _location.Items.Cast<Choice>().FirstOrDefault(c => c.Id == id); }
    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
        if (!_session.World.TryModule<ScavengingModule>(out var module))
        { IsEnabled = false; _description.Text = "This save uses the earlier compound fixture. Reset starts the configured scenario."; return; }
        IsEnabled = true;
        if (!_location.Items.Cast<Choice>().Select(c => c.Id).Order().SequenceEqual(module.Locations.Select(l => l.Definition.Id).Order())) Choices(_location, module.Locations.OrderBy(l => l.Definition.Id.StartsWith("pharmacy", StringComparison.Ordinal) ? 0 : 1).ThenBy(l => l.Definition.TravelTicks).Select(l => new Choice(l.Definition.Id, l.Definition.Name)));
        var workforce = _session.World.Module<WorkforceModule>();
        var crew = module.Health.Select(h => new Choice(h.WorkerId.ToString(), $"{h.WorkerId} {workforce.Find(h.WorkerId)!.Name} / {(h.Sick ? "ill" : module.IsAway(h.WorkerId) ? "away" : workforce.ReservationFor(h.WorkerId) is null ? "available" : "assigned")}")).ToArray();
        if (!TeamSelector.Items.Cast<Choice>().SequenceEqual(crew))
        {
            var selected = TeamSelector.Items.Count == 0 ? new[] { "2", "4" } : TeamSelector.SelectedItems!.Cast<Choice>().Select(c => c.Id).ToArray();
            TeamSelector.ItemsSource = crew; TeamSelector.SelectedItems!.Clear();
            foreach (var c in crew.Where(c => selected.Contains(c.Id))) TeamSelector.SelectedItems!.Add(c);
        }
        if (Selected(_location) is { } id)
        {
            var l = module.Location(id)!;
            _description.Text = $"{l.Definition.Name} ({l.Definition.X},{l.Definition.Y}) · {l.Definition.TravelTicks} ticks each way carefully · integrity {l.Integrity:P0} (−{l.Definition.DecayPerDay:P1}/day) · rival pressure {l.Definition.RivalPressure:P0} · exposure {l.Definition.ExposurePerTick:0.000}/search tick\n{l.Definition.Description}";
            if (_shownLocation != id || !SalvageSelector.Items.Cast<Choice>().Select(c => c.Id).SequenceEqual(l.Definition.Salvage.Select(s => s.Id)))
            {
                _shownLocation = id; SalvageSelector.ItemsSource = l.Definition.Salvage.Select(s => new Choice(s.Id, s.Object)).ToArray();
                SalvageSelector.SelectedItems!.Clear();
            }
            _yields.Children.Clear();
            foreach (var s in l.Definition.Salvage)
                _yields.Children.Add(Text($"{s.Object}\n  {l.Remaining(s.Id):0.###}/{s.InitialKg} kg {s.MaterialId} · purity {s.Quality.Purity:P1} · {s.Quality.Grade} · {s.Qualification.SkillId} {s.Qualification.MinimumRating} · access ≥{s.MinimumIntegrity:P0}{(s.NeedsPrybar ? " + prybar" : "")} · {s.EffortPerKg} effort/kg{(s.ComponentId is not null ? " · WHOLE UNIT" : "")}"));
        }
        Choices(_active, module.Expeditions.Where(e => e.Active).Select(e => new Choice(e.Plan.Id, $"{e.Plan.Id} / {e.Status}")));
        Choices(_patient, module.Health.Where(h => h.Sick).Select(h => new Choice(h.WorkerId.ToString(), _session.World.Module<WorkforceModule>().Find(h.WorkerId)!.Name)));
        _jobs.Children.Clear();
        if (module.Expeditions.Count == 0) _jobs.Children.Add(Text("No teams dispatched. Select a destination, crew and manifest to plan a recovery run."));
        foreach (var e in module.Expeditions.TakeLast(20))
            _jobs.Children.Add(Text($"{e.Plan.Id} → {module.Location(e.Plan.LocationId)!.Definition.Name} · {e.Status} · crew {string.Join(",", e.Plan.Team)} · cargo {e.Cargo.Sum(c => c.QuantityKg):0.##} kg / received {e.Receipts.Sum(c => c.QuantityKg):0.##} kg\n  {e.Reason}"));
        ConsoleUi.Table(_health, ["Operator", "Duty status", "Exposure"], "*,2*,*", module.Health.Select(h => new[]
        { workforce.Find(h.WorkerId)!.Name, h.Sick ? "Ill / unavailable" : module.IsAway(h.WorkerId) ? "On expedition" : "Home / fit for duty", $"{h.Exposure:0.000} / 1" }));
        foreach (var t in module.Treatments)
            _health.Children.Add(Text($"Care {t.Plan.Id}: {(t.Completed ? "completed" : t.Cancelled ? "cancelled" : t.Reason ?? "treating")} · {t.Effort:0.##}/4 effort"));
        }
        finally { _refreshing = false; }
        Preview();
    }
}
