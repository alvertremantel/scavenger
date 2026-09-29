using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

/// <summary>Authoritative single-level geometry, fixtures, room zones, and worker locations.</summary>
public sealed class CompoundModule : IPersistentModule
{
    public const string Persist = "scavenger.compound";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Tile[] _tiles;
    private readonly Dictionary<string, BuildingDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Building> _buildings = new(StringComparer.Ordinal);
    private readonly Dictionary<int, CellPosition> _workers = new();
    private readonly Dictionary<string, RoomDesignation> _rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkBinding> _bindings = new(StringComparer.Ordinal);

    public CompoundModule(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must contain 1–1,000,000 cells.");
        Width = width; Height = height;
        _tiles = Enumerable.Repeat(new Tile(Terrain.Ground), width * height).ToArray();
    }
    public int Width { get; }
    public int Height { get; }
    public long TopologyRevision { get; private set; }
    public IReadOnlyList<BuildingDefinition> Definitions => _definitions.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<Building> Buildings => _buildings.Values.OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<WorkerPosition> Workers => _workers.OrderBy(w => w.Key).Select(w => new WorkerPosition(w.Key, w.Value)).ToList();
    public IReadOnlyList<RoomDesignation> Rooms => _rooms.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<WorkBinding> WorkBindings => _bindings.Values.OrderBy(b => b.ProjectId, StringComparer.Ordinal).ToList();
    public BuildingDefinition? Definition(string id) => _definitions.GetValueOrDefault(id);
    public Building? FindBuilding(string id) => _buildings.GetValueOrDefault(id);
    public CellPosition? PositionOf(int workerId) => _workers.GetValueOrDefault(workerId);
    public WorkBinding? BindingFor(string projectId) => _bindings.GetValueOrDefault(projectId);

    public bool Contains(CellPosition cell) => cell.Level == 0 && cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;
    public Tile TileAt(CellPosition cell) => Contains(cell) ? _tiles[cell.Y * Width + cell.X]
        : throw new ArgumentOutOfRangeException(nameof(cell));
    private static bool TerrainWalkable(Terrain terrain) => terrain is Terrain.Ground or Terrain.Floor or Terrain.OpenDoor;

    public bool IsWalkable(CellPosition cell)
        => Contains(cell) && TerrainWalkable(TileAt(cell).Terrain)
            && !_buildings.Values.Any(b => Definition(b.DefinitionId)!.BlocksMovement && Footprint(b).Contains(cell));

    public GridRect Footprint(Building building)
    {
        var definition = Definition(building.DefinitionId)!;
        return new GridRect(building.Origin.X, building.Origin.Y, definition.Width, definition.Height);
    }

    public IEnumerable<CellPosition> Neighbors(CellPosition cell)
    {
        // Equal-length path choices remain stable across save/load and platforms.
        foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
        {
            var neighbor = new CellPosition(cell.X + dx, cell.Y + dy, cell.Level);
            if (Contains(neighbor)) yield return neighbor;
        }
    }

    public void SetTile(CellPosition cell, Tile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (!Contains(cell) || !Enum.IsDefined(tile.Terrain)) throw new ArgumentOutOfRangeException(nameof(cell));
        if (!TerrainWalkable(tile.Terrain) && _workers.Values.Contains(cell))
            throw new InvalidOperationException("A worker occupies this cell.");
        if (_tiles[cell.Y * Width + cell.X] != tile)
        {
            _tiles[cell.Y * Width + cell.X] = tile;
            TopologyRevision++;
        }
    }

    public void AddDefinition(BuildingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions.Add(definition.Id, definition);
    }

    public void PlaceWorker(int workerId, CellPosition cell)
    {
        if (!IsWalkable(cell)) throw new ArgumentException("Worker position must be walkable.", nameof(cell));
        _workers.Add(workerId, cell);
    }

    internal void MoveWorker(int workerId, CellPosition destination)
    {
        var source = PositionOf(workerId) ?? throw new ArgumentException("Unknown physical worker.", nameof(workerId));
        if (!IsWalkable(destination) || source.Level != destination.Level
            || Math.Abs(source.X - destination.X) + Math.Abs(source.Y - destination.Y) != 1)
            throw new InvalidOperationException("A worker can move only to an adjacent walkable cell.");
        _workers[workerId] = destination;
    }

