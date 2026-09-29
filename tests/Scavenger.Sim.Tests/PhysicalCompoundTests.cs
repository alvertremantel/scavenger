using System.Text.Json;
using Haldane.CareerSim.Work;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Tests;

public class PhysicalCompoundTests
{
    private static string State(Scheduler scheduler)
        => JsonSerializer.Serialize(SaveManager.Capture(scheduler, "test").World, SaveManager.Options);

    [Fact]
    public void ScenarioHasEnclosedRoomsAndOperationalUtilityConsumers()
    {
        var (world, _, _) = PhysicalCompoundScenario.Create();
        var report = PhysicalReport.Capture(world);
        Assert.Equal(200, report.TotalTrackedKg);
        Assert.All(report.Rooms, r => { Assert.True(r.Enclosed); Assert.True(r.Roofed); });
        Assert.True(world.Module<UtilitiesModule>().ServiceFor("workshop-bench")!.Operational);
        Assert.True(world.Module<UtilitiesModule>().ServiceFor("clinic-station")!.Operational);
    }

    [Fact]
    public void WorkerMustReachSourceBeforeLoadingAndWalkEveryCarriedCell()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new QueuePhysicalHaulCommand(new HaulRequest("test-haul", "salvage-hdpe", "depot-workshop", 2, 6)));
        var compound = world.Module<CompoundModule>();
        var previous = compound.PositionOf(2)!;
        bool wasLoaded = false;
        for (int tick = 0; tick < 40; tick++)
        {
            scheduler.Advance();
            var current = compound.PositionOf(2)!;
            Assert.InRange(Math.Abs(current.X - previous.X) + Math.Abs(current.Y - previous.Y), 0, 1);
            var job = world.Module<LogisticsModule>().FindJob("test-haul")!;
            if (job.Status == HaulStatus.Reserved) Assert.Equal(200, world.Module<InventoryModule>().StoredKg("salvage"));
            if (!wasLoaded && job.Status == HaulStatus.InTransit) Assert.Equal(new CellPosition(2, 4), current);
            if (job.Status == HaulStatus.InTransit) wasLoaded = true;
            Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
            previous = current;
            if (job.Status == HaulStatus.Delivered)
            { Assert.Equal(new CellPosition(16, 5), current); return; }
        }
        Assert.Fail("Shipment failed to physically arrive.");
    }

    [Fact]
    public void ClosedDoorBlocksCargoUntilReopenedWithoutTeleporting()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new QueuePhysicalHaulCommand(new HaulRequest("test-haul", "salvage-hdpe", "depot-workshop", 2, 6)));
        scheduler.FastForward(4);
        scheduler.Queue(new SetDoorOpenCommand(new CellPosition(13, 5), false));
        scheduler.FastForward(20);
        var job = world.Module<LogisticsModule>().FindJob("test-haul")!;
        Assert.Equal(HaulStatus.InTransit, job.Status);
        Assert.NotNull(job.Cargo);
        Assert.Equal(0, world.Module<InventoryModule>().StoredKg("workshop"));
        Assert.Contains("path", job.BlockedReason);
        scheduler.Queue(new SetDoorOpenCommand(new CellPosition(13, 5), true));
        scheduler.FastForward(30);
        Assert.Equal(HaulStatus.Delivered, job.Status);
        Assert.Equal(new CellPosition(16, 5), world.Module<CompoundModule>().PositionOf(2));
    }

    [Fact]
    public void ConstructionUsesPhysicalDeliveriesAndEmbedsExactlyItsMaterials()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new PlanBuildCommand(new BuildRequest("new-bench", "workbench", new CellPosition(8, 8), 3, 2, "salvage")));
        for (int tick = 0; tick < 150; tick++)
        {
            scheduler.Advance();
            Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
            if (world.Module<ConstructionModule>().FindJob("new-bench")!.Status == ConstructionStatus.Completed) break;
        }
        var job = world.Module<ConstructionModule>().FindJob("new-bench")!;
        Assert.Equal(ConstructionStatus.Completed, job.Status);
        var building = world.Module<CompoundModule>().FindBuilding("new-bench")!;
        Assert.Equal(8, building.EmbeddedMaterials.Sum(m => m.QuantityKg));
        Assert.Equal(job.InteractionCell, world.Module<CompoundModule>().PositionOf(3));
        Assert.False(world.Module<CompoundModule>().IsWalkable(new CellPosition(8, 8)));
        Assert.False(world.Module<UtilitiesModule>().ServiceFor("new-bench")!.Operational);
        Assert.NotEmpty(job.DeliveryIds);
        Assert.Null(world.Module<WorkforceModule>().ReservationFor(3));
        PhysicalSave.ValidateWorld(world);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(70)]
    public void MidMovementAndConstructionSavesResumeIdentically(int saveTick)
    {
        var (_, original, _) = PhysicalCompoundScenario.Create(seed: 77);
        void AdvanceScripted(Scheduler scheduler, int count)
        {
            for (int i = 0; i < count; i++) { PhysicalCompoundScenario.QueueDemoCommands(scheduler); scheduler.Advance(); }
        }
        AdvanceScripted(original, saveTick);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, "physical"));
        Assert.Equal(State(original), State(loaded));
        AdvanceScripted(original, 120 - saveTick);
        AdvanceScripted(loaded, 120 - saveTick);
        Assert.Equal(State(original), State(loaded));
    }

    [Fact]
    public void PowerIsolationStopsPumpWaterAndWorkstationEffort()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new AssignStaffCommand(1, "inspect-pump"));
        scheduler.FastForward(20);
        var project = world.Module<ProjectBoardModule>().Find("inspect-pump")!;
        double progress = project.EffortCompleted;
        Assert.False(project.Completed);
        scheduler.Queue(new SetConduitEnabledCommand(UtilityKind.Power, new CellPosition(12, 8), false));
        scheduler.FastForward(8);
        Assert.Equal(progress, project.EffortCompleted);
        Assert.False(world.Module<UtilitiesModule>().ServiceFor("well-pump")!.Operational);
        Assert.False(world.Module<UtilitiesModule>().ServiceFor("clinic-station")!.Operational);
    }

    [Fact]
    public void PersistedPlayerOrdersReplayPhysicalConstructionAndUtilityChangesExactly()
    {
        var (_, original, _) = PhysicalCompoundScenario.Create(seed: 99);
        original.Queue(new PlanBuildCommand(new("storage", "stockpile", new(4, 8), 3, 2, "salvage")));
        original.Queue(new AssignStaffCommand(4, "prepare-clinic"));
        original.FastForward(8);
        original.Queue(new SetBuildingConditionCommand("generator", .5m)); original.FastForward(12);
        original.Queue(new SetBuildingConditionCommand("generator", 1)); original.FastForward(80);
        var loaded = SaveManager.Load(SaveManager.Save(original, "replay"), PhysicalSave.CreateRegistry());
        var (_, replay, _) = PhysicalCompoundScenario.Create(seed: 99);
        var commands = loaded.CommandLog.ToLookup(c => c.Tick);
        while (replay.World.Clock.Tick < original.World.Clock.Tick)
        {
            foreach (var entry in commands[replay.World.Clock.Tick]) replay.Queue(entry.Command);
            replay.Advance();
        }
        Assert.Equal(State(original), State(replay));
    }
}
