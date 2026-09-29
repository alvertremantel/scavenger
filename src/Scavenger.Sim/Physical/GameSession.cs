using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed record CommandResult(bool Accepted, string Message);

/// <summary>Framework-independent application boundary. Edits execute at the start of one game tick.</summary>
public sealed class GameSession
{
    public GameSession() { var (_, scheduler, _) = PhysicalCompoundScenario.Create(); Scheduler = scheduler; }
    public Scheduler Scheduler { get; private set; }
    public World World => Scheduler.World;
    public PhysicalReport Report => PhysicalReport.Capture(World);
    public string Status { get; private set; } = "Inspect a cell, dispatch supplies, or designate a construction site.";
    public event Action? Changed;

    public CommandResult Submit(ICommand command)
    {
        if (!command.CanExecute(World))
        {
            Status = command is PlanBuildCommand build
                ? World.Module<ConstructionModule>().ExplainPlan(World, build.Request) ?? "Plan cannot be accepted."
                : command is QueuePhysicalHaulCommand haul
                    ? World.Module<LogisticsModule>().ExplainRequest(World, haul.Request) ?? "No physical path is currently available."
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
        var result = Scheduler.FastForward(ticks, stopOnDecision ? new FastForwardStopConditions
        { InterruptCatalogue = PhysicalCompoundScenario.CreateCatalogue() } : FastForwardStopConditions.RunToEnd);
        Status = result.StoppedEarly ? $"Stopped at tick {World.Clock.Tick} for a decision." : $"Advanced to tick {World.Clock.Tick}.";
        Changed?.Invoke();
        return result;
    }
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
        Status = $"Loaded compound at tick {World.Clock.Tick}.";
        Changed?.Invoke();
    }
    public void BeginDemo()
    {
        if (World.Clock.Tick != 0) { Status = "Demo orders are available in a fresh compound."; Changed?.Invoke(); return; }
        PhysicalCompoundScenario.QueueDemoCommands(Scheduler);
        Scheduler.Advance();
        Status = "Supply delivery, inspection, and an extension bench are planned.";
        Changed?.Invoke();
    }
    public void Reset()
    {
        var (_, scheduler, _) = PhysicalCompoundScenario.Create(); Scheduler = scheduler;
        Status = "Fresh compound."; Changed?.Invoke();
    }
    private string NextId(string prefix)
    {
        int suffix = 1;
        while (true)
        {
            string id = $"{prefix}-{World.Clock.Tick}-{suffix++}";
            if (World.Module<ConstructionModule>().FindJob(id) is null && World.Module<LogisticsModule>().FindJob(id) is null
                && World.Module<CompoundModule>().FindBuilding(id) is null) return id;
        }
    }
}
