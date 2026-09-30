using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;
using Scavenger.Sim.Region;

namespace Scavenger.Sim.Physical;

public sealed record CommandResult(bool Accepted, string Message);

/// <summary>Framework-independent application boundary. Edits execute at the start of one game tick.</summary>
public sealed class GameSession
{
    private bool _regional;
    private bool _generated;
    private ulong _seed;
    private SettlementMotif? _motif;
    private bool _legacyRegion;
    public GameSession(bool regional = true, bool generated = true, ulong seed = GeneratedScenario.DefaultSeed)
    {
        _regional = regional; _generated = regional && generated; _seed = seed;
        var (_, scheduler, _) = _generated ? GeneratedScenario.Create(seed) : regional ? ScavengingScenario.Create(seed) : IndustrialScenario.Create(seed); Scheduler = scheduler;
        Status = regional ? "Mira is ill. Follow the opening priorities on Dashboard. Supply plans let you review and dispatch a qualified team; time starts paused."
            : "Plan recovery orders, examine the electronics cache, or build your compound.";
    }
    public SettlementMotif? RegionMotifPreference => _motif;
    public Scheduler Scheduler { get; private set; }
    public World World => Scheduler.World;
    public PhysicalReport Report => PhysicalReport.Capture(World);
    public string Status { get; private set; }
    public event Action? Changed;

