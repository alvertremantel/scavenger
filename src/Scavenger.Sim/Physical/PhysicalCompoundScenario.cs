using Haldane.CareerSim;
using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Randomness;
using Haldane.Kernel.Simulation;
using Haldane.Kernel.Time;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Physical;

public static class PhysicalCompoundScenario
{
    public static (World World, Scheduler Scheduler, EventCatalogue Catalogue) Create(ulong seed = 20260928, bool leanSupplies = false)
    {
        var catalogue = CreateCatalogue();
        var compound = new CompoundModule(26, 18);
        foreach (var definition in BuildCatalogue()) compound.AddDefinition(definition);
        MakeRoom(compound, new GridRect(14, 2, 8, 6), new CellPosition(13, 5));
        MakeRoom(compound, new GridRect(15, 11, 7, 5), new CellPosition(14, 13));
        compound.DesignateRoom(new RoomDesignation("workshop-room", "Workshop", "Mechanical work", new GridRect(14, 2, 8, 6)));
        compound.DesignateRoom(new RoomDesignation("clinic-room", "Clinic", "Medical work", new GridRect(15, 11, 7, 5)));

        var inventory = new InventoryModule();
        foreach (var (id, name) in new[] { ("polymer.hdpe", "Recovered HDPE"), ("metal.steel", "Steel"),
            ("metal.copper", "Copper"), ("timber", "Timber"), ("stone", "Stone") }) inventory.AddMaterial(new MaterialDefinition(id, name));
        inventory.AddStockpile(new Stockpile("salvage", "Salvage depot", 400, new CellPosition(2, 4)))
            .AddStockpile(new Stockpile("workshop", "Workshop store", 60, new CellPosition(16, 5)))
            .AddStockpile(new Stockpile("clinic", "Clinic store", 30, new CellPosition(16, 13)));
        foreach (var (material, quantity) in new[] { ("metal.steel", leanSupplies ? 6m : 50m), ("metal.copper", leanSupplies ? .5m : 10m), ("timber", leanSupplies ? 8m : 40m), ("stone", leanSupplies ? 20m : 80m) })
            inventory.AddBatch(new MaterialBatch($"salvage-{material}", material, "salvage", quantity, new BatchQuality(0.99m, "identified")));
        inventory.AddBatch(new MaterialBatch("salvage-hdpe", "polymer.hdpe", "salvage", leanSupplies ? 2 : 20,
            new BatchQuality(0.94m, "unclassified", new Dictionary<string, decimal> { ["soil"] = 0.04m })));

        foreach (var fixture in new[]
        {
            new BuildingSnapshot("depot-store", "stockpile", new CellPosition(2, 3), new CellPosition(2, 4), 1, [], "salvage"),
            new BuildingSnapshot("workshop-store", "stockpile", new CellPosition(16, 4), new CellPosition(16, 5), 1, [], "workshop"),
            new BuildingSnapshot("clinic-store", "stockpile", new CellPosition(16, 13), new CellPosition(16, 13), 1, [], "clinic"),
            new BuildingSnapshot("workshop-bench", "workbench", new CellPosition(18, 3), new CellPosition(18, 4), 1, []),
            new BuildingSnapshot("generator", "generator", new CellPosition(7, 11), new CellPosition(7, 10), 1, []),
            new BuildingSnapshot("well-pump", "pump", new CellPosition(8, 3), new CellPosition(8, 4), 1, []),
            new BuildingSnapshot("clinic-station", "clinic-station", new CellPosition(18, 12), new CellPosition(18, 13), 1, []),
        }) compound.AddExistingBuilding(fixture);

        var workforce = new WorkforceModule();
        AddWorker(workforce, compound, 1, "Mira", new CellPosition(8, 6), ("mechanical", 75), ("hauling", 45));
        AddWorker(workforce, compound, 2, "Ivo", new CellPosition(3, 6), ("hauling", 60), ("mechanical", 15));
        AddWorker(workforce, compound, 3, "Noor", new CellPosition(4, 14), ("construction", 60), ("mechanical", 45), ("electrical", 40), ("hauling", 35));
        AddWorker(workforce, compound, 4, "Anya", new CellPosition(19, 14), ("medicine", 75), ("chemistry", 65), ("hauling", 20));
        var projects = new ProjectBoardModule()
            .Add(new Project { Id = "inspect-pump", Name = "Inspect pump components", EffortRequired = 40, RequiredSkill = new SkillRequirement("mechanical", 40) })
            .Add(new Project { Id = "prepare-clinic", Name = "Prepare clinic supplies", EffortRequired = 60, RequiredSkill = new SkillRequirement("medicine", 30) });
        compound.BindWork(new WorkBinding("inspect-pump", "workshop-bench", "mechanical"));
        compound.BindWork(new WorkBinding("prepare-clinic", "clinic-station", "medical"));
        var logistics = new LogisticsModule()
            .AddRoute(new TransportRoute("depot-workshop", "salvage", "workshop", 14, 8, new SkillRequirement("hauling", 20), usesWorldMovement: true))
            .AddRoute(new TransportRoute("depot-clinic", "salvage", "clinic", 23, 8, usesWorldMovement: true));
        var utilities = new UtilitiesModule();
        AddLine(utilities, UtilityKind.Power, new CellPosition(7, 11), new CellPosition(12, 11));
        AddLine(utilities, UtilityKind.Power, new CellPosition(12, 11), new CellPosition(12, 3));
        AddLine(utilities, UtilityKind.Power, new CellPosition(8, 3), new CellPosition(18, 3));
        AddLine(utilities, UtilityKind.Power, new CellPosition(12, 11), new CellPosition(18, 11));
        AddLine(utilities, UtilityKind.Power, new CellPosition(18, 11), new CellPosition(18, 12));
        AddLine(utilities, UtilityKind.Water, new CellPosition(8, 3), new CellPosition(18, 3));
        AddLine(utilities, UtilityKind.Water, new CellPosition(10, 3), new CellPosition(10, 12));
        AddLine(utilities, UtilityKind.Water, new CellPosition(10, 12), new CellPosition(18, 12));

        var world = new World(new Clock(new TimeConfig(4)), new RngStreams(seed))
            .Register(workforce).Register(projects).Register(inventory).Register(logistics)
            .Register(compound).Register(new ConstructionModule()).Register(utilities);
        UtilitySystem.Recalculate(world);
        PhysicalSave.ValidateWorld(world);
        return (world, new Scheduler(world, Systems()), catalogue);
    }

