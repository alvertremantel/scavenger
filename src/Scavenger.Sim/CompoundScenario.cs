using Haldane.CareerSim;
using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Randomness;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.Kernel.Time;
using Haldane.SupplyChain;

namespace Scavenger.Sim;

/// <summary>A small headless logistics experiment. Content is provisional; no survival rules yet.</summary>
public static class CompoundScenario
{
    public static (World World, Scheduler Scheduler, EventCatalogue Catalogue) Create(ulong seed = 20260928, int ticksPerDay = 4)
    {
        var catalogue = new EventCatalogue();
        CalendarEventKinds.RegisterAll(catalogue);
        CareerSimEventKinds.RegisterAll(catalogue);
        SupplyChainEventKinds.RegisterAll(catalogue);

        var inventory = new InventoryModule()
            .AddMaterial(new MaterialDefinition("polymer.hdpe", "Recovered HDPE"))
            .AddMaterial(new MaterialDefinition("metal.steel", "Steel"))
            .AddStockpile(new Stockpile("salvage", "Salvage depot", 60, new CellPosition(0, 0)))
            .AddStockpile(new Stockpile("workshop", "Workshop store", 12, new CellPosition(8, 2)))
            .AddStockpile(new Stockpile("clinic", "Clinic store", 8, new CellPosition(3, 5)))
            .AddBatch(new MaterialBatch("salvage-hdpe", "polymer.hdpe", "salvage", 20,
                new BatchQuality(0.94m, "unclassified", new Dictionary<string, decimal> { ["soil"] = 0.04m })));

        var workforce = new WorkforceModule()
            .Hire(new StaffMember
            {
                Id = 1, Name = "Mira", Skill = 55,
                Skills = new SkillProfile(new Dictionary<string, int> { ["mechanical"] = 75, ["hauling"] = 45 }),
            })
            .Hire(new StaffMember
            {
                Id = 2, Name = "Ivo", Skill = 65,
                Skills = new SkillProfile(new Dictionary<string, int> { ["hauling"] = 60, ["chemistry"] = 15 }),
            });

        var board = new ProjectBoardModule().Add(new Project
        {
            Id = "inspect-pump", Name = "Inspect the damaged water pump", EffortRequired = 40,
            RequiredSkill = new SkillRequirement("mechanical", 40),
        });
        var logistics = new LogisticsModule()
            .AddRoute(new TransportRoute("depot-workshop", "salvage", "workshop", 4, 8, new SkillRequirement("hauling", 20)))
            .AddRoute(new TransportRoute("depot-clinic", "salvage", "clinic", 6, 8));

        var world = new World(new Clock(new TimeConfig(ticksPerDay)), new RngStreams(seed))
            .Register(workforce).Register(board).Register(inventory).Register(logistics);
        SupplyChainSave.ValidateWorld(world);
        return (world, new Scheduler(world, Systems()), catalogue);
    }

    public static IEnumerable<ISystem> Systems() => [new CalendarSystem(), new WorkSystem(), new LogisticsSystem()];

    public static Scheduler Load(string json)
    {
        var loaded = SaveManager.Load(json, SupplyChainSave.CreateRegistry());
        SupplyChainSave.ValidateWorld(loaded.World);
        return new Scheduler(loaded.World, Systems(), loaded.CommandLog);
    }

    /// <summary>Commands only: two workers, one qualification gate, and a temporary route closure.</summary>
    public static void QueueDemoCommands(Scheduler scheduler)
    {
        switch (scheduler.World.Clock.Tick)
        {
            case 0:
                scheduler.Queue(new AssignStaffCommand(1, "inspect-pump"));
                scheduler.Queue(new QueueHaulCommand(new HaulRequest("hdpe-for-workshop", "salvage-hdpe", "depot-workshop", 2, 6)));
                break;
            case 1:
                scheduler.Queue(new SetRouteOpenCommand("depot-workshop", false));
                break;
            case 3:
                scheduler.Queue(new SetRouteOpenCommand("depot-workshop", true));
                break;
        }
    }
}
