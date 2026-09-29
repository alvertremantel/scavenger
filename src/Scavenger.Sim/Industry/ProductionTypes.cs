using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using Haldane.CareerSim.Work;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public sealed record ProductSpec(string MaterialId, decimal QuantityKg, bool Waste, BatchQuality? Quality = null, string? PreserveMaterialId = null);
public sealed record RecipeDefinition
{
    [JsonConstructor]
    public RecipeDefinition(string id, string name, string capability, SkillRequirement qualification,
        IReadOnlyList<MaterialRequirement> inputs, IReadOnlyList<ProductSpec> outputs, decimal effort,
        decimal powerKw, decimal waterLph = 0, decimal wearPerTick = .0005m, bool automatable = false, IReadOnlyDictionary<string, string>? inputGrades = null, bool rejectContaminatedInputs = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability); ArgumentNullException.ThrowIfNull(qualification);
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(outputs);
        if (inputs.Count == 0 || outputs.Count == 0 || inputs.Any(i => string.IsNullOrWhiteSpace(i.MaterialId) || i.QuantityKg <= 0 || i.MinimumPurity is < 0 or > 1)
            || inputs.Select(i => i.MaterialId).Distinct(StringComparer.Ordinal).Count() != inputs.Count
            || outputs.Any(o => string.IsNullOrWhiteSpace(o.MaterialId) || o.QuantityKg is <= 0 or > 6
                || (o.Quality is null && !inputs.Any(i => i.MaterialId == o.PreserveMaterialId)))
            || inputs.Sum(i => i.QuantityKg) != outputs.Sum(o => o.QuantityKg)
            || effort <= 0 || powerKw < 0 || waterLph < 0 || wearPerTick is < 0 or > 1)
            throw new ArgumentException("Invalid recipe, quality policy, or mass balance.");
        if (inputGrades is not null && inputGrades.Any(g => !inputs.Any(i => i.MaterialId == g.Key) || string.IsNullOrWhiteSpace(g.Value)))
            throw new ArgumentException("Quality grade gates must refer to recipe inputs.");
        RejectContaminatedInputs = rejectContaminatedInputs;
        InputGrades = new ReadOnlyDictionary<string, string>((inputGrades ?? new Dictionary<string, string>()).ToDictionary(g => g.Key, g => g.Value, StringComparer.Ordinal));
        Id = id; Name = name; Capability = capability; Qualification = qualification;
        Inputs = Array.AsReadOnly(inputs.ToArray()); Outputs = Array.AsReadOnly(outputs.ToArray());
        Effort = effort; PowerKw = powerKw; WaterLph = waterLph; WearPerTick = wearPerTick; Automatable = automatable;
    }
    public IReadOnlyDictionary<string, string> InputGrades { get; }
    public bool RejectContaminatedInputs { get; }
    public bool AcceptsInputQuality(MaterialBatch b) => (!InputGrades.TryGetValue(b.MaterialId, out var grade) || b.Quality.Grade == grade)
        && (!RejectContaminatedInputs || b.Quality.Contaminants.Count == 0);
    public string Id { get; }
    public string Name { get; }
    public string Capability { get; }
    public SkillRequirement Qualification { get; }
    public IReadOnlyList<MaterialRequirement> Inputs { get; }
    public IReadOnlyList<ProductSpec> Outputs { get; }
    public decimal Effort { get; }
    public decimal PowerKw { get; }
    public decimal WaterLph { get; }
    public decimal WearPerTick { get; }
    public bool Automatable { get; }
}
public sealed record ProductionPlan(string Id, string RecipeId, string StationId, int? OperatorId, int CarrierId,
    IReadOnlyList<string> SourceStores, string ProductStoreId, string WasteStoreId, bool Automatic = false, string? OrderId = null);
public enum ProductionStatus { WaitingForInputs, Walking, Processing, Delivering, Completed, Cancelled }
public sealed record ProductShipment(int Index, MaterialCargo Cargo, string DestinationId, string LocalClaim, string DestinationClaim,
    string RouteId, string? BatchId = null, string? HaulId = null);
public sealed class ProductionJob
{
    internal ProductionJob(ProductionPlan plan, Worksite site) { Plan = plan; Site = site; }
    public ProductionPlan Plan { get; }
    public Worksite Site { get; }
    public string Owner => $"scavenger.process/{Plan.Id}";
    public ProductionStatus Status { get; internal set; }
    public decimal EffortCompleted { get; internal set; }
    public string? BlockedReason { get; internal set; }
    private readonly List<ProductShipment> _products = [];
    public IReadOnlyList<ProductShipment> Products => _products.AsReadOnly();
    public bool Active => Status is not ProductionStatus.Completed and not ProductionStatus.Cancelled;
    internal void SetProducts(IEnumerable<ProductShipment> products) { _products.Clear(); _products.AddRange(products); }
    internal void UpdateProduct(int index, ProductShipment product) => _products[index] = product;
    internal ProductionJobSnapshot Snapshot() => new(Plan, Site.Snapshot(), Status, EffortCompleted, BlockedReason, Products.ToArray());
}
public sealed record ProductionJobSnapshot(ProductionPlan Plan, WorksiteSnapshot Site, ProductionStatus Status,
    decimal EffortCompleted, string? BlockedReason, IReadOnlyList<ProductShipment> Products);
public sealed record ProductionSnapshot(IReadOnlyList<RecipeDefinition> Recipes, IReadOnlyList<ProductionJobSnapshot> Jobs);
