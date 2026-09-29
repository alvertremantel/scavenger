using System.Text.Json.Serialization;
using Haldane.CareerSim.Work;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public enum Terrain { Ground, Floor, Wall, OpenDoor, ClosedDoor, Water, Rock }
public sealed record Tile(Terrain Terrain, bool Roofed = false);
public enum BuildingKind { Floor, Wall, Door, Stockpile, Workbench, Generator, Pump, Clinic, PowerCable, WaterPipe }
public enum UtilityKind { Power, Water }
public sealed record MaterialRequirement(string MaterialId, decimal QuantityKg, decimal MinimumPurity = 0);

public sealed record BuildingDefinition
{
    [JsonConstructor]
    public BuildingDefinition(string id, string name, BuildingKind kind, int width = 1, int height = 1,
        bool blocksMovement = false, bool buildable = true, decimal effortRequired = 3,
        SkillRequirement? requiredSkill = null, IReadOnlyList<MaterialRequirement>? materials = null,
        IReadOnlyList<MaterialRequirement>? repairMaterials = null, IReadOnlyList<string>? capabilities = null,
        decimal storeCapacityKg = 0, decimal powerSupplyKw = 0, decimal powerDemandKw = 0,
        decimal waterSupplyLph = 0, decimal waterDemandLph = 0, int utilityPriority = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(kind) || width < 1 || height < 1 || effortRequired <= 0
            || storeCapacityKg < 0 || (kind == BuildingKind.Stockpile && storeCapacityKg <= 0)
            || powerSupplyKw < 0 || powerDemandKw < 0 || waterSupplyLph < 0 || waterDemandLph < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid building dimensions, effort, or utility capacity.");
        static IReadOnlyList<MaterialRequirement> CopyRequirements(IReadOnlyList<MaterialRequirement>? items)
        {
            var result = (items ?? []).OrderBy(m => m.MaterialId, StringComparer.Ordinal).ToArray();
            if (result.Select(m => m.MaterialId).Distinct(StringComparer.Ordinal).Count() != result.Length
                || result.Any(m => string.IsNullOrWhiteSpace(m.MaterialId) || m.QuantityKg <= 0 || m.MinimumPurity is < 0 or > 1))
                throw new ArgumentException("Material requirements must be positive and have unique ids.", nameof(items));
            return Array.AsReadOnly(result);
        }
        Id = id; Name = name; Kind = kind; Width = width; Height = height;
        BlocksMovement = blocksMovement; Buildable = buildable; EffortRequired = effortRequired;
        RequiredSkill = requiredSkill ?? new SkillRequirement("construction", 10);
        Materials = CopyRequirements(materials); RepairMaterials = CopyRequirements(repairMaterials);
        if ((capabilities ?? []).Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Capabilities need nonempty names.", nameof(capabilities));
        Capabilities = Array.AsReadOnly((capabilities ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        StoreCapacityKg = storeCapacityKg; PowerSupplyKw = powerSupplyKw; PowerDemandKw = powerDemandKw;
        WaterSupplyLph = waterSupplyLph; WaterDemandLph = waterDemandLph; UtilityPriority = utilityPriority;
    }

    public string Id { get; }
    public string Name { get; }
    public BuildingKind Kind { get; }
    public int Width { get; }
    public int Height { get; }
    public bool BlocksMovement { get; }
    public bool Buildable { get; }
    public decimal EffortRequired { get; }
    public SkillRequirement RequiredSkill { get; }
    public IReadOnlyList<MaterialRequirement> Materials { get; }
    public IReadOnlyList<MaterialRequirement> RepairMaterials { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public decimal StoreCapacityKg { get; }
    public decimal PowerSupplyKw { get; }
    public decimal PowerDemandKw { get; }
    public decimal WaterSupplyLph { get; }
    public decimal WaterDemandLph { get; }
    public int UtilityPriority { get; }
    public int Layer => Kind switch { BuildingKind.Floor => 0, BuildingKind.PowerCable => 2, BuildingKind.WaterPipe => 3, _ => 1 };
}

public sealed class Building
{
    internal Building(string id, string definitionId, CellPosition origin, CellPosition interactionCell,
        decimal condition, IReadOnlyList<MaterialCargo>? embeddedMaterials = null, string? stockpileId = null)
    {
        Id = id; DefinitionId = definitionId; Origin = origin; InteractionCell = interactionCell;
        Condition = condition; StockpileId = stockpileId;
        _materials.AddRange(embeddedMaterials ?? []);
    }
    private readonly List<MaterialCargo> _materials = [];
    public string Id { get; }
    public string DefinitionId { get; }
    public CellPosition Origin { get; }
    public CellPosition InteractionCell { get; }
    public decimal Condition { get; internal set; }
    public string? StockpileId { get; }
    public IReadOnlyList<MaterialCargo> EmbeddedMaterials => _materials.AsReadOnly();
    internal void AddMaterials(IEnumerable<MaterialCargo> materials) => _materials.AddRange(materials);
    internal BuildingSnapshot Snapshot() => new(Id, DefinitionId, Origin, InteractionCell, Condition, EmbeddedMaterials.ToList(), StockpileId);
}

public sealed record BuildingSnapshot(string Id, string DefinitionId, CellPosition Origin, CellPosition InteractionCell,
    decimal Condition, IReadOnlyList<MaterialCargo> EmbeddedMaterials, string? StockpileId = null);
public sealed record GridRect(int X, int Y, int Width, int Height)
{
    public bool Contains(CellPosition cell) => cell.Level == 0 && cell.X >= X && cell.Y >= Y
        && (long)cell.X < (long)X + Width && (long)cell.Y < (long)Y + Height;
    public IEnumerable<CellPosition> Cells()
    {
        for (int y = Y; y < Y + Height; y++)
            for (int x = X; x < X + Width; x++) yield return new CellPosition(x, y);
    }
}
public sealed record RoomDesignation(string Id, string Name, string Purpose, GridRect Area);
public sealed record RoomReport(string Id, string Name, string Purpose, GridRect Area, bool Enclosed, bool Roofed, string Status);
public sealed record WorkerPosition(int WorkerId, CellPosition Cell);
public sealed record WorkBinding(string ProjectId, string BuildingId, string Capability);
public sealed record MoveResult(bool Arrived, bool Moved, string? BlockedReason);
