using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Region;

public static class GeneratedScenario
{
    public const ulong DefaultSeed = 20260929;
    public static (World World, Scheduler Scheduler, EventCatalogue Catalogue) Create(ulong seed = DefaultSeed)
    {
        var (world, scheduler, catalogue) = ScavengingScenario.Create(seed);
        var generated = RegionGenerator.Generate(seed, world.Clock.Time.TicksPerDay);
        RegionalContent.AddMaterials(world.Module<InventoryModule>());
        var scavenging = new ScavengingModule();
        foreach (var location in generated.Locations) scavenging.AddLocation(location);
        foreach (var resident in world.Module<ScavengingModule>().Health) scavenging.AddResident(resident.WorkerId, resident.Sick);
        world.Register(scavenging); world.Register(generated.Geography);
        PhysicalSave.ValidateWorld(world);
        return (world, scheduler, catalogue);
    }
}
