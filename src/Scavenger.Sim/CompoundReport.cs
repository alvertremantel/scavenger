using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.SupplyChain;

namespace Scavenger.Sim;

/// <summary>Read-only projections shared by a console, spreadsheet interface, and eventual map.</summary>
public sealed record CompoundReport(long Tick, decimal StoredKg, decimal InTransitKg,
    IReadOnlyList<StoreRow> Stores, IReadOnlyList<BatchRow> Batches, IReadOnlyList<WorkerRow> Workers, IReadOnlyList<HaulRow> Hauls)
{
    public static CompoundReport Capture(World world)
    {
        var inventory = world.Module<InventoryModule>();
        var logistics = world.Module<LogisticsModule>();
        var workforce = world.Module<WorkforceModule>();
        var board = world.Module<ProjectBoardModule>();
        return new CompoundReport(world.Clock.Tick, inventory.Batches.Sum(b => b.QuantityKg), logistics.InTransitKg(),
            inventory.Stockpiles.Select(s => new StoreRow(s.Id, s.Name, s.Position, s.CapacityKg,
                inventory.StoredKg(s.Id), inventory.IncomingKg(s.Id), inventory.FreeCapacityKg(s.Id))).ToList(),
            inventory.Batches.Select(b => new BatchRow(b.Id, b.MaterialId, b.StockpileId, b.QuantityKg,
                inventory.AvailableKg(b.Id), b.Quality)).ToList(),
            workforce.Staff.OrderBy(s => s.Id).Select(s => new WorkerRow(s.Id, s.Name, s.Present, s.Skills.Ratings,
                workforce.ReservationFor(s.Id) ?? board.Projects.FirstOrDefault(p => !p.Completed && p.AssignedStaffIds.Contains(s.Id))?.Id)).ToList(),
            logistics.Jobs.Select(j => new HaulRow(j.Request.Id, j.Status, j.Request.QuantityKg,
                j.ElapsedTravelTicks, logistics.FindRoute(j.Request.RouteId)!.TravelTicks, j.BlockedReason, j.DeliveredBatchId)).ToList());
    }
}

public sealed record StoreRow(string Id, string Name, CellPosition? Position, decimal CapacityKg,
    decimal StoredKg, decimal IncomingKg, decimal FreeKg);
public sealed record BatchRow(string Id, string MaterialId, string StockpileId, decimal QuantityKg, decimal AvailableKg, BatchQuality Quality);
public sealed record WorkerRow(int Id, string Name, bool Present, IReadOnlyDictionary<string, int> Skills, string? Assignment);
public sealed record HaulRow(string Id, HaulStatus Status, decimal QuantityKg, int ElapsedTicks, int TravelTicks,
    string? BlockedReason, string? DeliveredBatchId);
