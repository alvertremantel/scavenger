using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed class ConstructionSystem : ISystem
{
    public string Name => "Compound construction";
    public IReadOnlyList<Type> Dependencies => [typeof(CalendarSystem)];

    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    {
        var compound = world.Module<CompoundModule>();
        var construction = world.Module<ConstructionModule>();
        var inventory = world.Module<InventoryModule>();
        var workforce = world.Module<WorkforceModule>();
        var board = world.Module<ProjectBoardModule>();
        var events = new List<GameEvent>();

        foreach (var job in construction.Jobs.Where(j => j.Active))
        {
            var definition = compound.Definition(job.Request.DefinitionId)!;
            var requirements = ConstructionModule.Requirements(compound, job);
            string? blocked = null;
            if (job.InputOwners.Count == 0 && workforce.ReservationFor(job.Request.BuilderId) != job.Owner)
            {
                blocked = ArrangeDelivery(world, job, requirements, events);
                bool inputsReady = requirements.All(r => SuitableBatches(inventory, job.StagingStockpileId, r).Sum(b => inventory.AvailableKg(b.Id)) >= r.QuantityKg);
                if (!inputsReady)
                {
                    SetReason(world, job, blocked ?? "Waiting for materials to arrive at the site.", events, blocked is not null);
                    continue;
                }
                if (workforce.Find(job.Request.BuilderId) is not { Present: true } builder)
                    blocked = "Builder is absent.";
                else if (!builder.Skills.Meets(ConstructionModule.Qualification(compound, job)))
                    blocked = "Builder no longer meets this job's qualification.";
                else if (workforce.ReservationFor(builder.Id) is not null || board.IsAssigned(builder.Id))
                    blocked = "Builder is assigned to another task.";
                if (blocked is null && job.Request.RepairTargetId is { } repairTarget && world.TryModule<Industry.EquipmentModule>(out var equipment)
                    && equipment.OwnerOf(repairTarget) is not null) blocked = "Fixture is operating or reserved by another task.";
                if (blocked is not null) { SetReason(world, job, blocked, events); continue; }
                if (!workforce.TryReserve(job.Request.BuilderId, job.Owner)) throw new InvalidOperationException("Builder claim changed during a serial tick.");
                if (job.Request.RepairTargetId is { } reserveTarget && world.TryModule<Industry.EquipmentModule>(out var sharedEquipment)
                    && !sharedEquipment.TryReserve(reserveTarget, job.Owner)) throw new InvalidOperationException("Repair station claim changed during a serial tick.");
                int index = 0;
                foreach (var requirement in requirements)
                {
                    decimal remaining = requirement.QuantityKg;
                    foreach (var batch in SuitableBatches(inventory, job.StagingStockpileId, requirement))
                    {
                        decimal amount = Math.Min(remaining, inventory.AvailableKg(batch.Id));
                        if (amount <= 0) continue;
                        string owner = $"{job.Owner}/input/{index++}";
                        if (!inventory.TryReserveStock(owner, batch.Id, amount)) throw new InvalidOperationException("Local input claim failed.");
                        job.AddInputOwner(owner);
                        remaining -= amount;
                        if (remaining == 0) break;
                    }
                }
                job.Status = ConstructionStatus.WalkingToSite;
            }

            if (workforce.Find(job.Request.BuilderId) is not { Present: true })
                blocked = "Builder is absent.";
            else if (workforce.ReservationFor(job.Request.BuilderId) != job.Owner)
                blocked = "Builder reservation is missing.";
            if (blocked is not null) { SetReason(world, job, blocked, events); continue; }
            var movement = Navigation.StepTowards(compound, job.Request.BuilderId, job.InteractionCell);
            if (movement.BlockedReason is not null) { SetReason(world, job, movement.BlockedReason, events); continue; }
            if (!movement.Arrived) { SetReason(world, job, "Builder is walking to the site.", events, false); continue; }

            job.Status = ConstructionStatus.Building;
            SetReason(world, job, null, events, false);
            int skill = workforce.Find(job.Request.BuilderId)!.Skills.Rating(ConstructionModule.Qualification(compound, job).SkillId);
            job.EffortCompleted = Math.Min(definition.EffortRequired, job.EffortCompleted + 0.25m + skill / 100m);
            if (job.EffortCompleted < definition.EffortRequired) continue;

            if (job.Request.RepairTargetId is null && compound.ExplainPlacement(definition, job.Request.Origin, job.InteractionCell) is { } placementReason)
            { SetReason(world, job, placementReason, events); continue; }
            if (job.Request.RepairTargetId is null)
            {
                var footprint = new GridRect(job.Request.Origin.X, job.Request.Origin.Y, definition.Width, definition.Height);
                if (definition.BlocksMovement && inventory.Stockpiles.Any(s => s.Position is not null && footprint.Contains(s.Position)
                    && (inventory.StoredKg(s.Id) > 0 || inventory.IncomingKg(s.Id) > 0)))
                { SetReason(world, job, "Stored or incoming goods occupy the footprint.", events); continue; }
                if ((definition.Kind == BuildingKind.Stockpile && inventory.FindStockpile($"store/{job.Request.Id}") is not null)
                    || (definition.ConduitKind is { } conduitKind && world.Module<UtilitiesModule>().HasNode(conduitKind, job.Request.Origin)))
                { SetReason(world, job, "The destination store or conduit is already installed.", events); continue; }
            }

            var embedded = job.InputOwners.Select(inventory.TakeReservedStock).ToList();
            job.ClearInputOwners();
            if (job.Request.RepairTargetId is { } targetId)
            {
                var target = compound.FindBuilding(targetId)!;
                target.Condition = 1;
                target.AddMaterials(embedded);
            }
            else
            {
                string? storeId = definition.Kind == BuildingKind.Stockpile ? $"store/{job.Request.Id}" : null;
                if (storeId is not null)
                    inventory.AddStockpile(new Stockpile(storeId, definition.Name, definition.StoreCapacityKg, job.InteractionCell));
                compound.Install(new BuildingSnapshot(job.Request.Id, definition.Id, job.Request.Origin, job.InteractionCell, 1, embedded, storeId));
                if (definition.ConduitKind is { } installedConduit) world.Module<UtilitiesModule>().AddNode(installedConduit, job.Request.Origin);
            }
            workforce.Release(job.Request.BuilderId, job.Owner);
            if (job.Request.RepairTargetId is { } releaseTarget && world.TryModule<Industry.EquipmentModule>(out var releaseEquipment)) releaseEquipment.Release(releaseTarget, job.Owner);
            job.Status = ConstructionStatus.Completed;
            job.BlockedReason = null;
            events.Add(new GameEvent(world.Clock.Tick, CompoundEventKinds.ConstructionCompleted, EventSeverity.Positive,
                $"{(job.Request.RepairTargetId is null ? "Built" : "Repaired")} {definition.Name} ({job.Request.Id})."));
        }
        return events;
    }

    private static IEnumerable<MaterialBatch> SuitableBatches(InventoryModule inventory, string storeId, MaterialRequirement requirement)
        => inventory.Batches.Where(b => b.StockpileId == storeId && b.MaterialId == requirement.MaterialId
            && b.Quality.Purity >= requirement.MinimumPurity);

    private static string? ArrangeDelivery(World world, ConstructionJob job, IReadOnlyList<MaterialRequirement> requirements, List<GameEvent> events)
    {
        var inventory = world.Module<InventoryModule>();
        var logistics = world.Module<LogisticsModule>();
        foreach (var requirement in requirements)
        {
            decimal local = SuitableBatches(inventory, job.StagingStockpileId, requirement).Sum(b => inventory.AvailableKg(b.Id));
            decimal incoming = job.DeliveryIds.Select(logistics.FindJob).Where(j => j is { Active: true })
                .Where(j => (j!.Cargo?.MaterialId ?? inventory.FindBatch(j.Request.BatchId)?.MaterialId) == requirement.MaterialId)
                .Sum(j => j!.Request.QuantityKg);
            decimal missing = requirement.QuantityKg - local - incoming;
            if (missing <= 0) continue;
            var source = SuitableBatches(inventory, job.Request.SourceStockpileId, requirement)
                .FirstOrDefault(b => inventory.AvailableKg(b.Id) > 0);
            if (source is null) return $"Source store lacks available {requirement.MaterialId} at the required purity.";
            decimal quantity = Math.Min(6, Math.Min(missing, inventory.AvailableKg(source.Id)));
            string id = $"build-delivery/{job.Request.Id}/{job.DeliverySequence}";
            var request = new HaulRequest(id, source.Id, $"site-route/{job.Request.Id}", job.Request.DeliveryWorkerId, quantity);
            if (logistics.ExplainRequest(world, request) is { } reason)
                return incoming > 0 ? null : reason;
            var command = new QueueHaulCommand(request);
            events.AddRange(command.Execute(world));
            job.AddDelivery(id);
            job.DeliverySequence++;
            // One trip per job per tick. The shared workforce claim prevents parallel use of this carrier.
            return null;
        }
        return null;
    }

    private static void SetReason(World world, ConstructionJob job, string? reason, List<GameEvent> events, bool warning = true)
    {
        if (warning && reason is not null && job.BlockedReason != reason)
            events.Add(new GameEvent(world.Clock.Tick, CompoundEventKinds.ConstructionBlocked, EventSeverity.Warning,
                $"Construction '{job.Request.Id}' blocked: {reason}"));
        job.BlockedReason = reason;
    }
}
