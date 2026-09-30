using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;
using Haldane.Kernel.Time;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public static class IndustrialScenario
{
    public static readonly IReadOnlyList<string> StarterKit = Array.AsReadOnly(new[]
        { "board-a", "cpu-a", "ram-a", "ram-b", "ssd-a", "psu-a", "gpu-3060", "fan-a" });
    public static (World World, Scheduler Scheduler, EventCatalogue Catalogue) Create(ulong seed = 20260929, bool seedComputers = true, bool leanSupplies = false)
    {
        var (physical, _, _) = PhysicalCompoundScenario.Create(seed, leanSupplies);
        var world = new World(new Clock(new TimeConfig(96)), physical.Rng);
        foreach (var module in physical.Modules) world.Register(module);
        var roster = new WorkforceModule();
        foreach (var worker in world.Module<WorkforceModule>().Staff)
        {
            var skills = worker.Skills.Ratings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
            skills["electronics"] = worker.Id == 1 ? 60 : worker.Id == 3 ? 45 : 20;
            skills["computing"] = worker.Id == 1 ? 65 : worker.Id == 4 ? 45 : 20;
            roster.Hire(new StaffMember { Id = worker.Id, Name = worker.Name, Skill = worker.Skill, DailyWage = worker.DailyWage, Present = worker.Present, Skills = new SkillProfile(skills) });
        }
        world.Register(roster);
        var inventory = world.Module<InventoryModule>(); var compound = world.Module<CompoundModule>(); var utilities = world.Module<UtilitiesModule>();
        foreach (var (id, name) in new[] { ("water.clean", "Process water"), ("fuel.diesel", "Generator diesel"),
            ("polymer.hdpe.washed", "Washed HDPE"), ("polymer.hdpe.dry", "Dried HDPE"), ("polymer.hdpe.flakes", "HDPE flakes"),
            ("part.polymer", "Noncritical moulded polymer clips"), ("waste.washwater", "Wash effluent"), ("waste.condensate", "Captured moisture and trace residue"),
            ("waste.polymer-fines", "HDPE fines"), ("waste.polymer-offcuts", "HDPE offcuts") }) inventory.AddMaterial(new MaterialDefinition(id, name));
        inventory.AddStockpile(new Stockpile("waste", "Waste and byproduct store", 200, new CellPosition(6, 14)))
            .AddStockpile(new Stockpile("electronics-cache", "Unexamined electronics cache", 50, new CellPosition(2, 12)))
            .AddStockpile(new Stockpile("generator-tank", "Generator fuel tank", 100, new CellPosition(7, 10)));
        world.Module<LogisticsModule>().AddRoute(new TransportRoute("refuel-route", "salvage", "generator-tank", 1, 6,
            new("hauling", 20), usesWorldMovement: true));
        inventory.AddBatch(new MaterialBatch("process-water", "water.clean", "salvage", leanSupplies ? 6 : 20, new BatchQuality(1, "process water / not medical")))
            .AddBatch(new MaterialBatch("depot-diesel", "fuel.diesel", "salvage", leanSupplies ? 8 : 40, new BatchQuality(.99m, "generator fuel")))
            .AddBatch(new MaterialBatch("tank-diesel", "fuel.diesel", "generator-tank", leanSupplies ? 12 : 80, new BatchQuality(.99m, "generator fuel")));
        foreach (var definition in new[]
        {
            new BuildingDefinition("washer", "Polymer wash/sort station", BuildingKind.Workbench, blocksMovement: true, buildable: false,
                capabilities: ["polymer-wash"], repairMaterials: [new("metal.steel", .2m), new("part.polymer", .1m, .97m)], utilityPriority: 5),
            new BuildingDefinition("dryer", "Condensing polymer dryer", BuildingKind.Workbench, blocksMovement: true, buildable: false,
                capabilities: ["polymer-dry"], repairMaterials: [new("metal.steel", .2m), new("part.polymer", .1m, .97m)], utilityPriority: 5),
            new BuildingDefinition("shredder", "Polymer shredder", BuildingKind.Workbench, blocksMovement: true, buildable: false,
                capabilities: ["polymer-shred"], repairMaterials: [new("metal.steel", .2m), new("part.polymer", .1m, .97m)], utilityPriority: 5),
            new BuildingDefinition("moulder", "Hand-operated polymer moulder", BuildingKind.Workbench, blocksMovement: true, buildable: false,
                capabilities: ["polymer-mould"], repairMaterials: [new("metal.steel", .2m), new("part.polymer", .1m, .97m)], utilityPriority: 5),
            new BuildingDefinition("electronics-bench", "Electronics service bench", BuildingKind.Workbench, width: 2, blocksMovement: true, buildable: false,
                capabilities: ["electronics"], repairMaterials: [new("metal.steel", .5m)], utilityPriority: 4),
            new BuildingDefinition("computer", "Scavenged computing station", BuildingKind.Computer, blocksMovement: true, buildable: false,
                capabilities: ["computing"], repairMaterials: [new("metal.steel", .2m), new("part.polymer", .05m, .97m)], utilityPriority: 20),
            new BuildingDefinition("battery", "Recovered battery/inverter bank", BuildingKind.Battery, blocksMovement: true, buildable: false, utilityPriority: 0),
            new BuildingDefinition("data-cable", "Data connection", BuildingKind.DataCable, materials: [new("metal.copper", .05m, .95m), new("polymer.hdpe", .02m)], requiredSkill: new("electrical", 20), effortRequired: 1),
        }) compound.AddDefinition(definition);
        foreach (var fixture in new[]
        {
            new BuildingSnapshot("washer", "washer", new(14, 2), new(14, 3), 1, []),
            new BuildingSnapshot("dryer", "dryer", new(14, 6), new(14, 7), 1, []),
            new BuildingSnapshot("shredder", "shredder", new(20, 5), new(20, 6), 1, []),
            new BuildingSnapshot("moulder", "moulder", new(18, 6), new(18, 7), 1, []),
            new BuildingSnapshot("electronics-bench", "electronics-bench", new(15, 6), new(15, 7), 1, []),
            new BuildingSnapshot("computer-rig", "computer", new(20, 2), new(20, 3), 1, []),
            new BuildingSnapshot("battery-bank", "battery", new(17, 2), new(17, 3), 1, []),
        }) compound.AddExistingBuilding(fixture);
        AddLine(utilities, UtilityKind.Power, new(14, 3), new(20, 3));
        AddLine(utilities, UtilityKind.Power, new(14, 2), new(14, 6));
        AddLine(utilities, UtilityKind.Power, new(20, 2), new(20, 5));
        AddLine(utilities, UtilityKind.Power, new(18, 3), new(18, 6));
        AddLine(utilities, UtilityKind.Power, new(15, 3), new(15, 6));
        AddLine(utilities, UtilityKind.Power, new(17, 2), new(17, 3));
        AddLine(utilities, UtilityKind.Water, new(14, 2), new(14, 3));
        AddLine(utilities, UtilityKind.Water, new(18, 3), new(18, 6));
        AddLine(utilities, UtilityKind.Data, new(20, 2), new(20, 6));
        AddLine(utilities, UtilityKind.Data, new(14, 6), new(20, 6));
        AddLine(utilities, UtilityKind.Data, new(14, 2), new(14, 6));
        var production = new ProductionModule(); foreach (var recipe in Recipes()) production.AddRecipe(recipe);
        var computing = new ComputingModule(); var energy = new EnergyModule();
        world.Register(production).Register(computing).Register(energy).Register(new EquipmentModule()).Register(new OrdersModule());
        energy.AddGenerator(new GeneratorPlan("generator", "generator-tank", "fuel.diesel", .3m));
        energy.AddBattery(new BatteryPlan("battery-bank", 2, 1.5m, .5m, 1));
        foreach (var building in compound.Buildings)
        {
            decimal idle = building.Id switch { "well-pump" => .15m, "clinic-station" => .05m, "workshop-bench" => .03m,
                "washer" or "dryer" or "shredder" or "moulder" or "electronics-bench" => .01m, _ => 0 };
            decimal water = building.Id == "clinic-station" ? 1 : 0;
            energy.AddIdleLoad(new OperatingLoad(building.Id, idle, water));
        }
        foreach (var definition in ComponentModels()) computing.AddDefinition(definition);
        computing.AddComputer("rig-1", "computer-rig");
        if (seedComputers) foreach (var (id, model, fault) in new[]
        {
            ("board-a", "board-am4", ComponentFault.None), ("cpu-a", "cpu-am4", ComponentFault.None),
            ("ram-a", "ram-ddr4-16", ComponentFault.None), ("ram-b", "ram-ddr4-16", ComponentFault.None),
            ("ssd-a", "ssd-nvme", ComponentFault.None), ("psu-a", "psu-550", ComponentFault.None),
            ("gpu-3060", "rtx3060-12", ComponentFault.None), ("fan-a", "cooler-am4", ComponentFault.BrokenFanClip),
            ("usb-archive", "usb-16", ComponentFault.None), ("sensor-a", "sensor-module", ComponentFault.None),
            ("controller-a", "control-module", ComponentFault.None), ("ram-incompatible", "ram-ddr3", ComponentFault.None),
            ("gpu-3060-8", "rtx3060-8", ComponentFault.None), ("cpu-dead", "cpu-am4", ComponentFault.DeadSilicon),
            ("ssd-corrupt", "ssd-sata", ComponentFault.MediaCorruption),
        }) computing.AddComponent(world, id, model, "electronics-cache", fault: fault);
        if (seedComputers) foreach (var asset in ArchiveAssets()) computing.AddAsset(asset);
        world.Module<LogisticsModule>().AddRoute(new TransportRoute("refuel-generator", "salvage", "generator-tank", 1, 6, new("hauling", 20), usesWorldMovement: true));
        UtilitySystem.Recalculate(world); // Budgets are prepared at the first actual interval; no fuel is spent at world creation.
        PhysicalSave.ValidateWorld(world);
        return (world, new Scheduler(world, Systems()), CreateCatalogue());
    }
    public static IEnumerable<ISystem> Systems() => [new CalendarSystem(), new ConstructionSystem(), new StandingOrderSystem(),
        new IndustryPreparationSystem(), new UtilitySystem(), new ProductionSystem(), new ComputingSystem(), new ProjectTravelSystem(),
        new WorkSystem(new CompoundWorkContext()), new LogisticsSystem(new CompoundTransportDriver()), new EnergyAccountingSystem()];
    public static EventCatalogue CreateCatalogue()
    { var catalogue = PhysicalCompoundScenario.CreateCatalogue(); IndustryEvents.RegisterAll(catalogue); return catalogue; }
    public static IEnumerable<RecipeDefinition> Recipes()
    {
        yield return new("wash-hdpe", "Wash/sort identified HDPE", "polymer-wash", new("chemistry", 30),
            [new("polymer.hdpe", 1, .94m), new("water.clean", 2, 1)],
            [new("polymer.hdpe.washed", .95m, false, new BatchQuality(.97m, "washed / wet / untested", new Dictionary<string, decimal> { ["water"] = .02m, ["soil"] = .01m })),
             new("waste.washwater", 2.05m, true, new BatchQuality(.95m, "contaminated wash effluent"))], 4, .2m);
        yield return new("dry-hdpe", "Dry HDPE / capture moisture", "polymer-dry", new("mechanical", 20), [new("polymer.hdpe.washed", .95m, .97m)],
            [new("polymer.hdpe.dry", .93m, false, new BatchQuality(.9908m, "dried / untested", new Dictionary<string, decimal> { ["soil"] = .0092m })),
             new("waste.condensate", .02m, true, new BatchQuality(.98m, "captured moisture / untested"))], 8, .8m, automatable: true);
        yield return new("shred-hdpe", "Shred dried HDPE", "polymer-shred", new("mechanical", 20), [new("polymer.hdpe.dry", .93m, .99m)],
            [new("polymer.hdpe.flakes", .90m, false, PreserveMaterialId: "polymer.hdpe.dry"), new("waste.polymer-fines", .03m, true, PreserveMaterialId: "polymer.hdpe.dry")], 4, 1.2m);
        yield return new("mould-clips", "Hand mould repair clips", "polymer-mould", new("mechanical", 30), [new("polymer.hdpe.flakes", .90m, .99m)],
            [new("part.polymer", .80m, false, PreserveMaterialId: "polymer.hdpe.flakes"), new("waste.polymer-offcuts", .10m, true, PreserveMaterialId: "polymer.hdpe.flakes")], 6, .8m);
    }
    public static IEnumerable<ComponentDefinition> ComponentModels()
    {
        yield return new("board-am4", "AM4 / DDR4 board", ComponentKind.Motherboard, 1.2m, "AM4", "DDR4", "NVMe", 2);
        yield return new("cpu-am4", "AM4 processor", ComponentKind.Processor, .05m, socket: "AM4", cpuWatts: 65, computeRating: 70);
        yield return new("ram-ddr4-16", "16 GiB DDR4 DIMM", ComponentKind.Memory, .06m, memoryType: "DDR4", ramGiB: 16);
        yield return new("ram-ddr3", "8 GiB DDR3 DIMM", ComponentKind.Memory, .05m, memoryType: "DDR3", ramGiB: 8);
        yield return new("ssd-nvme", "128 GiB NVMe SSD", ComponentKind.Storage, .07m, storageInterface: "NVMe", storageGiB: 128);
        yield return new("ssd-sata", "Damaged SATA drive", ComponentKind.Storage, .08m, storageInterface: "SATA", storageGiB: 64);
        yield return new("psu-550", "550 W PSU / PCIe power", ComponentKind.PowerSupply, 2, psuWatts: 550, pciePower: true, efficiency: .85m);
        yield return new("rtx3060-12", "RTX 3060 / 12 GB", ComponentKind.GraphicsCard, .7m, vramGiB: 12, gpuWatts: 170, computeRating: 70);
        yield return new("rtx3060-8", "RTX 3060 / 8 GB", ComponentKind.GraphicsCard, .7m, vramGiB: 8, gpuWatts: 170, computeRating: 60);
        yield return new("cooler-am4", "AM4 cooler", ComponentKind.Cooler, .4m, socket: "AM4", coolingWatts: 125);
        yield return new("usb-16", "16 GiB recovered USB archive", ComponentKind.PortableMedia, .03m, storageGiB: 16);
        yield return new("sensor-module", "Recovered telemetry interface", ComponentKind.Sensor, .05m);
        yield return new("control-module", "Recovered timed-equipment controller", ComponentKind.Controller, .05m);
    }
    public static IEnumerable<DigitalAsset> ArchiveAssets()
    {
        yield return new("offline-os", "Offline x86 Linux image", 4, "usb-archive", 1, ["os"], []);
        yield return new("python-runtime", "Offline runtime/dependency bundle", 2, "usb-archive", 1, ["python"], ["os"]);
        yield return new("gpu-driver", "Ampere-compatible GPU runtime", 1, "usb-archive", 1, ["gpu-runtime"], ["os"]);
        yield return new("quant-runtime", "Quantization/adapter toolkit", 1, "usb-archive", 1, ["quant-runtime"], ["python", "gpu-runtime"]);
        yield return new("model-4b", "Recovered 4B Q4 weights / authored 3 GiB profile", 3, "usb-archive", 1, ["model-4b"], ["quant-runtime"]);
        yield return new("model-tokenizer", "Matching tokenizer and model configuration", .01m, "usb-archive", 1, ["tokenizer"], ["python"]);
        yield return new("training-textbook", "Textbook: dataset preparation and adapters", .05m, "usb-archive", 1, ["training-manual"], ["os"], "adapter-training");
        yield return new("telemetry-tools", "Telemetry and archive tools", .01m, "usb-archive", 1, ["telemetry"], ["python"]);
        yield return new("rule-control", "Verified timer/rule-control source", .005m, "usb-archive", 1, ["rule-control"], ["python", "telemetry"]);
        yield return new("corrupt-runtime", "Corrupt runtime archive", .5m, "usb-archive", .4m, ["quant-runtime"], ["python"]);
    }
    public static ProductionPlan ProcessPlan(string id, string recipeId, bool automatic = false)
    {
        string station = recipeId switch { "wash-hdpe" => "washer", "dry-hdpe" => "dryer", "shred-hdpe" => "shredder", "mould-clips" => "moulder", _ => throw new ArgumentException("Unknown recipe.", nameof(recipeId)) };
        return new ProductionPlan(id, recipeId, station, automatic ? null : recipeId == "wash-hdpe" ? 4 : 3, 2, ["workshop", "salvage"], "workshop", "waste", automatic);
    }
    public static void QueueRecoveryOrders(Scheduler scheduler)
    {
        var orders = scheduler.World.Module<OrdersModule>();
        foreach (var recipe in scheduler.World.Module<ProductionModule>().Recipes.Where(r => r.Id is "wash-hdpe" or "dry-hdpe" or "shred-hdpe" or "mould-clips"))
        {
            if (orders.Orders.Any(o => o.Plan.Template.RecipeId == recipe.Id)) continue;
            var primary = recipe.Outputs.First(o => !o.Waste);
            decimal target = recipe.Id == "mould-clips" ? 1.6m : primary.QuantityKg;
            scheduler.Queue(new AddStockOrderCommand(new StockOrderPlan(recipe.Id, ProcessPlan("template", recipe.Id), primary.MaterialId, target)));
        }
    }
    private static void AddLine(UtilitiesModule utilities, UtilityKind kind, CellPosition start, CellPosition end)
    {
        if (start.X != end.X && start.Y != end.Y) throw new ArgumentException("Conduit runs are cardinal.");
        int dx = Math.Sign(end.X - start.X), dy = Math.Sign(end.Y - start.Y);
        for (var cell = start; ; cell = new(cell.X + dx, cell.Y + dy))
        { if (!utilities.HasNode(kind, cell)) utilities.AddNode(kind, cell); if (cell == end) break; }
    }
}
