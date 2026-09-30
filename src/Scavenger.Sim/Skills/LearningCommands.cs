using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.CareerSim.Work;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Skills;

public static class SkillEvents
{
    public const string Planned = "scavenger.learning.planned", Learned = "scavenger.learning.learned", Promoted = "scavenger.skill.promoted", Cancelled = "scavenger.learning.cancelled";
    public static void RegisterAll(EventCatalogue catalogue)
    { catalogue.Register(Planned, "Learning planned"); catalogue.Register(Learned, "Theory learned", EventSeverity.Positive, true); catalogue.Register(Promoted, "Skill advanced", EventSeverity.Positive, true); catalogue.Register(Cancelled, "Learning cancelled"); }
    internal static GameEvent Event(World world, string kind, string text) => new(world.Clock.Tick, kind, kind is Learned or Promoted ? EventSeverity.Positive : EventSeverity.Info, text);
}
public sealed class StartLearningCommand(LearningPlan plan) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.learning";
    public LearningPlan Plan { get; } = plan;
    public string PersistId => Persist;
    public string Description => $"{Plan.Kind} {Plan.SkillId} with member {Plan.WorkerId} ({Plan.Id}).";
    public bool CanExecute(World world) => world.Has<SkillsModule>() && world.Module<SkillsModule>().Explain(world, Plan) is null;
    public object ToSnapshot() => Plan;
    public IReadOnlyList<GameEvent> Execute(World world) { world.Module<SkillsModule>().Start(world, Plan); return [SkillEvents.Event(world, SkillEvents.Planned, Description)]; }
}
public sealed class CancelLearningCommand(string id) : IPersistentCommand
{
    public const string Persist = "scavenger.cmd.learning-cancel";
    public string Id { get; } = id;
    public string PersistId => Persist;
    public string Description => $"Cancel learning {Id}; retain learned progress.";
    public bool CanExecute(World world) => world.Has<SkillsModule>() && world.Module<SkillsModule>().Job(Id) is { Active: true };
    public object ToSnapshot() => new Industry.IdPayload(Id);
    public IReadOnlyList<GameEvent> Execute(World world) { world.Module<SkillsModule>().Cancel(world, world.Module<SkillsModule>().Job(Id)!); return [SkillEvents.Event(world, SkillEvents.Cancelled, Description)]; }
}
public sealed class LearningSystem(IReadOnlyList<Type> dependencies) : ISystem
{
    public string Name => "Study, hands-on learning and skill advancement";
    public IReadOnlyList<Type> Dependencies => dependencies;
    public IReadOnlyList<GameEvent> Tick(World world, int ticks) => world.Has<SkillsModule>() ? world.Module<SkillsModule>().Tick(world) : [];
}
/// <summary>Credit only the effort the engine actually accepts, divided among eligible contributors.</summary>
public sealed class SkilledProjectSystem : ISystem
{
    private readonly WorkSystem _work = new(new CompoundWorkContext());
    public string Name => "Skilled project work";
    public IReadOnlyList<Type> Dependencies => _work.Dependencies;
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    {
        var workforce = world.Module<WorkforceModule>();
        var before = world.Module<ProjectBoardModule>().Projects.Where(p => !p.Completed).Select(p => new
        { Project = p, Effort = p.EffortCompleted, Contributors = p.AssignedStaffIds.Where(id => workforce.Find(id) is { } w && CompoundWorkContext.Explain(world, p, w) is null).ToArray() }).ToArray();
        var events = _work.Tick(world, ticks);
        foreach (var entry in before.Where(p => p.Contributors.Length > 0 && p.Project.EffortCompleted > p.Effort && p.Project.RequiredSkill is not null))
            foreach (int id in entry.Contributors) SkillsModule.Credit(world, id, entry.Project.RequiredSkill!.SkillId, (decimal)(entry.Project.EffortCompleted - entry.Effort) / entry.Contributors.Length);
        return events;
    }
}
