using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public static class PhysicalSave
{
    public static SaveRegistry CreateRegistry()
    {
        var registry = SupplyChainSave.CreateRegistry();
        registry.RegisterModule<CompoundSnapshot>(CompoundModule.Persist, (s, v) => { CheckVersion(v); return CompoundModule.FromSnapshot(s); });
        registry.RegisterModule<ConstructionSnapshot>(ConstructionModule.Persist, (s, v) => { CheckVersion(v); return ConstructionModule.FromSnapshot(s); });
        registry.RegisterModule<UtilitiesSnapshot>(UtilitiesModule.Persist, (s, v) => { CheckVersion(v); return UtilitiesModule.FromSnapshot(s); });
        registry.RegisterCommand<BuildRequest>(PlanBuildCommand.Persist, s => new PlanBuildCommand(s));
        registry.RegisterCommand<JobIdSnapshot>(CancelBuildCommand.Persist, s => new CancelBuildCommand(s.JobId));
        registry.RegisterCommand<DoorSnapshot>(SetDoorOpenCommand.Persist, s => new SetDoorOpenCommand(s.Cell, s.Open));
        registry.RegisterCommand<RoomDesignation>(DesignateRoomCommand.Persist, s => new DesignateRoomCommand(s));
        registry.RegisterCommand<ConditionSnapshot>(SetBuildingConditionCommand.Persist, s => new SetBuildingConditionCommand(s.BuildingId, s.Condition));
        registry.RegisterCommand<ConduitSnapshot>(SetConduitEnabledCommand.Persist, s => new SetConduitEnabledCommand(s.Kind, s.Cell, s.Enabled));
        registry.RegisterCommand<HaulRequest>(QueuePhysicalHaulCommand.Persist, s => new QueuePhysicalHaulCommand(s));
        registry.RegisterCommand<StoreTransferPlan>(StoreTransferCommand.Persist, s => new StoreTransferCommand(s));
        Industry.IndustrySave.RegisterAll(registry);
        Scavenging.ScavengingSave.RegisterAll(registry);
        return registry;
    }
    public static Scheduler Load(string json)
    {
        var loaded = SaveManager.Load(json, CreateRegistry());
        ValidateWorld(loaded.World);
        // Industry services describe the completed energy interval; the next preparation derives fresh budgets.
        if (!loaded.World.Has<Industry.EnergyModule>()) UtilitySystem.Recalculate(loaded.World);
        return new Scheduler(loaded.World, loaded.World.Has<Scavenging.ScavengingModule>() ? Scavenging.ScavengingScenario.Systems() : loaded.World.Has<Industry.ProductionModule>() ? Industry.IndustrialScenario.Systems() : PhysicalCompoundScenario.Systems(), loaded.CommandLog);
    }
    private static void CheckVersion(int version)
    { if (version != 1) throw new NotSupportedException($"Unsupported physical compound schema {version}."); }

    public static void ValidateWorld(World world)
    {
        SupplyChainSave.ValidateWorld(world);
        var compound = world.Module<CompoundModule>();
        var construction = world.Module<ConstructionModule>();
        var workforce = world.Module<WorkforceModule>();
        var inventory = world.Module<InventoryModule>();
        var logistics = world.Module<LogisticsModule>();
        var board = world.Module<ProjectBoardModule>();
        if (compound.Workers.Count != workforce.Staff.Count || compound.Workers.Any(w => workforce.Find(w.WorkerId) is null || !compound.IsWalkable(w.Cell)))
            throw new InvalidOperationException("Every roster member needs a valid physical position.");
        foreach (var store in inventory.Stockpiles)
            if (store.Position is null || !compound.Contains(store.Position)) throw new InvalidOperationException("Every physical store needs an in-bounds interaction cell.");
        foreach (var definition in compound.Definitions)
            if (definition.Materials.Concat(definition.RepairMaterials).Any(r => inventory.FindMaterial(r.MaterialId) is null))
                throw new InvalidOperationException("A building definition refers to an unknown material.");
        foreach (var building in compound.Buildings)
        {
            if (!compound.Contains(building.InteractionCell) || building.EmbeddedMaterials.Any(m => inventory.FindMaterial(m.MaterialId) is null)
                || (compound.Definition(building.DefinitionId)!.Kind == BuildingKind.Stockpile && building.StockpileId is null)
                || (building.StockpileId is not null && inventory.FindStockpile(building.StockpileId)?.Position != building.InteractionCell))
                throw new InvalidOperationException($"Fixture '{building.Id}' has inconsistent references.");
        }
        foreach (var binding in compound.WorkBindings)
            if (board.Find(binding.ProjectId) is null || !compound.Definition(compound.FindBuilding(binding.BuildingId)!.DefinitionId)!.Capabilities.Contains(binding.Capability))
                throw new InvalidOperationException("Invalid workstation binding.");
        foreach (var node in world.Module<UtilitiesModule>().Nodes)
            if (!compound.Contains(node.Cell)) throw new InvalidOperationException("Utility node is outside the map.");

        foreach (var job in construction.Jobs)
        {
            var definition = compound.Definition(job.Request.DefinitionId);
            if (definition is null || job.EffortCompleted > definition.EffortRequired
                || job.Request.Origin.Level != 0 || !compound.AreaWithinMap(new GridRect(job.Request.Origin.X, job.Request.Origin.Y, definition.Width, definition.Height))
                || !compound.Contains(job.InteractionCell) || job.DeliverySequence != job.DeliveryIds.Count
                || inventory.FindStockpile(job.StagingStockpileId)?.Position != job.InteractionCell
                || workforce.Find(job.Request.BuilderId) is null || workforce.Find(job.Request.DeliveryWorkerId) is null
                || inventory.FindStockpile(job.Request.SourceStockpileId) is null
                || (job.Request.RepairTargetId is not null && (compound.FindBuilding(job.Request.RepairTargetId) is not { } target
                    || target.DefinitionId != definition.Id || target.Origin != job.Request.Origin || target.InteractionCell != job.InteractionCell)))
                throw new InvalidOperationException($"Invalid construction references for '{job.Request.Id}'.");
            foreach (var deliveryId in job.DeliveryIds)
            {
                var delivery = logistics.FindJob(deliveryId);
                if (delivery is null || delivery.Request.WorkerId != job.Request.DeliveryWorkerId
                    || logistics.FindRoute(delivery.Request.RouteId)?.DestinationId != job.StagingStockpileId)
                    throw new InvalidOperationException("Construction delivery ownership is inconsistent.");
            }
            if (job.Status is ConstructionStatus.WalkingToSite or ConstructionStatus.Building)
            {
                if (workforce.ReservationFor(job.Request.BuilderId) != job.Owner || board.IsAssigned(job.Request.BuilderId))
                    throw new InvalidOperationException("Builder labor claim is inconsistent.");
                var amounts = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (var owner in job.InputOwners)
                {
                    var claim = inventory.StockReservedBy(owner);
                    var batch = claim is null ? null : inventory.FindBatch(claim.BatchId);
                    if (batch is null || batch.StockpileId != job.StagingStockpileId || !owner.StartsWith(job.Owner + "/input/", StringComparison.Ordinal))
                        throw new InvalidOperationException("Construction input claim is inconsistent.");
                    if (!ConstructionModule.Requirements(compound, job).Any(r => r.MaterialId == batch.MaterialId && batch.Quality.Purity >= r.MinimumPurity))
                        throw new InvalidOperationException("Construction input does not meet its material or purity requirement.");
                    amounts[batch.MaterialId] = amounts.GetValueOrDefault(batch.MaterialId) + claim!.QuantityKg;
                }
                var requirements = ConstructionModule.Requirements(compound, job);
                if (requirements.Any(r => amounts.GetValueOrDefault(r.MaterialId) != r.QuantityKg)
                    || amounts.Keys.Any(id => !requirements.Any(r => r.MaterialId == id)))
                    throw new InvalidOperationException("Construction has incomplete or excessive input claims.");
            }
            else if (job.InputOwners.Count > 0 || workforce.ReservationFor(job.Request.BuilderId) == job.Owner)
                throw new InvalidOperationException("A nonworking construction job retains work claims.");
            if (job.Status == ConstructionStatus.Completed && (job.EffortCompleted != definition.EffortRequired
                || (job.Request.RepairTargetId is null && (compound.FindBuilding(job.Request.Id) is not { } completed
                    || completed.DefinitionId != definition.Id || completed.Origin != job.Request.Origin))))
                throw new InvalidOperationException("Completed construction has no completed fixture.");
        }
        var activeSites = construction.Jobs.Where(j => j.Active && j.Request.RepairTargetId is null).ToList();
        for (int i = 0; i < activeSites.Count; i++)
            for (int j = i + 1; j < activeSites.Count; j++)
            {
                var a = activeSites[i]; var b = activeSites[j];
                var da = compound.Definition(a.Request.DefinitionId)!; var db = compound.Definition(b.Request.DefinitionId)!;
                if (da.Layer == db.Layer && new GridRect(a.Request.Origin.X, a.Request.Origin.Y, da.Width, da.Height).Cells()
                    .Any(new GridRect(b.Request.Origin.X, b.Request.Origin.Y, db.Width, db.Height).Contains))
                    throw new InvalidOperationException("Active construction footprints overlap on the same layer.");
            }
        var activeInputOwners = construction.Jobs.Where(j => j.Active).SelectMany(j => j.InputOwners).ToHashSet(StringComparer.Ordinal);
        foreach (var reservation in inventory.StockReservations.Where(r => r.Owner.StartsWith("scavenger.build/", StringComparison.Ordinal)))
            if (!activeInputOwners.Contains(reservation.Owner)) throw new InvalidOperationException("Orphaned construction stock claim.");
        foreach (var reservation in workforce.Reservations.Where(r => r.Owner.StartsWith("scavenger.build/", StringComparison.Ordinal)))
            if (!construction.Jobs.Any(j => j.Active && j.Owner == reservation.Owner && j.Request.BuilderId == reservation.StaffId))
                throw new InvalidOperationException("Orphaned construction labor claim.");
        if (world.Has<Industry.ProductionModule>()) Industry.IndustrySave.Validate(world);
        if (world.Has<Scavenging.ScavengingModule>()) Scavenging.ScavengingSave.Validate(world);
    }
}
