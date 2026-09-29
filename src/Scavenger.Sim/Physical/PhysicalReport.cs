using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed record MapTileRow(CellPosition Cell, Tile Tile);
public sealed record BuildingRow(string Id, string Name, BuildingKind Kind, GridRect Footprint, CellPosition InteractionCell,
    decimal Condition, bool Operational, string? BlockedReason, string? StockpileId, decimal EmbeddedKg);
public sealed record ConstructionRow(string Id, string Name, CellPosition Origin, CellPosition InteractionCell, GridRect Footprint,
    ConstructionStatus Status, decimal CompletedEffort, decimal RequiredEffort, string? BlockedReason, string StagingId, bool IsRepair);
public sealed record PhysicalWorkerRow(int Id, string Name, CellPosition Position, string? Assignment, bool Carrying,
    decimal CargoKg, string? WorkReason, IReadOnlyDictionary<string, int> Skills);
public sealed record TravelPathRow(int WorkerId, CellPosition Target, IReadOnlyList<CellPosition> Cells);
public sealed record PhysicalProjectRow(string Id, string Name, string? StationId, double CompletedEffort,
    double RequiredEffort, bool Completed, IReadOnlyList<int> WorkerIds, string? BlockedReason);
public sealed record PhysicalReport(long Tick, int Width, int Height, long TopologyRevision, decimal TotalTrackedKg,
    CompoundReport Inventory, IReadOnlyList<MapTileRow> Tiles, IReadOnlyList<BuildingRow> Buildings,
    IReadOnlyList<PhysicalWorkerRow> Workers, IReadOnlyList<ConstructionRow> Construction,
    IReadOnlyList<RoomReport> Rooms, IReadOnlyList<UtilityNode> Conduits, IReadOnlyList<UtilityService> Utilities,
    IReadOnlyList<string> RecentEvents, IReadOnlyList<TravelPathRow> Paths, IReadOnlyList<PhysicalProjectRow> Projects)
{
    public static PhysicalReport Capture(World world)
    {
        var compound = world.Module<CompoundModule>();
        var utilities = world.Module<UtilitiesModule>();
        var workforce = world.Module<WorkforceModule>();
        var logistics = world.Module<LogisticsModule>();
        var board = world.Module<ProjectBoardModule>();
        var inventoryReport = CompoundReport.Capture(world);
        var tiles = new List<MapTileRow>();
        for (int y = 0; y < compound.Height; y++)
            for (int x = 0; x < compound.Width; x++)
            { var cell = new CellPosition(x, y); tiles.Add(new MapTileRow(cell, compound.TileAt(cell))); }
        var buildings = compound.Buildings.Select(b => new BuildingRow(b.Id, compound.Definition(b.DefinitionId)!.Name,
            compound.Definition(b.DefinitionId)!.Kind, compound.Footprint(b), b.InteractionCell, b.Condition,
            utilities.ServiceFor(b.Id)?.Operational ?? false, utilities.ServiceFor(b.Id)?.BlockedReason,
            b.StockpileId, b.EmbeddedMaterials.Sum(m => m.QuantityKg))).ToList();
        var workers = workforce.Staff.OrderBy(w => w.Id).Select(w =>
        {
            var haul = logistics.Jobs.FirstOrDefault(j => j.Active && j.Request.WorkerId == w.Id);
            var project = board.Projects.FirstOrDefault(p => !p.Completed && p.AssignedStaffIds.Contains(w.Id));
            string? reason = haul?.BlockedReason;
            if (project is not null) reason = CompoundWorkContext.Explain(world, project, w);
            var build = world.Module<ConstructionModule>().Jobs.FirstOrDefault(j => j.Active && j.Request.BuilderId == w.Id
                && workforce.ReservationFor(w.Id) == j.Owner);
            reason ??= build?.BlockedReason;
            return new PhysicalWorkerRow(w.Id, w.Name, compound.PositionOf(w.Id)!, workforce.ReservationFor(w.Id) ?? project?.Id,
                haul?.Cargo is not null, haul?.Cargo?.QuantityKg ?? 0, reason, w.Skills.Ratings);
        }).ToList();
        var construction = world.Module<ConstructionModule>().Jobs.Select(j =>
        {
            var definition = compound.Definition(j.Request.DefinitionId)!;
            return new ConstructionRow(j.Request.Id, definition.Name, j.Request.Origin, j.InteractionCell,
                new GridRect(j.Request.Origin.X, j.Request.Origin.Y, definition.Width, definition.Height), j.Status,
                j.EffortCompleted, definition.EffortRequired, j.BlockedReason, j.StagingStockpileId, j.Request.RepairTargetId is not null);
        }).ToList();
        var paths = new List<TravelPathRow>();
        foreach (var worker in workers)
        {
            var haul = logistics.Jobs.FirstOrDefault(j => j.Active && j.Request.WorkerId == worker.Id);
            CellPosition? target = null;
            if (haul is not null)
            {
                var route = logistics.FindRoute(haul.Request.RouteId)!;
                target = world.Module<InventoryModule>().FindStockpile(haul.Cargo is null ? route.SourceId : route.DestinationId)?.Position;
            }
            else
            {
                target = world.Module<ConstructionModule>().Jobs.FirstOrDefault(j => j.Active && j.Owner == workforce.ReservationFor(worker.Id))?.InteractionCell;
                var project = board.Projects.FirstOrDefault(p => !p.Completed && p.AssignedStaffIds.Contains(worker.Id));
                if (target is null && project is not null && compound.BindingFor(project.Id) is { } binding)
                    target = compound.FindBuilding(binding.BuildingId)?.InteractionCell;
            }
            if (target is not null) paths.Add(new TravelPathRow(worker.Id, target, Navigation.FindPath(compound, worker.Position, target)));
        }
        return new PhysicalReport(world.Clock.Tick, compound.Width, compound.Height, compound.TopologyRevision,
            inventoryReport.StoredKg + inventoryReport.InTransitKg + buildings.Sum(b => b.EmbeddedKg), inventoryReport,
            tiles, buildings, workers, construction, compound.Rooms.Select(compound.InspectRoom).ToList(), utilities.Nodes,
            utilities.Services, world.Events.Entries.TakeLast(30).Select(e => $"[{e.Tick}] {e.Description}").ToList(), paths,
            board.Projects.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => new PhysicalProjectRow(p.Id, p.Name,
                compound.BindingFor(p.Id)?.BuildingId, p.EffortCompleted, p.EffortRequired, p.Completed,
                p.AssignedStaffIds.Order().ToList(), p.Completed ? null : p.AssignedStaffIds.Count == 0 ? "No team assigned."
                    : p.AssignedStaffIds.Select(id => CompoundWorkContext.Explain(world, p, workforce.Find(id)!)).FirstOrDefault(r => r is not null))).ToList());
    }
}
