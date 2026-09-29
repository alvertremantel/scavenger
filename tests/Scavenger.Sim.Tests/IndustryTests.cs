using System.Text.Json;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Tests;

public class IndustryTests
{
    private static string State(Scheduler scheduler) => JsonSerializer.Serialize(SaveManager.Capture(scheduler, "industry").World, SaveManager.Options);
    private static void Until(Scheduler scheduler, Func<bool> done, int limit = 2000)
    {
        decimal mass = PhysicalReport.Capture(scheduler.World).TotalTrackedKg;
        for (int i = 0; i < limit && !done(); i++)
        {
            scheduler.Advance();
            Assert.InRange(Math.Abs(mass - PhysicalReport.Capture(scheduler.World).TotalTrackedKg), 0, .000000000001m);
            PhysicalSave.ValidateWorld(scheduler.World);
        }
        Assert.True(done(), string.Join("\n", scheduler.World.Module<ProductionModule>().Jobs.Select(j => $"{j.Plan.Id}: {j.Status} {j.BlockedReason}")) + "\n" + string.Join("\n", scheduler.World.Module<ComputingModule>().Jobs.Select(j => $"{j.Plan.Id}: {j.Status} {j.BlockedReason}")) + "\n" + string.Join("\n", scheduler.World.Module<LogisticsModule>().Jobs.Where(j => j.Active).Select(j => $"{j.Request.Id}: {j.Status} {j.BlockedReason}")) + "\n" + string.Join("\n", scheduler.World.Module<UtilitiesModule>().Services.Where(s => !s.Operational).Select(s => $"{s.BuildingId}: {s.BlockedReason}")));
    }
    private static void Task(Scheduler scheduler, string id, ComputerTaskKind kind, IReadOnlyList<string>? parts = null, string? asset = null, string? target = null)
    {
        var command = new QueueComputerTaskCommand(new(id, kind, "rig-1", kind is ComputerTaskKind.RuleMonitor or ComputerTaskKind.ModelMonitor ? null : 1, 2, parts, asset, target));
        Assert.True(command.CanExecute(scheduler.World)); scheduler.Queue(command);
        Until(scheduler, () => scheduler.World.Module<ComputingModule>().Job(id) is { Status: ComputerTaskStatus.Completed } or { Status: ComputerTaskStatus.Running, Plan.Kind: ComputerTaskKind.RuleMonitor or ComputerTaskKind.ModelMonitor });
    }
    private static void RecoverClips(Scheduler scheduler)
    {
        IndustrialScenario.QueueRecoveryOrders(scheduler);
        Until(scheduler, () => scheduler.World.Module<InventoryModule>().Batches.Where(b => b.StockpileId == "workshop" && b.MaterialId == "part.polymer").Sum(b => b.QuantityKg) >= 1.6m);
        foreach (var order in scheduler.World.Module<OrdersModule>().Orders) scheduler.Queue(new SetStockOrderEnabledCommand(order.Plan.Id, false));
        scheduler.Advance();
    }
    private static void AssembleComputer(Scheduler scheduler)
    {
        RecoverClips(scheduler);
        Task(scheduler, "diagnose-kit", ComputerTaskKind.DiagnoseComponents, IndustrialScenario.StarterKit.Concat(["usb-archive", "sensor-a", "controller-a"]).ToArray());
        Task(scheduler, "repair-fan", ComputerTaskKind.RepairComponent, ["fan-a"]);
        Task(scheduler, "assemble", ComputerTaskKind.Assemble, IndustrialScenario.StarterKit);
        scheduler.Queue(new SetComputerPoweredCommand("rig-1", true)); scheduler.Advance();
    }
    [Fact]
    public void FreshScenarioAndIdleFuelAccountingAreValid()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); PhysicalSave.ValidateWorld(world);
        decimal mass = PhysicalReport.Capture(world).TotalTrackedKg;
        scheduler.FastForward(100); PhysicalSave.ValidateWorld(world);
        Assert.InRange(Math.Abs(mass - PhysicalReport.Capture(world).TotalTrackedKg), 0, .000000000001m);
        Assert.True(world.Module<EnergyModule>().GeneratedKwh > 0);
        Assert.True(world.Module<EnergyModule>().SpentFuelKg > 0);
        var loaded = PhysicalSave.Load(SaveManager.Save(scheduler, "idle"));
        Assert.Equal(State(scheduler), State(loaded)); scheduler.FastForward(100); loaded.FastForward(100); Assert.Equal(State(scheduler), State(loaded));
    }
    [Fact]
    public void RecoveryChainProducesRepairPartsAndExplicitWasteWithPhysicalDelivery()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); RecoverClips(scheduler);
        Assert.All(world.Module<ProductionModule>().Jobs.Where(j => j.Status == ProductionStatus.Completed), job =>
            Assert.All(job.Products, p => Assert.Equal(HaulStatus.Delivered, world.Module<LogisticsModule>().FindJob(p.HaulId!)!.Status)));
        Assert.True(world.Module<InventoryModule>().StoredKg("waste") > 0);
        int count = world.Module<ProductionModule>().Jobs.Count; scheduler.FastForward(50);
        Assert.Equal(count, world.Module<ProductionModule>().Jobs.Count);
    }
    [Fact]
    public void RecoveredComputerReachesRulesTrainingValidationAndModelMonitoring()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); AssembleComputer(scheduler);
        var computing = world.Module<ComputingModule>(); Assert.Null(computing.HardwareReason("rig-1"));
        Assert.Equal(32, computing.RamGiB("rig-1")); Assert.Equal(12, computing.VramGiB("rig-1"));
        foreach (string asset in new[] { "offline-os", "python-runtime", "gpu-driver", "quant-runtime", "model-4b", "model-tokenizer", "training-textbook", "telemetry-tools", "rule-control" })
            Task(scheduler, $"install-{asset}", ComputerTaskKind.InstallAsset, asset: asset);
        Task(scheduler, "study", ComputerTaskKind.Study, asset: "training-textbook");
        Task(scheduler, "interface", ComputerTaskKind.InstallTelemetry, ["sensor-a", "controller-a"], target: "dryer");
        Task(scheduler, "rules", ComputerTaskKind.RuleMonitor);
        Until(scheduler, () => computing.Samples.Count >= 48);
        Task(scheduler, "labels", ComputerTaskKind.LabelTelemetry);
        Task(scheduler, "train", ComputerTaskKind.TrainAdapter, asset: "model-4b");
        Assert.Null(world.Module<Haldane.CareerSim.Work.WorkforceModule>().ReservationFor(1));
        Task(scheduler, "validate", ComputerTaskKind.ValidateAdapter, asset: "adapter/train");
        Assert.True(computing.Adapter("adapter/train")!.ValidationScore >= .75m);
        Task(scheduler, "model", ComputerTaskKind.ModelMonitor, asset: "adapter/train");
        Assert.True(computing.CanControl(world, "dryer"));
        scheduler.Queue(new SetBuildingConditionCommand("dryer", .90m)); scheduler.FastForward(2);
        scheduler.Queue(new SetBuildingConditionCommand("dryer", .85m)); scheduler.FastForward(2);
        Assert.Contains(world.Events.Entries, e => e.Kind == IndustryEvents.ComputerAlert && e.Description.Contains("forecasts", StringComparison.Ordinal));
        var loaded = PhysicalSave.Load(SaveManager.Save(scheduler, "endgame")); Assert.Equal(State(scheduler), State(loaded));
        scheduler.FastForward(20); loaded.FastForward(20); Assert.Equal(State(scheduler), State(loaded));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(90)]
    [InlineData(240)]
    [InlineData(500)]
    public void MidRecoverySavesKeepInputsPromisesAndMovementDeterministic(int tick)
    {
        var (_, original, _) = IndustrialScenario.Create(83); IndustrialScenario.QueueRecoveryOrders(original); original.FastForward(tick);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, "mid-process")); Assert.Equal(State(original), State(loaded));
        original.FastForward(300); loaded.FastForward(300); Assert.Equal(State(original), State(loaded)); PhysicalSave.ValidateWorld(original.World);
    }
    [Theory]
    [InlineData(ProductionStatus.WaitingForInputs)]
    [InlineData(ProductionStatus.Processing)]
    [InlineData(ProductionStatus.Delivering)]
    public void CancellationReleasesPromisesAndLeavesMaterialPhysical(ProductionStatus state)
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); scheduler.Queue(new QueueProductionCommand(IndustrialScenario.ProcessPlan("wash", "wash-hdpe")));
        Until(scheduler, () => world.Module<ProductionModule>().Find("wash") is { } job && job.Status == state
            && (state != ProductionStatus.WaitingForInputs || job.Site.Deliveries.Any(id => world.Module<LogisticsModule>().FindJob(id)!.Status == HaulStatus.InTransit)));
        var process = world.Module<ProductionModule>().Find("wash")!;
        decimal staged = world.Module<InventoryModule>().StoredKg(process.Site.LocalStoreId) + process.Site.Deliveries.Select(world.Module<LogisticsModule>().FindJob).Sum(j => j!.Cargo?.QuantityKg ?? 0);
        scheduler.Queue(new CancelProductionCommand("wash")); scheduler.Advance();
        Until(scheduler, () => !world.Module<LogisticsModule>().Jobs.Any(j => j.Active));
        Assert.Equal(ProductionStatus.Cancelled, process.Status); Assert.Empty(process.Site.InputOwners);
        Assert.DoesNotContain(world.Module<InventoryModule>().CapacityReservations, c => c.Owner.StartsWith(process.Owner, StringComparison.Ordinal));
        Assert.DoesNotContain(world.Module<InventoryModule>().StockReservations, c => c.Owner.StartsWith(process.Owner, StringComparison.Ordinal));
        Assert.Null(world.Module<EquipmentModule>().OwnerOf("washer"));
        Assert.Equal(staged, world.Module<InventoryModule>().StoredKg(process.Site.LocalStoreId));
    }
    [Fact]
    public void WasteSpaceBlocksCommitAndDoesNotHoldLaborOrConsumeInputs()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); var inventory = world.Module<InventoryModule>();
        Assert.True(inventory.TryReserveCapacity("fill-waste", "waste", 200));
        scheduler.Queue(new QueueProductionCommand(IndustrialScenario.ProcessPlan("wash", "wash-hdpe")));
        Until(scheduler, () => world.Module<ProductionModule>().Find("wash")?.BlockedReason?.Contains("receiving space", StringComparison.OrdinalIgnoreCase) == true);
        var job = world.Module<ProductionModule>().Find("wash")!; Assert.Equal(0, job.EffortCompleted); Assert.Equal(3, inventory.StoredKg(job.Site.LocalStoreId));
        Assert.Empty(job.Site.InputOwners); Assert.Null(world.Module<Haldane.CareerSim.Work.WorkforceModule>().ReservationFor(4));
        inventory.ReleaseReservations("fill-waste"); Until(scheduler, () => job.Status == ProductionStatus.Completed);
    }
    [Fact]
    public void WholeComponentsCannotBeSplitAndMissingOrDuplicatedOwnershipIsRejected()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); var inventory = world.Module<InventoryModule>();
        world.Module<LogisticsModule>().AddRoute(new TransportRoute("electronics-workshop", "electronics-cache", "workshop", 1, 6, usesWorldMovement: true));
        Assert.False(new QueuePhysicalHaulCommand(new("split-board", "component/board-a", "electronics-workshop", 2, .1m)).CanExecute(world));
        inventory.AddBatch(new MaterialBatch("duplicate-board", "item/board-a", "electronics-cache", 1.2m, new BatchQuality(1, "identified")));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(scheduler, "duplicate")));
    }
    [Fact]
    public void DiagnosisMakesCompatibilitySpecificAndDeadPartsStayIrreparable()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); var computing = world.Module<ComputingModule>();
        Assert.Contains("diagnosis", computing.Compatibility(IndustrialScenario.StarterKit));
        Task(scheduler, "diagnosis", ComputerTaskKind.DiagnoseComponents, IndustrialScenario.StarterKit.Concat(["ram-incompatible", "cpu-dead", "ssd-corrupt"]).ToArray());
        Assert.Contains("repair", computing.Compatibility(IndustrialScenario.StarterKit));
        string[] withoutFanFault = IndustrialScenario.StarterKit.Select(id => id == "ram-b" ? "ram-incompatible" : id).ToArray();
        Assert.Contains("RAM", computing.Compatibility(withoutFanFault, requireDiagnosis: false));
        scheduler.Queue(new QueueComputerTaskCommand(new("dead-repair", ComputerTaskKind.RepairComponent, "rig-1", 1, Components: ["cpu-dead"]))); scheduler.Advance();
        Assert.Contains("replacement", computing.Job("dead-repair")!.BlockedReason); Assert.Equal(ComponentFault.DeadSilicon, computing.Component("cpu-dead")!.Fault);
    }
    private static readonly Lazy<string> AssembledSave = new(() =>
    {
        var (_, scheduler, _) = IndustrialScenario.Create(); AssembleComputer(scheduler); return SaveManager.Save(scheduler, "assembled fixture");
    });
    private static Scheduler ReadyComputer() => PhysicalSave.Load(AssembledSave.Value);
    private static void InstallSoftware(Scheduler scheduler)
    { foreach (string asset in IndustrialWalkthrough.Software) Task(scheduler, $"install-{asset}", ComputerTaskKind.InstallAsset, asset: asset); }
    private static readonly Lazy<string> ModelReadySave = new(() =>
    {
        var scheduler = ReadyComputer(); InstallSoftware(scheduler); Task(scheduler, "study", ComputerTaskKind.Study, asset: "training-textbook");
        Task(scheduler, "interface", ComputerTaskKind.InstallTelemetry, ["sensor-a", "controller-a"], target: "dryer");
        Task(scheduler, "rules", ComputerTaskKind.RuleMonitor); Until(scheduler, () => scheduler.World.Module<ComputingModule>().Samples.Count >= 48);
        Task(scheduler, "labels", ComputerTaskKind.LabelTelemetry); return SaveManager.Save(scheduler, "model-ready fixture");
    });
    [Fact]
    public void TrainingCheckpointsPauseOnPowerLossAndSavesResumeExactly()
    {
        var scheduler = PhysicalSave.Load(ModelReadySave.Value); var world = scheduler.World;
        scheduler.Queue(new QueueComputerTaskCommand(new("train", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b")));
        Until(scheduler, () => world.Module<ComputingModule>().Job("train")?.Progress >= 9);
        var job = world.Module<ComputingModule>().Job("train")!;
        Assert.Null(world.Module<Haldane.CareerSim.Work.WorkforceModule>().ReservationFor(1));
        decimal checkpoint = job.Checkpoint; Assert.True(checkpoint > 0 && checkpoint < job.Progress);
        var loaded = PhysicalSave.Load(SaveManager.Save(scheduler, "mid-training")); Assert.Equal(State(scheduler), State(loaded));
        foreach (var current in new[] { scheduler, loaded }) { current.Queue(new SetComputerPoweredCommand("rig-1", false)); current.FastForward(3); }
        Assert.Equal(checkpoint, job.Progress); Assert.Equal(1, job.Restarts); Assert.Equal(State(scheduler), State(loaded));
        foreach (var current in new[] { scheduler, loaded }) { current.Queue(new SetComputerPoweredCommand("rig-1", true)); current.FastForward(80); }
        Assert.Equal(ComputerTaskStatus.Completed, job.Status); Assert.Equal(State(scheduler), State(loaded));
    }
    [Fact]
    public void AuthoredWorkloadsRespectVramRamAndUnvalidatedModelGates()
    {
        var scheduler = PhysicalSave.Load(ModelReadySave.Value); var world = scheduler.World; var computing = world.Module<ComputingModule>();
        var snapshot = (ComputingSnapshot)computing.ToSnapshot();
        world.Register(ComputingModule.FromSnapshot(snapshot with { Components = snapshot.Components.Select(c => c.Id == "gpu-3060" ? c with { DefinitionId = "rtx3060-8" } : c).ToArray() }));
        computing = world.Module<ComputingModule>(); scheduler.Queue(new QueueComputerTaskCommand(new("too-little-vram", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b"))); scheduler.Advance();
        Assert.Contains("VRAM", computing.Job("too-little-vram")!.BlockedReason);
        scheduler.Queue(new CancelComputerTaskCommand("too-little-vram")); scheduler.Advance();
        world.Register(ComputingModule.FromSnapshot(snapshot with { Definitions = snapshot.Definitions.Select(d => d.Id == "ram-ddr4-16" ? new ComponentDefinition(d.Id, "DDR4 / authored 8 GiB fixture", ComponentKind.Memory, d.MassKg, memoryType: "DDR4", ramGiB: 8) : d).ToArray() }));
        computing = world.Module<ComputingModule>(); scheduler.Queue(new QueueComputerTaskCommand(new("overbooked-ram", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b"))); scheduler.Advance();
        Assert.Contains("RAM", computing.Job("overbooked-ram")!.BlockedReason); PhysicalSave.ValidateWorld(world);
        scheduler = PhysicalSave.Load(ModelReadySave.Value); computing = scheduler.World.Module<ComputingModule>();
        Task(scheduler, "train", ComputerTaskKind.TrainAdapter, asset: "model-4b");
        scheduler.Queue(new QueueComputerTaskCommand(new("unvalidated", ComputerTaskKind.ModelMonitor, "rig-1", AssetId: "adapter/train"))); scheduler.Advance();
        Assert.Contains("validation", computing.Job("unvalidated")!.BlockedReason);
    }
    [Fact]
    public void DataAndPowerLossImmediatelyStopOperatorFreeProcessing()
    {
        var scheduler = PhysicalSave.Load(ModelReadySave.Value); var world = scheduler.World; var computing = world.Module<ComputingModule>();
        var production = world.Module<ProductionModule>();
        Assert.False(new QueueProductionCommand(IndustrialScenario.ProcessPlan("auto-mould", "mould-clips", true)).CanExecute(world));
        scheduler.Queue(new QueueProductionCommand(IndustrialScenario.ProcessPlan("wash-for-automation", "wash-hdpe")));
        Until(scheduler, () => production.Find("wash-for-automation")?.Status == ProductionStatus.Completed);
        scheduler.Queue(new QueueProductionCommand(IndustrialScenario.ProcessPlan("auto-dry", "dry-hdpe", true)));
        Until(scheduler, () => production.Find("auto-dry")?.EffortCompleted > 0);
        var job = production.Find("auto-dry")!; Assert.Null(world.Module<Haldane.CareerSim.Work.WorkforceModule>().ReservationFor(3));
        decimal progress = job.EffortCompleted;
        scheduler.Queue(new SetConduitEnabledCommand(UtilityKind.Data, new(20, 2), false)); scheduler.FastForward(4);
        Assert.Equal(progress, job.EffortCompleted); Assert.False(computing.CanControl(world, "dryer"));
        scheduler.Queue(new SetConduitEnabledCommand(UtilityKind.Data, new(20, 2), true)); scheduler.Queue(new SetComputerPoweredCommand("rig-1", false)); scheduler.FastForward(2);
        Assert.Equal(progress, job.EffortCompleted);
        scheduler.Queue(new SetComputerPoweredCommand("rig-1", true)); Until(scheduler, () => job.Status == ProductionStatus.Completed);
    }
    [Fact]
    public void ExhaustedFuelUsesBatteryAndCannotGenerateUnpaidEnergy()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); var inventory = world.Module<InventoryModule>(); var energy = world.Module<EnergyModule>();
        Assert.True(inventory.TryReserveStock("fixture-remove-fuel", "tank-diesel", 79.99m)); inventory.TakeReservedStock("fixture-remove-fuel");
        decimal mass = PhysicalReport.Capture(world).TotalTrackedKg; scheduler.Advance();
        Assert.InRange(energy.GeneratedKwh, 0, .01m / .3m);
        scheduler.FastForward(50); PhysicalSave.ValidateWorld(world);
        Assert.InRange(energy.SpentFuelKg, .009999999999m, .01m); Assert.InRange(Math.Abs(mass - PhysicalReport.Capture(world).TotalTrackedKg), 0, .000000000001m);
        Assert.False(world.Module<UtilitiesModule>().ServiceFor("well-pump")!.Operational);
    }
    [Fact]
    public void ConcurrentTrainingDoesNotOverbookMemoryAndCancelReleasesMachineClaims()
    {
        var scheduler = PhysicalSave.Load(ModelReadySave.Value); var world = scheduler.World; var computing = world.Module<ComputingModule>();
        scheduler.Queue(new QueueComputerTaskCommand(new("train-a", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b")));
        scheduler.Queue(new QueueComputerTaskCommand(new("train-b", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b")));
        Until(scheduler, () => computing.Job("train-a")?.Progress >= 9);
        Assert.Contains("RAM", computing.Job("train-b")!.BlockedReason); Assert.False(computing.Job("train-b")!.ResourceClaim);
        scheduler.Queue(new CancelComputerTaskCommand("train-a")); scheduler.Advance();
        Until(scheduler, () => computing.Job("train-b")!.Progress > 0);
        Assert.False(computing.Job("train-a")!.ResourceClaim); Assert.True(computing.Job("train-b")!.ResourceClaim);
        scheduler.Queue(new CancelComputerTaskCommand("train-b")); scheduler.Advance(); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void DiskPromisesPreventTrainingWhenOnlyExistingArchivesFit()
    {
        var scheduler = PhysicalSave.Load(ModelReadySave.Value); var world = scheduler.World; var computing = world.Module<ComputingModule>();
        decimal capacity = computing.UsedStorageGiB("rig-1") + .1m; var snapshot = (ComputingSnapshot)computing.ToSnapshot();
        world.Register(ComputingModule.FromSnapshot(snapshot with { Definitions = snapshot.Definitions.Select(d => d.Id == "ssd-nvme"
            ? new ComponentDefinition(d.Id, "Tight-capacity test drive", ComponentKind.Storage, d.MassKg, storageInterface: "NVMe", storageGiB: capacity) : d).ToArray() }));
        computing = world.Module<ComputingModule>(); scheduler.Queue(new QueueComputerTaskCommand(new("disk-full", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b"))); scheduler.Advance();
        Assert.Contains("storage", computing.Job("disk-full")!.BlockedReason); Assert.False(computing.Job("disk-full")!.ResourceClaim); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void InvalidSnapshotReferencesAndTelemetrySequenceAreRejected()
    {
        var (world, scheduler, _) = IndustrialScenario.Create();
        scheduler.Queue(new QueueProductionCommand(IndustrialScenario.ProcessPlan("wash", "wash-hdpe"))); scheduler.Advance();
        var original = (ProductionSnapshot)world.Module<ProductionModule>().ToSnapshot();
        world.Register(ProductionModule.FromSnapshot(original with { Jobs = original.Jobs.Select(j => j with { Plan = j.Plan with { StationId = "missing" } }).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(scheduler, "bad-site")));
        world.Register(ProductionModule.FromSnapshot(original));
        var computing = (ComputingSnapshot)world.Module<ComputingModule>().ToSnapshot();
        Assert.Throws<ArgumentException>(() => ComputingModule.FromSnapshot(computing with { NextSample = 0 }));
        world.Register(ComputingModule.FromSnapshot(computing with { Knowledge = [new(999, "adapter-training")] }));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(scheduler, "bad-knowledge")));
    }
    [Theory]
    [InlineData(5)]
    [InlineData(140)]
    [InlineData(420)]
    public void SavesDuringWholeComponentTransportKeepExactlyOneOwner(int tick)
    {
        var (_, original, _) = IndustrialScenario.Create();
        original.Queue(new QueueComputerTaskCommand(new("diagnose", ComputerTaskKind.DiagnoseComponents, "rig-1", 1, Components: IndustrialScenario.StarterKit)));
        original.FastForward(tick); var loaded = PhysicalSave.Load(SaveManager.Save(original, "parts-in-transit"));
        Assert.Equal(State(original), State(loaded)); original.FastForward(200); loaded.FastForward(200); Assert.Equal(State(original), State(loaded));
        PhysicalSave.ValidateWorld(original.World);
    }
    [Fact]
    public void SaveDuringAssemblyRestoresStagedPartsAndExclusiveLabor()
    {
        var (_, original, _) = IndustrialScenario.Create(); RecoverClips(original);
        Task(original, "diagnose", ComputerTaskKind.DiagnoseComponents, IndustrialScenario.StarterKit);
        Task(original, "repair", ComputerTaskKind.RepairComponent, ["fan-a"]);
        original.Queue(new QueueComputerTaskCommand(new("assembly", ComputerTaskKind.Assemble, "rig-1", 1, Components: IndustrialScenario.StarterKit)));
        Until(original, () => original.World.Module<ComputingModule>().Job("assembly")?.Progress > 0);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, "assembly")); Assert.Equal(State(original), State(loaded));
        original.FastForward(20); loaded.FastForward(20); Assert.Equal(State(original), State(loaded));
        Assert.Equal(8, original.World.Module<ComputingModule>().Computer("rig-1")!.ComponentIds.Count);
    }
    [Fact]
    public void ManualTransfersEnableFixtureRepairAndPreflightDoesNotMutateRoutes()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); RecoverClips(scheduler);
        var transfer = new StoreTransferCommand(new("repair-steel", "salvage-metal.steel", "workshop", 2, .2m));
        Until(scheduler, () => transfer.CanExecute(world));
        string before = State(scheduler); Assert.True(transfer.CanExecute(world)); Assert.Equal(before, State(scheduler));
        scheduler.Queue(transfer); Until(scheduler, () => world.Module<LogisticsModule>().FindJob("repair-steel")?.Status == HaulStatus.Delivered);
        scheduler.Queue(new SetBuildingConditionCommand("dryer", .3m));
        scheduler.Queue(new PlanBuildCommand(new("repair-dryer", "dryer", new(14, 6), 3, 2, "workshop", "dryer")));
        Until(scheduler, () => world.Module<ConstructionModule>().FindJob("repair-dryer")?.Status == ConstructionStatus.Completed);
        Assert.Equal(1, world.Module<CompoundModule>().FindBuilding("dryer")!.Condition);
        Assert.Equal(.3m, world.Module<CompoundModule>().FindBuilding("dryer")!.EmbeddedMaterials.Sum(m => m.QuantityKg));
        var loaded = SaveManager.Load(SaveManager.Save(scheduler, "manual transfer replay"), PhysicalSave.CreateRegistry());
        var (_, replay, _) = IndustrialScenario.Create(); var commands = loaded.CommandLog.ToLookup(c => c.Tick);
        while (replay.World.Clock.Tick < world.Clock.Tick) { foreach (var entry in commands[replay.World.Clock.Tick]) replay.Queue(entry.Command); replay.Advance(); }
        Assert.Equal(State(scheduler), State(replay));
    }
    [Fact]
    public void MissingDependenciesCorruptArchivesAndUnstudiedKnowledgeBlockWork()
    {
        var scheduler = ReadyComputer(); var computing = scheduler.World.Module<ComputingModule>();
        foreach (var (id, kind, asset, reason) in new[] {
            ("missing-os", ComputerTaskKind.InstallAsset, "python-runtime", "operating system"),
            ("corrupt", ComputerTaskKind.InstallAsset, "corrupt-runtime", "operating system"),
            ("unstudied", ComputerTaskKind.TrainAdapter, "model-4b", "operating system") })
        {
            scheduler.Queue(new QueueComputerTaskCommand(new(id, kind, "rig-1", 1, AssetId: asset))); scheduler.Advance();
            Assert.Contains(reason, computing.Job(id)!.BlockedReason); scheduler.Queue(new CancelComputerTaskCommand(id)); scheduler.Advance();
        }
        Task(scheduler, "os", ComputerTaskKind.InstallAsset, asset: "offline-os");
        scheduler.Queue(new QueueComputerTaskCommand(new("bad-archive", ComputerTaskKind.InstallAsset, "rig-1", 1, AssetId: "corrupt-runtime")));
        scheduler.Queue(new QueueComputerTaskCommand(new("no-knowledge", ComputerTaskKind.TrainAdapter, "rig-1", 1, AssetId: "model-4b"))); scheduler.Advance();
        Assert.Contains("integrity", computing.Job("bad-archive")!.BlockedReason); Assert.Contains("Study", computing.Job("no-knowledge")!.BlockedReason);
    }
    [Fact]
    public void RoutineServiceConservesMassAndMountedDataFollowsTheRemovedDrive()
    {
        var scheduler = ReadyComputer(); var world = scheduler.World; var computing = world.Module<ComputingModule>();
        Task(scheduler, "os", ComputerTaskKind.InstallAsset, asset: "offline-os");
        scheduler.Queue(new SetComputerPoweredCommand("rig-1", false)); scheduler.Advance();
        Task(scheduler, "service-ssd", ComputerTaskKind.ServiceComponent, ["ssd-a"]);
        Assert.Equal(.95m, computing.Component("ssd-a")!.Condition);
        Assert.Contains(world.Module<InventoryModule>().Batches, b => b.MaterialId == "waste.washwater" && b.StockpileId == "worksite/service-ssd" && b.QuantityKg == .05m);
        Task(scheduler, "remove-ssd", ComputerTaskKind.RemoveComponent, ["ssd-a"]);
        Assert.False(computing.HasCapability("rig-1", "os")); Assert.Single(computing.Copies);
        Task(scheduler, "reinstall-ssd", ComputerTaskKind.Assemble, ["ssd-a"]);
        Assert.True(computing.HasCapability("rig-1", "os")); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void FiniteBatteriesBridgeGeneratorLossAndNetworkIsolationDoesNotShareEnergy()
    {
        var (world, scheduler, _) = IndustrialScenario.Create(); var energy = world.Module<EnergyModule>();
        scheduler.Queue(new SetGeneratorEnabledCommand("generator", false)); scheduler.FastForward(4);
        Assert.Equal(0, energy.GeneratedKwh); Assert.True(energy.Battery("battery-bank")!.StoredKwh < 1.5m); Assert.True(energy.LossKwh > 0);
        scheduler.FastForward(40); Assert.InRange(energy.Battery("battery-bank")!.StoredKwh, 0, .1m);
        Assert.False(world.Module<UtilitiesModule>().ServiceFor("well-pump")!.Operational); PhysicalSave.ValidateWorld(world);
        var fresh = IndustrialScenario.Create();
        fresh.Scheduler.Queue(new SetConduitEnabledCommand(UtilityKind.Power, new(17, 2), false)); fresh.Scheduler.Queue(new SetGeneratorEnabledCommand("generator", false)); fresh.Scheduler.Advance();
        Assert.Equal(1.5m, fresh.World.Module<EnergyModule>().Battery("battery-bank")!.StoredKwh);
        Assert.False(fresh.World.Module<UtilitiesModule>().ServiceFor("well-pump")!.Operational);
    }
    [Fact]
    public void SavedWalkthroughPlayerCommandsReplayExactlyThroughModelDeployment()
    {
        var (_, original, _) = IndustrialScenario.Create(314);
        UntilScripted(original, () => original.World.Module<ComputingModule>().Job("demo-model-monitor")?.Status == ComputerTaskStatus.Running);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, "command log")); var (_, replay, _) = IndustrialScenario.Create(314);
        var commands = SaveManager.Load(SaveManager.Save(loaded, "history"), PhysicalSave.CreateRegistry()).CommandLog.ToLookup(e => e.Tick);
        while (replay.World.Clock.Tick < original.World.Clock.Tick)
        { foreach (var entry in commands[replay.World.Clock.Tick]) replay.Queue(entry.Command); replay.Advance(); }
        Assert.Equal(State(original), State(replay));
        Assert.Equal(ProductionStatus.Completed, original.World.Module<ProductionModule>().Find("demo-auto-dry")!.Status);
    }
    private static void UntilScripted(Scheduler scheduler, Func<bool> done)
    { for (int i = 0; i < 2400 && !done(); i++) { IndustrialWalkthrough.QueueNext(scheduler); scheduler.Advance(); PhysicalSave.ValidateWorld(scheduler.World); } Assert.True(done()); }
}