    public static EventCatalogue CreateCatalogue()
    {
        var catalogue = new EventCatalogue();
        CalendarEventKinds.RegisterAll(catalogue); CareerSimEventKinds.RegisterAll(catalogue);
        SupplyChainEventKinds.RegisterAll(catalogue); CompoundEventKinds.RegisterAll(catalogue);
        return catalogue;
    }
    public static IEnumerable<ISystem> Systems() =>
    [new CalendarSystem(), new ConstructionSystem(), new UtilitySystem(), new ProjectTravelSystem(),
        new WorkSystem(new CompoundWorkContext()), new LogisticsSystem(new CompoundTransportDriver())];

    public static IEnumerable<BuildingDefinition> BuildCatalogue()
    {
        var basicRepair = new[] { new MaterialRequirement("metal.steel", 1) };
        yield return new BuildingDefinition("floor", "Roofed stone floor", BuildingKind.Floor, materials: [new("stone", 1)], effortRequired: 1);
        yield return new BuildingDefinition("wall", "Stone wall", BuildingKind.Wall, blocksMovement: true, materials: [new("stone", 4)], repairMaterials: basicRepair, effortRequired: 2);
        yield return new BuildingDefinition("door", "Timber door", BuildingKind.Door, materials: [new("timber", 2), new("metal.steel", 1)], repairMaterials: basicRepair);
        yield return new BuildingDefinition("stockpile", "Stockpile", BuildingKind.Stockpile, width: 2, height: 2, materials: [new("timber", 3)], storeCapacityKg: 60);
        yield return new BuildingDefinition("workbench", "Workshop bench", BuildingKind.Workbench, width: 2, blocksMovement: true,
            effortRequired: 6, requiredSkill: new("mechanical", 30), materials: [new("metal.steel", 4), new("timber", 4)],
            repairMaterials: [new("metal.steel", 1), new("timber", 1)], capabilities: ["mechanical"], powerDemandKw: 3, waterDemandLph: 1, utilityPriority: 2);
        yield return new BuildingDefinition("power-cable", "Electrical conduit", BuildingKind.PowerCable,
            requiredSkill: new("electrical", 20), materials: [new("metal.copper", 0.2m, 0.95m), new("polymer.hdpe", 0.1m)], effortRequired: 1);
        yield return new BuildingDefinition("water-pipe", "Water pipe", BuildingKind.WaterPipe,
            requiredSkill: new("mechanical", 20), materials: [new("metal.steel", 0.5m)], effortRequired: 1);
        yield return new BuildingDefinition("generator", "Salvaged generator", BuildingKind.Generator, width: 2, height: 2, blocksMovement: true,
            buildable: false, effortRequired: 6, repairMaterials: [new("metal.steel", 2), new("metal.copper", 0.5m)], powerSupplyKw: 10);
        yield return new BuildingDefinition("pump", "Well pump", BuildingKind.Pump, width: 2, blocksMovement: true,
            buildable: false, effortRequired: 5, repairMaterials: [new("metal.steel", 1), new("metal.copper", 0.3m)], powerDemandKw: 3, waterSupplyLph: 8, utilityPriority: 0);
        yield return new BuildingDefinition("clinic-station", "Clinic station", BuildingKind.Clinic, width: 2, blocksMovement: true,
            buildable: false, effortRequired: 4, repairMaterials: [new("timber", 1), new("metal.steel", 0.5m)],
            capabilities: ["medical"], powerDemandKw: 2, waterDemandLph: 4, utilityPriority: 1);
    }

