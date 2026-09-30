using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
    private readonly TextBlock _description = Text(""), _preview = new() { Name = "ExpeditionReadiness", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = ConsoleUi.Brush(ConsoleUi.Muted) };
    private readonly StackPanel _manifest = new() { Spacing = 8 }, _medicineInputs = new(), _medicalJobs = new();
    private readonly Dictionary<string, decimal> _amounts = [];
    private readonly TextBlock _clinicStatus = Text(""), _careHint = Text("");
    private readonly ComboBox _medicalJob = new() { Name = "MedicalJob", MinWidth = 180 };
    private readonly Button _refine, _formulate, _treat, _dispatch, _wait, _recall, _releaseMedic;
    private readonly StackPanel _health = new();
    private readonly StackPanel _yields = new() { Spacing = 4 }, _jobs = new() { Spacing = 4 };
    public ListBox TeamSelector { get; } = new() { Name = "ExpeditionTeam", SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle, Height = 140, MinWidth = 170 };
    public ListBox SalvageSelector { get; } = new() { Name = "SalvageManifest", SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle, Height = 140, MinWidth = 260 };
    private string? _shownLocation, _focusedDecision;
    private bool _refreshing;
    public TabControl Tabs { get; } = new() { Name = "FieldTabs" };
    public event Action<string>? Navigate;
    public ScavengingPanel(GameSession session)
    {
        _session = session;
        TeamSelector.ItemTemplate = new FuncDataTemplate<Choice>((c, _) => Text(c?.Label ?? ""));
        SalvageSelector.ItemTemplate = new FuncDataTemplate<Choice>((c, _) => Text(c?.Label ?? ""));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        root.Children.Add(Bar(ConsoleUi.PageHeading("Field operations", "Recover supplies and keep the compound healthy."),
            Button("Plan recovery", () => Tabs.SelectedIndex = 0, "OpenPlannerButton"),
            Button("Active teams →", () => Tabs.SelectedIndex = 3, "OpenTeamsButton"),
            Button("Clinic →", () => Tabs.SelectedIndex = 1, "OpenClinicButton")));
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(ConsoleUi.Section("Supply plans", Bar(
            Button("Pharmacy supplies", () => Suggest("pharmacy"), "PlanPharmacyButton"),
            Button("Clinic sorbent", () => Suggest("sorbent"), "PlanSorbentButton"),
            Button("Generator fuel", () => Suggest("fuel"), "PlanFuelButton"),
            Button("Building materials", () => Suggest("materials"), "PlanMaterialsButton")),
            "Populate a qualified crew and exact cargo quantities from a reachable stocked site. Review, edit and dispatch below."));
        panel.Children.Add(ConsoleUi.Section("Destination", new StackPanel { Spacing = 12, Children =
        {
            Bar(ConsoleUi.Field("Recovery site", _location), ConsoleUi.Field("Approach", _approach), _protection,
                ConsoleUi.Button("Regional survey →", () => Navigate?.Invoke("region"))), _description
        }}));
        var selectors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14 };
        selectors.Children.Add(ConsoleUi.Section("Field team", TeamSelector, "Click workers to toggle membership. Specialties appear beside each name."));
        var cargo = ConsoleUi.Section("Recovery manifest", SalvageSelector, "Click objects to toggle selection; quantities can be edited below. Computer units travel whole."); Grid.SetColumn(cargo, 1); selectors.Children.Add(cargo);
        panel.Children.Add(selectors);
        panel.Children.Add(ConsoleUi.Section("Cargo quantities", _manifest));
        _dispatch = Button("Dispatch team", Dispatch, "DispatchExpeditionButton");
        panel.Children.Add(ConsoleUi.Section("Dispatch authorization", new StackPanel { Spacing = 12, Children =
        { Bar(ConsoleUi.Field("Set all selected bulk kg", _quantity), _dispatch), _preview }}));
        _wait = Button("Assess / wait", () => SelectedJob(e => new RespondToExpeditionCommand(e, ExpeditionResponse.Wait)), "WaitExpeditionButton");
        _recall = Button("Recall / retreat", () => SelectedJob(e => new RecallExpeditionCommand(e)), "RecallExpeditionButton");
        _releaseMedic = Button("Release Anya from project", () => _session.ReleaseProject(4), "ReleaseMedicButton");
        var activeTeams = ConsoleUi.Section("Active expeditions", new StackPanel { Spacing = 12, Children =
        { Bar(ConsoleUi.Field("Field team", _active), _wait, _recall), _jobs }});
        var clinic = new StackPanel { Spacing = 16 };
        _refine = Button("Refine fever stock", () => Medicine("refine-fever-stock"), "RefineMedicineButton");
        _formulate = Button("Formulate clinic dose", () => Medicine("formulate-fever-dose"), "FormulateMedicineButton");
        _treat = Button("Treat patient", Treat, "TreatPatientButton");
        var careControls = ConsoleUi.Section("Clinic orders", new StackPanel { Spacing = 8, Children =
        {
            Bar(_refine, _formulate,
                Button("Plan pharmacy run →", () => Suggest("pharmacy"), "ClinicPlanPharmacyButton"),
                Button("Plan sorbent run →", () => Suggest("sorbent"), "ClinicPlanSorbentButton")),
            Bar(ConsoleUi.Field("Patient", _patient), _treat, Button("Cancel care", CancelCare, "CancelTreatmentButton")), _careHint
        } });
        clinic.Children.Add(ConsoleUi.Section("Compound health", _health));
        clinic.Children.Add(ConsoleUi.Section("Medical work queue", new StackPanel { Spacing = 12, Children = {
            Bar(_medicalJob, Button("Cancel medical batch", CancelMedical, "CancelMedicalBatchButton")), _medicalJobs } }));
        clinic.Children.Add(ConsoleUi.Section("Medical readiness", new StackPanel { Spacing = 12, Children = { _clinicStatus,
            Bar(_releaseMedic, Button("Ingredients & stock →", () => Navigate?.Invoke("register/0"), "ClinicStockButton")),
            _medicineInputs,
            Text("Refine → formulate → treat. Pharmacy supplies and hardware sorbent must satisfy purity, grade and contamination gates; process water cannot replace sealed sterile water. Ivo delivers inputs and products, Anya processes and treats. Clinical compounds here are fictional.", 11)
        } }));
        var clinicPage = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12, Margin = new Thickness(0,14,0,0) };
        clinicPage.Children.Add(careControls);
        var careBody = new ScrollViewer { Content = clinic }; Grid.SetRow(careBody, 1); clinicPage.Children.Add(careBody);
        var yields = new StackPanel { Spacing = 16 }; yields.Children.Add(Text("Stock reflects the selected expedition destination. Recovery depends on worker skills, access, kit and building integrity."));
        yields.Children.Add(ConsoleUi.Section("Recoverable objects", _yields));
        Tabs.Items.Add(new TabItem { Header = "Expedition planner", Content = new ScrollViewer { Content = panel, Margin = new Thickness(0, 14, 0, 0) } });
        Tabs.Items.Add(new TabItem { Header = "Clinic", Content = clinicPage });
        Tabs.Items.Add(new TabItem { Header = "Site inventory", Content = new ScrollViewer { Content = yields, Margin = new Thickness(0, 14, 0, 0) } });
        Tabs.Items.Add(new TabItem { Header = "Active teams", Content = new ScrollViewer { Content = activeTeams, Margin = new Thickness(0, 14, 0, 0) } });
        Grid.SetRow(Tabs, 1); root.Children.Add(Tabs); Content = root;
        _active.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        _patient.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        _location.SelectionChanged += (_, _) => { _shownLocation = null; Refresh(); };
        TeamSelector.SelectionChanged += (_, _) => Preview(); SalvageSelector.SelectionChanged += (_, _) => { if (!_refreshing) { ManifestRows(); Preview(); } };
        _quantity.ValueChanged += (_, _) => { if (!_refreshing) { foreach (var c in SalvageSelector.SelectedItems!.Cast<Choice>()) _amounts[c.Id] = _quantity.Value ?? 1; ManifestRows(); Preview(); } }; _approach.SelectionChanged += (_, _) => Preview(); _protection.IsCheckedChanged += (_, _) => Preview();
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
            return new SalvageRequest(s.Id, s.ComponentId is not null ? s.InitialKg : _amounts.GetValueOrDefault(c.Id, _quantity.Value ?? 1));
        }).ToArray();
        return new(_session.NextId("expedition"), locationId, TeamSelector.SelectedItems!.Cast<Choice>().Select(c => int.Parse(c.Id)).Order().ToArray(), manifest,
            Approach: _approach.SelectedItem is ExpeditionApproach approach ? approach : ExpeditionApproach.Careful, UseProtection: _protection.IsChecked == true);
    }
    private void Preview()
    {
        if (_refreshing) return;
        if (Plan() is not { } plan) { _dispatch.IsEnabled = false; return; }
        var m = _session.World.Module<ScavengingModule>();
        string? reason = m.Explain(_session.World, plan);
        _dispatch.IsEnabled = reason is null;
        _preview.Text = $"Manifest {plan.Manifest.Sum(s => s.QuantityKg):0.##} / {plan.Team.Count * 12} kg carrying capacity. "
            + (reason ?? "Ready: team, kit and receiving space will be reserved; distant stock can still be taken by rivals.");
    }
    public void Suggest(string purpose)
    {
        var suggestion = OperationsGuide.Suggest(_session.World, purpose, _session.NextId("expedition"));
        if (suggestion.Plan is not { } plan) { _session.Notify(suggestion.Message); return; }
        Tabs.SelectedIndex = 0;
        SelectLocation(plan.LocationId);
        _refreshing = true;
        try
        {
            TeamSelector.SelectedItems!.Clear();
            foreach (var c in TeamSelector.Items.Cast<Choice>().Where(c => plan.Team.Contains(int.Parse(c.Id)))) TeamSelector.SelectedItems.Add(c);
            SalvageSelector.SelectedItems!.Clear(); _amounts.Clear();
            foreach (var request in plan.Manifest)
            { _amounts[request.SalvageId] = request.QuantityKg; SalvageSelector.SelectedItems.Add(SalvageSelector.Items.Cast<Choice>().Single(c => c.Id == request.SalvageId)); }
            _approach.SelectedItem = plan.Approach; _protection.IsChecked = plan.UseProtection;
        }
        finally { _refreshing = false; }
        ManifestRows(); Preview(); _session.Notify(suggestion.Message);
    }
    private void ManifestRows()
    {
        _manifest.Children.Clear();
        if (Selected(_location) is not { } id || !_session.World.TryModule<ScavengingModule>(out var m) || m.Location(id) is not { } location) return;
        foreach (var c in SalvageSelector.SelectedItems!.Cast<Choice>())
        {
            var s = location.Definition.Salvage.Single(s => s.Id == c.Id);
            decimal value = _amounts.GetValueOrDefault(c.Id, Math.Min(_quantity.Value ?? 1, location.Remaining(s.Id)));
            _amounts[c.Id] = s.ComponentId is not null ? s.InitialKg : value;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(Text($"{s.Object}\n{location.Remaining(s.Id):0.###} kg remains · {s.MaterialId} · {s.Qualification.SkillId} {s.Qualification.MinimumRating} · purity {s.Quality.Purity:P1}"));
            Control amount;
            if (s.ComponentId is not null) amount = Text($"Whole unit / {s.InitialKg:0.###} kg");
            else
            {
                var input = new NumericUpDown { Name = "CargoKg_" + s.Id.Replace('-', '_'), Minimum = .001m, Maximum = 48, Increment = .1m, Value = Math.Max(.001m, value), Width = 110, FormatString = "0.000" };
                input.ValueChanged += (_, _) => { _amounts[c.Id] = input.Value ?? .001m; Preview(); }; amount = input;
            }
            Grid.SetColumn(amount, 1); row.Children.Add(amount); _manifest.Children.Add(row);
        }
        if (_manifest.Children.Count == 0) _manifest.Children.Add(Text("No cargo selected. Choose objects above or use a supply plan."));
    }
    private void CancelMedical() { if (Selected(_medicalJob) is { } id) _session.Submit(new CancelProductionCommand(id)); }
    private void Dispatch() { if (Plan() is { } p && _session.Submit(new DispatchExpeditionCommand(p)).Accepted) Tabs.SelectedIndex = 3; }
    private void SelectedJob(Func<string, Haldane.Kernel.Simulation.ICommand> command)
    { if (Selected(_active) is { } id) _session.Submit(command(id)); }
    private void Medicine(string recipe)
    { if (_session.World.Has<ScavengingModule>() && OperationsGuide.ExplainMedicine(_session.World, recipe) is null) _session.Submit(new QueueProductionCommand(ScavengingScenario.MedicinePlan(_session.NextId("medicine"), recipe))); }
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
        var locations = module.Locations.OrderBy(l => l.Definition.Id.StartsWith("pharmacy", StringComparison.Ordinal) ? 0 : 1).ThenBy(l => l.Definition.TravelTicks).Select(l => new Choice(l.Definition.Id, l.Definition.Name)).ToArray();
        if (!_location.Items.Cast<Choice>().SequenceEqual(locations)) Choices(_location, locations);
        var workforce = _session.World.Module<WorkforceModule>();
        var crew = module.Health.Select(h => new Choice(h.WorkerId.ToString(), $"{h.WorkerId} {workforce.Find(h.WorkerId)!.Name} / {(h.Sick ? "ill" : module.IsAway(h.WorkerId) ? "away" : workforce.ReservationFor(h.WorkerId) is null ? "available" : "assigned")} · " + string.Join(", ", workforce.Find(h.WorkerId)!.Skills.Ratings.Where(s => s.Value >= 30).Select(s => $"{s.Key} {s.Value}")))).ToArray();
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
                _shownLocation = id; _amounts.Clear(); SalvageSelector.ItemsSource = l.Definition.Salvage.Select(s => new Choice(s.Id, s.Object)).ToArray();
                SalvageSelector.SelectedItems!.Clear();
            }
            _yields.Children.Clear();
            foreach (var s in l.Definition.Salvage)
                _yields.Children.Add(Text($"{s.Object}\n  {l.Remaining(s.Id):0.###}/{s.InitialKg} kg {s.MaterialId} · purity {s.Quality.Purity:P1} · {s.Quality.Grade} · {s.Qualification.SkillId} {s.Qualification.MinimumRating} · access ≥{s.MinimumIntegrity:P0}{(s.NeedsPrybar ? " + prybar" : "")} · {s.EffortPerKg} effort/kg{(s.ComponentId is not null ? " · WHOLE UNIT" : "")}"));
        }
        Choices(_medicalJob, _session.World.Module<ProductionModule>().Jobs.Where(j => j.Active && j.Plan.StationId == "clinic-station").Select(j => new Choice(j.Plan.Id, j.Plan.RecipeId)));
        var production = _session.World.Module<ProductionModule>();
        var medicalJobs = production.Jobs.Where(j => j.Plan.StationId == "clinic-station").ToArray();
        ConsoleUi.Table(_medicalJobs, ["Batch", "State", "Progress / next step"], "2*,*,3*", medicalJobs.Select(j => new[] { production.Recipe(j.Plan.RecipeId)!.Name, j.Status.ToString(), $"{j.EffortCompleted:0.##}/{production.Recipe(j.Plan.RecipeId)!.Effort} · {j.BlockedReason ?? (j.Status == ProductionStatus.Completed ? "Products and waste stored." : j.Status == ProductionStatus.Cancelled ? "Cancelled; remaining stock stays at its current store." : j.Status == ProductionStatus.Delivering ? "Carrier is moving products and waste to their stores." : "Advance time to continue.")}" }));
        var ingredients = OperationsGuide.Ingredients(_session.World, "refine-fever-stock").Concat(OperationsGuide.Ingredients(_session.World, "formulate-fever-dose"));
        ConsoleUi.Table(_medicineInputs, ["Ingredient", "Free / needed kg", "Quality required"], "2*,*,3*", ingredients.Select(i => new[] { i.Name, $"{i.AvailableKg:0.###} / {i.RequiredKg}", i.Quality }));
        string? refineReason = OperationsGuide.ExplainMedicine(_session.World, "refine-fever-stock"), formulateReason = OperationsGuide.ExplainMedicine(_session.World, "formulate-fever-dose");
        _refine.IsEnabled = refineReason is null; _formulate.IsEnabled = formulateReason is null;
        ToolTip.SetTip(_refine, refineReason ?? "Queue a batch. Ivo delivers inputs; Anya processes them at the clinic.");
        ToolTip.SetTip(_formulate, formulateReason ?? "Queue formulation; products must reach the clinic store before care can begin.");
        decimal released = _session.World.Module<InventoryModule>().Batches.Where(b => b.StockpileId == "clinic" && ScavengingModule.ReleasedMedicine(b)).Sum(b => _session.World.Module<InventoryModule>().AvailableKg(b.Id));
        _clinicStatus.Text = $"Released treatment: {released:0.###} kg in clinic / 0.1 kg per patient.\nRefining: {refineReason ?? "Ingredients ready; queue refinement."}\nFormulation: {formulateReason ?? "Ingredients ready; queue formulation."}\nAnya: {WorksiteLogistics.ExplainLabor(_session.World, 4, new("medicine", 30)) ?? "Available for medical work."} Workers walk automatically when their job and supplies are ready.";
        Choices(_active, module.Expeditions.Where(e => e.Active).Select(e => new Choice(e.Plan.Id, $"{e.Plan.Id} / {e.Status}")));
        string? decision = module.Expeditions.FirstOrDefault(e => e.Status == ExpeditionStatus.Decision)?.Plan.Id;
        if (decision != _focusedDecision)
        {
            _focusedDecision = decision;
            if (decision is not null) _active.SelectedItem = _active.Items.Cast<Choice>().First(c => c.Id == decision);
        }
        _wait.IsEnabled = Selected(_active) is { } job && module.Find(job)?.Status == ExpeditionStatus.Decision;
        _recall.IsEnabled = Selected(_active) is { } recall && module.Find(recall) is { Active: true, Recalled: false };
        _releaseMedic.IsEnabled = _session.World.Module<ProjectBoardModule>().IsAssigned(4);
        Choices(_patient, module.Health.Where(h => h.Sick).Select(h => new Choice(h.WorkerId.ToString(), _session.World.Module<WorkforceModule>().Find(h.WorkerId)!.Name)));
        _careHint.Text = module.Health.All(h => !h.Sick) ? $"No patients need care. Clinic reserve: {released:0.###} kg released treatment." : released >= .1m ? $"{released:0.###} kg released treatment available. Choose a patient and treat; Anya and the patient walk to the clinic." : "No released dose yet. Recover pharmacy supplies + sorbent, then refine → formulate → treat. Advance time for deliveries and work.";
        _treat.IsEnabled = released >= .1m && Selected(_patient) is { } patient && module.ExplainTreatment(_session.World, new(_session.NextId("clinic"), int.Parse(patient))) is null;
        _jobs.Children.Clear();
        if (module.Expeditions.Count == 0) _jobs.Children.Add(Text("No teams dispatched. Select a destination, crew and manifest to plan a recovery run."));
        foreach (var e in module.Expeditions.TakeLast(20))
            _jobs.Children.Add(Text($"{e.Plan.Id} → {module.Location(e.Plan.LocationId)!.Definition.Name} · {e.Status} · crew {string.Join(", ", e.Plan.Team.Select(id => workforce.Find(id)!.Name))} · cargo {e.Cargo.Sum(c => c.QuantityKg):0.##} kg / received {e.Receipts.Sum(c => c.QuantityKg):0.##} kg\n  {e.Reason} · object {Math.Min(e.SearchIndex + 1, e.Plan.Manifest.Count)}/{e.Plan.Manifest.Count} · travel {e.ElapsedTravel}/{module.Location(e.Plan.LocationId)!.Definition.TravelTicks} ticks"));
        _health.Children.Clear();
        foreach (var h in module.Health)
            _health.Children.Add(Text($"{workforce.Find(h.WorkerId)!.Name} · {(h.Sick ? "Ill / unavailable; clinic treatment required" : module.IsAway(h.WorkerId) ? "On expedition" : "Home / fit for duty")} · exposure {h.Exposure:0.000} / 1"));
        foreach (var t in module.Treatments)
            _health.Children.Add(Text($"Care {t.Plan.Id}: {(t.Completed ? "completed" : t.Cancelled ? "cancelled" : t.Reason ?? "treating")} · {t.Effort:0.##}/4 effort"));
        }
        finally { _refreshing = false; }
        ManifestRows(); Preview();
    }
}
