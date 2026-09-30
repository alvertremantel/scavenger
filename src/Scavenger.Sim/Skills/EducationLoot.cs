using Haldane.Kernel.Randomness;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Skills;

public static class EducationLoot
{
    public static IReadOnlyList<SalvageDefinition> Roll(string locationType, Rng rng)
    {
        bool education = locationType is "school" or "library";
        bool residence = locationType is "homes" or "apartments";
        string[] subjects = locationType switch
        {
            "workshop" => ["mechanical", "metalworking", "electrical"],
            "hardware" => ["construction", "electrical", "mechanical"],
            "warehouse" => ["hauling", "metalworking"],
            "laboratory" => ["chemistry", "research", "electronics"],
            "pharmacy" => ["chemistry", "medicine"],
            "clinic" or "hospital" => ["medicine", "chemistry"],
            "repair-shop" or "data-center" => ["electronics", "computing"],
            "substation" => ["electrical", "electronics"],
            "fuel-depot" or "waterworks" => ["mechanical", "chemistry"],
            "farm" or "greenhouse" => ["agriculture", "survival", "mechanical"],
            "grocery" => ["cooking", "hauling"],
            "recycling" => ["scavenging", "metalworking", "hauling"],
            _ => SkillCatalogue.All.Select(s => s.Id).ToArray()
        };
        if (!rng.Chance(education ? .9 : residence ? .7 : .6)) return [];
        int count = education ? rng.NextInt(4, 9) : rng.NextInt(1, 4);
        var books = new List<SalvageDefinition>();
        for (int n = 0; n < count; n++)
        {
            string skill = rng.Chance(.75) ? subjects[rng.NextInt(subjects.Length)] : SkillCatalogue.All[rng.NextInt(SkillCatalogue.All.Count)].Id;
            double roll = rng.NextDouble();
            int level = roll < .6 ? rng.NextInt(1, 5) : roll < .9 ? rng.NextInt(5, 8) : rng.NextInt(8, 11);
            var book = new SkillBook(skill, level);
            books.Add(new($"manual-{n + 1}-{skill}-{level}", $"Dry shelf / readable manual: {book.Name}", book.MaterialId,
                SkillBook.MassKg, new(1, "readable educational volume"), 2, new("hauling", 20), .15m));
        }
        return books;
    }
    public static LocationDefinition Add(LocationDefinition location, string type, Rng rng)
        => location with { Salvage = location.Salvage.Concat(Roll(type, rng)).ToArray() };
}
