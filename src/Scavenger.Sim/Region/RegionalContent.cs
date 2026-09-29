using Haldane.Kernel.Randomness;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Region;

public sealed record LocationType(string Id, string Name, string Symbol, int MinimumCount, int MaximumCount,
    IReadOnlyList<LandUse> Districts, IReadOnlyList<string> Variants);

/// <summary>Former activities explain each object. Variation changes stock, never invents sterile provenance.</summary>
public static class RegionalContent
{
    private static LocationType T(string id, string name, string symbol, int min, int max, LandUse[] districts, params string[] variants)
        => new(id, name, symbol, min, max, Array.AsReadOnly(districts), Array.AsReadOnly(variants));
    public static IReadOnlyList<LocationType> Types { get; } = Array.AsReadOnly(new[] {
        T("homes", "Houses", "H", 14, 22, [LandUse.Residential], "Family homes", "Renovation street", "Flood-damaged homes"),
        T("apartments", "Apartments", "A", 5, 9, [LandUse.TownCenter, LandUse.Residential], "Walk-up block", "Maintenance annex", "Stripped flats"),
        T("pharmacy", "Pharmacy", "P", 2, 4, [LandUse.TownCenter, LandUse.Residential], "Intact dispensary", "Picked-over shop", "Water-damaged stock"),
        T("hardware", "Hardware", "HW", 2, 4, [LandUse.TownCenter, LandUse.Industrial], "Plumbing supplier", "Garden and filters", "Stripped contractor counter"),
        T("laboratory", "Laboratory", "L", 1, 2, [LandUse.Civic, LandUse.Industrial], "Materials testing", "Teaching laboratory", "Collapsed archive"),
        T("repair-shop", "Computer repair", "PC", 2, 4, [LandUse.TownCenter, LandUse.Residential], "Customer repair bench", "Legacy spares", "Cleared retail floor"),
        T("grocery", "Grocery", "G", 3, 6, [LandUse.TownCenter, LandUse.Residential], "Dry-goods store", "Cold-chain failure", "Wholesale pantry"),
        T("clinic", "Clinic", "C", 2, 3, [LandUse.Civic, LandUse.Residential], "Outpatient clinic", "Emergency overflow", "Evacuated practice"),
        T("hospital", "Hospital", "+", 1, 1, [LandUse.Civic], "District hospital", "Isolation ward", "Evacuated hospital"),
        T("school", "School", "S", 2, 4, [LandUse.Civic, LandUse.Residential], "Secondary school", "Primary school", "Technical college"),
        T("library", "Library", "B", 1, 2, [LandUse.Civic, LandUse.TownCenter], "Public reading room", "Digital archive", "Evacuation shelter"),
        T("warehouse", "Warehouse", "WH", 3, 5, [LandUse.Industrial], "Packaging distributor", "Metal-stock warehouse", "Cleared dispatch shed"),
        T("workshop", "Mechanical workshop", "W", 2, 4, [LandUse.Industrial], "Vehicle service yard", "Machine repair shop", "Stripped tool room"),
        T("substation", "Substation", "E", 1, 2, [LandUse.Industrial, LandUse.Farmland], "Distribution yard", "Rural feeder", "Damaged switchhouse"),
        T("fuel-depot", "Fuel depot", "F", 1, 2, [LandUse.Industrial], "Maintenance fuel store", "Empty bulk depot", "Contaminated tank yard"),
        T("waterworks", "Water treatment", "WT", 1, 1, [LandUse.Floodplain], "Municipal filter works", "Pump and settling station", "Flooded treatment plant"),
        T("recycling", "Recycling yard", "R", 1, 2, [LandUse.Industrial], "Sorted municipal recovery", "Mixed scrap yard", "Plastics baler"),
        T("farm", "Farm", "FM", 4, 7, [LandUse.Farmland], "Grain farm", "Equipment barn", "Abandoned smallholding"),
        T("greenhouse", "Greenhouse", "GH", 1, 3, [LandUse.Farmland], "Glass nursery", "Polyhouse nursery", "Damaged growing beds"),
        T("data-center", "Data center", "DC", 1, 1, [LandUse.Industrial], "Small hosting facility", "Cleared rack hall", "Water-damaged server rooms") });
    public static LocationType? Type(string id) => Types.FirstOrDefault(t => t.Id == id);
    public static void AddMaterials(InventoryModule inventory)
    {
        foreach (var (id, name) in new[] { ("food.dry", "Recovered sealed dry food / unassayed"), ("fabric", "Recovered cloth"),
            ("metal.aluminum", "Sorted aluminum"), ("polymer.pvc", "Identified PVC / separate from HDPE"),
            ("rubber", "Identified rubber stock"), ("nutrient.fertilizer", "Labelled fertilizer / not food") })
            inventory.AddMaterial(new MaterialDefinition(id, name));
    }
    private static SalvageDefinition B(string id, string source, string material, decimal kg, decimal purity = .95m,
        string grade = "identified / recovered", string skill = "hauling", int rating = 20, decimal effort = 1,
        decimal integrity = .25m, bool tool = false)
        => new(id, source, material, kg, new(purity, grade), effort, new(skill, rating), integrity, tool);
    private static SalvageDefinition U(string id, string model, string source, ComponentFault fault = ComponentFault.None, bool archive = false)
    {
        var d = IndustrialScenario.ComponentModels().Single(d => d.Id == model);
        return new(id, source, $"item/{id}", d.MassKg, new(1, "unexamined component"), 2, new("electronics", 20), .35m,
            ComponentId: id, ComponentModel: model, Fault: fault, Assets: archive ? IndustrialScenario.ArchiveAssets().ToArray() : []);
    }
    public static IReadOnlyList<SalvageDefinition> Objects(string typeId, int v)
    {
        var old = ScavengingScenario.Region().FirstOrDefault(l => l.Id == typeId);
        if (old is not null)
        {
            // Sealed pharmacy goods and filter supplies only survive in selected variants.
            return old.Salvage.Where(s => typeId switch {
                "pharmacy" when v == 1 => s.Id is not ("stock-sealed" or "sterile-water"),
                "hardware" when v == 2 => s.Id is not ("filter-sorbent" or "diesel-cans" or "copper-pipe"),
                "laboratory" when v == 1 => s.Id is not ("sensor-a" or "controller-a" or "buried-glassware"),
                "laboratory" when v == 2 => s.Id != "clean-glassware",
                "repair-shop" when v == 1 => s.Id is "ram-incompatible" or "cpu-dead" or "ssd-corrupt" or "psu-a" or "fan-a",
                "repair-shop" when v == 2 => s.Id is "board-a" or "ram-a" or "ssd-a" or "usb-archive",
                _ => true }).ToArray();
        }
        return typeId switch {
            "apartments" => [B("bottles", "Communal recycling / bottle glass", "glass.soda-lime", 12), B("laundry", "Laundry rooms / identified detergent jugs", "polymer.hdpe", 7),
                B("linens", "Linen closets / dry cloth", "fabric", 8), B("pipe", "Service riser / copper pipe", "metal.copper", v == 1 ? 9 : 3, .97m, skill: "mechanical", rating: 30, effort: 3, integrity: .6m, tool: true)],
            "grocery" => [B("dry-food", v == 1 ? "Storeroom / surviving sealed dry stock; refrigerated food discarded" : "Shelving / intact dry-food packages", "food.dry", v == 1 ? 3 : 18, .98m, "sealed dry package / unassayed"),
                B("jars", "Bottle returns / ordinary glass jars", "glass.soda-lime", 12), B("pallets", "Loading bay / usable pallet timber", "timber", 16, skill: "construction"), B("film", "Cleaning shelf / identified HDPE bottles", "polymer.hdpe", 4)],
            "clinic" => [B("water", "Treatment cupboard / intact labelled sterile-water packs", "water.sterile", v == 2 ? 1 : 5, 1, "intact sterile label", "medicine"),
                B("protection", "Clean supply cupboard / sealed protective packs", "supply.protection", v == 1 ? .2m : .8m, .99m, "sealed protective consumables"),
                B("glass", "Preparation room / identified borosilicate vessels", "glass.borosilicate", 3, .98m, "sorted / not sterile", "chemistry", 30),
                B("steel", "Furniture store / steel frames", "metal.steel", 8, skill: "mechanical")],
            "hospital" => [B("sterile-water", "Intact supply pallets / labelled sterile water", "water.sterile", v == 2 ? 3 : 16, 1, "intact sterile label", "medicine"),
                B("protective", "Ward supply lockers / dry sealed protective packs", "supply.protection", v == 1 ? .3m : 2, .99m, "sealed protective consumables"),
                B("glassware", "Pathology prep room / borosilicate glass; not sterile", "glass.borosilicate", 16, .98m, "sorted / not sterile", "chemistry", 30, 2, .55m),
                B("frames", "Stores / dismantlable steel bed frames", "metal.steel", 28, skill: "mechanical", effort: 2, tool: true), U("sensor", "sensor-module", "Disconnected equipment / telemetry interface")],
            "school" => [B("tables", "Classrooms / dry timber furniture", "timber", 24, skill: "construction", tool: true),
                B("windows", "Maintenance store / ordinary window panes", "glass.soda-lime", 20, .9m, "window glass", "construction"),
                B("cloth", "Lost property / dry cloth", "fabric", 5), v == 0 ? B("lab-glass", "Teaching chemistry room / identified borosilicate vessels", "glass.borosilicate", 6, .98m, "sorted / not sterile", "chemistry", 30)
                    : U("memory", v == 1 ? "ram-ddr3" : "ram-ddr4-16", "ICT cupboard / loose memory module")],
            "library" => [B("shelves", "Stacks / removable dry timber shelving", "timber", 16, skill: "construction", tool: true), B("radiators", "Service room / steel radiators", "metal.steel", 12, skill: "mechanical", tool: true),
                U("catalog-drive", "usb-16", "Librarian's offline digital collection / USB carrier", archive: v == 1)],
            "warehouse" => [B("pallets", "Dispatch bays / recoverable pallet wood", "timber", 40, skill: "construction"), B("rack-steel", "Racking / sorted structural steel", "metal.steel", v == 1 ? 60 : 24, .98m, skill: "mechanical", effort: 2, tool: true),
                B("containers", "Packaging stock / identified HDPE crates", "polymer.hdpe", v == 0 ? 30 : 6), B("aluminum", "Metal-stock bins / labelled aluminum offcuts", "metal.aluminum", v == 1 ? 18 : 2, skill: "mechanical")],
            "workshop" => [B("steel", "Scrap rack / separated steel offcuts", "metal.steel", 24, .98m, skill: "mechanical"), B("rubber", "Spare stock / identified rubber hose", "rubber", v == 2 ? 1 : 6),
                B("diesel", "Service store / labelled closed diesel can", "fuel.diesel", v == 0 ? 12 : 2, .99m, "generator fuel", "mechanical"), B("copper", "Electrical spares / copper cable", "metal.copper", 5, .98m, skill: "electronics", tool: true)],
            "substation" => [B("busbars", "Isolated switchhouse / copper busbars", "metal.copper", v == 2 ? 4 : 18, .99m, "identified copper", "electronics", 40, 4, .6m, true),
                B("frames", "Fence and switchgear frames / steel", "metal.steel", 24, .97m, skill: "construction", effort: 3, tool: true), B("aluminum", "Spare conductor store / aluminum", "metal.aluminum", 12, skill: "electronics", rating: 30), U("controller", "control-module", "Control cabinet / timed controller")],
            "fuel-depot" => [B("diesel", v == 2 ? "Wet service shed / contaminated diesel cans" : "Service shed / labelled closed diesel cans; bulk tank inaccessible", "fuel.diesel", v == 1 ? 2 : 36, v == 2 ? .8m : .99m, v == 2 ? "water-contaminated fuel" : "generator fuel", "mechanical", 30, 2, .45m),
                B("pipe", "Disused loading fittings / steel", "metal.steel", 18, skill: "mechanical", effort: 3, tool: true), B("containers", "Empty drum store / identified HDPE", "polymer.hdpe", 10)],
            "waterworks" => [B("sorbent", "Filter maintenance store / labelled dry carbon refills", "chemical.sorbent", v == 2 ? 1 : 16, .95m, "identified dry sorbent", "chemistry", 20, 2),
                B("water", "Closed process reservoir / untreated process water; never sterile", "water.clean", 24, .95m, "process grade"), B("pvc", "Maintenance rack / identified PVC pipe; separate from HDPE", "polymer.pvc", 18, skill: "mechanical", tool: true),
                U("sensor", "sensor-module", "Disconnected monitoring cabinet / sensor")],
            "recycling" => [B("hdpe", "Sorted bale / identified soiled HDPE packaging", "polymer.hdpe", v == 2 ? 60 : 24, .94m, "identified / soiled"),
                B("glass", "Bottle bin / mixed soda-lime glass; no laboratory glass", "glass.soda-lime", 40, .9m, "mixed bottles"), B("steel", "Ferrous scrap / sorted steel", "metal.steel", 32, .96m, skill: "mechanical"),
                B("aluminum", "Can bale / identified aluminum", "metal.aluminum", 12, .95m)],
            "farm" => [B("timber", "Dry barn / reusable timber", "timber", 30, skill: "construction", tool: true), B("fertilizer", "Locked feed store / labelled fertilizer bags, separate from feed", "nutrient.fertilizer", v == 2 ? 2 : 12, .98m, "labelled dry fertilizer", "chemistry", 20),
                B("diesel", "Equipment shed / closed labelled diesel cans", "fuel.diesel", v == 1 ? 18 : 4, .99m, "generator fuel", "mechanical"),
                v == 0 ? B("grain", "Store room / sealed dry grain packages", "food.dry", 24, .98m, "sealed dry package / unassayed") : B("steel", "Repair rack / steel fittings", "metal.steel", 12, skill: "mechanical")],
            "greenhouse" => [B("glazing", v == 1 ? "Utility shed / spare ordinary glazing" : "Growing house / ordinary pane glass", "glass.soda-lime", v == 1 ? 4 : 32, .92m, "window glass", "construction", 20, 2, .45m, true),
                B("frames", "Growing racks / steel frames", "metal.steel", 14, skill: "construction", tool: true), B("fertilizer", "Dry maintenance cupboard / labelled fertilizer", "nutrient.fertilizer", 8, .98m, "labelled dry fertilizer", "chemistry"),
                B("pots", "Container store / identified HDPE pots", "polymer.hdpe", v == 1 ? 16 : 6)],
            "data-center" => [B("copper", "Decommissioned cable trays / copper", "metal.copper", 16, .98m, skill: "electronics", rating: 30, effort: 3, tool: true),
                B("racks", "Rack hall / steel frames", "metal.steel", 32, skill: "mechanical", tool: true), U("storage", "ssd-sata", "Discard tray / SATA media", v == 2 ? ComponentFault.MediaCorruption : ComponentFault.None),
                U("psu", "psu-550", "Office maintenance bench / desktop PSU; server PSUs not compatible"),
                U("memory", "ram-ddr3", "Office spares / legacy DDR3; rack memory not compatible")],
            _ => throw new ArgumentException("Unknown location type.", nameof(typeId)) };
    }
    public static LocationDefinition Instantiate(LocationType type, string id, int variant, RegionPoint parcel, int travelTicks,
        RegionCell cell, Rng rng, bool starter = false)
    {
        decimal integrity = starter ? .82m : rng.NextInt(42, 94) / 100m;
        bool damp = cell.District == LandUse.Floodplain || variant == 2 && rng.Chance(.6);
        if (damp && !starter) integrity = Math.Max(.3m, integrity - .12m);
        var objects = new List<SalvageDefinition>();
        foreach (var source in Objects(type.Id, variant))
        {
            // Missing objects reflect stripping; the experimental starter dispensary and filter shelf survive.
            if (!starter && rng.Chance(.18) && objects.Count > 0) continue;
            var quality = source.Quality;
            bool sealedStock = source.MaterialId.StartsWith("medicine.", StringComparison.Ordinal) || source.MaterialId is "water.sterile" or "supply.protection" or "chemical.sorbent" or "food.dry" or "nutrient.fertilizer";
            if (damp && !sealedStock && source.ComponentId is null)
                quality = new(Math.Max(.1m, quality.Purity - .03m), quality.Grade + " / damp", new Dictionary<string, decimal>(quality.Contaminants) { ["moisture / unassayed residue"] = .03m });
            if (source.ComponentId is { } component)
            {
                string unique = $"{id}/{component}";
                var assets = (source.Assets ?? []).Select(a => a with { Id = $"{id}/{a.Id}", SourceMediaId = unique }).ToArray();
                var fault = source.Fault;
                if (!starter && fault == ComponentFault.None && rng.Chance(damp ? .4 : .12)) fault = source.ComponentModel!.StartsWith("ssd", StringComparison.Ordinal) || source.ComponentModel == "usb-16" ? ComponentFault.MediaCorruption : ComponentFault.DeadSilicon;
                objects.Add(source with { ComponentId = unique, MaterialId = $"item/{unique}", Assets = assets, ComponentCondition = rng.NextInt(45, 96) / 100m, Fault = fault });
            }
            else objects.Add(source with { InitialKg = decimal.Round(source.InitialKg * rng.NextInt(55, 151) / 100m, 3), Quality = quality });
        }
        string[] names = ["Morrow", "Harker", "Willow", "Ash", "Vector", "Mill", "Orchard", "Cedar", "County", "Birch", "Ridge", "Foundry"];
        string name = $"{names[rng.NextInt(names.Length)]} {type.Name} · {id[(id.LastIndexOf('-') + 1)..]}";
        decimal pressure = cell.District switch { LandUse.TownCenter => .35m, LandUse.Residential => .3m, LandUse.Civic => .2m, _ => .12m };
        return new(id, name, $"{type.Variants[variant]}. {cell.District} district. {(damp ? "Moisture damage is visible." : "Dry pockets survive within damaged buildings.")} Object inventory records recoverable stock only; no functioning cold chain or ready-to-use complete computers are assumed.",
            parcel.X, parcel.Y, travelTicks, integrity, rng.NextInt(5, 16) / 1000m, pressure + rng.NextInt(0, 11) / 100m,
            rng.NextInt(2, 16) / 100m, type.Id is "hospital" or "clinic" ? .022m : rng.NextInt(4, 17) / 1000m, objects);
    }
}
