using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed record UtilityNode(UtilityKind Kind, CellPosition Cell, bool Enabled = true);
public sealed record UtilityService(string BuildingId, int? PowerNetwork, int? WaterNetwork,
    decimal PowerRequiredKw, decimal PowerAllocatedKw, decimal WaterRequiredLph, decimal WaterAllocatedLph,
    bool Operational, string? BlockedReason);

public sealed class UtilitiesModule : IPersistentModule
{
    public const string Persist = "scavenger.utilities";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<(UtilityKind Kind, CellPosition Cell), bool> _nodes = new();
    private readonly Dictionary<string, UtilityService> _services = new(StringComparer.Ordinal);
    public IReadOnlyList<UtilityNode> Nodes => _nodes.OrderBy(n => n.Key.Kind).ThenBy(n => n.Key.Cell.Y).ThenBy(n => n.Key.Cell.X)
        .Select(n => new UtilityNode(n.Key.Kind, n.Key.Cell, n.Value)).ToList();
    public IReadOnlyList<UtilityService> Services => _services.Values.OrderBy(s => s.BuildingId, StringComparer.Ordinal).ToList();
    public UtilityService? ServiceFor(string buildingId) => _services.GetValueOrDefault(buildingId);
    public bool HasNode(UtilityKind kind, CellPosition cell) => _nodes.ContainsKey((kind, cell));
    public void AddNode(UtilityKind kind, CellPosition cell)
    {
        if (!Enum.IsDefined(kind) || cell.Level != 0 || cell.X < 0 || cell.Y < 0) throw new ArgumentOutOfRangeException(nameof(cell));
        _nodes.Add((kind, cell), true);
    }
    public void SetEnabled(UtilityKind kind, CellPosition cell, bool enabled)
    {
        if (!HasNode(kind, cell)) throw new ArgumentException("No conduit exists at this cell.", nameof(cell));
        _nodes[(kind, cell)] = enabled;
    }
    internal void ReplaceServices(IEnumerable<UtilityService> services)
    {
        _services.Clear();
        foreach (var service in services) _services.Add(service.BuildingId, service);
    }

    public IReadOnlyDictionary<CellPosition, int> ConnectedComponents(CompoundModule compound, UtilityKind kind)
    {
        var active = Nodes.Where(n => n.Kind == kind && n.Enabled && !compound.Buildings.Any(b => b.Condition <= 0
            && b.Origin == n.Cell && compound.Definition(b.DefinitionId)!.ConduitKind == kind))
            .Select(n => n.Cell).ToHashSet();
        var components = new Dictionary<CellPosition, int>();
        int componentId = 0;
        foreach (var start in active.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            if (components.ContainsKey(start)) continue;
            var frontier = new Queue<CellPosition>();
            components[start] = componentId;
            frontier.Enqueue(start);
            while (frontier.TryDequeue(out var current))
                foreach (var neighbor in compound.Neighbors(current))
                    if (active.Contains(neighbor) && components.TryAdd(neighbor, componentId)) frontier.Enqueue(neighbor);
            componentId++;
        }
        return components;
    }

    public object ToSnapshot() => new UtilitiesSnapshot(Nodes, Services);
    public static UtilitiesModule FromSnapshot(UtilitiesSnapshot snapshot)
    {
        var module = new UtilitiesModule();
        foreach (var node in snapshot.Nodes) { module.AddNode(node.Kind, node.Cell); module.SetEnabled(node.Kind, node.Cell, node.Enabled); }
        module.ReplaceServices(snapshot.Services);
        return module;
    }
}
public sealed record UtilitiesSnapshot(IReadOnlyList<UtilityNode> Nodes, IReadOnlyList<UtilityService> Services);