    private static void AddWorker(WorkforceModule workforce, CompoundModule compound, int id, string name, CellPosition cell,
        params (string Skill, int Rating)[] ratings)
    {
        workforce.Hire(new StaffMember { Id = id, Name = name, Skills = new SkillProfile(ratings.ToDictionary(r => r.Skill, r => r.Rating, StringComparer.Ordinal)) });
        compound.PlaceWorker(id, cell);
    }
    private static void MakeRoom(CompoundModule compound, GridRect area, CellPosition door)
    {
        foreach (var cell in area.Cells()) compound.SetTile(cell, new Tile(Terrain.Floor, true));
        for (int x = area.X - 1; x <= area.X + area.Width; x++)
        {
            compound.SetTile(new CellPosition(x, area.Y - 1), new Tile(Terrain.Wall, true));
            compound.SetTile(new CellPosition(x, area.Y + area.Height), new Tile(Terrain.Wall, true));
        }
        for (int y = area.Y - 1; y <= area.Y + area.Height; y++)
        {
            compound.SetTile(new CellPosition(area.X - 1, y), new Tile(Terrain.Wall, true));
            compound.SetTile(new CellPosition(area.X + area.Width, y), new Tile(Terrain.Wall, true));
        }
        compound.SetTile(door, new Tile(Terrain.OpenDoor, true));
    }
    private static void AddLine(UtilitiesModule utilities, UtilityKind kind, CellPosition start, CellPosition end)
    {
        if (start.X != end.X && start.Y != end.Y) throw new ArgumentException("Utility lines are cardinal.");
        int dx = Math.Sign(end.X - start.X), dy = Math.Sign(end.Y - start.Y);
        for (var cell = start; ; cell = new CellPosition(cell.X + dx, cell.Y + dy))
        {
            if (!utilities.HasNode(kind, cell)) utilities.AddNode(kind, cell);
            if (cell == end) break;
        }
    }

    public static void QueueDemoCommands(Scheduler scheduler)
    {
        switch (scheduler.World.Clock.Tick)
        {
            case 0:
                scheduler.Queue(new AssignStaffCommand(1, "inspect-pump"));
                scheduler.Queue(new QueuePhysicalHaulCommand(new HaulRequest("hdpe-for-workshop", "salvage-hdpe", "depot-workshop", 2, 6)));
                scheduler.Queue(new PlanBuildCommand(new BuildRequest("extension-bench", "workbench", new CellPosition(8, 8), 3, 2, "salvage")));
                break;
            case 5: scheduler.Queue(new SetDoorOpenCommand(new CellPosition(13, 5), false)); break;
            case 18: scheduler.Queue(new SetDoorOpenCommand(new CellPosition(13, 5), true)); break;
        }
    }
}