    public CommandResult Submit(ICommand command)
    {
        if (!command.CanExecute(World))
        {
            Status = command is PlanBuildCommand build
                ? World.Module<ConstructionModule>().ExplainPlan(World, build.Request) ?? "Plan cannot be accepted."
                : command is QueuePhysicalHaulCommand haul
                    ? World.Module<LogisticsModule>().ExplainRequest(World, haul.Request) ?? "No physical path is currently available."
                    : command is QueueProductionCommand process ? World.Module<ProductionModule>().Explain(World, process.Plan) ?? "Process is unavailable."
                    : command is QueueComputerTaskCommand computer ? World.Module<ComputingModule>().Explain(World, computer.Plan) ?? "Computer task is unavailable."
                    : command is AddStockOrderCommand order ? World.Module<OrdersModule>().Explain(World, order.Plan) ?? "Stock order is unavailable."
                    : command is DispatchExpeditionCommand expedition && World.Has<ScavengingModule>() ? World.Module<ScavengingModule>().Explain(World, expedition.Plan) ?? "Expedition is unavailable."
                    : command is QueueTreatmentCommand treatment && World.Has<ScavengingModule>() ? World.Module<ScavengingModule>().ExplainTreatment(World, treatment.Plan) ?? "Clinic care is unavailable."
                    : command is StoreTransferCommand transfer ? transfer.Explain(World) ?? "Transfer is unavailable."
                    : "Action is unavailable in the current state.";
            Changed?.Invoke();
            return new CommandResult(false, Status);
        }
        Scheduler.Queue(command);
        Scheduler.Advance();
        Status = command.Description;
        Changed?.Invoke();
        return new CommandResult(true, Status);
    }
    public FastForwardResult Advance(int ticks = 1, bool stopOnDecision = false)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (stopOnDecision && PendingDecision is { } pending)
        {
            Status = pending;
            Changed?.Invoke();
            return new FastForwardResult(0, [], true);
        }
        var conditions = stopOnDecision ? new FastForwardStopConditions
        { MinSeverityToStop = Haldane.Kernel.Events.EventSeverity.Warning,
          StopOnKinds = new HashSet<string>(StringComparer.Ordinal) { ScavengingEvents.Decision, ScavengingEvents.Completed, IndustryEvents.Completed, CompoundEventKinds.ConstructionCompleted } }
            : FastForwardStopConditions.RunToEnd;
        var events = new List<Haldane.Kernel.Events.GameEvent>();
        int advanced = 0; bool stopped = false, fuelWarning = false;
        while (advanced < ticks)
        {
            decimal priorFuel = TankFuel();
            bool hadPower = !World.Has<EnergyModule>() || World.Module<EnergyModule>().LastFrame is not { } prior || prior.ServedKw >= prior.RequestedKw;
            var frame = Scheduler.FastForward(1, conditions); advanced++; events.AddRange(frame.Events);
            bool lostPower = hadPower && World.Has<EnergyModule>() && World.Module<EnergyModule>().LastFrame is { } power && power.ServedKw < power.RequestedKw;
            fuelWarning = priorFuel >= 3 && TankFuel() < 3;
            if (stopOnDecision && (frame.StoppedEarly || PendingDecision is not null || lostPower || fuelWarning)) { stopped = true; break; }
        }
        var result = new FastForwardResult(advanced, events, stopped);
        Status = result.StoppedEarly && fuelWarning ? $"Generator tank is low ({TankFuel():0.00} kg). Open Compound / Power to deliver depot fuel, or plan a fuel expedition." : result.StoppedEarly ? PendingDecision ?? $"Stopped at tick {World.Clock.Tick}: {result.Events.LastOrDefault(e => e.Severity >= Haldane.Kernel.Events.EventSeverity.Warning || e.Kind == ScavengingEvents.Completed || e.Kind == IndustryEvents.Completed || e.Kind == CompoundEventKinds.ConstructionCompleted)?.Description ?? "Review the latest activity or power supply."}" : $"Advanced to tick {World.Clock.Tick}.";
        Changed?.Invoke();
        return result;
    }
    private decimal TankFuel() => World.Has<EnergyModule>()
        ? World.Module<InventoryModule>().Batches.Where(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "generator-tank").Sum(b => World.Module<InventoryModule>().AvailableKg(b.Id)) : decimal.MaxValue;
    public string? PendingDecision => World.TryModule<ScavengingModule>(out var m) && m.Expeditions.FirstOrDefault(e => e.Status == ExpeditionStatus.Decision) is { } e
        ? $"{e.Plan.Id} is waiting: {e.Reason} Open Region / Field teams and choose Assess / wait or Recall / retreat before advancing time." : null;
    public void Notify(string message) { Status = message; Changed?.Invoke(); }
    public CommandResult ReleaseProject(int workerId) => Submit(new UnassignStaffCommand(workerId));
    public CommandResult Build(string definitionId, CellPosition origin)
        => Submit(new PlanBuildCommand(new BuildRequest(NextId("build"), definitionId, origin, 3, 2, "salvage")));
    public CommandResult Repair(string buildingId)
    {
        var building = World.Module<CompoundModule>().FindBuilding(buildingId);
        if (building is null) return new CommandResult(false, "Unknown fixture.");
        return Submit(new PlanBuildCommand(new BuildRequest(NextId("repair"), building.DefinitionId, building.Origin,
            3, 2, "salvage", building.Id)));
    }
    public CommandResult DispatchSupplies()
        => Submit(new QueuePhysicalHaulCommand(new HaulRequest(NextId("supply"), "salvage-hdpe", "depot-workshop", 2, 6)));
    public CommandResult StartInspection() => Submit(new AssignStaffCommand(1, "inspect-pump"));
    public CommandResult StartClinic() => Submit(new AssignStaffCommand(4, "prepare-clinic"));
    public string SaveJson()
    {
        if (Scheduler.QueuedCommands.Count != 0) throw new InvalidOperationException("Advance queued commands before saving.");
        PhysicalSave.ValidateWorld(World);
        return SaveManager.Save(Scheduler, "Physical compound");
    }
    public void LoadJson(string json)
    {
        var loaded = PhysicalSave.Load(json);
        Scheduler = loaded;
        _regional = World.Has<ScavengingModule>(); _generated = World.Has<RegionModule>();
        _seed = World.Rng.MasterSeed;
        _motif = _generated ? World.Module<RegionModule>().Motif : null;
        _legacyRegion = _generated && World.Module<RegionModule>().GeneratorVersion == 1;
        Status = $"Loaded compound at tick {World.Clock.Tick}.";
        Changed?.Invoke();
    }
    public void BeginDemo()
    {
        if (World.Has<ProductionModule>())
        {
            if (new[] { "wash-hdpe", "dry-hdpe", "shred-hdpe", "mould-clips" }.All(r =>
                World.Module<OrdersModule>().Orders.Any(o => o.Plan.Template.RecipeId == r)))
            { Notify("Recovery stock orders already exist. Review targets and pause/resume them in Compound / Production."); return; }
            IndustrialScenario.QueueRecoveryOrders(Scheduler);
        }
        else PhysicalCompoundScenario.QueueDemoCommands(Scheduler);
        Scheduler.Advance();
        Status = "Recovery orders are planned; washed, dried, shredded and moulded stock will be maintained.";
        Changed?.Invoke();
    }
    public void Reset()
    {
        var (_, scheduler, _) = _generated ? GeneratedScenario.Create(_seed, _motif, _legacyRegion) : _regional ? ScavengingScenario.Create(_seed) : IndustrialScenario.Create(_seed); Scheduler = scheduler;
        Status = "Fresh compound."; Changed?.Invoke();
    }
    public void NewRegion(ulong seed, SettlementMotif? motif = null)
    {
        var (_, scheduler, _) = GeneratedScenario.Create(seed, motif); Scheduler = scheduler; _seed = seed; _motif = motif; _legacyRegion = false; _regional = true; _generated = true;
        Status = $"New region: {World.Module<RegionModule>().Name}, seed {seed}. Mira needs clinic treatment.";
        Changed?.Invoke();
    }
    public string NextId(string prefix)
    {
        int suffix = 1;
        while (true)
        {
            string id = $"{prefix}-{World.Clock.Tick}-{suffix++}";
            if (World.Module<ConstructionModule>().FindJob(id) is null && World.Module<LogisticsModule>().FindJob(id) is null
                && World.Module<CompoundModule>().FindBuilding(id) is null
                && (!World.Has<ProductionModule>() || World.Module<ProductionModule>().Find(id) is null)
                && (!World.Has<ComputingModule>() || World.Module<ComputingModule>().Job(id) is null)
                && (!World.Has<ScavengingModule>() || World.Module<ScavengingModule>().Find(id) is null && World.Module<ScavengingModule>().Treatments.All(t => t.Plan.Id != id))) return id;
        }
    }
}