    public void BindWork(WorkBinding binding)
    {
        if (FindBuilding(binding.BuildingId) is null || string.IsNullOrWhiteSpace(binding.Capability))
            throw new ArgumentException("Work binding requires an existing station and capability.", nameof(binding));
        _bindings.Add(binding.ProjectId, binding);
    }

    public void DesignateRoom(RoomDesignation room)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(room.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(room.Purpose);
        if (!AreaWithinMap(room.Area)) throw new ArgumentOutOfRangeException(nameof(room));
        if (Rooms.Any(r => r.Id != room.Id && r.Area.Cells().Any(room.Area.Contains)))
            throw new InvalidOperationException("Room designations cannot overlap.");
        _rooms[room.Id] = room;
    }

    public bool AreaWithinMap(GridRect area) => area.Width > 0 && area.Height > 0 && area.X >= 0 && area.Y >= 0
        && (long)area.X + area.Width <= Width && (long)area.Y + area.Height <= Height;

    public RoomReport InspectRoom(RoomDesignation room)
    {
        bool roofed = room.Area.Cells().All(c => TileAt(c).Roofed);
        bool enclosed = room.Area.Cells().SelectMany(Neighbors).Where(c => !room.Area.Contains(c))
            .All(c => TileAt(c).Terrain is Terrain.Wall or Terrain.OpenDoor or Terrain.ClosedDoor)
            && room.Area.X > 0 && room.Area.Y > 0 && room.Area.X + room.Area.Width < Width && room.Area.Y + room.Area.Height < Height;
        return new RoomReport(room.Id, room.Name, room.Purpose, room.Area, enclosed, roofed,
            enclosed && roofed ? "Enclosed and roofed" : !enclosed ? "Open boundary" : "Roof incomplete");
    }

    public string? ExplainPlacement(BuildingDefinition definition, CellPosition origin, CellPosition interactionCell)
    {
        var area = new GridRect(origin.X, origin.Y, definition.Width, definition.Height);
        if (origin.Level != 0 || !AreaWithinMap(area)) return "Footprint is outside the compound.";
        if (!IsWalkable(interactionCell)) return "The interaction cell is not walkable.";
        if (definition.BlocksMovement && area.Contains(interactionCell)) return "Interaction must be outside a solid footprint.";
        if (definition.BlocksMovement && !area.Cells().Any(c => Neighbors(c).Contains(interactionCell)))
            return "Interaction must border the footprint.";
        if (Buildings.Any(b => Definition(b.DefinitionId)!.Layer == definition.Layer && Footprint(b).Cells().Any(area.Contains)))
            return "Another structure occupies this layer of the footprint.";
        if (definition.Layer <= 1 && area.Cells().Any(c => !TerrainWalkable(TileAt(c).Terrain)
            && !(definition.Kind == BuildingKind.Door && TileAt(c).Terrain == Terrain.Wall)))
            return "Construction requires clear, walkable ground.";
        if ((definition.BlocksMovement || definition.Kind is BuildingKind.Wall or BuildingKind.Door)
            && _workers.Values.Any(area.Contains)) return "A worker occupies the footprint.";
        if (definition.BlocksMovement && Buildings.Any(b => area.Contains(b.InteractionCell)))
            return "This footprint would obstruct an existing interaction cell.";
        return null;
    }

    public CellPosition? ChooseInteraction(BuildingDefinition definition, CellPosition origin)
    {
        var area = new GridRect(origin.X, origin.Y, definition.Width, definition.Height);
        if (!definition.BlocksMovement && definition.Kind is not BuildingKind.Wall and not BuildingKind.Door && IsWalkable(origin))
            return origin;
        return area.Cells().SelectMany(Neighbors).Where(c => !area.Contains(c)).Distinct()
            .OrderBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault(IsWalkable);
    }

