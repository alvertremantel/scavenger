using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Events;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed record BuildRequest(string Id, string DefinitionId, CellPosition Origin, int BuilderId,
    int DeliveryWorkerId, string SourceStockpileId, string? RepairTargetId = null);
public enum ConstructionStatus { WaitingForMaterials, WalkingToSite, Building, Completed, Cancelled }

public sealed class ConstructionJob
{
    internal ConstructionJob(BuildRequest request, CellPosition interactionCell, string stagingStockpileId)
    { Request = request; InteractionCell = interactionCell; StagingStockpileId = stagingStockpileId; }
    public BuildRequest Request { get; }
    public string Owner => $"scavenger.build/{Request.Id}";
    public CellPosition InteractionCell { get; }
    public string StagingStockpileId { get; }
    public ConstructionStatus Status { get; internal set; }
    public decimal EffortCompleted { get; internal set; }
    public string? BlockedReason { get; internal set; }
    public int DeliverySequence { get; internal set; }
    private readonly List<string> _deliveryIds = [];
    private readonly List<string> _inputOwners = [];
    public IReadOnlyList<string> DeliveryIds => _deliveryIds.AsReadOnly();
    public IReadOnlyList<string> InputOwners => _inputOwners.AsReadOnly();
    public bool Active => Status is not ConstructionStatus.Completed and not ConstructionStatus.Cancelled;
    internal void AddDelivery(string id) => _deliveryIds.Add(id);
    internal void AddInputOwner(string owner) => _inputOwners.Add(owner);
    internal void ClearInputOwners() => _inputOwners.Clear();
    internal ConstructionJobSnapshot Snapshot() => new(Request, InteractionCell, StagingStockpileId, Status,
        EffortCompleted, BlockedReason, DeliverySequence, DeliveryIds.ToList(), InputOwners.ToList());
}

public sealed class ConstructionModule : IPersistentModule
{
    public const string Persist = "scavenger.construction";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, ConstructionJob> _jobs = new(StringComparer.Ordinal);
    public IReadOnlyList<ConstructionJob> Jobs => _jobs.Values.OrderBy(j => j.Request.Id, StringComparer.Ordinal).ToList();
    public ConstructionJob? FindJob(string id) => _jobs.GetValueOrDefault(id);

    public static IReadOnlyList<MaterialRequirement> Requirements(CompoundModule compound, ConstructionJob job)
    {
        var definition = compound.Definition(job.Request.DefinitionId)!;
        return job.Request.RepairTargetId is null ? definition.Materials : definition.RepairMaterials;
    }
    public static SkillRequirement Qualification(CompoundModule compound, ConstructionJob job)
        => job.Request.RepairTargetId is null ? compound.Definition(job.Request.DefinitionId)!.RequiredSkill
            : new SkillRequirement("mechanical", 30);

    public string? ExplainPlan(World world, BuildRequest request)
    {
        var compound = world.Module<CompoundModule>();
        var inventory = world.Module<InventoryModule>();
        var workforce = world.Module<WorkforceModule>();
        if (string.IsNullOrWhiteSpace(request.Id) || FindJob(request.Id) is not null || compound.FindBuilding(request.Id) is not null)
            return "A unique construction id is required.";
        if (compound.Definition(request.DefinitionId) is not { } definition) return "Unknown building definition.";
        if (inventory.FindStockpile(request.SourceStockpileId)?.Position is null) return "Source store needs a physical location.";
        if (inventory.FindStockpile($"site/{request.Id}") is not null) return "Construction staging id is already in use.";
        if (inventory.FindStockpile($"store/{request.Id}") is not null || world.Module<LogisticsModule>().FindRoute($"site-route/{request.Id}") is not null)
            return "A generated construction store or route id is already in use.";
        if (workforce.Find(request.BuilderId) is not { } builder || workforce.Find(request.DeliveryWorkerId) is null)
            return "Builder and delivery worker must exist.";
        var qualification = request.RepairTargetId is null ? definition.RequiredSkill : new SkillRequirement("mechanical", 30);
        if (!builder.Skills.Meets(qualification)) return $"Builder needs {qualification.SkillId} at rating {qualification.MinimumRating}.";
        CellPosition? interaction;
        if (request.RepairTargetId is { } repairId)
        {
            var target = compound.FindBuilding(repairId);
            if (target is null || target.DefinitionId != definition.Id || target.Origin != request.Origin)
                return "Repair target does not match this definition and origin.";
            if (target.Condition >= 1) return "The fixture is already in full condition.";
            if (definition.RepairMaterials.Count == 0) return "This fixture has no repair specification.";
            if (Jobs.Any(j => j.Active && j.Request.RepairTargetId == repairId)) return "A repair is already planned for this fixture.";
            interaction = target.InteractionCell;
        }
        else
        {
            if (!definition.Buildable) return "This fixture must be recovered intact; it cannot be built from bulk materials.";
            if (definition.Kind is BuildingKind.PowerCable or BuildingKind.WaterPipe
                && world.Module<UtilitiesModule>().HasNode(definition.Kind == BuildingKind.PowerCable ? UtilityKind.Power : UtilityKind.Water, request.Origin))
                return "A conduit of this kind already occupies this cell.";
            interaction = compound.ChooseInteraction(definition, request.Origin);
            if (interaction is null) return "No walkable interaction cell borders this site.";
            if (compound.ExplainPlacement(definition, request.Origin, interaction) is { } reason) return reason;
            var footprint = new GridRect(request.Origin.X, request.Origin.Y, definition.Width, definition.Height);
            if (Jobs.Any(j => j.Active && j.Request.RepairTargetId is null
                && compound.Definition(j.Request.DefinitionId)!.Layer == definition.Layer
                && new GridRect(j.Request.Origin.X, j.Request.Origin.Y,
                    compound.Definition(j.Request.DefinitionId)!.Width, compound.Definition(j.Request.DefinitionId)!.Height).Cells().Any(footprint.Contains)))
                return "Another construction site reserves this footprint.";
            if (definition.BlocksMovement && inventory.Stockpiles.Any(s => s.Position is not null && footprint.Contains(s.Position)
                && (inventory.StoredKg(s.Id) > 0 || inventory.IncomingKg(s.Id) > 0)))
                return "Stored or incoming goods occupy this footprint.";
        }
        if (!compound.IsWalkable(interaction)) return "The site's interaction cell is inaccessible.";
        var requirements = request.RepairTargetId is null ? definition.Materials : definition.RepairMaterials;
        if (requirements.Any(r => inventory.FindMaterial(r.MaterialId) is null)) return "A required material is not defined.";
        return null;
    }

