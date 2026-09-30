using Haldane.Kernel;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public static class Navigation
{
    /// <summary>Shortest cardinal path, including start and goal. Empty means inaccessible.</summary>
    public static IReadOnlyList<CellPosition> FindPath(CompoundModule compound, CellPosition start, CellPosition goal)
    {
        if (!compound.IsWalkable(start) || !compound.IsWalkable(goal)) return [];
        var frontier = new Queue<CellPosition>();
        var parents = new Dictionary<CellPosition, CellPosition?> { [start] = null };
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var current))
        {
            if (current == goal)
            {
                var path = new List<CellPosition>();
                for (CellPosition? cursor = goal; cursor is not null; cursor = parents[cursor]) path.Add(cursor);
                path.Reverse();
                return path;
            }
            foreach (var next in compound.Neighbors(current))
                if (compound.IsWalkable(next) && parents.TryAdd(next, current)) frontier.Enqueue(next);
        }
        return [];
    }

    public static MoveResult StepTowards(CompoundModule compound, int workerId, CellPosition goal)
    {
        var current = compound.PositionOf(workerId);
        if (current is null) return new MoveResult(false, false, "Worker has no physical position.");
        var path = FindPath(compound, current, goal);
        if (path.Count == 0) return new MoveResult(false, false, "No walkable path to the destination.");
        if (path.Count == 1) return new MoveResult(true, false, null);
        compound.MoveWorker(workerId, path[1]);
        return new MoveResult(path.Count == 2, true, null);
    }
}

public sealed class CompoundTransportDriver : ITransportDriver
{
    public IReadOnlyList<Type> Dependencies => [typeof(ConstructionSystem)];
    public TransportStep Step(World world, HaulJob job, TransportRoute route)
    {
        var compound = world.Module<CompoundModule>();
        var inventory = world.Module<InventoryModule>();
        string locationId = job.Status == HaulStatus.Reserved ? route.SourceId : route.DestinationId;
        var target = inventory.FindStockpile(locationId)?.Position;
        if (target is null) return new TransportStep(false, false, false, "Store has no physical interaction cell.");
        var move = Navigation.StepTowards(compound, job.Request.WorkerId, target);
        if (job.Status == HaulStatus.InTransit && move.Moved && job.Cargo is { } cargo)
            Skills.SkillsModule.Credit(world, job.Request.WorkerId, "hauling", Math.Min(1, cargo.QuantityKg / 6));
        return new TransportStep(job.Status == HaulStatus.Reserved && move.Arrived,
            job.Status == HaulStatus.InTransit && move.Arrived, move.Moved, move.BlockedReason);
    }
}
