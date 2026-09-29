using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public sealed class IndustryPreparationSystem : ISystem
{
    public string Name => "Industry claims and workload preparation";
    public IReadOnlyList<Type> Dependencies => [typeof(StandingOrderSystem), typeof(ConstructionSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    {
        var events = new List<GameEvent>(); world.Module<ProductionModule>().Prepare(world, events);
        world.Module<ComputingModule>().Prepare(world, events); world.Module<EnergyModule>().Prepare(world); return events;
    }
}
public sealed class ProductionSystem : ISystem
{
    public string Name => "Physical production";
    public IReadOnlyList<Type> Dependencies => [typeof(UtilitySystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    { var events = new List<GameEvent>(); world.Module<ProductionModule>().Work(world, events); return events; }
}
public sealed class ComputingSystem : ISystem
{
    public string Name => "Scavenged computer tasks";
    public IReadOnlyList<Type> Dependencies => [typeof(ProductionSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    { var events = new List<GameEvent>(); world.Module<ComputingModule>().Work(world, events); return events; }
}
public sealed class EnergyAccountingSystem : ISystem
{
    public string Name => "Electrical energy and fuel accounting";
    public IReadOnlyList<Type> Dependencies => [typeof(ComputingSystem), typeof(LogisticsSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    { world.Module<EnergyModule>().Account(world); return []; }
}
