using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public sealed class Worksite
{
    public Worksite(string id, string stationId, int carrierId, IReadOnlyList<string> sources, string localStoreId)
    { Id = id; StationId = stationId; CarrierId = carrierId; Sources = Array.AsReadOnly(sources.ToArray()); LocalStoreId = localStoreId; }
    public string Id { get; }
    public string StationId { get; }
    public int CarrierId { get; }
    public IReadOnlyList<string> Sources { get; }
    public string LocalStoreId { get; }
    public int Sequence { get; internal set; }
    private readonly List<string> _deliveries = [];
    private readonly List<string> _inputs = [];
    public IReadOnlyList<string> Deliveries => _deliveries.AsReadOnly();
    public IReadOnlyList<string> InputOwners => _inputs.AsReadOnly();
    internal void AddDelivery(string id) => _deliveries.Add(id);
    internal void AddInput(string id) => _inputs.Add(id);
    internal void ClearInputs() => _inputs.Clear();
    public WorksiteSnapshot Snapshot() => new(Id, StationId, CarrierId, Sources, LocalStoreId, Sequence, Deliveries.ToArray(), InputOwners.ToArray());
    public static Worksite Restore(WorksiteSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Id) || snapshot.Sequence < 0 || snapshot.Sequence != snapshot.Deliveries.Count
            || snapshot.Sources.Count == 0 || snapshot.Sources.Distinct(StringComparer.Ordinal).Count() != snapshot.Sources.Count
            || snapshot.Deliveries.Distinct(StringComparer.Ordinal).Count() != snapshot.Deliveries.Count
            || snapshot.InputOwners.Distinct(StringComparer.Ordinal).Count() != snapshot.InputOwners.Count)
            throw new ArgumentException("Invalid worksite snapshot.", nameof(snapshot));
        var site = new Worksite(snapshot.Id, snapshot.StationId, snapshot.CarrierId, snapshot.Sources, snapshot.LocalStoreId) { Sequence = snapshot.Sequence };
        foreach (string id in snapshot.Deliveries) site.AddDelivery(id);
        foreach (string id in snapshot.InputOwners) site.AddInput(id);
        return site;
    }
}
public sealed record WorksiteSnapshot(string Id, string StationId, int CarrierId, IReadOnlyList<string> Sources,
    string LocalStoreId, int Sequence, IReadOnlyList<string> Deliveries, IReadOnlyList<string> InputOwners);

