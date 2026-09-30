using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;
using Scavenger.Sim.Skills;

namespace Scavenger.Sim.Physical;

public sealed record ActivityRow(string Id, string Kind, bool Active, string Status, decimal Effort, decimal Required, string? Reason);
public static class ActivityReport
{
    public static IReadOnlyList<ActivityRow> Capture(World world)
    {
        var rows = new List<ActivityRow>();
        rows.AddRange(world.Module<ConstructionModule>().Jobs.Select(j => new ActivityRow(j.Request.Id, "build", j.Active, j.Status.ToString(), j.EffortCompleted, world.Module<CompoundModule>().Definition(j.Request.DefinitionId)!.EffortRequired, j.BlockedReason)));
        rows.AddRange(world.Module<LogisticsModule>().Jobs.Select(j => new ActivityRow(j.Request.Id, "haul", j.Active, j.Status.ToString(), j.ElapsedTravelTicks, 0, j.BlockedReason)));
        rows.AddRange(world.Module<ProjectBoardModule>().Projects.Select(j => new ActivityRow(j.Id, "project", !j.Completed, j.Completed ? "Completed" : "Planned", (decimal)j.EffortCompleted, (decimal)j.EffortRequired, null)));
        if (world.TryModule<ProductionModule>(out var production)) rows.AddRange(production.Jobs.Select(j => new ActivityRow(j.Plan.Id, "produce", j.Active, j.Status.ToString(), j.EffortCompleted, production.Recipe(j.Plan.RecipeId)!.Effort, j.BlockedReason)));
        if (world.TryModule<ComputingModule>(out var computing)) rows.AddRange(computing.Jobs.Select(j => new ActivityRow(j.Plan.Id, "computer", j.Active, j.Status.ToString(), j.Progress, ComputingModule.Profile(j.Plan.Kind).Effort, j.BlockedReason)));
        if (world.TryModule<ScavengingModule>(out var scavenging))
        {
            rows.AddRange(scavenging.Expeditions.Select(j => new ActivityRow(j.Plan.Id, "expedition", j.Active, j.Status.ToString(), j.SearchEffort, 0, j.Reason)));
            rows.AddRange(scavenging.Treatments.Select(j => new ActivityRow(j.Plan.Id, "treat", j.Active, j.Completed ? "Completed" : j.Cancelled ? "Cancelled" : "Care", j.Effort, 4, j.Reason)));
        }
        if (world.TryModule<SkillsModule>(out var skills)) rows.AddRange(skills.Jobs.Select(j => new ActivityRow(j.Plan.Id, "learning", j.Active, j.Status.ToString(), j.Effort,
            j.Plan.Kind == LearningKind.Study ? SkillCatalogue.StudyRequired(j.TargetLevel) : SkillCatalogue.ExperienceRequired(j.TargetLevel), j.BlockedReason)));
        return rows;
    }
}
