using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;

namespace Scavenger.Sim.Skills;

/// <summary>Per-person theory and practice gates. Workforce ratings are a derived compatibility view.</summary>
public sealed class SkillsModule : IPersistentModule
{
    public const string Persist = "scavenger.skills";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<(int, string), MemberSkill> _members = new();
    private readonly Dictionary<string, LearningJob> _jobs = new(StringComparer.Ordinal);
    public IReadOnlyList<MemberSkill> Members => _members.Values.OrderBy(s => s.WorkerId).ThenBy(s => s.SkillId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<LearningJob> Jobs => _jobs.Values.OrderBy(j => j.Plan.Id, StringComparer.Ordinal).ToArray();
    public MemberSkill? Find(int workerId, string skillId) => _members.GetValueOrDefault((workerId, skillId));
    public LearningJob? Job(string id) => _jobs.GetValueOrDefault(id);
    public static void Initialize(World world)
    {
        var module = new SkillsModule();
        foreach (var worker in world.Module<WorkforceModule>().Staff)
            foreach (var skill in SkillCatalogue.All)
                module._members.Add((worker.Id, skill.Id), new(worker.Id, skill.Id, worker.Skills.Rating(skill.Id)));
        world.Register(module);
        SkillCatalogue.AddMaterials(world.Module<InventoryModule>());
        module.SynchronizeRatings(world);
    }
    public string? Explain(World world, LearningPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Id) || Job(plan.Id) is not null) return "Choose an unused learning task id.";
        if (!Enum.IsDefined(plan.Kind) || Find(plan.WorkerId, plan.SkillId) is not { } progress) return "Choose a known member and skill.";
        if (progress.Mastered) return "This skill is already mastered.";
        if (LaborReason(world, plan.WorkerId) is { } unavailable) return unavailable;
        var compound = world.Module<CompoundModule>();
        CellPosition? destination;
        if (plan.Kind == LearningKind.Study)
        {
            if (plan.MentorId is not null) return "Study uses the source volume; mentors are for practical exercises.";
            if (progress.TheoryReady) return "Theory is complete. Earn practical experience for this level.";
            var inventory = world.Module<InventoryModule>();
            var batch = plan.BookBatchId is null ? null : inventory.FindBatch(plan.BookBatchId);
            if (batch is null || SkillCatalogue.Book(batch.MaterialId) is not { } book) return "Select a recovered educational volume in a compound store.";
            if (book.SkillId != plan.SkillId || book.Level != progress.NextLevel) return $"Need {plan.SkillId} level {progress.NextLevel} material; prerequisites cannot be skipped.";
            if (batch.QuantityKg != SkillBook.MassKg || inventory.AvailableKg(batch.Id) != SkillBook.MassKg || batch.Quality.Purity < .9m) return "The complete readable volume is reserved, damaged or unavailable.";
            destination = inventory.FindStockpile(batch.StockpileId)?.Position;
        }
        else
        {
            if (plan.BookBatchId is not null) return "Practice uses a fixture, not a book claim.";
            if (!progress.TheoryReady) return $"Study {plan.SkillId} level {progress.NextLevel} before earning practical XP.";
            var fixture = compound.FindBuilding(SkillCatalogue.Find(plan.SkillId)!.FixtureId);
            if (fixture is null || fixture.Condition < .2m) return "A serviceable exercise fixture is required.";
            destination = fixture.InteractionCell;
            if (world.TryModule<EquipmentModule>(out var equipment) && equipment.OwnerOf(fixture.Id) is not null) return "The exercise fixture is reserved by another task.";
            if (world.Module<ProjectBoardModule>().Projects.Any(p => !p.Completed && p.AssignedStaffIds.Count > 0 && compound.BindingFor(p.Id)?.BuildingId == fixture.Id)) return "The exercise fixture is in use by a project.";
            if (Jobs.Any(j => j.Active && j.Plan.Kind == LearningKind.Practice && SkillCatalogue.Find(j.Plan.SkillId)!.FixtureId == fixture.Id)) return "The exercise fixture is in use by another learner.";
            if (plan.MentorId is { } mentor)
            {
                if (mentor == plan.WorkerId || Find(mentor, plan.SkillId) is not { } mentorSkill || mentorSkill.Level < progress.NextLevel) return "Mentor must be another member with the target skill level.";
                if (LaborReason(world, mentor) is { } reason) return "Mentor: " + reason;
                if (destination is null || Navigation.FindPath(compound, compound.PositionOf(mentor)!, destination).Count == 0) return "Mentor cannot reach the exercise.";
            }
        }
        if (destination is null || compound.PositionOf(plan.WorkerId) is not { } start || Navigation.FindPath(compound, start, destination).Count == 0) return "The learning location is unreachable.";
        return null;
    }
    private static string? LaborReason(World world, int workerId)
    {
        var workforce = world.Module<WorkforceModule>();
        if (workforce.Find(workerId) is not { Present: true }) return "Member is absent, ill or unknown.";
        if (workforce.ReservationFor(workerId) is not null || world.Module<ProjectBoardModule>().IsAssigned(workerId)) return "Member is assigned to another task.";
        return null;
    }
    public LearningJob Start(World world, LearningPlan plan)
    {
        if (Explain(world, plan) is { } reason) throw new InvalidOperationException(reason);
        var job = new LearningJob(plan, Find(plan.WorkerId, plan.SkillId)!.NextLevel);
        var workforce = world.Module<WorkforceModule>();
        if (!workforce.TryReserve(plan.WorkerId, job.Owner)) throw new InvalidOperationException("Learner claim changed during command execution.");
        if (plan.MentorId is { } mentor && !workforce.TryReserve(mentor, job.Owner)) throw new InvalidOperationException("Mentor claim changed during command execution.");
        if (plan.Kind == LearningKind.Study && !world.Module<InventoryModule>().TryReserveStock(job.BookOwner, plan.BookBatchId!, SkillBook.MassKg)) throw new InvalidOperationException("Book claim changed during command execution.");
        if (plan.Kind == LearningKind.Practice && world.TryModule<EquipmentModule>(out var equipment)
            && !equipment.TryReserve(SkillCatalogue.Find(plan.SkillId)!.FixtureId, job.Owner)) throw new InvalidOperationException("Exercise fixture claim changed during command execution.");
        _jobs.Add(plan.Id, job); return job;
    }
    public void Cancel(World world, LearningJob job) { Release(world, job); job.Status = LearningStatus.Cancelled; job.BlockedReason = null; }
    private static void Release(World world, LearningJob job)
    {
        world.Module<WorkforceModule>().Release(job.Plan.WorkerId, job.Owner);
        if (job.Plan.MentorId is { } mentor) world.Module<WorkforceModule>().Release(mentor, job.Owner);
        world.Module<InventoryModule>().ReleaseReservations(job.BookOwner);
        if (job.Plan.Kind == LearningKind.Practice && world.TryModule<EquipmentModule>(out var equipment)) equipment.Release(SkillCatalogue.Find(job.Plan.SkillId)!.FixtureId, job.Owner);
    }
    /// <summary>Credit productive effort only after the next level's theory. Promotion is flushed at the end of the tick.</summary>
    internal void Earn(int workerId, string skillId, decimal effort)
    {
        if (effort <= 0 || Find(workerId, skillId) is not { TheoryReady: true } progress) return;
        decimal earned = Math.Min(effort, SkillCatalogue.ExperienceRequired(progress.NextLevel) - progress.Experience);
        progress.Experience += earned; progress.LifetimeExperience += earned;
    }
    internal static void Credit(World world, int workerId, string skillId, decimal effort)
    { if (world.TryModule<SkillsModule>(out var module)) module.Earn(workerId, skillId, effort); }
    internal IReadOnlyList<GameEvent> Tick(World world)
    {
        var events = new List<GameEvent>(); var compound = world.Module<CompoundModule>();
        var workforce = world.Module<WorkforceModule>(); var inventory = world.Module<InventoryModule>();
        foreach (var job in Jobs.Where(j => j.Active))
        {
            var plan = job.Plan; var progress = Find(plan.WorkerId, plan.SkillId)!;
            var fixture = plan.Kind == LearningKind.Practice ? compound.FindBuilding(SkillCatalogue.Find(plan.SkillId)!.FixtureId) : null;
            var book = plan.BookBatchId is null ? null : inventory.FindBatch(plan.BookBatchId);
            CellPosition? destination = plan.Kind == LearningKind.Study ? book is null ? null : inventory.FindStockpile(book.StockpileId)?.Position : fixture?.InteractionCell;
            string? reason = workforce.Find(plan.WorkerId) is not { Present: true } ? "Learner is absent." : destination is null ? "Learning source is unavailable." : null;
            if (plan.MentorId is { } mentor && workforce.Find(mentor) is not { Present: true }) reason = "Mentor is absent.";
            if (plan.Kind == LearningKind.Practice && fixture?.Condition < .2m) reason = "Exercise fixture is damaged.";
            if (reason is null)
            {
                var movement = Navigation.StepTowards(compound, plan.WorkerId, destination!);
                bool arrived = movement.Arrived; reason = movement.BlockedReason;
                if (plan.MentorId is { } teacher) { var move = Navigation.StepTowards(compound, teacher, destination!); arrived &= move.Arrived; reason ??= move.BlockedReason; }
                if (!arrived) reason ??= "Walking to the learning location.";
            }
            job.BlockedReason = reason;
            if (reason is not null) continue;
            job.Status = LearningStatus.Working;
            if (plan.Kind == LearningKind.Study)
            {
                decimal rate = 1 + (Find(plan.WorkerId, "research")?.Rating ?? 0) / 400m;
                decimal effort = Math.Min(rate, SkillCatalogue.StudyRequired(job.TargetLevel) - progress.StudyEffort);
                progress.StudyEffort += effort; job.Effort += effort;
                // Reading other subjects is productive research; reading research itself must not satisfy its practical gate.
                if (plan.SkillId != "research") Earn(plan.WorkerId, "research", effort / 4);
                if (progress.StudyEffort < SkillCatalogue.StudyRequired(job.TargetLevel)) continue;
                progress.KnowledgeLevel = job.TargetLevel;
                events.Add(SkillEvents.Event(world, SkillEvents.Learned, $"{workforce.Find(plan.WorkerId)!.Name} learned {plan.SkillId} L{job.TargetLevel} theory. Practical work is now required."));
            }
            else
            {
                decimal rate = (.5m + progress.Rating / 100m) * (plan.MentorId is null ? 1 : 1.25m);
                decimal effort = Math.Min(rate, SkillCatalogue.ExperienceRequired(job.TargetLevel) - progress.Experience);
                Earn(plan.WorkerId, plan.SkillId, effort); job.Effort += effort;
                if (progress.Experience < SkillCatalogue.ExperienceRequired(job.TargetLevel)) continue;
            }
            Release(world, job); job.Status = LearningStatus.Completed; job.BlockedReason = null;
        }
        bool promoted = false;
        foreach (var progress in Members.Where(s => s.TheoryReady && s.Experience >= SkillCatalogue.ExperienceRequired(s.NextLevel)))
        {
            progress.Level++; progress.Experience = 0; progress.StudyEffort = 0; promoted = true;
            events.Add(SkillEvents.Event(world, SkillEvents.Promoted, $"{workforce.Find(progress.WorkerId)!.Name} advanced {progress.SkillId} to L{progress.Level} (rating {progress.Rating})."));
        }
        if (promoted) SynchronizeRatings(world);
        return events;
    }
    private void SynchronizeRatings(World world)
    {
        // Haldane profiles are immutable. Replace the roster projection, preserving presence and every reservation.
        var workforce = world.Module<WorkforceModule>();
        var staff = workforce.Staff.Select(w => new StaffMember { Id = w.Id, Name = w.Name, Skill = w.Skill, DailyWage = w.DailyWage, Present = w.Present,
            Skills = new SkillProfile(w.Skills.Ratings.Concat(Members.Where(s => s.WorkerId == w.Id).Select(s => new KeyValuePair<string, int>(s.SkillId, s.Rating)))
                .GroupBy(s => s.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal)) }).ToArray();
        world.Register(WorkforceModule.FromSnapshot(new(staff, workforce.Reservations)));
    }
    public object ToSnapshot() => new SkillsSnapshot(Members.Select(s => s.Snapshot()).ToArray(), Jobs.Select(j => j.Snapshot()).ToArray());
    public static SkillsModule FromSnapshot(SkillsSnapshot snapshot)
    {
        var module = new SkillsModule();
        foreach (var s in snapshot.Members)
        {
            if (SkillCatalogue.Find(s.SkillId) is null || s.BackgroundRating is < 0 or > 100 || s.Level < s.BackgroundRating / 10 || s.Level > 10
                || s.KnowledgeLevel < s.Level || s.KnowledgeLevel > Math.Min(10, s.Level + 1) || s.StudyEffort < 0 || s.Experience < 0 || s.LifetimeExperience < s.Experience
                || s.StudyEffort > SkillCatalogue.StudyRequired(Math.Min(10, s.Level + 1))
                || (s.KnowledgeLevel == s.Level && (s.Experience != 0 || s.Level < 10 && s.StudyEffort >= SkillCatalogue.StudyRequired(s.Level + 1)))
                || (s.KnowledgeLevel > s.Level && s.StudyEffort != SkillCatalogue.StudyRequired(s.Level + 1))
                || (s.Level < 10 && s.Experience >= SkillCatalogue.ExperienceRequired(s.Level + 1))
                || (s.Level == 10 && (s.Experience != 0 || s.StudyEffort != 0))) throw new ArgumentException("Invalid skill progression.");
            decimal completedXp = Enumerable.Range(s.BackgroundRating / 10 + 1, s.Level - s.BackgroundRating / 10).Sum(l => SkillCatalogue.ExperienceRequired(l));
            if (s.LifetimeExperience != completedXp + s.Experience) throw new ArgumentException("Skill experience ledger does not balance.");
            module._members.Add((s.WorkerId, s.SkillId), new(s.WorkerId, s.SkillId, s.BackgroundRating) { Level = s.Level, KnowledgeLevel = s.KnowledgeLevel, StudyEffort = s.StudyEffort, Experience = s.Experience, LifetimeExperience = s.LifetimeExperience });
        }
        foreach (var j in snapshot.Jobs)
        {
            if (string.IsNullOrWhiteSpace(j.Plan.Id) || !Enum.IsDefined(j.Plan.Kind) || !Enum.IsDefined(j.Status) || j.TargetLevel is < 1 or > 10 || j.Effort < 0
                || (j.Plan.Kind == LearningKind.Study && j.Effort > SkillCatalogue.StudyRequired(j.TargetLevel))
                || (j.Plan.Kind == LearningKind.Practice && j.Effort > SkillCatalogue.ExperienceRequired(j.TargetLevel))) throw new ArgumentException("Invalid learning task.");
            module._jobs.Add(j.Plan.Id, new(j.Plan, j.TargetLevel) { Status = j.Status, Effort = j.Effort, BlockedReason = j.BlockedReason });
        }
        return module;
    }
}
