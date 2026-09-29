using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Scavenger.Sim.Industry;

namespace Scavenger.Sim.Scavenging;

public static class ScavengingEvents
{
    public const string Changed = "scavenger.expedition.changed";
    public const string Decision = "scavenger.expedition.decision";
    public const string Completed = "scavenger.expedition.completed";
    public static void RegisterAll(EventCatalogue catalogue)
    {
        catalogue.Register(Changed, "Expedition activity");
        catalogue.Register(Decision, "Expedition needs instructions", EventSeverity.Warning, true);
        catalogue.Register(Completed, "Team returned / clinic care completed", EventSeverity.Positive, true);
    }
    internal static GameEvent Event(World world, string kind, string text, EventSeverity severity = EventSeverity.Info)
        => new(world.Clock.Tick, kind, severity, text);
}
public sealed class DispatchExpeditionCommand(ExpeditionPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.expedition";
    public ExpeditionPlan Plan { get; } = plan with { Team = Array.AsReadOnly(plan.Team.ToArray()), Manifest = Array.AsReadOnly(plan.Manifest.ToArray()) };
    public string PersistId => Persist;
    public string Description => $"Dispatch {Plan.Id} to {Plan.LocationId}.";
    public object ToSnapshot() => Plan;
    public bool CanExecute(World world) => world.Has<ScavengingModule>() && world.Module<ScavengingModule>().Explain(world, Plan) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<ScavengingModule>().Dispatch(world, Plan); return [ScavengingEvents.Event(world, ScavengingEvents.Changed, Description)]; }
}
public sealed class RecallExpeditionCommand(string id) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.expedition-recall";
    public string Id { get; } = id;
    public string PersistId => Persist;
    public string Description => $"Recall expedition {Id}.";
    public object ToSnapshot() => new IdPayload(Id);
    public bool CanExecute(World world) => world.Has<ScavengingModule>() && world.Module<ScavengingModule>().Find(Id) is { Active: true, Recalled: false };
    public IReadOnlyList<GameEvent> Execute(World world) => world.Module<ScavengingModule>().Recall(world, world.Module<ScavengingModule>().Find(Id)!);
}
public sealed record ResponsePayload(string Id, ExpeditionResponse Response);
public sealed class RespondToExpeditionCommand(string id, ExpeditionResponse response) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.expedition-response";
    public ResponsePayload Payload { get; } = new(id, response);
    public string PersistId => Persist;
    public string Description => $"{Payload.Response} at expedition {Payload.Id}.";
    public object ToSnapshot() => Payload;
    public bool CanExecute(World world) => world.Has<ScavengingModule>() && Enum.IsDefined(Payload.Response)
        && world.Module<ScavengingModule>().Find(Payload.Id) is { Status: ExpeditionStatus.Decision };
    public IReadOnlyList<GameEvent> Execute(World world) => world.Module<ScavengingModule>().Respond(world, world.Module<ScavengingModule>().Find(Payload.Id)!, Payload.Response);
}
public sealed class QueueTreatmentCommand(TreatmentPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.clinic-treatment";
    public TreatmentPlan Plan { get; } = plan;
    public string PersistId => Persist;
    public string Description => $"Treat patient {Plan.PatientId} at the clinic.";
    public object ToSnapshot() => Plan;
    public bool CanExecute(World world) => world.Has<ScavengingModule>() && world.Module<ScavengingModule>().ExplainTreatment(world, Plan) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<ScavengingModule>().Treat(world, Plan); return [ScavengingEvents.Event(world, ScavengingEvents.Changed, Description)]; }
}
public sealed class CancelTreatmentCommand(string id) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.clinic-cancel";
    public string Id { get; } = id;
    public string PersistId => Persist;
    public string Description => $"Cancel clinic task {Id}.";
    public object ToSnapshot() => new IdPayload(Id);
    public bool CanExecute(World world) => world.Has<ScavengingModule>() && world.Module<ScavengingModule>().Treatments.Any(t => t.Plan.Id == Id && t.Active);
    public IReadOnlyList<GameEvent> Execute(World world) => world.Module<ScavengingModule>().CancelTreatment(world, world.Module<ScavengingModule>().Treatments.Single(t => t.Plan.Id == Id));
}
public sealed class ScavengingSystem : ISystem
{
    public string Name => "Regional scavenging and clinic care";
    public IReadOnlyList<Type> Dependencies => [typeof(EnergyAccountingSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks) => world.Module<ScavengingModule>().Tick(world);
}
