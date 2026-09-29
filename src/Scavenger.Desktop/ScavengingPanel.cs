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
    private readonly TextBlock _description = Text(""), _preview = Text(""), _health = Text("");
    private readonly StackPanel _yields = new() { Spacing = 4 }, _jobs = new() { Spacing = 4 };
    public ListBox TeamSelector { get; } = new() { Name = "ExpeditionTeam", SelectionMode = SelectionMode.Multiple, Height = 95, MinWidth = 170 };
    public ListBox SalvageSelector { get; } = new() { Name = "SalvageManifest", SelectionMode = SelectionMode.Multiple, Height = 125, MinWidth = 260 };
    private string? _shownLocation;
    private bool _refreshing;
    public ScavengingPanel(GameSession session)
    {
        _session = session;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        panel.Children.Add(Text("Choose destinations for specific shortages. Select several workers and objects; bulk quantity applies to each selected object, capped by remaining stock. Computer parts always travel whole.", 12));
        panel.Children.Add(Bar(_location, _approach, _protection));
        panel.Children.Add(_description);
        var selectors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2*"), ColumnSpacing = 12 };
        selectors.Children.Add(TeamSelector); Grid.SetColumn(SalvageSelector, 1); selectors.Children.Add(SalvageSelector);
        panel.Children.Add(Bar(Text("kg per bulk object"), _quantity, Button("Dispatch team", Dispatch, "DispatchExpeditionButton")));
        panel.Children.Add(selectors);
        panel.Children.Add(_preview);
        panel.Children.Add(Bar(_active, Button("Assess / wait", () => SelectedJob(e => new RespondToExpeditionCommand(e, ExpeditionResponse.Wait)), "WaitExpeditionButton"),
            Button("Recall / retreat", () => SelectedJob(e => new RecallExpeditionCommand(e)), "RecallExpeditionButton")));
        panel.Children.Add(_jobs);
        panel.Children.Add(Text("EARLY CLINIC CHAIN", 13));
        panel.Children.Add(Text("Mira's fictional fever needs pharmacy treatment stock, excipient and intact sterile water, plus labelled carbon sorbent from hardware. Bring stock home, refine with captured rejects, then formulate at the clinic. Opened stock and generic process water fail the recipe's grade/purity gates.", 12));
        panel.Children.Add(Bar(Button("Refine fever stock", () => Medicine("refine-fever-stock"), "RefineMedicineButton"),
            Button("Formulate clinic dose", () => Medicine("formulate-fever-dose"), "FormulateMedicineButton"), _patient,
            Button("Treat patient", Treat, "TreatPatientButton"),
            Button("Cancel patient care", CancelCare, "CancelTreatmentButton")));
        panel.Children.Add(_health);
        panel.Children.Add(Text("LOCATION YIELDS · finite recoverable stock", 13)); panel.Children.Add(_yields);
        Content = new ScrollViewer { Content = panel };
        _location.SelectionChanged += (_, _) => { _shownLocation = null; Refresh(); };
        TeamSelector.SelectionChanged += (_, _) => Preview(); SalvageSelector.SelectionChanged += (_, _) => Preview();
        _quantity.ValueChanged += (_, _) => Preview(); _approach.SelectionChanged += (_, _) => Preview(); _protection.IsCheckedChanged += (_, _) => Preview();
        Refresh();
    }
    private static TextBlock Text(string value, double size = 12) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E4EDF0")) };
    private static Button Button(string text, Action action, string name)
    { var b = new Button { Content = text, Name = name, Padding = new Thickness(9, 6) }; b.Click += (_, _) => action(); return b; }
    private static WrapPanel Bar(params Control[] controls)
    { var p = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var c in controls) { c.Margin = new Thickness(0, 0, 8, 4); p.Children.Add(c); } return p; }
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
    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
        if (!_session.World.TryModule<ScavengingModule>(out var module))
        { IsEnabled = false; _description.Text = "This save uses the earlier compound fixture. Reset starts the configured scenario."; return; }
        IsEnabled = true;
        if (!_location.Items.Cast<Choice>().Select(c => c.Id).Order().SequenceEqual(module.Locations.Select(l => l.Definition.Id).Order())) Choices(_location, module.Locations.OrderBy(l => l.Definition.Id == "pharmacy" ? 0 : 1).Select(l => new Choice(l.Definition.Id, l.Definition.Name)));
        if (TeamSelector.Items.Count != module.Health.Count)
        {
            TeamSelector.ItemsSource = module.Health.Select(h => new Choice(h.WorkerId.ToString(), $"{h.WorkerId} {_session.World.Module<WorkforceModule>().Find(h.WorkerId)!.Name}")).ToArray();
            foreach (var c in TeamSelector.Items.Cast<Choice>().Where(c => c.Id is "2" or "4")) TeamSelector.SelectedItems!.Add(c);
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
        foreach (var e in module.Expeditions.TakeLast(20))
            _jobs.Children.Add(Text($"{e.Plan.Id} → {module.Location(e.Plan.LocationId)!.Definition.Name} · {e.Status} · crew {string.Join(",", e.Plan.Team)} · cargo {e.Cargo.Sum(c => c.QuantityKg):0.##} kg / received {e.Receipts.Sum(c => c.QuantityKg):0.##} kg\n  {e.Reason}"));
        _health.Text = string.Join("\n", module.Health.Select(h => $"{_session.World.Module<WorkforceModule>().Find(h.WorkerId)!.Name}: {(h.Sick ? "ill / unavailable" : module.IsAway(h.WorkerId) ? "on expedition" : "home")} · exposure {h.Exposure:0.000} / symptoms at 1"))
            + string.Concat(module.Treatments.Select(t => $"\nClinic {t.Plan.Id}: {(t.Completed ? "completed" : t.Cancelled ? "cancelled" : t.Reason ?? "treating")} · {t.Effort:0.##}/4 effort"));
        }
        finally { _refreshing = false; }
        Preview();
    }
}
