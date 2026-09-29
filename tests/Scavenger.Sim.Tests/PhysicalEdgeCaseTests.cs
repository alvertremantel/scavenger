using System.Text.Json;
using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Tests;

public class PhysicalEdgeCaseTests
{
    [Fact]
    public void PathsRespectBoundsReplanAroundWallsAndUseDeterministicTies()
    {
        var map = new CompoundModule(5, 5);
        var path = Navigation.FindPath(map, new(0, 0), new(2, 2));
        Assert.Equal(new CellPosition(1, 0), path[1]);
        map.SetTile(new(1, 0), new Tile(Terrain.Wall));
        path = Navigation.FindPath(map, new(0, 0), new(2, 2));
        Assert.Equal(new CellPosition(0, 1), path[1]);
        Assert.All(path, c => Assert.True(map.IsWalkable(c)));
        Assert.Empty(Navigation.FindPath(map, new(-1, 0), new(2, 2)));
        Assert.Empty(Navigation.FindPath(map, new(0, 0), new(2, 2, 1)));
    }

    [Fact]
    public void PlansRejectOccupiedOutOfBoundsOverlappingAndUnqualifiedSitesWithoutClaims()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        BuildRequest Request(string id, CellPosition at, int worker = 3) => new(id, "workbench", at, worker, 2, "salvage");
        var module = world.Module<ConstructionModule>();
        Assert.NotNull(module.ExplainPlan(world, Request("outside", new(25, 17))));
        Assert.NotNull(module.ExplainPlan(world, Request("occupied", new(4, 14))));
        Assert.NotNull(module.ExplainPlan(world, Request("fixture", new(18, 3))));
        Assert.Contains("rating", module.ExplainPlan(world, Request("skill", new(8, 8), 2)));
        scheduler.Queue(new PlanBuildCommand(Request("first", new(8, 8)))); scheduler.Advance();
        Assert.Contains("reserves", module.ExplainPlan(world, Request("second", new(9, 8))));
        Assert.Single(module.Jobs);
        Assert.Null(world.Module<WorkforceModule>().ReservationFor(3));
    }

    [Fact]
    public void LoadedCargoFindsAlternateDoorAfterOriginalPathCloses()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new QueuePhysicalHaulCommand(new("alternate", "salvage-hdpe", "depot-workshop", 2, 6)));
        scheduler.FastForward(4);
        scheduler.Queue(new SetDoorOpenCommand(new(13, 5), false)); scheduler.Advance();
        Assert.Equal(HaulStatus.InTransit, world.Module<LogisticsModule>().FindJob("alternate")!.Status);
        world.Module<CompoundModule>().SetTile(new(13, 6), new Tile(Terrain.OpenDoor, true));
        scheduler.FastForward(40);
        Assert.Equal(HaulStatus.Delivered, world.Module<LogisticsModule>().FindJob("alternate")!.Status);
        Assert.Equal(6, world.Module<InventoryModule>().StoredKg("workshop"));
        Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationReleasesUnloadedTripsButFinishesLoadedDelivery(bool loaded)
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new PlanBuildCommand(new("cancel-site", "workbench", new(8, 8), 3, 2, "salvage")));
        scheduler.Advance();
        var construction = world.Module<ConstructionModule>().FindJob("cancel-site")!;
        if (loaded)
            Until(scheduler, () => world.Module<LogisticsModule>().Jobs.Any(j => j.Status == HaulStatus.InTransit));
        scheduler.Queue(new CancelBuildCommand("cancel-site")); scheduler.Advance();
        var resumed = PhysicalSave.Load(SaveManager.Save(scheduler, "cancelled"));
        resumed.FastForward(100);
        world = resumed.World;
        Assert.Equal(ConstructionStatus.Cancelled, world.Module<ConstructionModule>().FindJob("cancel-site")!.Status);
        Assert.Null(world.Module<CompoundModule>().FindBuilding("cancel-site"));
        Assert.Equal(loaded ? 4 : 0, world.Module<InventoryModule>().StoredKg(construction.StagingStockpileId));
        Assert.Empty(world.Module<InventoryModule>().StockReservations);
        Assert.Empty(world.Module<InventoryModule>().CapacityReservations);
        Assert.Empty(world.Module<WorkforceModule>().Reservations);
        Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
        PhysicalSave.ValidateWorld(world);
    }

    [Fact]
    public void CancelOnSiteKeepsDeliveredMaterialsAvailableAndReleasesBuilder()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new PlanBuildCommand(new("cancel-work", "workbench", new(8, 8), 3, 2, "salvage")));
        var job = world.Module<ConstructionModule>();
        Until(scheduler, () => job.FindJob("cancel-work")?.Status == ConstructionStatus.Building);
        Assert.NotEmpty(job.FindJob("cancel-work")!.InputOwners);
        scheduler.Queue(new CancelBuildCommand("cancel-work")); scheduler.Advance();
        Assert.Equal(8, world.Module<InventoryModule>().StoredKg("site/cancel-work"));
        Assert.Empty(world.Module<InventoryModule>().StockReservations);
        Assert.Null(world.Module<WorkforceModule>().ReservationFor(3));
        Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
        PhysicalSave.ValidateWorld(world);
    }

    [Fact]
    public void RepairsDeliverPartsToTargetAndResumeIdenticallyAfterSave()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new SetBuildingConditionCommand("generator", 0)); scheduler.Advance();
        Assert.False(world.Module<UtilitiesModule>().ServiceFor("well-pump")!.Operational);
        scheduler.Queue(new PlanBuildCommand(new("fix-generator", "generator", new(7, 11), 3, 2, "salvage", "generator")));
        scheduler.FastForward(12);
        var resumed = PhysicalSave.Load(SaveManager.Save(scheduler, "repair"));
        scheduler.FastForward(100); resumed.FastForward(100);
        Assert.Equal(State(scheduler), State(resumed));
        var target = world.Module<CompoundModule>().FindBuilding("generator")!;
        Assert.Equal(1, target.Condition);
        Assert.Equal(2.5m, target.EmbeddedMaterials.Sum(m => m.QuantityKg));
        Assert.True(world.Module<UtilitiesModule>().ServiceFor("workshop-bench")!.Operational);
        Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
    }

    [Fact]
    public void ConstructionInstallsNewStorageAndSeparateConduitLayers()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new PlanBuildCommand(new("storage", "stockpile", new(4, 8), 3, 2, "salvage")));
        Until(scheduler, () => world.Module<ConstructionModule>().FindJob("storage")?.Status == ConstructionStatus.Completed);
        var store = world.Module<InventoryModule>().FindStockpile("store/storage")!;
        Assert.Equal(60, store.CapacityKg);
        Assert.Equal(new CellPosition(4, 8), store.Position);
        Assert.True(world.Module<CompoundModule>().IsWalkable(new(4, 8)));
        foreach (string definition in new[] { "power-cable", "water-pipe", "floor" })
        {
            scheduler.Queue(new PlanBuildCommand(new(definition, definition, new(6, 6), 3, 2, "salvage")));
            Until(scheduler, () => world.Module<ConstructionModule>().FindJob(definition)?.Status == ConstructionStatus.Completed);
        }
        Assert.True(world.Module<UtilitiesModule>().HasNode(UtilityKind.Power, new(6, 6)));
        Assert.True(world.Module<UtilitiesModule>().HasNode(UtilityKind.Water, new(6, 6)));
        Assert.Equal(new Tile(Terrain.Floor, true), world.Module<CompoundModule>().TileAt(new(6, 6)));
        Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
        PhysicalSave.ValidateWorld(world);
    }

    [Fact]
    public void UtilityShortageHonorsPriorityAndWaterIsolationPreservesOtherBranch()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new SetBuildingConditionCommand("generator", .5m)); scheduler.Advance();
        var utilities = world.Module<UtilitiesModule>();
        Assert.True(utilities.ServiceFor("well-pump")!.Operational);
        Assert.True(utilities.ServiceFor("clinic-station")!.Operational);
        Assert.Contains("capacity", utilities.ServiceFor("workshop-bench")!.BlockedReason);
        scheduler.Queue(new SetBuildingConditionCommand("generator", 1));
        scheduler.Queue(new SetConduitEnabledCommand(UtilityKind.Water, new(10, 8), false)); scheduler.Advance();
        Assert.True(utilities.ServiceFor("workshop-bench")!.Operational);
        Assert.False(utilities.ServiceFor("clinic-station")!.Operational);
        scheduler.Queue(new SetConduitEnabledCommand(UtilityKind.Water, new(10, 8), true)); scheduler.Advance();
        Assert.True(utilities.ServiceFor("clinic-station")!.Operational);
    }

    [Fact]
    public void WorkRequiresPhysicalArrivalAndStopsAtBrokenStation()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        var project = world.Module<ProjectBoardModule>().Find("inspect-pump")!;
        scheduler.Queue(new AssignStaffCommand(1, project.Id)); scheduler.FastForward(4);
        Assert.Equal(0, project.EffortCompleted);
        Assert.NotEqual(new CellPosition(18, 4), world.Module<CompoundModule>().PositionOf(1));
        scheduler.FastForward(24); Assert.True(project.EffortCompleted > 0);
        double progress = project.EffortCompleted;
        scheduler.Queue(new SetBuildingConditionCommand("workshop-bench", 0)); scheduler.FastForward(8);
        Assert.Equal(progress, project.EffortCompleted);
        Assert.Contains("broken", CompoundWorkContext.Explain(world, project, world.Module<WorkforceModule>().Find(1)!));
    }

    [Fact]
    public void UnreachableStationNeverAccruesEffortAndLowWaterCapacityStopsConsumer()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        scheduler.Queue(new SetDoorOpenCommand(new(13, 5), false));
        scheduler.Queue(new AssignStaffCommand(1, "inspect-pump")); scheduler.FastForward(12);
        Assert.Equal(0, world.Module<ProjectBoardModule>().Find("inspect-pump")!.EffortCompleted);
        Assert.Contains("unreachable", PhysicalReport.Capture(world).Projects.Single(p => p.Id == "inspect-pump").BlockedReason);
        scheduler.Queue(new SetBuildingConditionCommand("well-pump", .5m)); scheduler.Advance();
        var utilities = world.Module<UtilitiesModule>();
        Assert.True(utilities.ServiceFor("clinic-station")!.Operational);
        Assert.Equal(4, utilities.ServiceFor("clinic-station")!.WaterAllocatedLph);
        Assert.Contains("water capacity", utilities.ServiceFor("workshop-bench")!.BlockedReason);
    }

    [Fact]
    public void MissingPurityNeverConsumesUnsuitableStockAndReportsReason()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        world.Module<CompoundModule>().AddDefinition(new BuildingDefinition("pure-part", "Pure part", BuildingKind.Floor,
            materials: [new("polymer.hdpe", 1, .99m)]));
        scheduler.Queue(new PlanBuildCommand(new("pure-site", "pure-part", new(5, 9), 3, 2, "salvage")));
        scheduler.FastForward(30);
        Assert.Contains("purity", world.Module<ConstructionModule>().FindJob("pure-site")!.BlockedReason);
        Assert.Empty(world.Module<LogisticsModule>().Jobs);
        Assert.Equal(20, world.Module<InventoryModule>().FindBatch("salvage-hdpe")!.QuantityKg);
        Assert.Equal(200, PhysicalReport.Capture(world).TotalTrackedKg);
    }

    [Fact]
    public void SaveValidationRejectsOrphanClaimsAndOutOfBoundsNodes()
    {
        var (world, scheduler, _) = PhysicalCompoundScenario.Create();
        Assert.True(world.Module<InventoryModule>().TryReserveStock("scavenger.build/missing/input/0", "salvage-hdpe", 1));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(scheduler, "bad claim")));
        world.Module<InventoryModule>().ReleaseReservations("scavenger.build/missing/input/0");
        world.Module<UtilitiesModule>().AddNode(UtilityKind.Power, new(100, 100));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(scheduler, "bad node")));
    }

    [Fact]
    public void RoofAndEnclosureAreDerivedAndRoomDesignationsCannotOverlap()
    {
        var (world, _, _) = PhysicalCompoundScenario.Create();
        var compound = world.Module<CompoundModule>(); var room = compound.Rooms[0];
        var cell = new CellPosition(room.Area.X, room.Area.Y);
        compound.SetTile(cell, new Tile(Terrain.Floor));
        Assert.False(compound.InspectRoom(room).Roofed);
        compound.SetTile(new(room.Area.X - 1, room.Area.Y + 1), new Tile(Terrain.Ground));
        Assert.False(compound.InspectRoom(room).Enclosed);
        Assert.Throws<InvalidOperationException>(() => compound.DesignateRoom(new("overlap", "Overlap", "Test", room.Area)));
        Assert.Throws<ArgumentOutOfRangeException>(() => compound.DesignateRoom(new("outside", "Outside", "Test", new(25, 17, 2, 2))));
    }

    private static void Until(Scheduler scheduler, Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) scheduler.Advance();
        Assert.True(condition(), "Simulation did not reach the expected state within 250 ticks.");
    }
    private static string State(Scheduler scheduler) => JsonSerializer.Serialize(SaveManager.Capture(scheduler, "test").World, SaveManager.Options);
}
