using System.Text.Json.Nodes;
using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Randomness;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Skills;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;
using Scavenger.Sim.Region;

namespace Scavenger.Sim.Tests;

public class SkillsTests
{
    public static IEnumerable<object[]> SkillIds => SkillCatalogue.All.Select(s => new object[] { s.Id });
    [Theory]
    [MemberData(nameof(SkillIds))]
    public void EachSpecialtyRequiresStudiedTheoryThenPracticalWorkAndUnlocksItsNewRating(string skill)
    {
        var (_, s, _) = IndustrialScenario.Create(); var m = s.World.Module<SkillsModule>();
        var initial = m.Find(2, skill)!; int priorLevel = initial.Level; int priorRating = initial.Rating;
        string book = AddBook(s.World, skill, priorLevel + 1);
        Assert.False(new StartLearningCommand(new("early-practice", 2, skill, LearningKind.Practice)).CanExecute(s.World));
        s.FastForward(5); Assert.Equal(0, initial.Experience);
        Study(s, 2, skill, book);
        Assert.Equal(priorLevel, initial.Level); Assert.Equal(priorRating, s.World.Module<WorkforceModule>().Find(2)!.Skills.Rating(skill));
        Assert.True(initial.TheoryReady); Assert.Equal(0, initial.Experience);
        var practice = new StartLearningCommand(new("practice", 2, skill, LearningKind.Practice));
        Assert.True(practice.CanExecute(s.World)); s.Queue(practice); s.Advance();
        Until(s, () => !m.Job("practice")!.Active);
        Assert.Equal(priorLevel + 1, initial.Level); Assert.Equal(initial.Level, initial.KnowledgeLevel);
        Assert.Equal((priorLevel + 1) * 10, s.World.Module<WorkforceModule>().Find(2)!.Skills.Rating(skill));
        Assert.Equal(SkillCatalogue.ExperienceRequired(priorLevel + 1), initial.LifetimeExperience);
        Assert.Equal(0, initial.Experience); Assert.Equal(0, initial.StudyEffort); Assert.False(initial.TheoryReady);
        Assert.Null(s.World.Module<WorkforceModule>().ReservationFor(2));
        s.FastForward(30); Assert.Equal(priorLevel + 1, initial.Level); Assert.Equal(0, initial.Experience);
        Assert.False(new StartLearningCommand(new("skip", 2, skill, LearningKind.Practice)).CanExecute(s.World));
        PhysicalSave.ValidateWorld(s.World);
    }
    [Fact]
    public void SourceMustMatchSubjectNextLevelWholeReadableLocalVolumeAndAvailableMember()
    {
        var session = new GameSession(regional: false); var world = session.World;
        AddBook(world, "construction", 2, "too-high"); AddBook(world, "medicine", 1, "wrong-subject");
        AddBook(world, "construction", 1, "damaged", .8m); AddBook(world, "construction", 1, "right");
        long tick = world.Clock.Tick;
        foreach (string book in new[] { "missing", "too-high", "wrong-subject", "damaged" })
        { Assert.False(session.Submit(new StartLearningCommand(new("bad", 2, "construction", LearningKind.Study, book))).Accepted); Assert.Equal(tick, world.Clock.Tick); }
        Assert.True(session.Submit(new StartLearningCommand(new("valid", 2, "construction", LearningKind.Study, "right"))).Accepted);
        Assert.False(session.Submit(new StartLearningCommand(new("busy-worker", 2, "medicine", LearningKind.Study, "wrong-subject"))).Accepted);
        Assert.False(session.Submit(new StartLearningCommand(new("busy-book", 4, "construction", LearningKind.Study, "right"))).Accepted);
        Assert.False(new StoreTransferCommand(new("steal-book", "right", "workshop", 3, .4m)).CanExecute(world));
        Assert.False(new StoreTransferCommand(new("split-book", "too-high", "workshop", 3, .2m)).CanExecute(world));
        Assert.True(session.Submit(new CancelLearningCommand("valid")).Accepted);
        Assert.Equal(.4m, world.Module<InventoryModule>().AvailableKg("right")); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void InterruptedStudyRetainsEffortAndResumesDeterministicallyAcrossSaveLoad()
    {
        var (_, s, _) = IndustrialScenario.Create(); string book = AddBook(s.World, "construction", 1);
        Navigation.StepTowards(s.World.Module<CompoundModule>(), 2, new(2, 4));
        Navigation.StepTowards(s.World.Module<CompoundModule>(), 2, new(2, 4));
        s.Queue(new StartLearningCommand(new("first", 2, "construction", LearningKind.Study, book))); s.FastForward(4);
        decimal partial = s.World.Module<SkillsModule>().Find(2, "construction")!.StudyEffort;
        Assert.InRange(partial, 1, SkillCatalogue.StudyRequired(1) - 1);
        s.Queue(new CancelLearningCommand("first")); s.Advance();
        Assert.Equal(partial, s.World.Module<SkillsModule>().Find(2, "construction")!.StudyEffort);
        s.Queue(new StartLearningCommand(new("resume", 2, "construction", LearningKind.Study, book))); s.Advance();
        var loaded = PhysicalSave.Load(SaveManager.Save(s, "Learning"));
        s.FastForward(20); loaded.FastForward(20);
        Assert.Equal(State(s), State(loaded));
        Assert.True(loaded.World.Module<SkillsModule>().Find(2, "construction")!.TheoryReady);
        Assert.Equal(.4m, loaded.World.Module<InventoryModule>().AvailableKg(book));
    }
    [Fact]
    public void PracticalExperienceCannotBeBankedBeforeTheoryOrEarnedByIdleBlockedOrUnloadedTravel()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World; var inventory = world.Module<InventoryModule>();
        var progress = world.Module<SkillsModule>().Find(2, "hauling")!;
        s.Queue(new StoreTransferCommand(new("before", "depot-diesel", "generator-tank", 2, 6))); s.FastForward(40);
        Assert.Equal(0, progress.LifetimeExperience);
        Study(s, 2, "hauling", AddBook(world, "hauling", 7));
        s.Queue(new StoreTransferCommand(new("after", "depot-diesel", "generator-tank", 2, 6))); s.Advance();
        var haul = world.Module<LogisticsModule>().FindJob("after")!;
        Assert.Equal(0, progress.Experience); // first tick walks to the source, with no load
        Until(s, () => !haul.Active); decimal earned = progress.Experience;
        Assert.True(earned > 0); s.FastForward(25); Assert.Equal(earned, progress.Experience);
        Assert.Equal(6, haul.Request.QuantityKg); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void ProductiveConstructionCreditsOnlyAcceptedEffort()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        Study(s, 3, "construction", AddBook(world, "construction", 7));
        var p = world.Module<SkillsModule>().Find(3, "construction")!;
        s.Queue(new PlanBuildCommand(new("bench", "wall", new(8, 8), 3, 2, "salvage"))); s.Advance();
        var job = world.Module<ConstructionModule>().FindJob("bench")!;
        s.FastForward(1); Assert.Equal(0, p.Experience);
        Until(s, () => !job.Active);
        Assert.Equal(2, p.Experience); s.FastForward(20); Assert.Equal(2, p.Experience); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void ProductionAndHardwareGiveExperienceButBlockedInputsAndUnattendedComputeDoNot()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        Study(s, 4, "chemistry", AddBook(world, "chemistry", 7));
        var chemistry = world.Module<SkillsModule>().Find(4, "chemistry")!;
        s.Queue(new QueueProductionCommand(new("wash", "wash-hdpe", "washer", 4, 2, ["salvage"], "salvage", "waste"))); s.Advance();
        Until(s, () => world.Module<ProductionModule>().Find("wash")!.Status == ProductionStatus.Completed);
        Assert.Equal(4, chemistry.Experience);
        Study(s, 1, "electronics", AddBook(world, "electronics", 7));
        s.Queue(new QueueComputerTaskCommand(new("diagnosis", ComputerTaskKind.DiagnoseComponents, "rig-1", 1, Components: IndustrialScenario.StarterKit))); s.Advance();
        Until(s, () => !world.Module<ComputingModule>().Job("diagnosis")!.Active);
        Assert.Equal(ComputingModule.Profile(ComputerTaskKind.DiagnoseComponents).Effort, world.Module<SkillsModule>().Find(1, "electronics")!.Experience);
        PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void MentorNeedsTargetLevelAndBothMembersMustArriveAndRemainPresent()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        Study(s, 2, "construction", AddBook(world, "construction", 1));
        Assert.False(new StartLearningCommand(new("wrong", 2, "construction", LearningKind.Practice, MentorId: 4)).CanExecute(world));
        var command = new StartLearningCommand(new("mentored", 2, "construction", LearningKind.Practice, MentorId: 3));
        Assert.True(command.CanExecute(world)); s.Queue(command); s.Advance();
        var job = world.Module<SkillsModule>().Job("mentored")!;
        Assert.Equal(0, job.Effort);
        Until(s, () => job.Effort > 0);
        Assert.Equal(.625m, job.Effort); // novice .5 effort with 25% mentor bonus
        world.Module<WorkforceModule>().Find(3)!.Present = false;
        decimal prior = job.Effort; s.FastForward(5); Assert.Equal(prior, job.Effort); Assert.Equal("Mentor is absent.", job.BlockedReason);
        world.Module<WorkforceModule>().Find(3)!.Present = true;
        var restored = PhysicalSave.Load(SaveManager.Save(s, "Mentored"));
        s.FastForward(60); restored.FastForward(60);
        Assert.Equal(State(s), State(restored));
        Assert.Null(restored.World.Module<WorkforceModule>().ReservationFor(3));
        Assert.Null(restored.World.Module<EquipmentModule>().OwnerOf("workshop-bench"));
    }
    [Fact]
    public void CancellationOfPracticeRetainsXpAndReleasesLearnerMentorAndFixture()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        Study(s, 2, "construction", AddBook(world, "construction", 1));
        s.Queue(new StartLearningCommand(new("exercise", 2, "construction", LearningKind.Practice, MentorId: 3))); s.Advance();
        Until(s, () => world.Module<SkillsModule>().Job("exercise")!.Effort > 2);
        decimal earned = world.Module<SkillsModule>().Find(2, "construction")!.Experience;
        s.Queue(new CancelLearningCommand("exercise")); s.Advance(); s.FastForward(5);
        Assert.Equal(earned, world.Module<SkillsModule>().Find(2, "construction")!.Experience);
        Assert.Null(world.Module<WorkforceModule>().ReservationFor(2)); Assert.Null(world.Module<WorkforceModule>().ReservationFor(3));
        Assert.Null(world.Module<EquipmentModule>().OwnerOf("workshop-bench")); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void BooksRecoverWholeConserveMassAndSaveMidExpeditionWithoutPrematureLearning()
    {
        var (_, s, _) = GeneratedScenario.Create(); var world = s.World; var region = world.Module<ScavengingModule>();
        var location = region.Locations.First(l => l.Definition.Salvage.Any(o => o.MaterialId == "book/construction/7"));
        var book = location.Definition.Salvage.First(o => o.MaterialId == "book/construction/7");
        decimal mass = PhysicalReport.Capture(world).TotalTrackedKg;
        Assert.False(new DispatchExpeditionCommand(new("partial", location.Definition.Id, [2], [new(book.Id, .2m)])).CanExecute(world));
        Assert.False(new StartLearningCommand(new("remote", 3, "construction", LearningKind.Study, book.Id)).CanExecute(world));
        s.Queue(new DispatchExpeditionCommand(new("books", location.Definition.Id, [2], [new(book.Id, .4m)]))); s.Advance();
        Until(s, () => region.Find("books")!.Status == ExpeditionStatus.Outbound);
        var loaded = PhysicalSave.Load(SaveManager.Save(s, "Books"));
        FinishExpedition(s, "books"); FinishExpedition(loaded, "books");
        Assert.Equal(State(s), State(loaded));
        Assert.Equal(mass, PhysicalReport.Capture(world).TotalTrackedKg);
        var batch = world.Module<InventoryModule>().Batches.Single(b => b.MaterialId == book.MaterialId);
        Study(s, 3, "construction", batch.Id); Assert.Equal(mass, PhysicalReport.Capture(world).TotalTrackedKg);
        Assert.Equal(.4m, world.Module<InventoryModule>().AvailableKg(batch.Id));
    }
    [Fact]
    public void LootIsSeededCommonButNeitherSiteNorExactSubjectIsGuaranteedAndSpecialistsBiasSubjects()
    {
        var streams = new RngStreams(42); int empty = 0, count = 0, medicine = 0; var distinct = new HashSet<string>();
        for (int i = 0; i < 500; i++)
        {
            var books = EducationLoot.Roll("homes", streams.Stream("homes")); if (books.Count == 0) empty++; count += books.Count;
            foreach (var b in books) distinct.Add(b.MaterialId);
            foreach (var b in EducationLoot.Roll("clinic", streams.Stream("clinics"))) if (SkillCatalogue.Book(b.MaterialId)!.SkillId is "medicine" or "chemistry") medicine++;
        }
        Assert.InRange(empty, 100, 200); Assert.InRange(count, 600, 850); Assert.True(distinct.Count > 90); Assert.True(medicine > 350);
        var a = EducationLoot.Roll("library", new RngStreams(123).Stream("books")); var b2 = EducationLoot.Roll("library", new RngStreams(123).Stream("books"));
        Assert.Equal(a.Select(b => b.MaterialId), b2.Select(b => b.MaterialId));
        Assert.Contains(EducationLoot.Roll("library", new RngStreams(42).Stream("books")), b => SkillCatalogue.Book(b.MaterialId) is not null);
    }
    [Fact]
    public void BadSkillGatesFractionalBooksAndOrphanedClaimsAreRejectedOnSaveLoad()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        var snapshot = (SkillsSnapshot)world.Module<SkillsModule>().ToSnapshot();
        var original = snapshot.Members.First();
        Assert.Throws<ArgumentException>(() => SkillsModule.FromSnapshot(snapshot with { Members = snapshot.Members.Select(p => p == original ? p with { Experience = 10, LifetimeExperience = 10 } : p).ToArray() }));
        Assert.Throws<ArgumentException>(() => SkillsModule.FromSnapshot(snapshot with { Members = snapshot.Members.Select(p => p == original ? p with { KnowledgeLevel = p.Level + 2 } : p).ToArray() }));
        Assert.True(world.Module<WorkforceModule>().TryReserve(2, "scavenger.learning/orphan"));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.ValidateWorld(world));
        world.Module<WorkforceModule>().Release(2, "scavenger.learning/orphan");
        world.Module<InventoryModule>().AddBatch(new("fraction", "book/construction/1", "salvage", .2m, new(1, "readable")));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.ValidateWorld(world));
    }
    [Fact]
    public void PreSkillSaveMigratesExistingRatingsWithoutAddingOrRerollingRegionalLoot()
    {
        var (_, s, _) = GeneratedScenario.Create(); var root = JsonNode.Parse(SaveManager.Save(s, "Legacy"))!;
        // Module discriminator keys are stable; remove only the new progression module.
        var modules = root["world"]!["modules"]!.AsArray(); var skillModule = modules.First(n => n!["id"]!.GetValue<string>() == SkillsModule.Persist); modules.Remove(skillModule);
        var loaded = PhysicalSave.Load(root.ToJsonString());
        Assert.Equal(75, loaded.World.Module<SkillsModule>().Find(1, "mechanical")!.Rating);
        Assert.Equal(s.World.Module<ScavengingModule>().RegionalKg, loaded.World.Module<ScavengingModule>().RegionalKg);
        PhysicalSave.ValidateWorld(loaded.World);
    }
    [Fact]
    public void DuplicateVolumesAllowParallelReadersAndReleaseBothPhysicalClaims()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        AddBook(world, "construction", 1, "volume-a"); AddBook(world, "construction", 1, "volume-b");
        s.Queue(new StartLearningCommand(new("reader-a", 2, "construction", LearningKind.Study, "volume-a")));
        s.Queue(new StartLearningCommand(new("reader-b", 4, "construction", LearningKind.Study, "volume-b"))); s.Advance();
        Assert.Equal(2, world.Module<SkillsModule>().Jobs.Count(j => j.Active));
        Assert.Equal(0, world.Module<InventoryModule>().AvailableKg("volume-a")); Assert.Equal(0, world.Module<InventoryModule>().AvailableKg("volume-b"));
        Until(s, () => world.Module<SkillsModule>().Jobs.All(j => !j.Active));
        Assert.True(world.Module<SkillsModule>().Find(2, "construction")!.TheoryReady); Assert.True(world.Module<SkillsModule>().Find(4, "construction")!.TheoryReady);
        Assert.Equal(.4m, world.Module<InventoryModule>().AvailableKg("volume-a")); Assert.Equal(.4m, world.Module<InventoryModule>().AvailableKg("volume-b"));
        PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void GenericEngineHaulsCannotCreateFractionalBooksInValidGameSaves()
    {
        var (_, s, _) = IndustrialScenario.Create();
        string book = AddBook(s.World, "construction", 1);
        s.Queue(new QueueHaulCommand(new("fractional-haul", book, "depot-workshop", 2, .2m))); s.Advance();
        Assert.NotNull(s.World.Module<LogisticsModule>().FindJob("fractional-haul"));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.ValidateWorld(s.World));
    }
    [Fact]
    public void LearnedTheoryCannotBeForgedWithoutAStudyLedger()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        var snapshot = (SkillsSnapshot)world.Module<SkillsModule>().ToSnapshot();
        world.Register(SkillsModule.FromSnapshot(snapshot with { Members = snapshot.Members.Select(m => m.WorkerId == 2 && m.SkillId == "construction"
            ? m with { KnowledgeLevel = 1, StudyEffort = SkillCatalogue.StudyRequired(1) } : m).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.ValidateWorld(world));
    }
    [Fact]
    public void NewSavesCannotLoseProgressionByPretendingToBePreSkillSaves()
    {
        var (_, s, _) = IndustrialScenario.Create();
        Study(s, 2, "construction", AddBook(s.World, "construction", 1));
        var root = JsonNode.Parse(SaveManager.Save(s, "Corrupt"))!;
        var modules = root["world"]!["modules"]!.AsArray();
        modules.Remove(modules.First(n => n!["id"]!.GetValue<string>() == SkillsModule.Persist));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(root.ToJsonString()));
    }
    [Theory]
    [InlineData("cooking", "food.dry")]
    [InlineData("agriculture", "nutrient.fertilizer")]
    [InlineData("metalworking", "metal.steel")]
    [InlineData("tailoring", "fabric")]
    public void FieldSpecialtiesEarnPracticalExperienceFromRelevantFiniteRecovery(string skill, string material)
    {
        var (_, baseWorld, _) = IndustrialScenario.Create();
        var world = baseWorld.World; RegionalContent.AddMaterials(world.Module<InventoryModule>());
        var region = new ScavengingModule(); foreach (var w in world.Module<WorkforceModule>().Staff) region.AddResident(w.Id); world.Register(region);
        var s = new Scheduler(world, ScavengingScenario.Systems());
        var progress = world.Module<SkillsModule>().Find(2, skill)!;
        Study(s, 2, skill, AddBook(world, skill, progress.NextLevel));
        Study(s, 2, "scavenging", AddBook(world, "scavenging", 4));
        Study(s, 2, "survival", AddBook(world, "survival", 3));
        Study(s, 2, "navigation", AddBook(world, "navigation", 4));
        region.AddLocation(new("exercise-site", "Safe recovery yard", "Separated supplies for recovery.", 0, 0, 6, 1, 0, 0, 0, .002m,
            [new("stock", "Dry stock", material, 2, new(1, "identified"), 4, new("hauling", 20))]));
        var dispatch = new DispatchExpeditionCommand(new("field", "exercise-site", [2], [new("stock", 1)], UseProtection: false));
        Assert.True(dispatch.CanExecute(world), region.Explain(world, dispatch.Plan));
        s.Queue(dispatch); s.Advance(); FinishExpedition(s, "field");
        Assert.Equal(2.5m, progress.Experience); // 1kg × 4 effort/kg × 1.25 careful / 2 secondary XP
        Assert.Equal(2.5m, world.Module<SkillsModule>().Find(2, "scavenging")!.Experience);
        Assert.True(world.Module<SkillsModule>().Find(2, "survival")!.Experience > 0);
        Assert.Equal(1.5m, world.Module<SkillsModule>().Find(2, "navigation")!.Experience);
        Assert.Equal(1, region.Location("exercise-site")!.Remaining("stock")); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void ReadingAnotherSubjectEarnsResearchExperienceAfterResearchTheory()
    {
        var (_, s, _) = IndustrialScenario.Create();
        Study(s, 2, "research", AddBook(s.World, "research", 2));
        Study(s, 2, "construction", AddBook(s.World, "construction", 1));
        Assert.Equal(3, s.World.Module<SkillsModule>().Find(2, "research")!.Experience);
        Assert.Equal(0, s.World.Module<SkillsModule>().Find(2, "construction")!.Experience);
    }
    [Fact]
    public void PromotionPreservesUnrelatedHaulReservationsAndDoesNotSpillIntoNextLevel()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        Study(s, 2, "construction", AddBook(world, "construction", 1));
        s.Queue(new StartLearningCommand(new("practice", 2, "construction", LearningKind.Practice))); s.Advance();
        var progress = world.Module<SkillsModule>().Find(2, "construction")!;
        Until(s, () => progress.Experience >= SkillCatalogue.ExperienceRequired(1) - 1);
        s.Queue(new StoreTransferCommand(new("concurrent", "salvage-metal.steel", "clinic", 4, 1))); s.Advance();
        string? owner = world.Module<WorkforceModule>().ReservationFor(4); Assert.NotNull(owner);
        Until(s, () => progress.Level == 1);
        Assert.Equal(owner, world.Module<WorkforceModule>().ReservationFor(4)); Assert.Equal(0, progress.Experience);
        Assert.Equal(20, progress.LifetimeExperience); Assert.False(progress.TheoryReady); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void FinalLevelRequiresItsTwoGatesAndThenDisablesFurtherLearning()
    {
        var (_, s, _) = IndustrialScenario.Create(); var world = s.World;
        var skills = world.Module<SkillsModule>(); var snapshot = (SkillsSnapshot)skills.ToSnapshot();
        // A migrated veteran's background is 90, not newly awarded XP.
        world.Register(SkillsModule.FromSnapshot(snapshot with { Members = snapshot.Members.Select(m => m.WorkerId == 2 && m.SkillId == "mechanical"
            ? m with { BackgroundRating = 90, Level = 9, KnowledgeLevel = 9 } : m).ToArray() }));
        // Use an actual workforce snapshot to set the veteran's existing rating.
        var workforce = world.Module<WorkforceModule>(); var roster = (WorkforceSnapshot)workforce.ToSnapshot();
        world.Register(WorkforceModule.FromSnapshot(roster with { Staff = roster.Staff.Select(w => w.Id == 2 ? new StaffMember { Id = w.Id, Name = w.Name, Present = w.Present,
            Skills = new SkillProfile(w.Skills.Ratings.ToDictionary(p => p.Key, p => p.Key == "mechanical" ? 90 : p.Value)) } : w).ToArray() }));
        Study(s, 2, "mechanical", AddBook(world, "mechanical", 10));
        s.Queue(new StartLearningCommand(new("mastery", 2, "mechanical", LearningKind.Practice))); s.Advance();
        Until(s, () => world.Module<SkillsModule>().Find(2, "mechanical")!.Mastered);
        Assert.Equal(100, world.Module<WorkforceModule>().Find(2)!.Skills.Rating("mechanical"));
        Assert.False(new StartLearningCommand(new("extra", 2, "mechanical", LearningKind.Study, "book-mechanical-10")).CanExecute(world));
        Assert.False(new StartLearningCommand(new("extra", 2, "mechanical", LearningKind.Practice)).CanExecute(world));
        PhysicalSave.ValidateWorld(world);
    }
    private static string State(Scheduler s) => System.Text.Json.JsonSerializer.Serialize(SaveManager.Capture(s, "state").World, SaveManager.Options);
    private static string AddBook(World world, string skill, int level, string? id = null, decimal purity = 1)
    {
        id ??= "book-" + skill + "-" + level;
        world.Module<InventoryModule>().AddBatch(new(id, new SkillBook(skill, level).MaterialId, "salvage", .4m, new(purity, "readable educational volume"))); return id;
    }
    private static void Study(Scheduler s, int worker, string skill, string book)
    {
        string id = "study-" + worker + "-" + skill + "-" + s.World.Clock.Tick;
        var c = new StartLearningCommand(new(id, worker, skill, LearningKind.Study, book));
        Assert.True(c.CanExecute(s.World), s.World.Module<SkillsModule>().Explain(s.World, c.Plan));
        s.Queue(c); s.Advance(); Until(s, () => !s.World.Module<SkillsModule>().Job(id)!.Active);
    }
    private static void Until(Scheduler s, Func<bool> done)
    { for (int i = 0; i < 700 && !done(); i++) s.Advance(); Assert.True(done(), "Timed out. " + string.Join("; ", s.World.Module<SkillsModule>().Jobs.Select(j => j.Plan.Id + " " + j.BlockedReason))); }
    private static void FinishExpedition(Scheduler s, string id)
    {
        var region = s.World.Module<ScavengingModule>();
        for (int n = 0; n < 700 && region.Find(id)!.Active; n++)
        { if (region.Find(id)!.Status == ExpeditionStatus.Decision) s.Queue(new RespondToExpeditionCommand(id, ExpeditionResponse.Wait)); s.Advance(); }
        Assert.False(region.Find(id)!.Active);
    }
}
