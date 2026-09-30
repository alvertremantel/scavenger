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
    private readonly Expander _qualities = new() { Header = "Quality & access requirements", Name = "CargoQuality" };
    private readonly StackPanel _manifest = new() { Spacing = 8 }, _medicineInputs = new(), _medicalJobs = new();
    private readonly Dictionary<string, decimal> _amounts = [];
    private readonly ContentControl _planningBody = new();
    private readonly List<Control> _planningSteps = [];
    private readonly TextBlock _planContext = Text("");
    private readonly ScrollViewer _plannerScroll = new();
    private readonly List<Button> _stepButtons = [];
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
    public event Action<string>? DestinationChanged;
    public string? PlannedDestination => Selected(_location);
    public string? ActiveDestination => _session.World.TryModule<ScavengingModule>(out var m) && Selected(_active) is { } id ? m.Find(id)?.Plan.LocationId : null;
    public void SelectTeam(string id) => _active.SelectedItem = _active.Items.Cast<Choice>().FirstOrDefault(c => c.Id == id);
    public Control ClinicView { get; private set; } = null!;
    public ScavengingPanel(GameSession session)
    {
        _session = session;
        TeamSelector.ItemTemplate = new FuncDataTemplate<Choice>((c, _) => Text(c?.Label ?? ""));
        SalvageSelector.ItemTemplate = new FuncDataTemplate<Choice>((c, _) => Text(c?.Label ?? ""));
        ConsoleUi.HideHeaders(Tabs);
        var root = new Grid { RowDefinitions = new RowDefinitions("*") };
        var planner = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        var planHeader = new StackPanel { Spacing = 8, Children = { _planContext } };
        var steps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        string[] stepNames = ["1 · Site", "2 · Team", "3 · Cargo"];
        for (int i = 0; i < stepNames.Length; i++)
        {
            int step = i;
            var button = Button(stepNames[i], () => ShowPlanningStep(step), "PlanStep" + i + "Button");
            _stepButtons.Add(button); Grid.SetColumn(button, i); steps.Children.Add(button);
        }
        planHeader.Children.Add(steps); planner.Children.Add(planHeader);
        var site = new StackPanel { Spacing = 12, Children = {
            ConsoleUi.Field("Recovery site", _location), _description,
            ConsoleUi.Eyebrow("Supply shortcuts"), ConsoleUi.Bar(
                Button("Pharmacy", () => Suggest("pharmacy"), "PlanPharmacyButton"),
                Button("Sorbent", () => Suggest("sorbent"), "PlanSorbentButton"),
                Button("Fuel", () => Suggest("fuel"), "PlanFuelButton"),
                Button("Materials", () => Suggest("materials"), "PlanMaterialsButton")),
            Button("Choose crew →", () => ShowPlanningStep(1), "PlanChooseCrewButton") } };
        TeamSelector.Height = 220; TeamSelector.MinWidth = 0;
        var crew = new StackPanel { Spacing = 12, Children = { ConsoleUi.Eyebrow("Toggle workers to assign the expedition"),
            TeamSelector, ConsoleUi.Field("Approach", _approach), _protection,
            Button("Choose cargo →", () => ShowPlanningStep(2), "PlanChooseCargoButton") } };
        SalvageSelector.MinWidth = 0; SalvageSelector.Height = 180;
        _qualities.Content = _yields;
        var cargo = new StackPanel { Spacing = 12, Children = { ConsoleUi.Eyebrow("Toggle objects, then set individual amounts"),
            SalvageSelector, _manifest,
            _qualities,
            new Expander { Header = "Set all selected bulk quantities", Name = "BulkQuantities", Content = ConsoleUi.Field("Kilograms per object", _quantity) } } };
        _planningSteps.AddRange([site, crew, cargo]); _plannerScroll.Content = _planningBody;
        Grid.SetRow(_plannerScroll, 1); planner.Children.Add(_plannerScroll); ShowPlanningStep(0);
        _dispatch = Button("Dispatch team →", Dispatch, "DispatchExpeditionButton"); _dispatch.HorizontalAlignment = HorizontalAlignment.Stretch;
        var authorization = new StackPanel { Spacing = 8, Children = { _preview, _dispatch } };
        Grid.SetRow(authorization, 2); planner.Children.Add(authorization);
        _wait = Button("Assess / wait", () => SelectedJob(e => new RespondToExpeditionCommand(e, ExpeditionResponse.Wait)), "WaitExpeditionButton");
        _recall = Button("Recall / retreat", () => SelectedJob(e => new RecallExpeditionCommand(e)), "RecallExpeditionButton");
        var teams = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        teams.Children.Add(new StackPanel { Spacing = 8, Children = { Button("Plan another recovery →", () => Navigate?.Invoke("scavenging"), "OpenPlannerButton"), ConsoleUi.Field("Selected team", _active), ConsoleUi.Bar(_wait, _recall) } });
        Grid.SetRow(_jobs, 1); var teamScroll = new ScrollViewer { Content = _jobs }; Grid.SetRow(teamScroll, 1); teams.Children.Add(teamScroll);
        _releaseMedic = Button("Release Anya from project", () => _session.ReleaseProject(4), "ReleaseMedicButton");
        _refine = Button("Refine fever stock", () => Medicine("refine-fever-stock"), "RefineMedicineButton");
        _formulate = Button("Formulate clinic dose", () => Medicine("formulate-fever-dose"), "FormulateMedicineButton");
        _treat = Button("Treat patient →", Treat, "TreatPatientButton"); _treat.HorizontalAlignment = HorizontalAlignment.Stretch;
        var clinic = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
        var clinicBody = new StackPanel { Spacing = 12 };
        clinicBody.Children.Add(ConsoleUi.Text("Clinic", 20));
        clinicBody.Children.Add(_health);
        clinicBody.Children.Add(ConsoleUi.Eyebrow("Make treatment"));
        clinicBody.Children.Add(ConsoleUi.Bar(_refine, _formulate));
        clinicBody.Children.Add(ConsoleUi.Bar(
            Button("Recover pharmacy inputs", () => { Navigate?.Invoke("scavenging"); Suggest("pharmacy"); }, "ClinicPlanPharmacyButton"),
            Button("Recover sorbent", () => { Navigate?.Invoke("scavenging"); Suggest("sorbent"); }, "ClinicPlanSorbentButton")));
        clinicBody.Children.Add(_clinicStatus);
        clinicBody.Children.Add(new Expander { Header = "Ingredients & quality", Content = _medicineInputs });
        clinicBody.Children.Add(ConsoleUi.Eyebrow("Medical work")); clinicBody.Children.Add(_medicalJobs);
        clinicBody.Children.Add(new Expander { Header = "Cancel or release assignments", Content = new StackPanel { Spacing = 8, Children = {
            _medicalJob, Button("Cancel medical batch", CancelMedical, "CancelMedicalBatchButton"), _releaseMedic,
            Button("Ingredient register →", () => Navigate?.Invoke("register/0"), "ClinicStockButton") } } });
        clinic.Children.Add(new ScrollViewer { Content = clinicBody });
        var care = new StackPanel { Spacing = 8, Children = { _careHint, _patient, _treat,
            Button("Cancel care", CancelCare, "CancelTreatmentButton") } };
        Grid.SetRow(care, 1); clinic.Children.Add(care); ClinicView = clinic;
        Tabs.Items.Add(new TabItem { Header = "Plan", Content = planner });
        Tabs.Items.Add(new TabItem { Header = "Teams", Content = teams });
        root.Children.Add(Tabs); Content = root;
        _active.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        _patient.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        _location.SelectionChanged += (_, _) => { _shownLocation = null; Refresh(); if (Selected(_location) is { } id) DestinationChanged?.Invoke(id); };
        TeamSelector.SelectionChanged += (_, _) => Preview(); SalvageSelector.SelectionChanged += (_, _) => { if (!_refreshing) { ManifestRows(); Preview(); } };
        _quantity.ValueChanged += (_, _) => { if (!_refreshing) { foreach (var c in SalvageSelector.SelectedItems!.Cast<Choice>()) _amounts[c.Id] = _quantity.Value ?? 1; ManifestRows(); Preview(); } }; _approach.SelectionChanged += (_, _) => Preview(); _protection.IsCheckedChanged += (_, _) => Preview();
        Refresh();
    }
    public void ShowPlanningStep(int step)
    {
        _planningBody.Content = _planningSteps[step];
        for (int i = 0; i < _stepButtons.Count; i++) _stepButtons[i].Classes.Set("primary", step == i);
        _plannerScroll.Offset = default;
    }
    public void ShowCargoDetails() { ShowPlanningStep(2); _qualities.IsExpanded = true; }
    private static TextBlock Text(string value, double size = 12) => ConsoleUi.Text(value, size, ConsoleUi.Muted);
    private static Button Button(string text, Action action, string name) => ConsoleUi.Button(text, action, name, name is "DispatchExpeditionButton" or "TreatPatientButton");
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
            return new SalvageRequest(s.Id, (s.ComponentId is not null || Scavenger.Sim.Skills.SkillCatalogue.IsBook(s.MaterialId)) ? s.InitialKg : _amounts.GetValueOrDefault(c.Id, _quantity.Value ?? 1));
        }).ToArray();
        return new(_session.NextId("expedition"), locationId, TeamSelector.SelectedItems!.Cast<Choice>().Select(c => int.Parse(c.Id)).Order().ToArray(), manifest,
            Approach: _approach.SelectedItem is ExpeditionApproach approach ? approach : ExpeditionApproach.Careful, UseProtection: _protection.IsChecked == true);
    }
    private void Preview()
    {
        if (_refreshing) return;
        if (Plan() is not { } plan) { _dispatch.IsEnabled = false; return; }
        var m = _session.World.Module<ScavengingModule>();
        _planContext.Text = $"{m.Location(plan.LocationId)!.Definition.Name}\nCrew: {string.Join(", ", plan.Team.Select(id => _session.World.Module<WorkforceModule>().Find(id)!.Name))}";
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
        ManifestRows(); Preview(); ShowPlanningStep(2); _session.Notify(suggestion.Message);
    }
    private void ManifestRows()
    {
        _manifest.Children.Clear();
        if (Selected(_location) is not { } id || !_session.World.TryModule<ScavengingModule>(out var m) || m.Location(id) is not { } location) return;
        foreach (var c in SalvageSelector.SelectedItems!.Cast<Choice>())
        {
            var s = location.Definition.Salvage.Single(s => s.Id == c.Id);
            decimal value = _amounts.GetValueOrDefault(c.Id, Math.Min(_quantity.Value ?? 1, location.Remaining(s.Id)));
            _amounts[c.Id] = (s.ComponentId is not null || Scavenger.Sim.Skills.SkillCatalogue.IsBook(s.MaterialId)) ? s.InitialKg : value;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(Text($"{s.Object}\n{location.Remaining(s.Id):0.###} kg available", 11));
            ToolTip.SetTip(row, $"{s.MaterialId} · {s.Qualification.SkillId} {s.Qualification.MinimumRating} · purity {s.Quality.Purity:P1} · {s.Quality.Grade}");
            Control amount;
            if (s.ComponentId is not null || Scavenger.Sim.Skills.SkillCatalogue.IsBook(s.MaterialId)) amount = Text($"Whole unit / {s.InitialKg:0.###} kg");
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
    private void Dispatch() { if (Plan() is { } p && _session.Submit(new DispatchExpeditionCommand(p)).Accepted) { Tabs.SelectedIndex = 1; Navigate?.Invoke("fieldteams"); } }
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
        var crew = module.Health.Select(h => new Choice(h.WorkerId.ToString(), $"{h.WorkerId} {workforce.Find(h.WorkerId)!.Name} / {(h.Sick ? "ill" : module.IsAway(h.WorkerId) ? "away" : workforce.ReservationFor(h.WorkerId) is null ? "available" : "assigned")} · " + string.Join(", ", workforce.Find(h.WorkerId)!.Skills.Ratings.Where(s => s.Value >= 30).OrderByDescending(s => s.Value).Take(2).Select(s => $"{s.Key} {s.Value}")))).ToArray();
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
                _yields.Children.Add(Text($"{s.Object}\n  {l.Remaining(s.Id):0.###}/{s.InitialKg} kg {s.MaterialId} · purity {s.Quality.Purity:P1} · {s.Quality.Grade} · {s.Qualification.SkillId} {s.Qualification.MinimumRating} · access ≥{s.MinimumIntegrity:P0}{(s.NeedsPrybar ? " + prybar" : "")} · {s.EffortPerKg} effort/kg{((s.ComponentId is not null || Scavenger.Sim.Skills.SkillCatalogue.IsBook(s.MaterialId)) ? " · WHOLE UNIT" : "")}"));
        }
        _planContext.Text = $"{module.Location(Selected(_location)!)!.Definition.Name}\nCrew: {string.Join(", ", TeamSelector.SelectedItems!.Cast<Choice>().Select(c => workforce.Find(int.Parse(c.Id))!.Name))}";
        Choices(_medicalJob, _session.World.Module<ProductionModule>().Jobs.Where(j => j.Active && j.Plan.StationId == "clinic-station").Select(j => new Choice(j.Plan.Id, j.Plan.RecipeId)));
        var production = _session.World.Module<ProductionModule>();
        var medicalJobs = production.Jobs.Where(j => j.Plan.StationId == "clinic-station").ToArray();
        ConsoleUi.Cards(_medicalJobs, ["Batch", "State", "Progress / next step"], medicalJobs.Select(j => new[] { production.Recipe(j.Plan.RecipeId)!.Name, j.Status.ToString(), $"{j.EffortCompleted:0.##}/{production.Recipe(j.Plan.RecipeId)!.Effort} · {j.BlockedReason ?? (j.Status == ProductionStatus.Completed ? "Products and waste stored." : j.Status == ProductionStatus.Cancelled ? "Cancelled; remaining stock stays at its current store." : j.Status == ProductionStatus.Delivering ? "Carrier is moving products and waste to their stores." : "Advance time to continue.")}" }));
        var ingredients = OperationsGuide.Ingredients(_session.World, "refine-fever-stock").Concat(OperationsGuide.Ingredients(_session.World, "formulate-fever-dose"));
        ConsoleUi.Cards(_medicineInputs, ["Ingredient", "Free / needed kg", "Quality required"], ingredients.Select(i => new[] { i.Name, $"{i.AvailableKg:0.###} / {i.RequiredKg}", i.Quality }));
        string? refineReason = OperationsGuide.ExplainMedicine(_session.World, "refine-fever-stock"), formulateReason = OperationsGuide.ExplainMedicine(_session.World, "formulate-fever-dose");
        _refine.IsEnabled = refineReason is null; _formulate.IsEnabled = formulateReason is null;
        ToolTip.SetTip(_refine, refineReason ?? "Queue a batch. Ivo delivers inputs; Anya processes them at the clinic.");
        ToolTip.SetTip(_formulate, formulateReason ?? "Queue formulation; products must reach the clinic store before care can begin.");
        decimal released = _session.World.Module<InventoryModule>().Batches.Where(b => b.StockpileId == "clinic" && ScavengingModule.ReleasedMedicine(b)).Sum(b => _session.World.Module<InventoryModule>().AvailableKg(b.Id));
        _clinicStatus.Text = $"Released treatment: {released:0.###} kg in clinic / 0.1 kg per patient.\nRefining: {refineReason ?? "Ingredients ready; queue refinement."}\nFormulation: {formulateReason ?? "Ingredients ready; queue formulation."}\nAnya: {WorksiteLogistics.ExplainLabor(_session.World, 4, new("medicine", 30)) ?? "Available for medical work."} Workers walk automatically when their job and supplies are ready.";
        Choices(_active, module.Expeditions.Where(e => e.Active).Select(e => new Choice(e.Plan.Id, $"{string.Join(", ", e.Plan.Team.Select(id => workforce.Find(id)!.Name))} · {e.Status}")));
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
        foreach (var e in module.Expeditions.OrderByDescending(e => e.Active).ThenByDescending(e => e.Plan.Id).Take(12))
        {
            var location = module.Location(e.Plan.LocationId)!;
            string state = e.Status switch { ExpeditionStatus.Gathering => "Packing supplies", ExpeditionStatus.LeavingCompound => "Walking to the exit",
                ExpeditionStatus.Searching => "Recovering cargo", ExpeditionStatus.Decision => "Needs instructions", ExpeditionStatus.Completed => "Home / unloaded", _ => e.Status.ToString() };
            var card = new StackPanel { Spacing = 8, Children = {
                ConsoleUi.Text(location.Definition.Name, 14, ConsoleUi.Accent),
                ConsoleUi.Text(string.Join(", ", e.Plan.Team.Select(id => workforce.Find(id)!.Name)) + " · " + state, 12),
                Text($"Cargo {e.Cargo.Sum(c => c.QuantityKg):0.##} kg · delivered {e.Receipts.Sum(c => c.QuantityKg):0.##} kg\n{e.Reason}", 11)
            } };
            if (e.Status is ExpeditionStatus.Outbound or ExpeditionStatus.Returning)
                card.Children.Add(new ProgressBar { Minimum = 0, Maximum = module.TravelTicksFor(e), Value = e.ElapsedTravel, Height = 5 });
            if (e.Active)
            {
                string expeditionId = e.Plan.Id;
                card.Children.Add(ConsoleUi.Button("Inspect team / route", () => { _active.SelectedItem = _active.Items.Cast<Choice>().First(c => c.Id == expeditionId); DestinationChanged?.Invoke(e.Plan.LocationId); }, "InspectTeam_" + expeditionId));
            }
            _jobs.Children.Add(ConsoleUi.Frame(card, new Thickness(10)));
        }
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
