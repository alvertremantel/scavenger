using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Industry;

public sealed record StockOrderPlan(string Id, ProductionPlan Template, string MaterialId, decimal TargetKg, int MaxActiveJobs = 1);
public sealed class StockOrder
{
    internal StockOrder(StockOrderPlan plan) { Plan = plan; }
    public StockOrderPlan Plan { get; }
    public bool Enabled { get; internal set; } = true;
    public int Sequence { get; internal set; }
    public string? BlockedReason { get; internal set; }
}
public sealed record StockOrderSnapshot(StockOrderPlan Plan, bool Enabled, int Sequence, string? BlockedReason);
public sealed record OrdersSnapshot(IReadOnlyList<StockOrderSnapshot> Orders);
public sealed class OrdersModule : IPersistentModule
{
    public const string Persist = "scavenger.standing-orders";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, StockOrder> _orders = new(StringComparer.Ordinal);
    public IReadOnlyList<StockOrder> Orders => _orders.Values.OrderBy(o => o.Plan.Id, StringComparer.Ordinal).ToList();
    public StockOrder? Find(string id) => _orders.GetValueOrDefault(id);
    public string? Explain(World world, StockOrderPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Id) || Find(plan.Id) is not null || plan.TargetKg <= 0 || plan.MaxActiveJobs is < 1 or > 8)
            return "A unique standing order, positive target, and bounded queue are required.";
        var production = world.Module<ProductionModule>();
        if (production.Recipe(plan.Template.RecipeId) is not { } recipe || !recipe.Outputs.Any(o => !o.Waste && o.MaterialId == plan.MaterialId))
            return "The recipe does not produce this target material.";
        return production.Explain(world, plan.Template with { Id = $"standing/{plan.Id}/0", OrderId = plan.Id });
    }
    public void Add(World world, StockOrderPlan plan)
    {
        if (Explain(world, plan) is { } reason) throw new InvalidOperationException(reason);
        _orders.Add(plan.Id, new StockOrder(plan with { Template = plan.Template with { SourceStores = Array.AsReadOnly(plan.Template.SourceStores.ToArray()) } }));
    }
    public decimal CoveredKg(World world, StockOrder order)
    {
        var inventory = world.Module<InventoryModule>(); var logistics = world.Module<LogisticsModule>();
        decimal stock = inventory.Batches.Where(b => b.StockpileId == order.Plan.Template.ProductStoreId && b.MaterialId == order.Plan.MaterialId).Sum(b => inventory.AvailableKg(b.Id));
        decimal incoming = logistics.Jobs.Where(j => j.Active && logistics.FindRoute(j.Request.RouteId)!.DestinationId == order.Plan.Template.ProductStoreId
            && (j.Cargo?.MaterialId ?? inventory.FindBatch(j.Request.BatchId)?.MaterialId) == order.Plan.MaterialId).Sum(j => j.Request.QuantityKg);
        return stock + incoming + world.Module<ProductionModule>().PromisedKg(world, order.Plan.MaterialId, order.Plan.Template.ProductStoreId);
    }
    public object ToSnapshot() => new OrdersSnapshot(Orders.Select(o => new StockOrderSnapshot(o.Plan, o.Enabled, o.Sequence, o.BlockedReason)).ToArray());
    public static OrdersModule FromSnapshot(OrdersSnapshot snapshot)
    {
        var module = new OrdersModule();
        foreach (var s in snapshot.Orders)
        {
            if (string.IsNullOrWhiteSpace(s.Plan.Id) || s.Plan.TargetKg <= 0 || s.Plan.MaxActiveJobs is < 1 or > 8 || s.Sequence < 0)
                throw new ArgumentException("Invalid standing order.", nameof(snapshot));
            module._orders.Add(s.Plan.Id, new StockOrder(s.Plan with { Template = s.Plan.Template with { SourceStores = Array.AsReadOnly(s.Plan.Template.SourceStores.ToArray()) } })
            { Enabled = s.Enabled, Sequence = s.Sequence, BlockedReason = s.BlockedReason });
        }
        return module;
    }
}
public sealed class StandingOrderSystem : ISystem
{
    public string Name => "Standing production orders";
    public IReadOnlyList<Type> Dependencies => [typeof(Physical.ConstructionSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    {
        var module = world.Module<OrdersModule>(); var production = world.Module<ProductionModule>(); var events = new List<GameEvent>();
        foreach (var order in module.Orders.Where(o => o.Enabled))
        {
            if (module.CoveredKg(world, order) >= order.Plan.TargetKg) { order.BlockedReason = null; continue; }
            if (production.Jobs.Count(j => j.Active && j.Plan.OrderId == order.Plan.Id) >= order.Plan.MaxActiveJobs) continue;
            var plan = order.Plan.Template with { Id = $"standing/{order.Plan.Id}/{order.Sequence}", OrderId = order.Plan.Id };
            if (production.Explain(world, plan) is { } reason) { order.BlockedReason = reason; continue; }
            production.Accept(world, plan); order.Sequence++; order.BlockedReason = null;
            events.Add(IndustryEvents.Event(world, IndustryEvents.Planned, $"Standing order '{order.Plan.Id}' queued '{plan.Id}'."));
        }
        return events;
    }
}