    internal ConstructionJob Accept(World world, BuildRequest request)
    {
        if (ExplainPlan(world, request) is { } reason) throw new InvalidOperationException(reason);
        var compound = world.Module<CompoundModule>();
        var definition = compound.Definition(request.DefinitionId)!;
        var interaction = request.RepairTargetId is { } repairId
            ? compound.FindBuilding(repairId)!.InteractionCell : compound.ChooseInteraction(definition, request.Origin)!;
        string stagingId = $"site/{request.Id}";
        var job = new ConstructionJob(request, interaction, stagingId);
        var requirements = Requirements(compound, job);
        world.Module<InventoryModule>().AddStockpile(new Stockpile(stagingId, $"Site: {request.Id}",
            Math.Max(1, requirements.Sum(r => r.QuantityKg)), interaction));
        var source = world.Module<InventoryModule>().FindStockpile(request.SourceStockpileId)!;
        int distance = Math.Abs(source.Position!.X - interaction.X) + Math.Abs(source.Position.Y - interaction.Y);
        world.Module<LogisticsModule>().AddRoute(new TransportRoute($"site-route/{request.Id}", source.Id,
            stagingId, Math.Max(1, distance), 6, usesWorldMovement: true));
        _jobs.Add(request.Id, job);
        return job;
    }

    internal IReadOnlyList<GameEvent> Cancel(World world, ConstructionJob job)
    {
        if (!job.Active) throw new InvalidOperationException("This job is already finished.");
        var inventory = world.Module<InventoryModule>();
        foreach (var owner in job.InputOwners) inventory.ReleaseReservations(owner);
        job.ClearInputOwners();
        world.Module<WorkforceModule>().Release(job.Request.BuilderId, job.Owner);
        var events = new List<GameEvent>();
        foreach (var deliveryId in job.DeliveryIds)
        {
            var command = new CancelHaulCommand(deliveryId);
            if (command.CanExecute(world))
                events.AddRange(command.Execute(world));
        }
        job.Status = ConstructionStatus.Cancelled;
        job.BlockedReason = null;
        return events;
    }

    public object ToSnapshot() => new ConstructionSnapshot(Jobs.Select(j => j.Snapshot()).ToList());
    public static ConstructionModule FromSnapshot(ConstructionSnapshot snapshot)
    {
        var module = new ConstructionModule();
        foreach (var s in snapshot.Jobs)
        {
            if (string.IsNullOrWhiteSpace(s.Request.Id) || !Enum.IsDefined(s.Status) || s.EffortCompleted < 0
                || s.DeliverySequence < 0 || s.DeliveryIds.Distinct(StringComparer.Ordinal).Count() != s.DeliveryIds.Count
                || s.InputOwners.Distinct(StringComparer.Ordinal).Count() != s.InputOwners.Count)
                throw new ArgumentException("Invalid saved construction job.", nameof(snapshot));
            var job = new ConstructionJob(s.Request, s.InteractionCell, s.StagingStockpileId)
            {
                Status = s.Status, EffortCompleted = s.EffortCompleted, BlockedReason = s.BlockedReason, DeliverySequence = s.DeliverySequence,
            };
            foreach (var id in s.DeliveryIds) job.AddDelivery(id);
            foreach (var owner in s.InputOwners) job.AddInputOwner(owner);
            module._jobs.Add(s.Request.Id, job);
        }
        return module;
    }
}

public sealed record ConstructionJobSnapshot(BuildRequest Request, CellPosition InteractionCell, string StagingStockpileId,
    ConstructionStatus Status, decimal EffortCompleted, string? BlockedReason, int DeliverySequence,
    IReadOnlyList<string> DeliveryIds, IReadOnlyList<string> InputOwners);
public sealed record ConstructionSnapshot(IReadOnlyList<ConstructionJobSnapshot> Jobs);
