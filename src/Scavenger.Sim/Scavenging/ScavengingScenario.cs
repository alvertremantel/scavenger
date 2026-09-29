using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Scavenging;

public static class ScavengingScenario
{
    public static (World World, Scheduler Scheduler, EventCatalogue Catalogue) Create(ulong seed = 20260929)
    {
        var (world, _, _) = IndustrialScenario.Create(seed, seedComputers: false, leanSupplies: true);
        var inventory = world.Module<InventoryModule>();
        foreach (var (id, name) in new[] {
            ("glass.soda-lime", "Recovered bottle/window glass"), ("glass.borosilicate", "Identified laboratory borosilicate"),
            ("tool.prybar", "Reusable salvage prybar"), ("supply.protection", "Disposable protective supplies"),
            ("chemical.sorbent", "Identified carbon sorbent"), ("medicine.precursor", "Fictional fever treatment stock"),
            ("medicine.refined", "Refined fictional treatment stock"), ("medicine.excipient", "Sealed formulation excipient"),
            ("water.sterile", "Intact labelled sterile water"), ("medicine.fever-dose", "Fictional fever treatment"),
            ("waste.medical", "Captured clinic/process waste") }) inventory.AddMaterial(new MaterialDefinition(id, name));
        inventory.AddBatch(new("starting-prybar", "tool.prybar", "salvage", 1, new(1, "serviceable tool")));
        inventory.AddBatch(new("starting-protection", "supply.protection", "salvage", 1, new(.99m, "sealed protective consumables")));
        var scavenging = new ScavengingModule(); world.Register(scavenging);
        foreach (var location in Region()) scavenging.AddLocation(location);
        foreach (var resident in world.Module<Haldane.CareerSim.Work.WorkforceModule>().Staff) scavenging.AddResident(resident.Id, sick: resident.Id == 1);
        // A returned sick mechanic makes pharmacy/hardware/clinic logistics an immediate decision.
        world.Module<Haldane.CareerSim.Work.WorkforceModule>().Find(1)!.Present = false;
        foreach (var recipe in MedicineRecipes()) world.Module<ProductionModule>().AddRecipe(recipe);
        PhysicalSave.ValidateWorld(world);
        return (world, new Scheduler(world, Systems()), CreateCatalogue());
    }
    public static IEnumerable<ISystem> Systems() => IndustrialScenario.Systems().Append(new ScavengingSystem());
    public static EventCatalogue CreateCatalogue()
    { var c = IndustrialScenario.CreateCatalogue(); ScavengingEvents.RegisterAll(c); return c; }
    private static SalvageDefinition Bulk(string id, string source, string material, decimal kg, decimal purity,
        string grade, string skill = "hauling", int rating = 20, decimal effort = .5m, decimal integrity = .2m,
        bool tool = false, IReadOnlyDictionary<string, decimal>? contaminants = null)
        => new(id, source, material, kg, new(purity, grade, contaminants), effort, new(skill, rating), integrity, tool);
    public static IEnumerable<LocationDefinition> Region()
    {
        yield return new("pharmacy", "Morrow Pharmacy", "Front shelves were stripped. Sealed dispensing stock remains in drawers; the flooded back room contains damaged alternatives. No functioning cold chain remains.",
            2, 1, 8, .82m, .008m, .15m, .08m, .012m, [
            Bulk("stock-sealed", "Dispensary / intact fictional treatment bottles", "medicine.precursor", .6m, .98m, "sealed fictional stock", "chemistry", 30, 4),
            Bulk("excipient", "Dispensary / intact formulation sachets", "medicine.excipient", 1.2m, .999m, "sealed formulation stock", "medicine", 20, 1),
            Bulk("sterile-water", "Treatment shelf / intact sterile-water packs", "water.sterile", 4, 1, "intact sterile label", "medicine", 20),
            Bulk("protective-packs", "Supply cupboard / dry protective packs", "supply.protection", .4m, .99m, "sealed protective consumables"),
            Bulk("damaged-stock", "Flooded back room / opened treatment bottles", "medicine.precursor", 2, .86m, "wet / unidentified contamination", "chemistry", 30, 3, .6m, true, new Dictionary<string, decimal> { ["unknown"] = .14m }),
            Bulk("bottles", "Waste counter / empty dispensing bottles", "glass.soda-lime", 2, .92m, "mixed bottle glass", effort: 1) ]);
        yield return new("hardware", "Harker Hardware", "A useful materials stop: plumbing fittings, filter stock, shelving and identified containers. Industrial shelves provide no medicine or sterile inputs.",
            4, 1, 12, .72m, .012m, .25m, .3m, .008m, [
            Bulk("filter-sorbent", "Water-filter aisle / labelled dry carbon refills", "chemical.sorbent", 3, .95m, "identified dry sorbent", "chemistry", 20, 1),
            Bulk("hdpe-containers", "Cleaning aisle / empty identified HDPE containers", "polymer.hdpe", 8, .94m, "identified / soiled", contaminants: new Dictionary<string, decimal> { ["soil"] = .04m }),
            Bulk("steel-fittings", "Plumbing aisle / sorted steel fittings", "metal.steel", 12, .99m, "sorted steel", "mechanical", 20, .8m),
            Bulk("copper-pipe", "Locked plumbing rack / recoverable copper pipe", "metal.copper", 4, .98m, "identified pipe", "mechanical", 30, 2, .5m, true),
            Bulk("timber-rack", "Rear shelving / reusable dry timber", "timber", 16, .95m, "salvaged dry timber", "construction", 20, .5m, .55m, true),
            Bulk("diesel-cans", "Maintenance outbuilding / labelled closed diesel cans", "fuel.diesel", 10, .99m, "generator fuel", "mechanical", 20, 1, .4m),
            Bulk("prybar-rack", "Tool rack / serviceable prybar", "tool.prybar", 1, 1, "serviceable tool") ]);
        yield return new("laboratory", "County Materials Laboratory", "Glass is concentrated here because of the site's former work. Borosilicate benches, bottle stores and instrument control boxes have different recovery requirements. The upper archive is inaccessible.",
            5, 4, 20, .58m, .018m, .1m, .1m, .025m, [
            Bulk("clean-glassware", "Dry preparation bench / identified borosilicate vessels", "glass.borosilicate", 14, .99m, "sorted / not sterile", "chemistry", 30, 1.5m, .4m),
            Bulk("window-glass", "Ground-floor windows / soda-lime fragments", "glass.soda-lime", 18, .90m, "window fragments", "construction", 20, 2, .2m, true),
            Bulk("buried-glassware", "Collapsed store room / buried borosilicate vessels", "glass.borosilicate", 20, .88m, "mixed residues", "chemistry", 40, 3, .8m, true, new Dictionary<string, decimal> { ["unknown residue"] = .12m }),
            Bulk("steel-stands", "Bench clamps / recoverable steel", "metal.steel", 6, .97m, "sorted fixtures", "mechanical", 30, 2, .4m, true),
            Unit("sensor-a", "sensor-module", "Instrument control cabinet / telemetry interface"),
            Unit("controller-a", "control-module", "Timed oven cabinet / controller") ]);
        yield return new("homes", "Willow Terrace", "An ordinary street with modest domestic yields. Bottle glass and containers are easier than removing pipework from decaying houses. It cannot substitute for a laboratory or a pharmacy.",
            -2, 2, 6, .76m, .01m, .35m, .4m, .006m, [
            Bulk("bottle-glass", "Kitchen recycling / mixed empty bottles", "glass.soda-lime", 3, .94m, "mixed bottles"),
            Bulk("house-hdpe", "Utility closets / identified detergent containers", "polymer.hdpe", 2, .94m, "identified / soiled"),
            Bulk("shelf-timber", "Ground-floor shelving / usable timber", "timber", 5, .93m, "dry shelving", "construction", 20, 1, .4m, true),
            Bulk("house-copper", "Service cupboard / recoverable copper pipe", "metal.copper", .7m, .97m, "identified pipe", "mechanical", 30, 3, .65m, true) ]);
        yield return new("repair-shop", "Vector Computer Repair", "Customer repairs, parts bins and a technician's offline archive survived. Individual faults and incompatible spares still need diagnosis; nobody left a working complete PC ready to use.",
            -4, 4, 18, .68m, .012m, .2m, .02m, .01m, [
            Unit("board-a", "board-am4", "Repair bench / AM4 motherboard"), Unit("cpu-a", "cpu-am4", "Antistatic tray / AM4 processor"),
            Unit("ram-a", "ram-ddr4-16", "Memory bin / first DDR4 module"), Unit("ram-b", "ram-ddr4-16", "Memory bin / second DDR4 module"),
            Unit("ssd-a", "ssd-nvme", "Storage tray / NVMe drive"), Unit("psu-a", "psu-550", "Test rack / PSU with cables"),
            Unit("fan-a", "cooler-am4", "Parts bin / cooler with broken fan clip", ComponentFault.BrokenFanClip),
            Unit("gpu-3060", "rtx3060-12", "Locked customer shelf / 12 GB RTX 3060", integrity: .65m, tool: true),
            Unit("usb-archive", "usb-16", "Technician's drawer / offline USB archive", skill: "computing", rating: 20),
            Unit("ram-incompatible", "ram-ddr3", "Older memory bin / DDR3 module"),
            Unit("cpu-dead", "cpu-am4", "Reject tray / failed processor", ComponentFault.DeadSilicon),
            Unit("ssd-corrupt", "ssd-sata", "Reject tray / damaged SATA media", ComponentFault.MediaCorruption) ]);
    }
    private static SalvageDefinition Unit(string id, string model, string source, ComponentFault fault = ComponentFault.None,
        decimal integrity = .35m, bool tool = false, string skill = "electronics", int rating = 20)
    {
        var d = IndustrialScenario.ComponentModels().Single(d => d.Id == model);
        return new(id, source, $"item/{id}", d.MassKg, new(1, "unexamined component"), 2, new(skill, rating), integrity, tool, id, model, fault, Assets: id == "usb-archive" ? IndustrialScenario.ArchiveAssets().ToArray() : []);
    }
    public static IEnumerable<RecipeDefinition> MedicineRecipes()
    {
        yield return new("refine-fever-stock", "Refine fictional fever stock with sorbent", "medical", new("chemistry", 30),
            [new("medicine.precursor", .1m, .97m), new("chemical.sorbent", .1m, .95m)],
            [new("medicine.refined", .08m, false, new(.999m, "fictional refined stock")),
             new("waste.medical", .12m, true, new(.5m, "spent sorbent / captured rejects"))], 4, .05m, inputGrades: new Dictionary<string, string> { ["medicine.precursor"] = "sealed fictional stock", ["chemical.sorbent"] = "identified dry sorbent" }, rejectContaminatedInputs: true);
        yield return new("formulate-fever-dose", "Formulate fictional clinic treatment", "medical", new("medicine", 30),
            [new("medicine.refined", .08m, .999m), new("medicine.excipient", .12m, .999m), new("water.sterile", .8m, 1)],
            [new("medicine.fever-dose", 1, false, new(.999m, "fictional clinic release"))], 4, .05m, inputGrades: new Dictionary<string, string> { ["medicine.refined"] = "fictional refined stock", ["medicine.excipient"] = "sealed formulation stock", ["water.sterile"] = "intact sterile label" }, rejectContaminatedInputs: true);
    }
    public static ProductionPlan MedicinePlan(string id, string recipe)
        => new(id, recipe, "clinic-station", 4, 2, ["salvage", "clinic"], "clinic", "waste");
}