    public void AddExistingBuilding(BuildingSnapshot snapshot)
    {
        var definition = Definition(snapshot.DefinitionId) ?? throw new ArgumentException("Unknown building definition.", nameof(snapshot));
        if (snapshot.Condition is < 0 or > 1 || string.IsNullOrWhiteSpace(snapshot.Id)) throw new ArgumentException("Invalid fixture.", nameof(snapshot));
        var area = new GridRect(snapshot.Origin.X, snapshot.Origin.Y, definition.Width, definition.Height);
        if (!AreaWithinMap(area) || snapshot.Origin.Level != 0 || !Contains(snapshot.InteractionCell)
            || (definition.BlocksMovement && area.Contains(snapshot.InteractionCell)))
            throw new ArgumentException("Invalid fixture footprint or interaction cell.", nameof(snapshot));
        if (Buildings.Any(b => Definition(b.DefinitionId)!.Layer == definition.Layer && Footprint(b).Cells().Any(area.Contains)))
            throw new ArgumentException("Overlapping fixtures.", nameof(snapshot));
        if (definition.BlocksMovement && !area.Cells().Any(c => Neighbors(c).Contains(snapshot.InteractionCell)))
            throw new ArgumentException("Solid fixtures require an adjacent interaction cell.", nameof(snapshot));
        if (definition.BlocksMovement && Workers.Any(w => area.Contains(w.Cell)))
            throw new ArgumentException("A worker occupies the fixture footprint.", nameof(snapshot));
        _buildings.Add(snapshot.Id, new Building(snapshot.Id, snapshot.DefinitionId, snapshot.Origin,
            snapshot.InteractionCell, snapshot.Condition, snapshot.EmbeddedMaterials, snapshot.StockpileId));
        TopologyRevision++;
    }

    internal void Install(BuildingSnapshot snapshot)
    {
        AddExistingBuilding(snapshot);
        var definition = Definition(snapshot.DefinitionId)!;
        foreach (var cell in Footprint(FindBuilding(snapshot.Id)!).Cells())
        {
            if (definition.Kind == BuildingKind.Floor) SetTile(cell, new Tile(Terrain.Floor, true));
            if (definition.Kind == BuildingKind.Wall) SetTile(cell, new Tile(Terrain.Wall, true));
            if (definition.Kind == BuildingKind.Door) SetTile(cell, new Tile(Terrain.OpenDoor, true));
        }
    }

    public object ToSnapshot() => new CompoundSnapshot(Width, Height, TopologyRevision, _tiles.ToList(), Definitions,
        Buildings.Select(b => b.Snapshot()).ToList(), Workers, Rooms, WorkBindings);

    public static CompoundModule FromSnapshot(CompoundSnapshot snapshot)
    {
        var module = new CompoundModule(snapshot.Width, snapshot.Height);
        if (snapshot.Tiles.Count != snapshot.Width * snapshot.Height || snapshot.TopologyRevision < 0
            || snapshot.Tiles.Any(t => !Enum.IsDefined(t.Terrain))) throw new ArgumentException("Invalid saved grid.", nameof(snapshot));
        snapshot.Tiles.ToArray().CopyTo(module._tiles, 0);
        foreach (var definition in snapshot.Definitions) module.AddDefinition(definition);
        foreach (var building in snapshot.Buildings) module.AddExistingBuilding(building);
        foreach (var worker in snapshot.Workers) module.PlaceWorker(worker.WorkerId, worker.Cell);
        foreach (var room in snapshot.Rooms) module.DesignateRoom(room);
        foreach (var binding in snapshot.WorkBindings) module.BindWork(binding);
        module.TopologyRevision = snapshot.TopologyRevision;
        return module;
    }
}

public sealed record CompoundSnapshot(int Width, int Height, long TopologyRevision, IReadOnlyList<Tile> Tiles,
    IReadOnlyList<BuildingDefinition> Definitions, IReadOnlyList<BuildingSnapshot> Buildings,
    IReadOnlyList<WorkerPosition> Workers, IReadOnlyList<RoomDesignation> Rooms, IReadOnlyList<WorkBinding> WorkBindings);
