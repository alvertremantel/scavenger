using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;

namespace Scavenger.Sim.Scavenging;

public sealed record RecoverySuggestion(ExpeditionPlan? Plan, string Message);
public sealed record IngredientStatus(string MaterialId, string Name, decimal RequiredKg, decimal AvailableKg, string Quality);

/// <summary>Read-only planning aids. Suggestions obey the same rules as custom player orders.</summary>
public static class OperationsGuide
{
    public static IReadOnlyList<IngredientStatus> Ingredients(World world, string recipeId)
    {
        var inventory = world.Module<InventoryModule>();
        var recipe = world.Module<ProductionModule>().Recipe(recipeId)!;
        return recipe.Inputs.Select(i => new IngredientStatus(i.MaterialId, inventory.FindMaterial(i.MaterialId)!.Name,
            i.QuantityKg, inventory.Batches.Where(b => b.StockpileId is "salvage" or "clinic" && b.MaterialId == i.MaterialId
                && b.Quality.Purity >= i.MinimumPurity && recipe.AcceptsInputQuality(b)).Sum(b => inventory.AvailableKg(b.Id)),
            $"≥{i.MinimumPurity:P1}" + (recipe.InputGrades.TryGetValue(i.MaterialId, out var grade) ? $" / {grade}" : ""))).ToArray();
    }
    public static string? ExplainMedicine(World world, string recipeId)
    {
        if (world.Module<ProductionModule>().Jobs.Any(j => j.Active && j.Plan.RecipeId == recipeId)) return "This medical batch is already queued. Follow its progress below.";
        if (Ingredients(world, recipeId).FirstOrDefault(i => i.AvailableKg < i.RequiredKg) is { } missing)
            return $"Recover {missing.RequiredKg - missing.AvailableKg:0.###} kg more {missing.Name} with the listed quality.";
        return null;
    }
    public static RecoverySuggestion Suggest(World world, string purpose, string id = "suggested-expedition")
    {
        if (!world.TryModule<ScavengingModule>(out var module)) return new(null, "Regional recovery is unavailable in this scenario.");
        var workforce = world.Module<WorkforceModule>();
        // Purpose is a shopping list, never an automatic dispatch or a source of free stock.
        SalvageRequest[] manifest = purpose switch
        {
            "pharmacy" => [new("stock-sealed", .1m), new("excipient", .12m), new("sterile-water", .8m), new("protective-packs", .1m)],
            "sorbent" => [new("filter-sorbent", .1m)],
            "fuel" => [],
            "materials" => [new("steel-fittings", 4), new("timber-rack", 4), new("hdpe-containers", 2)],
            _ => throw new ArgumentException("Unknown recovery purpose.", nameof(purpose))
        };
        var crew = module.Health.Where(h => !h.Sick && WorksiteLogistics.ExplainLabor(world, h.WorkerId, new("hauling", 20)) is null)
            .Select(h => h.WorkerId).Order().ToArray();
        bool Suitable(SalvageDefinition s)
        {
            if (purpose == "fuel") return s.MaterialId == "fuel.diesel" && s.Quality.Purity >= .95m && s.Quality.Grade == "generator fuel";
            if (purpose is not ("pharmacy" or "sorbent")) return true;
            var recipes = world.Module<ProductionModule>().Recipes.Where(r => r.Id is "refine-fever-stock" or "formulate-fever-dose");
            return recipes.All(r => r.Inputs.FirstOrDefault(i => i.MaterialId == s.MaterialId) is not { } requirement
                || s.Quality.Purity >= requirement.MinimumPurity && r.AcceptsInputQuality(new MaterialBatch("preview", s.MaterialId, "salvage", 1, s.Quality)));
        }
        var candidates = module.Locations.Select(l => (Location: l, Manifest: purpose == "fuel"
            ? l.Definition.Salvage.Where(s => Suitable(s) && l.Remaining(s.Id) >= .001m)
                .OrderByDescending(s => l.Remaining(s.Id)).Take(1)
                .Select(s => new SalvageRequest(s.Id, Math.Min(6, l.Remaining(s.Id)))).ToArray()
            : manifest))
            .Where(c => c.Manifest.Length > 0 && c.Manifest.All(r => c.Location.Remaining(r.SalvageId) >= r.QuantityKg
                && c.Location.Definition.Salvage.Any(s => s.Id == r.SalvageId && Suitable(s))))
            .OrderBy(c => c.Location.Definition.TravelTicks).ThenBy(c => c.Location.Definition.Id, StringComparer.Ordinal).ToArray();
        string? firstReason = null;
        // Prefer the smallest qualified team, leaving other operators home.
        for (int size = 1; size <= crew.Length; size++)
            for (int mask = 1; mask < 1 << crew.Length; mask++)
            {
                var team = crew.Where((_, n) => (mask & 1 << n) != 0).ToArray();
                if (team.Length != size) continue;
                foreach (var candidate in candidates)
                {
                    var location = candidate.Location;
                    var plan = new ExpeditionPlan(id, location.Definition.Id, team, candidate.Manifest);
                    if (module.Explain(world, plan) is { } reason) { firstReason ??= reason; continue; }
                    return new(plan, $"{location.Definition.Name} · {string.Join(", ", team.Select(w => workforce.Find(w)!.Name))} · {plan.Manifest.Sum(m => m.QuantityKg):0.##} kg. Review the manifest, then dispatch.");
                }
            }
        return new(null, crew.Length == 0 ? "No healthy, unassigned carriers are available. Finish, cancel or release their current work."
            : candidates.Length == 0 ? "No site still contains this complete supply list. Use a custom manifest to split recovery across sites."
            : firstReason ?? "No available team can recover this supply list.");
    }
}
