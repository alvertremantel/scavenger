using Haldane.SupplyChain;

namespace Scavenger.Sim.Skills;

public sealed record SkillDefinition(string Id, string Name, string Description, string Exercise, string FixtureId);
public sealed record SkillBook(string SkillId, int Level)
{
    public const decimal MassKg = .4m;
    public string MaterialId => $"book/{SkillId}/{Level}";
    public string Name => $"{SkillCatalogue.Find(SkillId)!.Name} · level {Level}";
}
public static class SkillCatalogue
{
    public const int MaxLevel = 10;
    public static decimal StudyRequired(int level) => 8 + 4 * level;
    public static decimal ExperienceRequired(int level) => 12 + 8 * level;
    public static IReadOnlyList<SkillDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new SkillDefinition("hauling", "Logistics", "Carrying, deliveries and depot operations.", "Load balancing and safe lifting", "depot-store"),
        new SkillDefinition("construction", "Construction", "Structures, building and safe access.", "Joint layout and structural mock-up", "workshop-bench"),
        new SkillDefinition("mechanical", "Mechanics", "Repairs, machinery and recovered fittings.", "Mechanical fault tracing", "workshop-bench"),
        new SkillDefinition("electrical", "Electrical work", "Power conduits and installations.", "Unpowered wiring and circuit tracing", "workshop-bench"),
        new SkillDefinition("chemistry", "Chemistry", "Material identification and process operation.", "Dry process sequencing exercise", "clinic-station"),
        new SkillDefinition("medicine", "Medicine", "Fictional clinic care and medical stock handling.", "Care simulation and supply checks", "clinic-station"),
        new SkillDefinition("electronics", "Electronics", "Components, diagnosis and hardware assembly.", "Component identification and connector drills", "workshop-bench"),
        new SkillDefinition("computing", "Computing", "Software installation, datasets and automation.", "Algorithm tracing and data preparation", "workshop-bench"),
        new SkillDefinition("scavenging", "Scavenging", "Efficient field searching and recovery.", "Salvage sorting and recovery planning", "depot-store"),
        new SkillDefinition("navigation", "Navigation", "Field routes and incident assessment.", "Route planning and map orientation", "depot-store"),
        new SkillDefinition("survival", "Field safety", "Managing exposure at recovery sites.", "Protective equipment and hazard drills", "depot-store"),
        new SkillDefinition("agriculture", "Agriculture", "Identifying and recovering growing supplies.", "Growing-bed layout and soil assessment", "depot-store"),
        new SkillDefinition("cooking", "Food handling", "Identifying and recovering dry food.", "Pantry inspection and meal planning", "depot-store"),
        new SkillDefinition("metalworking", "Metalworking", "Metal stock identification and recovery.", "Metal layout and fitting drills", "workshop-bench"),
        new SkillDefinition("tailoring", "Textiles", "Fabric and protective supply handling.", "Pattern layout and repair stitching", "depot-store"),
        new SkillDefinition("research", "Research", "Reading and organizing technical information.", "Source comparison and technical note taking", "depot-store")
    });
    public static SkillDefinition? Find(string id) => All.FirstOrDefault(s => s.Id == id);
    public static SkillBook? Book(string materialId)
    {
        var parts = materialId.Split('/');
        return parts.Length == 3 && parts[0] == "book" && Find(parts[1]) is not null && int.TryParse(parts[2], out int level)
            && level is >= 1 and <= MaxLevel && parts[2] == level.ToString(System.Globalization.CultureInfo.InvariantCulture) ? new(parts[1], level) : null;
    }
    public static bool IsBook(string materialId) => materialId.StartsWith("book/", StringComparison.Ordinal);
    public static void AddMaterials(InventoryModule inventory)
    {
        foreach (var skill in All)
            for (int level = 1; level <= MaxLevel; level++)
            {
                var book = new SkillBook(skill.Id, level);
                if (inventory.FindMaterial(book.MaterialId) is null) inventory.AddMaterial(new(book.MaterialId, "Manual: " + book.Name));
            }
    }
}
