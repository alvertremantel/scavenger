using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;

namespace Scavenger.Sim.Skills;

public static class SkillsSave
{
    public static void Register(SaveRegistry registry)
    {
        registry.RegisterModule<SkillsSnapshot>(SkillsModule.Persist, (s, v) => v == 1 ? SkillsModule.FromSnapshot(s) : throw new NotSupportedException($"Unsupported skills schema {v}."));
        registry.RegisterCommand<LearningPlan>(StartLearningCommand.Persist, s => new StartLearningCommand(s));
        registry.RegisterCommand<IdPayload>(CancelLearningCommand.Persist, s => new CancelLearningCommand(s.Id));
    }
    public static void Validate(World world)
    {
        var module = world.Module<SkillsModule>(); var workforce = world.Module<WorkforceModule>();
        var inventory = world.Module<InventoryModule>(); var compound = world.Module<CompoundModule>();
        // Recheck scalar invariants even for live worlds before saving.
        SkillsModule.FromSnapshot((SkillsSnapshot)module.ToSnapshot());
        void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        Require(module.Members.Count == workforce.Staff.Count * SkillCatalogue.All.Count && module.Members.All(s => workforce.Find(s.WorkerId)?.Skills.Rating(s.SkillId) == s.Rating), "Skill roster and workforce ratings disagree.");
        foreach (var batch in inventory.Batches.Where(b => SkillCatalogue.IsBook(b.MaterialId)))
            Require(SkillCatalogue.Book(batch.MaterialId) is not null && batch.QuantityKg == SkillBook.MassKg, "Invalid or fractional educational volume.");
        foreach (var haul in world.Module<LogisticsModule>().Jobs)
        {
            string? material = haul.Cargo?.MaterialId ?? inventory.FindBatch(haul.Request.BatchId)?.MaterialId ?? (haul.DeliveredBatchId is { } delivered ? inventory.FindBatch(delivered)?.MaterialId : null);
            if (material is not null && SkillCatalogue.IsBook(material))
                Require(SkillCatalogue.Book(material) is not null && haul.Request.QuantityKg == SkillBook.MassKg && (haul.Cargo is null || haul.Cargo.QuantityKg == SkillBook.MassKg), "An educational volume was split in transport.");
        }
        var labor = new Dictionary<int, string>(); var books = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in module.Jobs)
        {
            var plan = job.Plan; var progress = module.Find(plan.WorkerId, plan.SkillId);
            Require(progress is not null && job.TargetLevel > progress.BackgroundRating / 10 && job.TargetLevel <= progress.Level + 1, "Learning task has invalid member, skill or target level.");
            Require(plan.Kind == LearningKind.Study ? plan.BookBatchId is not null && plan.MentorId is null : plan.BookBatchId is null, "Learning source/mentor differs from its task kind.");
            if (plan.MentorId is { } mentor) Require(mentor != plan.WorkerId && module.Find(mentor, plan.SkillId)?.Level >= job.TargetLevel, "Learning mentor is missing or unqualified.");
            if (job.Status == LearningStatus.Completed)
                Require(plan.Kind == LearningKind.Study ? progress!.KnowledgeLevel >= job.TargetLevel : progress!.Level >= job.TargetLevel, "Completed learning did not meet its gate.");
            if (!job.Active)
            {
                Require(workforce.Reservations.All(r => r.Owner != job.Owner) && inventory.StockReservedBy(job.BookOwner) is null
                    && (!world.Has<EquipmentModule>() || world.Module<EquipmentModule>().Claims.All(c => c.Owner != job.Owner)), "Inactive learning retains a claim.");
                continue;
            }
            Require(job.TargetLevel == progress!.Level + 1 && (plan.Kind == LearningKind.Study ? !progress.TheoryReady : progress.TheoryReady), "Active learning has passed or skipped its target gate.");
            foreach (int id in plan.MentorId is { } teacher ? new[] { plan.WorkerId, teacher } : new[] { plan.WorkerId })
                Require(labor.TryAdd(id, job.Owner) && workforce.ReservationFor(id) == job.Owner && !world.Module<ProjectBoardModule>().IsAssigned(id), "Learning labor is duplicated, missing or assigned elsewhere.");
            if (plan.Kind == LearningKind.Study)
            {
                var batch = inventory.FindBatch(plan.BookBatchId!); var book = batch is null ? null : SkillCatalogue.Book(batch.MaterialId); var claim = inventory.StockReservedBy(job.BookOwner);
                Require(book is not null && book.SkillId == plan.SkillId && book.Level == job.TargetLevel && batch!.Quality.Purity >= .9m && claim?.BatchId == batch.Id
                    && claim.QuantityKg == SkillBook.MassKg && books.Add(job.BookOwner) && inventory.FindStockpile(batch.StockpileId)?.Position is not null, "Learning book claim or source is invalid.");
            }
            else
            {
                var fixture = compound.FindBuilding(SkillCatalogue.Find(plan.SkillId)!.FixtureId);
                Require(fixture is not null && (!world.Has<EquipmentModule>() || world.Module<EquipmentModule>().OwnerOf(fixture.Id) == job.Owner), "Learning fixture claim is missing.");
            }
        }
        foreach (var progress in module.Members)
        {
            var studies = module.Jobs.Where(j => j.Plan.Kind == LearningKind.Study && j.Plan.WorkerId == progress.WorkerId && j.Plan.SkillId == progress.SkillId).ToArray();
            for (int level = progress.BackgroundRating / 10 + 1; level <= progress.KnowledgeLevel; level++)
                Require(studies.Where(j => j.TargetLevel == level).Sum(j => j.Effort) == SkillCatalogue.StudyRequired(level), "Learned theory lacks its completed study ledger.");
            if (!progress.Mastered && !progress.TheoryReady)
                Require(studies.Where(j => j.TargetLevel == progress.NextLevel).Sum(j => j.Effort) == progress.StudyEffort, "Partial study differs from its task ledger.");
            Require(module.Jobs.Where(j => j.Plan.Kind == LearningKind.Practice && j.Plan.WorkerId == progress.WorkerId && j.Plan.SkillId == progress.SkillId).Sum(j => j.Effort) <= progress.LifetimeExperience,
                "Practical exercise effort exceeds earned experience.");
        }
        foreach (var claim in workforce.Reservations.Where(r => r.Owner.StartsWith("scavenger.learning/", StringComparison.Ordinal))) Require(labor.GetValueOrDefault(claim.StaffId) == claim.Owner, "Orphaned learning labor.");
        foreach (var claim in inventory.StockReservations.Where(r => r.Owner.StartsWith("scavenger.learning/", StringComparison.Ordinal))) Require(books.Contains(claim.Owner), "Orphaned learning source claim.");
        Require(!inventory.CapacityReservations.Any(r => r.Owner.StartsWith("scavenger.learning/", StringComparison.Ordinal)), "Learning has an unexpected receiving claim.");
        if (world.Has<EquipmentModule>()) foreach (var claim in world.Module<EquipmentModule>().Claims.Where(r => r.Owner.StartsWith("scavenger.learning/", StringComparison.Ordinal)))
            Require(module.Jobs.Any(j => j.Active && j.Plan.Kind == LearningKind.Practice && j.Owner == claim.Owner && SkillCatalogue.Find(j.Plan.SkillId)!.FixtureId == claim.StationId), "Orphaned learning fixture.");
    }
}
