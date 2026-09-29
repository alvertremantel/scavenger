using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;

namespace Scavenger.Sim.Industry;

public sealed class QueueComputerTaskCommand(ComputerTaskPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.computer-task";
    public ComputerTaskPlan Plan { get; } = plan;
    public string PersistId => Persist;
    public string Description => $"Plan computer task '{Plan.Id}' ({Plan.Kind}).";
    public object ToSnapshot() => Plan;
    public bool CanExecute(World world) => world.Has<ComputingModule>() && world.Module<ComputingModule>().Explain(world, Plan) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<ComputingModule>().Accept(world, Plan); return [IndustryEvents.Event(world, IndustryEvents.Planned, Description)]; }
}
public sealed class CancelComputerTaskCommand(string id) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.cancel-computer-task";
    public string Id { get; } = id;
    public string PersistId => Persist;
    public string Description => $"Cancel computer task '{Id}'.";
    public object ToSnapshot() => new IdPayload(Id);
    public bool CanExecute(World world) => world.Has<ComputingModule>() && world.Module<ComputingModule>().Job(Id) is { Active: true };
    public IReadOnlyList<GameEvent> Execute(World world) => world.Module<ComputingModule>().Cancel(world, world.Module<ComputingModule>().Job(Id)!);
}
public sealed class SetComputerPoweredCommand(string id, bool powered) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.computer-power";
    public string Id { get; } = id;
    public bool Powered { get; } = powered;
    public string PersistId => Persist;
    public string Description => $"Power {(Powered ? "on" : "off")} computer '{Id}'.";
    public object ToSnapshot() => new EnabledPayload(Id, Powered);
    public bool CanExecute(World world) => world.Has<ComputingModule>() && world.Module<ComputingModule>().Computer(Id) is not null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<ComputingModule>().Computer(Id)!.Powered = Powered; return [IndustryEvents.Event(world, IndustryEvents.Planned, Description)]; }
}
public sealed class SetGeneratorEnabledCommand(string id, bool enabled) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.generator-enabled";
    public string Id { get; } = id;
    public bool Enabled { get; } = enabled;
    public string PersistId => Persist;
    public string Description => $"{(Enabled ? "Start" : "Stop")} generator '{Id}'.";
    public object ToSnapshot() => new EnabledPayload(Id, Enabled);
    public bool CanExecute(World world) => world.Has<EnergyModule>() && world.Module<EnergyModule>().Generator(Id) is not null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<EnergyModule>().Generator(Id)!.Enabled = Enabled; return [IndustryEvents.Event(world, IndustryEvents.PowerChanged, Description)]; }
}
public sealed class SetBatteryEnabledCommand(string id, bool enabled) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.battery-enabled";
    public string Id { get; } = id;
    public bool Enabled { get; } = enabled;
    public string PersistId => Persist;
    public string Description => $"{(Enabled ? "Connect" : "Isolate")} battery '{Id}'.";
    public object ToSnapshot() => new EnabledPayload(Id, Enabled);
    public bool CanExecute(World world) => world.Has<EnergyModule>() && world.Module<EnergyModule>().Battery(Id) is not null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<EnergyModule>().Battery(Id)!.Enabled = Enabled; return [IndustryEvents.Event(world, IndustryEvents.PowerChanged, Description)]; }
}
public sealed record ComponentDamagePayload(string Id, decimal Condition, ComponentFault Fault);
public sealed class DamageComponentCommand(string id, decimal condition, ComponentFault fault) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.component-damage";
    public string Id { get; } = id;
    public decimal Condition { get; } = condition;
    public ComponentFault Fault { get; } = fault;
    public string PersistId => Persist;
    public string Description => $"Set prototype fault on component '{Id}'.";
    public object ToSnapshot() => new ComponentDamagePayload(Id, Condition, Fault);
    public bool CanExecute(World world) => Condition is >= 0 and <= 1 && Enum.IsDefined(Fault)
        && world.Has<ComputingModule>() && world.Module<ComputingModule>().Component(Id) is not null;
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        var component = world.Module<ComputingModule>().Component(Id)!;
        component.Condition = Condition; component.Fault = Fault; component.Diagnosed = false;
        return [IndustryEvents.Event(world, IndustryEvents.ComputerAlert, Description, EventSeverity.Warning)];
    }
}
