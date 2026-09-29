using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;

namespace Scavenger.Sim.Industry;

public sealed class QueueProductionCommand(ProductionPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.production";
    public ProductionPlan Plan { get; } = plan;
    public string PersistId => Persist;
    public string Description => $"Plan process '{Plan.Id}'.";
    public object ToSnapshot() => Plan;
    public bool CanExecute(World world) => world.Has<ProductionModule>() && world.Module<ProductionModule>().Explain(world, Plan) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<ProductionModule>().Accept(world, Plan); return [IndustryEvents.Event(world, IndustryEvents.Planned, Description)]; }
}
public sealed class CancelProductionCommand(string id) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.cancel-production";
    public string Id { get; } = id;
    public string PersistId => Persist;
    public string Description => $"Cancel process '{Id}'.";
    public object ToSnapshot() => new IdPayload(Id);
    public bool CanExecute(World world) => world.Has<ProductionModule>() && world.Module<ProductionModule>().Find(Id) is { Active: true };
    public IReadOnlyList<GameEvent> Execute(World world) => world.Module<ProductionModule>().Cancel(world, world.Module<ProductionModule>().Find(Id)!);
}
public sealed record IdPayload(string Id);
public sealed record EnabledPayload(string Id, bool Enabled);
public sealed class AddStockOrderCommand(StockOrderPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.stock-order";
    public StockOrderPlan Plan { get; } = plan;
    public string PersistId => Persist;
    public string Description => $"Maintain {Plan.TargetKg} kg of {Plan.MaterialId}.";
    public object ToSnapshot() => Plan;
    public bool CanExecute(World world) => world.Has<OrdersModule>() && world.Module<OrdersModule>().Explain(world, Plan) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<OrdersModule>().Add(world, Plan); return [IndustryEvents.Event(world, IndustryEvents.Planned, Description)]; }
}
public sealed class SetStockOrderEnabledCommand(string id, bool enabled) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.stock-order-enabled";
    public string Id { get; } = id;
    public bool Enabled { get; } = enabled;
    public string PersistId => Persist;
    public string Description => $"{(Enabled ? "Resume" : "Pause")} stock order '{Id}'.";
    public object ToSnapshot() => new EnabledPayload(Id, Enabled);
    public bool CanExecute(World world) => world.Has<OrdersModule>() && world.Module<OrdersModule>().Find(Id) is not null;
    public IReadOnlyList<GameEvent> Execute(World world)
    { world.Module<OrdersModule>().Find(Id)!.Enabled = Enabled; return [IndustryEvents.Event(world, IndustryEvents.Planned, Description)]; }
}
