using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;

namespace Scavenger.Sim.Physical;

/// <summary>Deterministic capacity allocation. Power is resolved before powered water sources.</summary>
public sealed class UtilitySystem : ISystem
{
    public string Name => "Compound utilities";
    public IReadOnlyList<Type> Dependencies => [typeof(ConstructionSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks) => Recalculate(world, true);

    public static IReadOnlyList<GameEvent> Recalculate(World world, bool emitEvents = false)
    {
        var compound = world.Module<CompoundModule>();
        var utilities = world.Module<UtilitiesModule>();
        var power = utilities.ConnectedComponents(compound, UtilityKind.Power);
        var water = utilities.ConnectedComponents(compound, UtilityKind.Water);
        var powerCapacity = new Dictionary<int, decimal>();
        var waterCapacity = new Dictionary<int, decimal>();
        var powerAllocated = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var buildings = compound.Buildings.OrderBy(b => compound.Definition(b.DefinitionId)!.UtilityPriority)
            .ThenBy(b => b.Id, StringComparer.Ordinal).ToList();
        foreach (var building in buildings)
        {
            var definition = compound.Definition(building.DefinitionId)!;
            if (building.Condition > 0 && definition.PowerSupplyKw > 0 && definition.PowerDemandKw == 0
                && definition.WaterDemandLph == 0 && power.TryGetValue(building.Origin, out int network))
                powerCapacity[network] = powerCapacity.GetValueOrDefault(network) + definition.PowerSupplyKw * building.Condition;
        }
        foreach (var building in buildings)
        {
            var definition = compound.Definition(building.DefinitionId)!;
            decimal allocated = 0;
            if (building.Condition > 0 && definition.PowerDemandKw > 0 && power.TryGetValue(building.Origin, out int network)
                && powerCapacity.GetValueOrDefault(network) >= definition.PowerDemandKw)
            {
                allocated = definition.PowerDemandKw;
                powerCapacity[network] -= allocated;
            }
            powerAllocated[building.Id] = allocated;
        }
        foreach (var building in buildings)
        {
            var definition = compound.Definition(building.DefinitionId)!;
            if (building.Condition > 0 && definition.WaterSupplyLph > 0
                && powerAllocated[building.Id] >= definition.PowerDemandKw && water.TryGetValue(building.Origin, out int network))
                waterCapacity[network] = waterCapacity.GetValueOrDefault(network) + definition.WaterSupplyLph * building.Condition;
        }
        var services = new List<UtilityService>();
        var events = new List<GameEvent>();
        foreach (var building in buildings)
        {
            var definition = compound.Definition(building.DefinitionId)!;
            decimal allocatedWater = 0;
            if (building.Condition > 0 && definition.WaterDemandLph > 0 && powerAllocated[building.Id] >= definition.PowerDemandKw
                && water.TryGetValue(building.Origin, out int network) && waterCapacity.GetValueOrDefault(network) >= definition.WaterDemandLph)
            {
                allocatedWater = definition.WaterDemandLph;
                waterCapacity[network] -= allocatedWater;
            }
            int? powerNetwork = power.TryGetValue(building.Origin, out int p) ? p : null;
            int? waterNetwork = water.TryGetValue(building.Origin, out int w) ? w : null;
            string? reason = building.Condition <= 0 ? "Fixture is broken." : null;
            if (reason is null && (definition.PowerDemandKw > 0 || definition.PowerSupplyKw > 0) && powerNetwork is null)
                reason = "Power port is disconnected.";
            if (reason is null && powerAllocated[building.Id] < definition.PowerDemandKw) reason = "Insufficient connected power capacity.";
            if (reason is null && (definition.WaterDemandLph > 0 || definition.WaterSupplyLph > 0) && waterNetwork is null)
                reason = "Water port is disconnected.";
            if (reason is null && allocatedWater < definition.WaterDemandLph) reason = "Insufficient connected water capacity.";
            var service = new UtilityService(building.Id, powerNetwork, waterNetwork, definition.PowerDemandKw,
                powerAllocated[building.Id], definition.WaterDemandLph, allocatedWater, reason is null, reason);
            var previous = utilities.ServiceFor(building.Id);
            if (emitEvents && reason is not null && previous?.BlockedReason != reason)
                events.Add(new GameEvent(world.Clock.Tick, CompoundEventKinds.UtilityBlocked, EventSeverity.Warning, $"{definition.Name}: {reason}"));
            else if (emitEvents && previous is { Operational: false } && service.Operational)
                events.Add(new GameEvent(world.Clock.Tick, CompoundEventKinds.UtilityRestored, EventSeverity.Positive, $"{definition.Name} utilities restored."));
            services.Add(service);
        }
        utilities.ReplaceServices(services);
        return events;
    }
}
