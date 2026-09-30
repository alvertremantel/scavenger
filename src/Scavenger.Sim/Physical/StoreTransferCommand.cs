using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed record StoreTransferPlan(string Id, string BatchId, string DestinationId, int CarrierId, decimal QuantityKg);

/// <summary>A player-selected physical route, persisted as one command without mutating the world during preflight.</summary>
public sealed class StoreTransferCommand(StoreTransferPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.store-transfer";
    public StoreTransferPlan Plan { get; } = plan;
    public string PersistId => Persist;
    public string Description => $"Dispatch {Plan.QuantityKg} kg from batch '{Plan.BatchId}' to '{Plan.DestinationId}'.";
    public object ToSnapshot() => Plan;
    private string RouteId => $"manual-route/{Plan.Id}";
    private HaulRequest Request => new(Plan.Id, Plan.BatchId, RouteId, Plan.CarrierId, Plan.QuantityKg);
    public string? Explain(World world)
    {
        var inventory = world.Module<InventoryModule>(); var logistics = world.Module<LogisticsModule>();
        if (string.IsNullOrWhiteSpace(Plan.Id) || logistics.FindJob(Plan.Id) is not null || logistics.FindRoute(RouteId) is not null) return "Choose a unique transfer id.";
        if (inventory.FindBatch(Plan.BatchId) is not { } batch || inventory.FindStockpile(batch.StockpileId)?.Position is null
            || inventory.FindStockpile(Plan.DestinationId)?.Position is null || batch.StockpileId == Plan.DestinationId) return "Choose a physical source batch and a different destination store.";
        if (Skills.SkillCatalogue.IsBook(batch.MaterialId) && Plan.QuantityKg != Skills.SkillBook.MassKg) return "An educational volume must be hauled whole.";
        if (world.TryModule<Industry.ComputingModule>(out var computing) && computing.Components.FirstOrDefault(c => c.MaterialId == batch.MaterialId) is { } component
            && Plan.QuantityKg != component.MassKg) return "A component must be hauled as one complete unit.";
        // The engine preflight only reads inventory/workforce from World. A temporary route avoids changing authoritative state.
        var preview = new LogisticsModule().AddRoute(new TransportRoute(RouteId, batch.StockpileId, Plan.DestinationId, 1, 6, new SkillRequirement("hauling", 20), usesWorldMovement: true));
        return preview.ExplainRequest(world, Request);
    }
    public bool CanExecute(World world) => Explain(world) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        if (Explain(world) is { } reason) throw new InvalidOperationException(reason);
        var batch = world.Module<InventoryModule>().FindBatch(Plan.BatchId)!;
        world.Module<LogisticsModule>().AddRoute(new TransportRoute(RouteId, batch.StockpileId, Plan.DestinationId, 1, 6, new SkillRequirement("hauling", 20), usesWorldMovement: true));
        return new QueuePhysicalHaulCommand(Request).Execute(world);
    }
}
