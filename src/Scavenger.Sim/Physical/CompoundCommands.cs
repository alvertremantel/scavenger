using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public sealed class PlanBuildCommand(BuildRequest request) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.plan-build";
    public BuildRequest Request { get; } = request;
    public string PersistId => Persist;
    public string Description => $"Plan {(Request.RepairTargetId is null ? "construction" : "repair")} '{Request.Id}'.";
    public object ToSnapshot() => Request;
    public bool CanExecute(World world) => world.Module<ConstructionModule>().ExplainPlan(world, Request) is null;
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        world.Module<ConstructionModule>().Accept(world, Request);
        return [new GameEvent(world.Clock.Tick, CompoundEventKinds.ConstructionPlanned, EventSeverity.Info, Description)];
    }
}

public sealed class CancelBuildCommand(string jobId) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.cancel-build";
    public string JobId { get; } = jobId;
    public string PersistId => Persist;
    public string Description => $"Cancel construction '{JobId}'.";
    public object ToSnapshot() => new JobIdSnapshot(JobId);
    public bool CanExecute(World world) => world.Module<ConstructionModule>().FindJob(JobId) is { Active: true };
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        var events = world.Module<ConstructionModule>().Cancel(world, world.Module<ConstructionModule>().FindJob(JobId)!);
        return events.Append(new GameEvent(world.Clock.Tick, CompoundEventKinds.ConstructionCancelled, EventSeverity.Info, Description)).ToList();
    }
}
public sealed record JobIdSnapshot(string JobId);

public sealed class SetDoorOpenCommand(CellPosition cell, bool open) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.set-door";
    public CellPosition Cell { get; } = cell;
    public bool Open { get; } = open;
    public string PersistId => Persist;
    public string Description => $"{(Open ? "Open" : "Close")} door at {Cell.X},{Cell.Y}.";
    public object ToSnapshot() => new DoorSnapshot(Cell, Open);
    public bool CanExecute(World world)
    {
        var compound = world.Module<CompoundModule>();
        return compound.Contains(Cell) && compound.TileAt(Cell).Terrain is Terrain.OpenDoor or Terrain.ClosedDoor
            && (Open || !compound.Workers.Any(w => w.Cell == Cell));
    }
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        var compound = world.Module<CompoundModule>();
        compound.SetTile(Cell, new Tile(Open ? Terrain.OpenDoor : Terrain.ClosedDoor, compound.TileAt(Cell).Roofed));
        return [new GameEvent(world.Clock.Tick, CompoundEventKinds.LayoutChanged, EventSeverity.Info, Description)];
    }
}
public sealed record DoorSnapshot(CellPosition Cell, bool Open);

public sealed class DesignateRoomCommand(RoomDesignation room) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.designate-room";
    public RoomDesignation Room { get; } = room;
    public string PersistId => Persist;
    public string Description => $"Designate {Room.Name}.";
    public object ToSnapshot() => Room;
    public bool CanExecute(World world)
    {
        var compound = world.Module<CompoundModule>();
        return !string.IsNullOrWhiteSpace(Room.Id) && !string.IsNullOrWhiteSpace(Room.Name) && !string.IsNullOrWhiteSpace(Room.Purpose)
            && compound.AreaWithinMap(Room.Area) && !compound.Rooms.Any(r => r.Id != Room.Id && r.Area.Cells().Any(Room.Area.Contains));
    }
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        world.Module<CompoundModule>().DesignateRoom(Room);
        return [new GameEvent(world.Clock.Tick, CompoundEventKinds.LayoutChanged, EventSeverity.Info, Description)];
    }
}

public sealed class SetBuildingConditionCommand(string buildingId, decimal condition) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.set-condition";
    public string BuildingId { get; } = buildingId;
    public decimal Condition { get; } = condition;
    public string PersistId => Persist;
    public string Description => $"Set fixture '{BuildingId}' condition to {Condition:P0}.";
    public object ToSnapshot() => new ConditionSnapshot(BuildingId, Condition);
    public bool CanExecute(World world) => Condition is >= 0 and <= 1 && world.Module<CompoundModule>().FindBuilding(BuildingId) is not null;
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        world.Module<CompoundModule>().FindBuilding(BuildingId)!.Condition = Condition;
        return [new GameEvent(world.Clock.Tick, CompoundEventKinds.ConditionChanged, EventSeverity.Info, Description)];
    }
}
public sealed record ConditionSnapshot(string BuildingId, decimal Condition);

public sealed class SetConduitEnabledCommand(UtilityKind kind, CellPosition cell, bool enabled) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.set-conduit";
    public UtilityKind Kind { get; } = kind;
    public CellPosition Cell { get; } = cell;
    public bool Enabled { get; } = enabled;
    public string PersistId => Persist;
    public string Description => $"{(Enabled ? "Connect" : "Isolate")} {Kind} conduit at {Cell.X},{Cell.Y}.";
    public object ToSnapshot() => new ConduitSnapshot(Kind, Cell, Enabled);
    public bool CanExecute(World world) => world.Module<UtilitiesModule>().HasNode(Kind, Cell);
    public IReadOnlyList<GameEvent> Execute(World world)
    {
        world.Module<UtilitiesModule>().SetEnabled(Kind, Cell, Enabled);
        return [new GameEvent(world.Clock.Tick, CompoundEventKinds.LayoutChanged, EventSeverity.Info, Description)];
    }
}
public sealed record ConduitSnapshot(UtilityKind Kind, CellPosition Cell, bool Enabled);

public sealed class QueuePhysicalHaulCommand(HaulRequest request) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.physical-haul";
    public HaulRequest Request { get; } = request;
    public string PersistId => Persist;
    public string Description => $"Dispatch physical haul '{Request.Id}'.";
    public object ToSnapshot() => Request;
    public bool CanExecute(World world)
    {
        if (!new QueueHaulCommand(Request).CanExecute(world)) return false;
        if (world.TryModule<Industry.ComputingModule>(out var computing)
            && world.Module<InventoryModule>().FindBatch(Request.BatchId) is { } batch
            && computing.Components.FirstOrDefault(c => c.MaterialId == batch.MaterialId) is { } component
            && Request.QuantityKg != component.MassKg) return false;
        var compound = world.Module<CompoundModule>();
        var inventory = world.Module<InventoryModule>();
        var route = world.Module<LogisticsModule>().FindRoute(Request.RouteId)!;
        var origin = inventory.FindStockpile(route.SourceId)?.Position;
        var destination = inventory.FindStockpile(route.DestinationId)?.Position;
        var worker = compound.PositionOf(Request.WorkerId);
        return route.UsesWorldMovement && origin is not null && destination is not null && worker is not null
            && Navigation.FindPath(compound, worker, origin).Count > 0 && Navigation.FindPath(compound, origin, destination).Count > 0;
    }
    public IReadOnlyList<GameEvent> Execute(World world) => new QueueHaulCommand(Request).Execute(world);
}
