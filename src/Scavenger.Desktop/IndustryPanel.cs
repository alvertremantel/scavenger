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

public sealed record Choice(string Id, string Label)
{ public override string ToString() => Label; }

/// <summary>Industry projections and player commands. Simulation remains independent of the UI.</summary>
public sealed class IndustryPanel : UserControl
{
    private readonly GameSession _session;
    private bool _refreshing;
    private readonly Grid _flow = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 10 };
    private readonly Dictionary<string, StackPanel> _rows = [];
    private readonly TextBlock _machine = Text(""), _energy = Text(""), _dataset = Text("");
    private readonly ComboBox _recipe = Select("RecipeSelector"), _order = Select("OrderSelector"), _production = Select("ProductionJobSelector");
    private readonly ComboBox _computer = Select("ComputerSelector"), _technician = Select("TechnicianSelector");
    private readonly ComboBox _asset = Select("AssetSelector"), _target = Select("TelemetryTargetSelector"), _adapter = Select("AdapterSelector"), _task = Select("ComputerJobSelector");
    private readonly ComboBox _batch = Select("TransferBatchSelector"), _destination = Select("TransferDestinationSelector"), _carrier = Select("TransferCarrierSelector"), _haul = Select("TransferJobSelector");
    private readonly NumericUpDown _transferKg = new() { Name = "TransferQuantity", Minimum = .001m, Maximum = 6, Increment = .1m, Value = 1, Width = 120, FormatString = "0.000" };
    private readonly CheckBox _wholeUnit = new() { Name = "TransferWholeUnit", Content = "Whole component", IsChecked = true };
    private readonly CheckBox _automatic = new() { Content = "Use installed automation", Name = "AutomaticProcess" };
    private readonly NumericUpDown _targetKg = new() { Name = "StockTarget", Minimum = .1m, Maximum = 20, Increment = .1m, Value = 1.6m, Width = 100, FormatString = "0.00" };
    public event Action<string>? Navigate;
    public TabControl Tabs { get; } = new() { Name = "IndustryTabs" };
    public ListBox ComponentSelector { get; } = new() { Name = "ComponentSelector", SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle, Height = 210 };
    public IndustryPanel(GameSession session)
    {
        _session = session;
        var production = Page("Production", "ProductionTab");
        production.Children.Add(_flow);
        production.Children.Add(ConsoleUi.Bar(ConsoleUi.Text("Inputs, products and captured waste require physical delivery.", 12, ConsoleUi.Muted),
            ConsoleUi.Button("Recipe library →", () => Tabs.SelectedIndex = 6)));
        production.Children.Add(Bar(_recipe, _automatic, Button("Queue batch", QueueProcess, "QueueProcessButton"), _targetKg,
            Button("Maintain stock", MaintainStock, "MaintainStockButton"), Button("Recovery orders", _session.BeginDemo, "RecoveryOrdersButton")));
        production.Children.Add(Bar(_order, Button("Pause / resume order", ToggleOrder, "ToggleOrderButton"), _production,
            Button("Cancel batch", () => SubmitSelected(_production, id => new CancelProductionCommand(id)), "CancelProcessButton")));
        AddTable(production, "Orders"); AddTable(production, "Production");

        var hardware = Page("Hardware", "HardwareTab");
        hardware.Children.Add(Text("Select individual units below. Diagnose first, make a replacement clip for the cooler, then assemble compatible parts. Hardware changes require shutdown and cancelled workloads.", 12));
        hardware.Children.Add(Bar(Button("Power on / off", ToggleComputer, "ComputerPowerButton"), ConsoleUi.Button("Store transfers →", () => Tabs.SelectedIndex = 5)));
        hardware.Children.Add(Bar(Button("Diagnose selected", () => HardwareTask(ComputerTaskKind.DiagnoseComponents), "DiagnoseButton"),
            Button("Repair selected unit", () => HardwareTask(ComputerTaskKind.RepairComponent), "RepairComponentButton"),
            Button("Service selected unit", () => HardwareTask(ComputerTaskKind.ServiceComponent), "ServiceComponentButton"),
            Button("Assemble selected", () => HardwareTask(ComputerTaskKind.Assemble), "AssembleButton"),
            Button("Remove selected unit", () => HardwareTask(ComputerTaskKind.RemoveComponent), "RemoveComponentButton")));

        hardware.Children.Add(ConsoleUi.Section("Component selection", ComponentSelector, "Select multiple individual units for diagnosis or assembly."));
        AddTable(hardware, "Components");

        var archives = Page("Archives", "ArchivesTab");
        archives.Children.Add(Text("Recovered USB contents must be intact, copied to a mounted drive, and satisfy offline dependencies. Studying a copied textbook teaches the selected technician.", 12));
        archives.Children.Add(Bar(_asset, Button("Install from media", () => AssetTask(ComputerTaskKind.InstallAsset), "InstallAssetButton"),
            Button("Study textbook", () => AssetTask(ComputerTaskKind.Study), "StudyButton")));
        AddTable(archives, "Archives");

        var compute = Page("Compute", "ComputeTab");
        compute.Children.Add(Text("Rules enable connected timer equipment. A model also needs weights, tokenizer, runtime, studied knowledge, labelled samples and independent validation. Deployment requires independent validation before a model can monitor equipment.", 12));
        compute.Children.Add(Bar(_target, Button("Fit selected sensor / controller", () => HardwareTask(ComputerTaskKind.InstallTelemetry), "TelemetryButton"),
            Button("Start rule monitor", () => MachineTask(ComputerTaskKind.RuleMonitor), "RuleMonitorButton")));
        compute.Children.Add(ConsoleUi.Section("Telemetry readiness", _dataset));
        compute.Children.Add(Bar(Button("Label telemetry", () => MachineTask(ComputerTaskKind.LabelTelemetry), "LabelTelemetryButton"),
            Button("Train 4B adapter", () => MachineTask(ComputerTaskKind.TrainAdapter, "model-4b"), "TrainAdapterButton"), _adapter,
            Button("Validate adapter", () => MachineTask(ComputerTaskKind.ValidateAdapter, Selected(_adapter)), "ValidateAdapterButton"),
            Button("Deploy model monitor", () => MachineTask(ComputerTaskKind.ModelMonitor, Selected(_adapter)), "ModelMonitorButton")));
        compute.Children.Add(Bar(_task, Button("Cancel task", () => SubmitSelected(_task, id => new CancelComputerTaskCommand(id)), "CancelComputerTaskButton")));
        AddTable(compute, "Tasks"); AddTable(compute, "Adapters");

        var energy = Page("Energy", "EnergyTab");
        energy.Children.Add(Text("Each tick is 15 minutes. Fuel generation, network priority, battery charge and losses are accounted in kW and kWh. A powered computer draws idle power plus claimed workloads through its PSU.", 12));
        energy.Children.Add(Bar(Button("Start / stop generator", ToggleGenerator, "GeneratorButton"), Button("Connect / isolate battery", ToggleBattery, "BatteryButton"),
            Button("Refuel from depot (up to 6 kg)", Refuel, "RefuelButton")));
        energy.Children.Add(ConsoleUi.Section("Energy balance", _energy)); AddTable(energy, "Power");
        var transfers = Page("Transfers", "TransfersTab");
        transfers.Children.Add(Text("Move bulk supplies, whole components, or leftover worksite stock. Deliver fuel to the generator tank and effluent to the waste store; repair orders use depot materials.", 12));
        transfers.Children.Add(Bar(_batch, _destination, _carrier, _transferKg, _wholeUnit, Button("Dispatch transfer", Transfer, "TransferButton")));
        transfers.Children.Add(Bar(_haul, Button("Cancel unloaded haul", () => SubmitSelected(_haul, id => new CancelHaulCommand(id)), "CancelTransferButton")));
        AddTable(transfers, "Transfers");
        var recipes = Page("Recipes", "RecipesTab");
        recipes.Children.Add(ConsoleUi.Bar(ConsoleUi.Text("Conversion specifications / input grades, purity gates, products and captured waste.", 12, ConsoleUi.Muted),
            ConsoleUi.Button("Production board →", () => Tabs.SelectedIndex = 0)));
        AddTable(recipes, "Recipes");
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 12 };
        root.Children.Add(ConsoleUi.PageHeading("Systems & industry", "Material conversion, recovered computers and the power that keeps them running."));
        var machineContext = new StackPanel { Spacing = 10 };
        machineContext.Children.Add(ConsoleUi.Bar(ConsoleUi.Field("Computer", _computer), ConsoleUi.Field("Technician", _technician),
            ConsoleUi.Button("Materials register →", () => Navigate?.Invoke("register/0"))));
        machineContext.Children.Add(_machine);
        Grid.SetRow(Tabs, 2); root.Children.Add(Tabs);
        var context = ConsoleUi.Frame(machineContext, new Thickness(14, 10)); Grid.SetRow(context, 1); root.Children.Add(context);
        context.IsVisible = false;
        Tabs.SelectionChanged += (_, _) => context.IsVisible = Tabs.SelectedIndex is 1 or 2 or 3;
        _computer.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        Content = root; Refresh();
    }
    private StackPanel Page(string title, string name)
    {
        var panel = new StackPanel { Spacing = 16 };
        Tabs.Items.Add(new TabItem { Header = title, Name = name, Content = new ScrollViewer { Content = panel, Margin = new Thickness(0, 14, 0, 0) } });
        return panel;
    }
    private void AddTable(StackPanel page, string name)
    { var rows = new StackPanel(); _rows.Add(name, rows); page.Children.Add(ConsoleUi.Section(name, rows)); }
    private static TextBlock Text(string value, double size = 12) => ConsoleUi.Text(value, size, ConsoleUi.Muted);
    private static ComboBox Select(string name) => new() { Name = name, MinWidth = 160, MaxWidth = 310 };
    private static Button Button(string text, Action action, string name) => ConsoleUi.Button(text, action, name, name is "QueueProcessButton" or "TransferButton" or "AssembleButton");
    private static Control Bar(params Control[] controls)
    {
        string Label(Control c) => c.Name switch
        {
            "RecipeSelector" => "Recipe", "StockTarget" => "Target kg", "OrderSelector" => "Standing order", "ProductionJobSelector" => "Active batch",
            "AssetSelector" => "Recovered asset", "TelemetryTargetSelector" => "Target equipment", "AdapterSelector" => "Trained adapter", "ComputerJobSelector" => "Active task",
            "TransferBatchSelector" => "Source batch", "TransferDestinationSelector" => "Receiving store", "TransferCarrierSelector" => "Carrier", "TransferQuantity" => "Bulk kg", "TransferJobSelector" => "Active haul", _ => ""
        };
        return ConsoleUi.Frame(ConsoleUi.Bar(controls.Select(c => Label(c) is { Length: > 0 } label ? ConsoleUi.Field(label, c) : c).ToArray()), new Thickness(14, 12, 14, 4));
    }
    private static string? Selected(ComboBox selector) => (selector.SelectedItem as Choice)?.Id;
    private string Computer => Selected(_computer) ?? "rig-1";
    private int Technician => int.TryParse(Selected(_technician), out int id) ? id : 1;
    private IReadOnlyList<string> Parts => ComponentSelector.SelectedItems!.Cast<Choice>().Select(c => c.Id).ToArray();
    private void SubmitSelected(ComboBox selector, Func<string, Haldane.Kernel.Simulation.ICommand> command)
    { if (Selected(selector) is { } id) _session.Submit(command(id)); }
    private ProductionPlan ProcessPlan(string jobId, string recipeId) => recipeId is "refine-fever-stock" or "formulate-fever-dose"
        ? ScavengingScenario.MedicinePlan(jobId, recipeId) : IndustrialScenario.ProcessPlan(jobId, recipeId, _automatic.IsChecked == true);
    private void QueueProcess()
    { if (Selected(_recipe) is { } id) _session.Submit(new QueueProductionCommand(ProcessPlan(_session.NextId("process"), id))); }
    private void MaintainStock()
    {
        if (Selected(_recipe) is not { } id) return;
        var recipe = _session.World.Module<ProductionModule>().Recipe(id)!;
        string orderId = _session.NextId("stock");
        _session.Submit(new AddStockOrderCommand(new(orderId, ProcessPlan("template", id), recipe.Outputs.First(o => !o.Waste).MaterialId, _targetKg.Value ?? 1.6m)));
    }
    private void ToggleOrder()
    { if (Selected(_order) is { } id) _session.Submit(new SetStockOrderEnabledCommand(id, !_session.World.Module<OrdersModule>().Find(id)!.Enabled)); }
    private void ToggleComputer() => _session.Submit(new SetComputerPoweredCommand(Computer, !_session.World.Module<ComputingModule>().Computer(Computer)!.Powered));
    private void ToggleGenerator() => _session.Submit(new SetGeneratorEnabledCommand("generator", !_session.World.Module<EnergyModule>().Generator("generator")!.Enabled));
    private void ToggleBattery() => _session.Submit(new SetBatteryEnabledCommand("battery-bank", !_session.World.Module<EnergyModule>().Battery("battery-bank")!.Enabled));
    private void Refuel()
    {
        var inventory = _session.World.Module<InventoryModule>();
        var batch = inventory.Batches.Where(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage" && inventory.AvailableKg(b.Id) >= .001m).OrderByDescending(b => inventory.AvailableKg(b.Id)).FirstOrDefault();
        if (batch is null) { _session.Notify("No usable diesel remains in the depot. Plan a generator-fuel expedition."); return; }
        _session.Submit(new QueuePhysicalHaulCommand(new(_session.NextId("refuel"), batch.Id, "refuel-route", 2, Math.Min(6, inventory.AvailableKg(batch.Id)))));
    }
    private void Transfer()
    {
        if (Selected(_batch) is not { } batchId || Selected(_destination) is not { } destination) return;
        var batch = _session.World.Module<InventoryModule>().FindBatch(batchId)!;
        var component = _session.World.Module<ComputingModule>().Components.FirstOrDefault(c => c.MaterialId == batch.MaterialId);
        decimal quantity = _wholeUnit.IsChecked == true && component is not null ? component.MassKg : _transferKg.Value ?? 1;
        _session.Submit(new StoreTransferCommand(new(_session.NextId("transfer"), batchId, destination, int.Parse(Selected(_carrier) ?? "2"), quantity)));
    }
    private void HardwareTask(ComputerTaskKind kind) => _session.Submit(new QueueComputerTaskCommand(new(_session.NextId("computer"), kind, Computer, Technician, 2, Parts, TargetBuildingId: Selected(_target))));
    private void AssetTask(ComputerTaskKind kind) => _session.Submit(new QueueComputerTaskCommand(new(_session.NextId("computer"), kind, Computer, Technician, AssetId: Selected(_asset))));
    private void MachineTask(ComputerTaskKind kind, string? asset = null) => _session.Submit(new QueueComputerTaskCommand(new(_session.NextId("computer"), kind, Computer,
        kind is ComputerTaskKind.RuleMonitor or ComputerTaskKind.ModelMonitor ? null : Technician, AssetId: asset)));
    private static void Choices(ComboBox selector, IEnumerable<Choice> values)
    {
        var choices = values.ToArray(); string? selected = Selected(selector);
        selector.ItemsSource = choices; selector.SelectedItem = choices.FirstOrDefault(c => c.Id == selected) ?? choices.FirstOrDefault();
    }
    public void Refresh()
    {
        if (_refreshing) return; _refreshing = true;
        try
        {
        bool industrial = _session.World.Has<ProductionModule>(); IsEnabled = industrial;
        if (!industrial) { _machine.Text = "This save predates industrial operations. Reset to start the industrial scenario."; return; }
        var world = _session.World; var production = world.Module<ProductionModule>(); var computing = world.Module<ComputingModule>(); var energy = world.Module<EnergyModule>();
        var report = IndustryReport.Capture(world); var inventory = world.Module<InventoryModule>();
        _flow.Children.Clear();
        var stages = new[] { ("Wash & sort", "polymer.hdpe.washed"), ("Dry", "polymer.hdpe.dry"), ("Shred", "polymer.hdpe.flakes"), ("Mould", "part.polymer") };
        for (int i = 0; i < stages.Length; i++)
        {
            decimal stock = inventory.Batches.Where(b => b.MaterialId == stages[i].Item2).Sum(b => b.QuantityKg);
            var stage = new StackPanel { Spacing = 4 }; stage.Children.Add(ConsoleUi.Eyebrow($"0{i + 1} / {stages[i].Item1}"));
            stage.Children.Add(ConsoleUi.Text($"{stock:0.##} kg", 22)); stage.Children.Add(ConsoleUi.Text("stored output", 10, ConsoleUi.Muted));
            var card = ConsoleUi.Frame(stage, new Thickness(14, 12)); Grid.SetColumn(card, i); _flow.Children.Add(card);
        }
        Choices(_recipe, production.Recipes.Select(r => new Choice(r.Id, r.Name)));
        Choices(_computer, computing.Computers.Select(c => new Choice(c.Id, c.Id)));
        Choices(_technician, world.Module<WorkforceModule>().Staff.Where(w => w.Skills.Rating("electronics") >= 40 || w.Skills.Rating("computing") >= 40).Select(w => new Choice(w.Id.ToString(), w.Name)));
        Choices(_order, report.Orders.Select(o => new Choice(o.Id, $"{o.Id} / {(o.Enabled ? "active" : "paused")}")));
        Choices(_production, production.Jobs.Where(j => j.Active).Select(j => new Choice(j.Plan.Id, j.Plan.Id)));
        Choices(_asset, computing.Assets.Select(a => new Choice(a.Id, a.Name)));
        Choices(_target, world.Module<CompoundModule>().Buildings.Where(b => production.Recipes.Any(r => world.Module<CompoundModule>().Definition(b.DefinitionId)!.Capabilities.Contains(r.Capability))).Select(b => new Choice(b.Id, b.Id)));
        Choices(_adapter, computing.Adapters.Select(a => new Choice(a.Id, a.Id)));
        Choices(_task, computing.Jobs.Where(j => j.Active).Select(j => new Choice(j.Plan.Id, $"{j.Plan.Id} / {j.Plan.Kind}")));
        Choices(_batch, inventory.Batches.Where(b => inventory.AvailableKg(b.Id) > 0).Select(b => new Choice(b.Id, $"{b.MaterialId} / {b.StockpileId} / {inventory.AvailableKg(b.Id):0.###} kg")));
        Choices(_destination, inventory.Stockpiles.Where(s => !s.Id.StartsWith("worksite/", StringComparison.Ordinal) && !s.Id.StartsWith("staging/", StringComparison.Ordinal)).OrderBy(s => s.Id == "workshop" ? 0 : 1).Select(s => new Choice(s.Id, $"{s.Id} / free {inventory.FreeCapacityKg(s.Id):0.##} kg")));
        Choices(_carrier, world.Module<WorkforceModule>().Staff.Where(w => w.Skills.Rating("hauling") >= 20).OrderBy(w => w.Id == 2 ? 0 : 1).Select(w => new Choice(w.Id.ToString(), w.Name)));
        Choices(_haul, world.Module<LogisticsModule>().Jobs.Where(j => j.Active).Select(j => new Choice(j.Request.Id, j.Request.Id)));
        if (!ComponentSelector.Items.Cast<Choice>().Select(c => c.Id).SequenceEqual(computing.Components.Select(c => c.Id)))
            ComponentSelector.ItemsSource = computing.Components.Select(c => new Choice(c.Id, $"{c.Id} · {computing.Definition(c.DefinitionId)!.Name}")).ToArray();
        var machine = report.Machines.First(m => m.Id == Computer);
        _machine.Text = $"{machine.Id}: {(machine.Powered ? "powered on" : "off")} · {machine.Components.Count} installed units · RAM {machine.RamGiB} GiB · VRAM {machine.VramGiB} GiB · disk {machine.UsedStorageGiB:0.000} / {machine.StorageGiB} GiB · requested {machine.RequestedKw:0.000} kW\n{machine.HardwareReason ?? "Compatible diagnosed assembly"}";
        _dataset.Text = $"Telemetry {computing.Samples.Count} records · labelled training {report.LabelledTrainingRecords} / 24 minimum · held-out {report.LabelledHeldOutRecords} / 8 minimum · learned: {string.Join(", ", computing.Knowledge.Select(k => $"worker {k.WorkerId}: {k.Capability}"))}";
        var frame = energy.LastFrame;
        decimal tankFuel = inventory.Batches.Where(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "generator-tank").Sum(b => inventory.AvailableKg(b.Id));
        decimal depotFuel = inventory.Batches.Where(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage").Sum(b => inventory.AvailableKg(b.Id));
        _energy.Text = $"Tank {tankFuel:0.00} kg / depot {depotFuel:0.00} kg free diesel. Deliver depot fuel to keep equipment running; Refuel handles partial batches; Store transfers allows custom quantities.\nAvailable diesel {report.FuelAvailableKg:0.00} kg · spent fuel {energy.SpentFuelKg:0.00} kg · battery {energy.Batteries.Sum(b => b.StoredKwh):0.000} / {energy.Batteries.Sum(b => b.Plan.CapacityKwh)} kWh\nLast interval: requested {frame?.RequestedKw ?? 0:0.000} kW / served {frame?.ServedKw ?? 0:0.000} kW · generated {frame?.GeneratedKwh ?? 0:0.000} kWh · used {frame?.UsedKwh ?? 0:0.000} kWh\nLifetime: generated {energy.GeneratedKwh:0.000} kWh · used {energy.UsedKwh:0.000} kWh · conversion losses {energy.LossKwh:0.000} kWh";
        Table("Orders", "2*,2*,*,*,2*", ["Standing order", "Material", "Target kg", "Covered kg", "State"], report.Orders.Select(o => new[] { o.Id, o.Material, $"{o.TargetKg:0.##}", $"{o.CoveredKg:0.##}", o.Enabled ? o.Reason ?? "Maintaining" : "Paused" }));
        Table("Recipes", "2*,3*,3*,*,*", ["Recipe", "Inputs / purity", "Products + waste", "Effort", "Active kW"], production.Recipes.Select(r => new[] { r.Name, string.Join(" + ", r.Inputs.Select(i => $"{i.QuantityKg} kg {i.MaterialId} ≥{i.MinimumPurity:P0}{(r.InputGrades.TryGetValue(i.MaterialId, out var grade) ? $" / {grade}" : "")}{(r.RejectContaminatedInputs ? " / no known contamination" : "")}")), string.Join(" + ", r.Outputs.Select(o => $"{o.QuantityKg} kg {o.MaterialId}")), r.Effort.ToString(), r.PowerKw.ToString() }));
        Table("Production", "2*,*,*,*,3*", ["Batch", "State", "Operator", "Effort", "Blocker / local staging"], production.Jobs.TakeLast(100).Select(j => new[] { j.Plan.Id, j.Status.ToString(), j.Plan.Automatic ? "Controller" : $"Worker {j.Plan.OperatorId}", $"{j.EffortCompleted:0.##}/{production.Recipe(j.Plan.RecipeId)!.Effort}", j.BlockedReason ?? j.Site.LocalStoreId }));
        Table("Components", "*,2*,*,2*,2*", ["Unit", "Part", "Condition", "Location / mass", "Diagnosis / claim"], computing.Components.Select(c => new[] { c.Id, computing.Definition(c.DefinitionId)!.Name, c.Diagnosed ? $"{c.Condition:P0}" : "Unknown", $"{c.InstalledOwner ?? inventory.Batches.FirstOrDefault(b => b.MaterialId == c.MaterialId)?.StockpileId ?? "carried"} / {c.MassKg} kg", $"{(c.Diagnosed ? c.Fault.ToString() : "Unexamined")} {c.ClaimedBy}" }));
        Table("Archives", "2*,*,2*,2*,2*", ["Recovered asset", "GiB", "Dependencies", "Integrity / source", "Installed"], computing.Assets.Select(a => new[] { a.Name, a.SizeGiB.ToString(), string.Join(", ", a.Dependencies), $"{a.Integrity:P0} / {a.SourceMediaId}", computing.Copies.Any(c => c.AssetId == a.Id && machine.Components.Contains(c.StorageId) && c.Verified) ? "Verified on mounted disk" : "Requires copy" }));
        Table("Tasks", "2*,*,*,*,3*", ["Task", "Workload", "State", "Progress / restart", "Blocker"], computing.Jobs.TakeLast(100).Select(j => new[] { j.Plan.Id, j.Plan.Kind.ToString(), j.Status.ToString(), $"{j.Progress:0.##}/{ComputingModule.Profile(j.Plan.Kind).Effort} · {j.Restarts}", j.BlockedReason ?? (j.ResourceClaim ? "Resources reserved" : "—") }));
        Table("Adapters", "2*,*,*,*,2*", ["Adapter", "Training", "Held-out", "Score", "State"], computing.Adapters.Select(a => new[] { a.Id, a.TrainingIds.Count.ToString(), a.ValidationIds.Count.ToString(), $"{a.ValidationScore:P0}", a.ValidationScore >= .75m ? "Eligible for deployment" : "Validation required" }));
        Table("Power", "2*,*,*,2*", ["Fixture", "Requested kW", "Served kW", "Service"], world.Module<UtilitiesModule>().Services.Where(s => s.PowerRequiredKw > 0).Select(s => new[] { s.BuildingId, $"{s.PowerRequiredKw:0.000}", $"{s.PowerAllocatedKw:0.000}", s.BlockedReason ?? "Served" }));
        Table("Transfers", "2*,*,2*,2*,3*", ["Haul", "kg", "State", "Destination", "Blocker"], world.Module<LogisticsModule>().Jobs.TakeLast(100).Select(j => new[] { j.Request.Id, j.Request.QuantityKg.ToString(), j.Status.ToString(), world.Module<LogisticsModule>().FindRoute(j.Request.RouteId)!.DestinationId, j.BlockedReason ?? "—" }));
            }
        finally { _refreshing = false; }
    }
    private void Table(string name, string widths, string[] header, IEnumerable<string[]> values)
    {
        ConsoleUi.Table(_rows[name], header, widths, values);
    }
}
