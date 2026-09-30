namespace Scavenger.Sim.Skills;

public sealed class MemberSkill
{
    internal MemberSkill(int workerId, string skillId, int backgroundRating)
    { WorkerId = workerId; SkillId = skillId; BackgroundRating = backgroundRating; Level = backgroundRating / 10; KnowledgeLevel = Level; }
    public int WorkerId { get; }
    public string SkillId { get; }
    public int BackgroundRating { get; }
    public int Rating => Math.Max(BackgroundRating, Level * 10);
    public int Level { get; internal set; }
    public int KnowledgeLevel { get; internal set; }
    public decimal StudyEffort { get; internal set; }
    public decimal Experience { get; internal set; }
    public decimal LifetimeExperience { get; internal set; }
    public int NextLevel => Math.Min(SkillCatalogue.MaxLevel, Level + 1);
    public bool Mastered => Level == SkillCatalogue.MaxLevel;
    public bool TheoryReady => !Mastered && KnowledgeLevel == Level + 1;
    public string Gate => Mastered ? "Mastered" : TheoryReady ? $"Work: {Experience:0.##}/{SkillCatalogue.ExperienceRequired(NextLevel)} XP"
        : $"Study L{NextLevel}: {StudyEffort:0.##}/{SkillCatalogue.StudyRequired(NextLevel)} effort";
    internal MemberSkillSnapshot Snapshot() => new(WorkerId, SkillId, BackgroundRating, Level, KnowledgeLevel, StudyEffort, Experience, LifetimeExperience);
}
public sealed record MemberSkillSnapshot(int WorkerId, string SkillId, int BackgroundRating, int Level, int KnowledgeLevel,
    decimal StudyEffort, decimal Experience, decimal LifetimeExperience);
public enum LearningKind { Study, Practice }
public enum LearningStatus { Walking, Working, Completed, Cancelled }
public sealed record LearningPlan(string Id, int WorkerId, string SkillId, LearningKind Kind, string? BookBatchId = null, int? MentorId = null);
public sealed class LearningJob
{
    internal LearningJob(LearningPlan plan, int targetLevel) { Plan = plan; TargetLevel = targetLevel; }
    public LearningPlan Plan { get; }
    public int TargetLevel { get; }
    public string Owner => "scavenger.learning/" + Plan.Id;
    public string BookOwner => Owner + "/book";
    public LearningStatus Status { get; internal set; } = LearningStatus.Walking;
    public bool Active => Status is LearningStatus.Walking or LearningStatus.Working;
    public decimal Effort { get; internal set; }
    public string? BlockedReason { get; internal set; }
    internal LearningJobSnapshot Snapshot() => new(Plan, TargetLevel, Status, Effort, BlockedReason);
}
public sealed record LearningJobSnapshot(LearningPlan Plan, int TargetLevel, LearningStatus Status, decimal Effort, string? BlockedReason);
public sealed record SkillsSnapshot(IReadOnlyList<MemberSkillSnapshot> Members, IReadOnlyList<LearningJobSnapshot> Jobs);