/// <summary>Shared physical input delivery and local stock claims for industry and electronics work.</summary>
public static class WorksiteLogistics
{
    public static string? ExplainNew(World world, string id, string stationId, int carrierId, IReadOnlyList<string> sources)
    {
        if (string.IsNullOrWhiteSpace(id) || world.Module<CompoundModule>().FindBuilding(stationId) is null) return "A unique job and physical station are required.";
        if (world.Module<InventoryModule>().FindStockpile($"worksite/{id}") is not null) return "This worksite id is already used.";
        if (world.Module<WorkforceModule>().Find(carrierId) is not { } worker || !worker.Skills.Meets(new("hauling", 20)))
            return "The delivery worker needs hauling 20.";
        if (sources.Count == 0 || sources.Distinct(StringComparer.Ordinal).Count() != sources.Count
            || sources.Any(s => world.Module<InventoryModule>().FindStockpile(s)?.Position is null)) return "Choose distinct physically located source stores.";
        return null;
    }
    public static Worksite Create(World world, string id, string stationId, int carrierId, IReadOnlyList<string> sources, decimal capacity)
    {
        if (ExplainNew(world, id, stationId, carrierId, sources) is { } reason) throw new InvalidOperationException(reason);
        var position = world.Module<CompoundModule>().FindBuilding(stationId)!.InteractionCell;
        string store = $"worksite/{id}";
        world.Module<InventoryModule>().AddStockpile(new Stockpile(store, $"Worksite: {id}", Math.Max(1, capacity), position));
        var logistics = world.Module<LogisticsModule>();
        for (int i = 0; i < sources.Count; i++)
            logistics.AddRoute(new TransportRoute($"worksite-route/{id}/{i}", sources[i], store, 1, 6,
                new("hauling", 20), usesWorldMovement: true));
        return new Worksite(id, stationId, carrierId, sources, store);
    }
    public static IEnumerable<MaterialBatch> Suitable(InventoryModule inventory, string store, MaterialRequirement requirement)
        => inventory.Batches.Where(b => b.StockpileId == store && b.MaterialId == requirement.MaterialId && b.Quality.Purity >= requirement.MinimumPurity);
    public static bool Ready(World world, Worksite site, IReadOnlyList<MaterialRequirement> requirements)
        => requirements.All(r => Suitable(world.Module<InventoryModule>(), site.LocalStoreId, r).Sum(b => world.Module<InventoryModule>().AvailableKg(b.Id)) >= r.QuantityKg);
    public static string? DeliverInputs(World world, Worksite site, IReadOnlyList<MaterialRequirement> requirements, List<GameEvent> events)
    {
        var inventory = world.Module<InventoryModule>(); var logistics = world.Module<LogisticsModule>();
        foreach (var requirement in requirements)
        {
            decimal incoming = site.Deliveries.Select(logistics.FindJob).Where(j => j is { Active: true })
                .Where(j => (j!.Cargo?.MaterialId ?? inventory.FindBatch(j.Request.BatchId)?.MaterialId) == requirement.MaterialId).Sum(j => j!.Request.QuantityKg);
            decimal missing = requirement.QuantityKg - incoming - Suitable(inventory, site.LocalStoreId, requirement).Sum(b => inventory.AvailableKg(b.Id));
            if (missing <= 0) continue;
            for (int sourceIndex = 0; sourceIndex < site.Sources.Count; sourceIndex++)
            {
                var batch = Suitable(inventory, site.Sources[sourceIndex], requirement).FirstOrDefault(b => inventory.AvailableKg(b.Id) > 0);
                if (batch is null) continue;
                if (world.TryModule<ComputingModule>(out var computing) && computing.Components.FirstOrDefault(c => c.MaterialId == requirement.MaterialId) is { } unit
                    && (missing != unit.MassKg || inventory.AvailableKg(batch.Id) != unit.MassKg || unit.MassKg > 6))
                    return "A whole component cannot be divided or moved by this carrier's six-kilogram route.";
                string id = $"worksite-delivery/{site.Id}/{site.Sequence}";
                var request = new HaulRequest(id, batch.Id, $"worksite-route/{site.Id}/{sourceIndex}", site.CarrierId, Math.Min(6, Math.Min(missing, inventory.AvailableKg(batch.Id))));
                if (logistics.ExplainRequest(world, request) is { } rejection)
                    return site.Deliveries.Any(d => logistics.FindJob(d) is { Active: true }) ? "Carrier is delivering inputs." : rejection;
                events.AddRange(new QueueHaulCommand(request).Execute(world)); site.AddDelivery(id); site.Sequence++;
                return "Inputs are travelling to the station.";
            }
            return $"No source has available {requirement.MaterialId} at purity {requirement.MinimumPurity:P0}.";
        }
        return Ready(world, site, requirements) ? null : "Waiting for local inputs.";
    }
    public static void ReserveInputs(World world, Worksite site, IReadOnlyList<MaterialRequirement> requirements, string owner)
    {
        if (!Ready(world, site, requirements)) throw new InvalidOperationException("Inputs are not available locally.");
        var inventory = world.Module<InventoryModule>(); int index = 0;
        foreach (var requirement in requirements)
        {
            decimal remaining = requirement.QuantityKg;
            foreach (var batch in Suitable(inventory, site.LocalStoreId, requirement))
            {
                decimal amount = Math.Min(remaining, inventory.AvailableKg(batch.Id)); if (amount <= 0) continue;
                string claim = $"{owner}/input/{index++}";
                if (!inventory.TryReserveStock(claim, batch.Id, amount)) throw new InvalidOperationException("Input availability changed during serial acquisition.");
                site.AddInput(claim); remaining -= amount; if (remaining == 0) break;
            }
        }
    }
    public static void ReleaseInputs(World world, Worksite site)
    { foreach (string owner in site.InputOwners) world.Module<InventoryModule>().ReleaseReservations(owner); site.ClearInputs(); }
    public static void CancelUnloaded(World world, Worksite site, List<GameEvent> events)
    {
        foreach (string id in site.Deliveries)
        { var cancel = new CancelHaulCommand(id); if (cancel.CanExecute(world)) events.AddRange(cancel.Execute(world)); }
    }
    public static string? ExplainLabor(World world, int id, Haldane.CareerSim.Work.SkillRequirement qualification)
    {
        var workforce = world.Module<WorkforceModule>();
        if (workforce.Find(id) is not { Present: true } worker) return "Operator is absent or unknown.";
        if (!worker.Skills.Meets(qualification)) return $"Operator needs {qualification.SkillId} {qualification.MinimumRating}.";
        if (workforce.ReservationFor(id) is not null || world.Module<ProjectBoardModule>().IsAssigned(id)) return "Operator is already assigned to another task.";
        return null;
    }
}
