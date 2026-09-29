using Haldane.CareerSim.Work;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;

namespace Scavenger.Sim.Scavenging;

public enum ExpeditionApproach { Careful, Quick }
public enum ExpeditionStatus { Gathering, LeavingCompound, Outbound, Searching, Decision, Returning, Unloading, Completed }
public enum ExpeditionDecision { Structure, Rivals }
public enum ExpeditionResponse { Wait, Retreat }

public sealed record SalvageDefinition(string Id, string Object, string MaterialId, decimal InitialKg,
    BatchQuality Quality, decimal EffortPerKg, SkillRequirement Qualification, decimal MinimumIntegrity = 0,
    bool NeedsPrybar = false, string? ComponentId = null, string? ComponentModel = null,
    ComponentFault Fault = ComponentFault.None, decimal ComponentCondition = .9m, IReadOnlyList<DigitalAsset>? Assets = null);
public sealed record LocationDefinition(string Id, string Name, string Description, int X, int Y, int TravelTicks,
    decimal InitialIntegrity, decimal DecayPerDay, decimal RivalPressure, decimal RivalKgPerDay,
    decimal ExposurePerTick, IReadOnlyList<SalvageDefinition> Salvage);
public sealed class ScavengeLocation
{
    public ScavengeLocation(LocationDefinition definition)
    {
        Definition = definition with { Salvage = Array.AsReadOnly(definition.Salvage.Select(s => s with
        { Assets = Array.AsReadOnly((s.Assets ?? []).Select(a => a with { Capabilities = Array.AsReadOnly(a.Capabilities.ToArray()), Dependencies = Array.AsReadOnly(a.Dependencies.ToArray()) }).ToArray()) }).ToArray()) };
        Integrity = definition.InitialIntegrity;
        foreach (var entry in definition.Salvage) _remaining.Add(entry.Id, entry.InitialKg);
    }
    public LocationDefinition Definition { get; }
    public decimal Integrity { get; internal set; }
    private readonly Dictionary<string, decimal> _remaining = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, decimal> RemovedByRivals = new(StringComparer.Ordinal);
    public decimal Remaining(string id) => _remaining.GetValueOrDefault(id);
    internal void Remove(string id, decimal kg) => _remaining[id] -= kg;
    public LocationSnapshot Snapshot() => new(Definition, Integrity,
        new SortedDictionary<string, decimal>(_remaining, StringComparer.Ordinal),
        new SortedDictionary<string, decimal>(RemovedByRivals, StringComparer.Ordinal));
    internal void Restore(LocationSnapshot snapshot)
    {
        Integrity = snapshot.Integrity;
        foreach (var pair in snapshot.Remaining) _remaining[pair.Key] = pair.Value;
        foreach (var pair in snapshot.RemovedByRivals) RemovedByRivals.Add(pair.Key, pair.Value);
    }
}
public sealed record SalvageRequest(string SalvageId, decimal QuantityKg);
public sealed record ExpeditionPlan(string Id, string LocationId, IReadOnlyList<int> Team,
    IReadOnlyList<SalvageRequest> Manifest, string ReceivingStoreId = "salvage",
    ExpeditionApproach Approach = ExpeditionApproach.Careful, bool UseProtection = true);
public sealed record RecoveredCargo(string SalvageId, string MaterialId, decimal QuantityKg, BatchQuality Quality);
public sealed class Expedition
{
    public Expedition(ExpeditionPlan plan) => Plan = plan with
    { Team = Array.AsReadOnly(plan.Team.ToArray()), Manifest = Array.AsReadOnly(plan.Manifest.ToArray()) };
    public ExpeditionPlan Plan { get; }
    public string Owner => $"scavenger.expedition/{Plan.Id}";
    public ExpeditionStatus Status { get; internal set; } = ExpeditionStatus.Gathering;
    public bool Active => Status != ExpeditionStatus.Completed;
    public int ElapsedTravel { get; internal set; }
    public int SearchIndex { get; internal set; }
    public decimal SearchEffort { get; internal set; }
    public int DelayTicks { get; internal set; }
    public ExpeditionDecision? Decision { get; internal set; }
    public string? Reason { get; internal set; }
    public bool Recalled { get; internal set; }
    public bool Departed { get; internal set; }
    internal readonly List<RecoveredCargo> CargoList = [];
    internal readonly List<RecoveredCargo> ReceiptsList = [];
    internal readonly List<string> KitClaims = [];
    public IReadOnlyList<RecoveredCargo> Cargo => CargoList.AsReadOnly();
    public IReadOnlyList<RecoveredCargo> Receipts => ReceiptsList.AsReadOnly();
    public decimal ManifestKg => Plan.Manifest.Sum(m => m.QuantityKg);
    public string SpaceOwner(int index) => $"{Owner}/space/{index}";
    public ExpeditionSnapshot Snapshot() => new(Plan, Status, ElapsedTravel, SearchIndex, SearchEffort,
        DelayTicks, Decision, Reason, Recalled, Departed, Cargo.ToArray(), Receipts.ToArray(), KitClaims.ToArray());
}
public sealed record LocationSnapshot(LocationDefinition Definition, decimal Integrity,
    IReadOnlyDictionary<string, decimal> Remaining, IReadOnlyDictionary<string, decimal> RemovedByRivals);
public sealed record ExpeditionSnapshot(ExpeditionPlan Plan, ExpeditionStatus Status, int ElapsedTravel,
    int SearchIndex, decimal SearchEffort, int DelayTicks, ExpeditionDecision? Decision, string? Reason,
    bool Recalled, bool Departed, IReadOnlyList<RecoveredCargo> Cargo, IReadOnlyList<RecoveredCargo> Receipts,
    IReadOnlyList<string> KitClaims);
public sealed record WorkerHealth(int WorkerId, decimal Exposure = 0, bool Sick = false);
public sealed record TreatmentPlan(string Id, int PatientId, int MedicId = 4);
public sealed class ClinicTreatment(TreatmentPlan plan)
{
    public TreatmentPlan Plan { get; } = plan;
    public string Owner => $"scavenger.clinic/{Plan.Id}";
    public string? DoseClaim { get; internal set; }
    public decimal Effort { get; internal set; }
    public bool Completed { get; internal set; }
    public bool Cancelled { get; internal set; }
    public bool Active => !Completed && !Cancelled;
    public string? Reason { get; internal set; }
    public TreatmentSnapshot Snapshot() => new(Plan, DoseClaim, Effort, Completed, Reason, Cancelled);
}
public sealed record TreatmentSnapshot(TreatmentPlan Plan, string? DoseClaim, decimal Effort, bool Completed, string? Reason, bool Cancelled = false);
public sealed record ScavengingSnapshot(IReadOnlyList<LocationSnapshot> Locations,
    IReadOnlyList<ExpeditionSnapshot> Expeditions, IReadOnlyList<WorkerHealth> Health,
    IReadOnlyList<TreatmentSnapshot> Treatments, decimal SpentSuppliesKg);
