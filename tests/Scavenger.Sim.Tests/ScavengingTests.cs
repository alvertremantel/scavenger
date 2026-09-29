using System.Text.Json;
using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Tests;

public class ScavengingTests
{
    private static string State(Scheduler s) => JsonSerializer.Serialize(SaveManager.Capture(s, "expedition").World, SaveManager.Options);
    private static ScavengingModule Region(Scheduler s) => s.World.Module<ScavengingModule>();
    private static ExpeditionPlan Pharmacy(string id = "pharmacy-run") => new(id, "pharmacy", [2, 4], [new("stock-sealed", .1m), new("excipient", .12m), new("sterile-water", .8m)]);
    private static void Until(Scheduler s, Func<bool> done, bool respond = true, int limit = 1200)
    {
        decimal mass = PhysicalReport.Capture(s.World).TotalTrackedKg;
        for (int i = 0; i < limit && !done(); i++)
        {
            if (respond) foreach (var e in Region(s).Expeditions.Where(e => e.Status == ExpeditionStatus.Decision)) s.Queue(new RespondToExpeditionCommand(e.Plan.Id, ExpeditionResponse.Wait));
            s.Advance(); PhysicalSave.ValidateWorld(s.World);
            Assert.InRange(Math.Abs(mass - PhysicalReport.Capture(s.World).TotalTrackedKg), 0, .00000000001m);
        }
        Assert.True(done(), string.Join("\n", Region(s).Expeditions.Select(e => $"{e.Plan.Id}: {e.Status} {e.Reason}"))
            + "\n" + string.Join("\n", s.World.Module<ProductionModule>().Jobs.Select(j => $"{j.Plan.Id}: {j.Status} {j.BlockedReason}")));
    }
    private static void Dispatch(Scheduler s, ExpeditionPlan plan)
    { var command = new DispatchExpeditionCommand(plan); Assert.True(command.CanExecute(s.World), Region(s).Explain(s.World, plan)); s.Queue(command); s.Advance(); PhysicalSave.ValidateWorld(s.World); }
    private static void Complete(Scheduler s, ExpeditionPlan plan)
    { Dispatch(s, plan); Until(s, () => Region(s).Find(plan.Id) is { Active: false }); }
    private static void AlterRegion(Scheduler s, Func<LocationSnapshot, LocationSnapshot> change)
    {
        var snapshot = (ScavengingSnapshot)Region(s).ToSnapshot();
        s.World.Register(ScavengingModule.FromSnapshot(snapshot with { Locations = snapshot.Locations.Select(change).ToArray() }));
        PhysicalSave.ValidateWorld(s.World);
    }
    [Fact]
    public void AuthoredLocationsHaveDistinctFiniteResourcesAndNoStartingComputer()
    {
        var (world, s, _) = ScavengingScenario.Create(); var module = Region(s);
        var lab = module.Location("laboratory")!; var homes = module.Location("homes")!;
        Assert.True(lab.Definition.Salvage.Where(e => e.MaterialId.StartsWith("glass.")).Sum(e => e.InitialKg) > homes.Definition.Salvage.Where(e => e.MaterialId.StartsWith("glass.")).Sum(e => e.InitialKg) * 10);
        Assert.DoesNotContain(module.Location("hardware")!.Definition.Salvage, e => e.MaterialId.StartsWith("medicine.") || e.MaterialId == "water.sterile");
        Assert.Empty(world.Module<ComputingModule>().Components); Assert.Empty(world.Module<ComputingModule>().Assets);
        Assert.True(module.HealthOf(1)!.Sick); Assert.False(world.Module<WorkforceModule>().Find(1)!.Present); PhysicalSave.ValidateWorld(world);
    }
    [Fact]
    public void DispatchPreflightIsReadOnlyAndRejectsBusyIllUnqualifiedOrOverloadedTeams()
    {
        var (_, s, _) = ScavengingScenario.Create(); var m = Region(s); string before = State(s);
        Assert.Null(m.Explain(s.World, Pharmacy())); Assert.Equal(before, State(s));
        Assert.Contains("ill", m.Explain(s.World, Pharmacy() with { Team = [1, 4] }));
        Assert.Contains("chemistry", m.Explain(s.World, Pharmacy() with { Team = [2, 3] }));
        Assert.Contains("distinct", m.Explain(s.World, Pharmacy() with { Team = [2, 2] }));
        Assert.Contains("12 kg", m.Explain(s.World, new("heavy", "hardware", [3], [new("timber-rack", 16)])));
        Assert.Contains("inaccessible", m.Explain(s.World, new("buried", "laboratory", [3, 4], [new("buried-glassware", 1)])));
        Assert.Equal(before, State(s)); Dispatch(s, Pharmacy());
        Assert.Contains("already assigned", m.Explain(s.World, Pharmacy("second")));
        Assert.False(new AssignStaffCommand(4, "prepare-clinic").CanExecute(s.World));
    }
    [Fact]
    public void ToolAndReceivingSpaceAreRequiredAndExclusivelyReserved()
    {
        var (_, s, _) = ScavengingScenario.Create(); var inv = s.World.Module<InventoryModule>();
        var toolRun = new ExpeditionPlan("copper", "hardware", [3], [new("copper-pipe", 1)]);
        Assert.True(inv.TryReserveStock("test", "starting-prybar", 1));
        Assert.Contains("tool.prybar", Region(s).Explain(s.World, toolRun)); inv.ReleaseReservations("test");
        decimal free = inv.FreeCapacityKg("salvage"); Assert.True(inv.TryReserveCapacity("test", "salvage", free));
        Assert.Contains("capacity", Region(s).Explain(s.World, Pharmacy())); inv.ReleaseReservations("test");
        Dispatch(s, toolRun); Assert.Contains(inv.StockReservations, r => r.Owner.StartsWith("scavenger.expedition/") && inv.FindBatch(r.BatchId)!.MaterialId == "tool.prybar");
        s.Queue(new RecallExpeditionCommand("copper")); s.Advance(); Assert.Empty(inv.StockReservations);
        Assert.DoesNotContain(inv.CapacityReservations, r => r.Owner.StartsWith("scavenger.expedition/")); PhysicalSave.ValidateWorld(s.World);
    }
    [Fact]
    public void ReturnedCargoPreservesBatchQualityAndStockCannotRespawn()
    {
        var (_, s, _) = ScavengingScenario.Create(); var l = Region(s).Location("pharmacy")!;
        Complete(s, Pharmacy()); Assert.Equal(.5m, l.Remaining("stock-sealed"));
        var batch = s.World.Module<InventoryModule>().Batches.Single(b => b.MaterialId == "medicine.precursor");
        Assert.Equal(.98m, batch.Quality.Purity); Assert.Equal("sealed fictional stock", batch.Quality.Grade); Assert.Equal(.1m, batch.QuantityKg);
        Assert.Equal(1.02m, Region(s).Find("pharmacy-run")!.Receipts.Sum(c => c.QuantityKg));
        Assert.False(new DispatchExpeditionCommand(Pharmacy("excess") with { Manifest = [new("stock-sealed", .6m)] }).CanExecute(s.World));
        Assert.All(s.World.Module<WorkforceModule>().Reservations, r => Assert.False(r.Owner.StartsWith("scavenger.expedition/")));
    }
    [Fact]
    public void DailyDecayAndRivalsRemoveRealStockWithAnExternalMassReceipt()
    {
        var (_, s, _) = ScavengingScenario.Create(); var l = Region(s).Location("homes")!; decimal initial = l.Remaining("bottle-glass");
        Until(s, () => s.World.Clock.Tick == 96);
        Assert.Equal(.75m, l.Integrity); Assert.Equal(initial - .4m, l.Remaining("bottle-glass")); Assert.True(Region(s).RivalRemovedKg > 0);
        var loaded = PhysicalSave.Load(SaveManager.Save(s, "daily decay")); Assert.Equal(State(s), State(loaded));
    }
    [Fact]
    public void DepletionDuringTravelProducesPartialCargoInsteadOfPromisingDistantOwnership()
    {
        var (_, s, _) = ScavengingScenario.Create(); s.FastForward(90);
        Complete(s, new("race", "homes", [2], [new("bottle-glass", 3)]));
        Assert.Equal(2.6m, Region(s).Find("race")!.Receipts.Single().QuantityKg);
    }
    [Fact]
    public void FieldDecisionStopsFastForwardAndRecallReturnsActualCargoWithoutTeleportation()
    {
        var (_, s, _) = ScavengingScenario.Create();
        AlterRegion(s, l => l.Definition.Id == "pharmacy" ? l with { Definition = l.Definition with { RivalPressure = 1, InitialIntegrity = 1 }, Integrity = 1 } : l);
        Dispatch(s, Pharmacy());
        var result = s.FastForward(100, new FastForwardStopConditions { InterruptCatalogue = ScavengingScenario.CreateCatalogue() });
        Assert.True(result.StoppedEarly); Assert.Equal(ExpeditionStatus.Decision, Region(s).Find("pharmacy-run")!.Status);
        s.Queue(new RespondToExpeditionCommand("pharmacy-run", ExpeditionResponse.Wait)); s.Advance();
        Until(s, () => Region(s).Find("pharmacy-run")!.Cargo.Count == 1);
        var e = Region(s).Find("pharmacy-run")!; s.Queue(new RecallExpeditionCommand(e.Plan.Id)); s.Advance();
        Assert.Equal(ExpeditionStatus.Returning, e.Status); Assert.DoesNotContain(s.World.Module<InventoryModule>().Batches, b => b.MaterialId == "medicine.precursor");
        Until(s, () => !e.Active); Assert.Single(e.Receipts); Assert.Equal(.1m, e.Receipts.Single().QuantityKg);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RecallInOutboundTravelReturnsOnlyTheDistanceAlreadyTravelled(int distance)
    {
        var (_, s, _) = ScavengingScenario.Create(); Dispatch(s, new("turn-back", "homes", [2], [new("bottle-glass", 1)]));
        Until(s, () => Region(s).Find("turn-back") is { Status: ExpeditionStatus.Outbound } e && e.ElapsedTravel == distance);
        var e = Region(s).Find("turn-back")!; long recalledAt = s.World.Clock.Tick;
        s.Queue(new RecallExpeditionCommand(e.Plan.Id)); s.Advance(); PhysicalSave.ValidateWorld(s.World);
        var loaded = PhysicalSave.Load(SaveManager.Save(s, "outbound recall"));
        Until(s, () => !e.Active); Until(loaded, () => !Region(loaded).Find(e.Plan.Id)!.Active);
        Assert.Empty(e.Receipts); Assert.Equal(State(s), State(loaded));
        Assert.InRange(s.World.Clock.Tick - recalledAt, 1, distance + 4);
    }
    [Fact]
    public void ClosingClinicDoorBlocksPhysicalUnloadingAndOpeningItResumes()
    {
        var (_, s, _) = ScavengingScenario.Create();
        Dispatch(s, Pharmacy() with { ReceivingStoreId = "clinic", UseProtection = false });
        Until(s, () => Region(s).Find("pharmacy-run")!.Status == ExpeditionStatus.Outbound);
        s.Queue(new SetDoorOpenCommand(new(14, 13), false)); s.Advance();
        Until(s, () => Region(s).Find("pharmacy-run")!.Status == ExpeditionStatus.Unloading);
        s.FastForward(5); Assert.Empty(Region(s).Find("pharmacy-run")!.Receipts); Assert.Contains("walkable", Region(s).Find("pharmacy-run")!.Reason);
        s.Queue(new SetDoorOpenCommand(new(14, 13), true)); s.Advance(); Until(s, () => !Region(s).Find("pharmacy-run")!.Active);
        Assert.All(s.World.Module<InventoryModule>().Batches.Where(b => b.MaterialId.StartsWith("medicine.")), b => Assert.Equal("clinic", b.StockpileId));
    }
    [Theory]
    [InlineData(ExpeditionStatus.Gathering)]
    [InlineData(ExpeditionStatus.LeavingCompound)]
    [InlineData(ExpeditionStatus.Outbound)]
    [InlineData(ExpeditionStatus.Searching)]
    [InlineData(ExpeditionStatus.Decision)]
    [InlineData(ExpeditionStatus.Returning)]
    [InlineData(ExpeditionStatus.Unloading)]
    public void SavesResumeEveryExpeditionPhaseIncludingDecisions(ExpeditionStatus phase)
    {
        var (_, original, _) = ScavengingScenario.Create();
        AlterRegion(original, l => l.Definition.Id == "pharmacy" ? l with { Definition = l.Definition with { RivalPressure = 1, InitialIntegrity = 1 }, Integrity = 1 } : l);
        Dispatch(original, Pharmacy()); Until(original, () => Region(original).Find("pharmacy-run")!.Status == phase, respond: phase != ExpeditionStatus.Decision);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, phase.ToString())); Assert.Equal(State(original), State(loaded));
        Until(original, () => !Region(original).Find("pharmacy-run")!.Active);
        Until(loaded, () => !Region(loaded).Find("pharmacy-run")!.Active); Assert.Equal(State(original), State(loaded));
    }
    [Fact]
    public void WholeComputerUnitsAndArchiveEnterComputingOnlyAfterPhysicalRecovery()
    {
        var (_, s, _) = ScavengingScenario.Create(); var computing = s.World.Module<ComputingModule>();
        var plan = new ExpeditionPlan("parts", "repair-shop", [3], [new("board-a", 1.2m), new("usb-archive", .03m)]);
        Assert.Contains("whole", Region(s).Explain(s.World, plan with { Manifest = [new("board-a", .6m)] }));
        Dispatch(s, plan); Until(s, () => Region(s).Find("parts")!.Status == ExpeditionStatus.Returning);
        Assert.Empty(computing.Components); Assert.Empty(computing.Assets);
        var loaded = PhysicalSave.Load(SaveManager.Save(s, "carried USB"));
        Until(s, () => !Region(s).Find("parts")!.Active); Until(loaded, () => !Region(loaded).Find("parts")!.Active);
        Assert.Equal(State(s), State(loaded)); Assert.Equal(2, computing.Components.Count); Assert.NotNull(computing.Asset("model-4b"));
        Assert.Contains(s.World.Module<InventoryModule>().Batches, b => b.MaterialId == "item/board-a" && b.QuantityKg == 1.2m);
        s.Queue(new QueueComputerTaskCommand(new("diagnose-recovered", ComputerTaskKind.DiagnoseComponents, "rig-1", 3, Components: ["board-a", "usb-archive"])));
        Until(s, () => computing.Job("diagnose-recovered")?.Status == ComputerTaskStatus.Completed); Assert.True(computing.Component("board-a")!.Diagnosed);
    }
    [Fact]
    public void TwoLocationMedicineChainTreatsInitialPatientConservesMassAndReplaysCommands()
    {
        var (_, original, _) = ScavengingScenario.Create(123); decimal mass = PhysicalReport.Capture(original.World).TotalTrackedKg;
        for (int i = 0; i < 800 && Region(original).HealthOf(1)!.Sick; i++)
        { ScavengingWalkthrough.QueueNext(original); original.Advance(); PhysicalSave.ValidateWorld(original.World); }
        Assert.False(Region(original).HealthOf(1)!.Sick); Assert.True(original.World.Module<WorkforceModule>().Find(1)!.Present);
        Assert.Equal(mass, PhysicalReport.Capture(original.World).TotalTrackedKg); Assert.Equal(.18m, Region(original).SpentSuppliesKg);
        Assert.Contains(original.World.Module<InventoryModule>().Batches, b => b.MaterialId == "waste.medical" && b.QuantityKg == .12m);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, "recovered")); var (_, replay, _) = ScavengingScenario.Create(123);
        var commands = loaded.History.ToLookup(c => c.Tick);
        while (replay.World.Clock.Tick < original.World.Clock.Tick)
        { foreach (var c in commands[replay.World.Clock.Tick]) replay.Queue(c.Command); replay.Advance(); }
        Assert.Equal(State(original), State(replay));
    }
    [Fact]
    public void ClinicQueueDoesNotSeizeMedicBeforeMedicineAndSavesDuringCare()
    {
        var (_, s, _) = ScavengingScenario.Create(); s.Queue(new QueueTreatmentCommand(new("early", 1))); s.Advance();
        Assert.Null(s.World.Module<WorkforceModule>().ReservationFor(4)); Assert.Contains("purity alone", Region(s).Treatments.Single().Reason);
        var inv = s.World.Module<InventoryModule>();
        inv.AddBatch(new("bad-dose", "medicine.fever-dose", "clinic", .1m, new(1, "unvalidated"))); s.Advance(); Assert.Null(Region(s).Treatments.Single().DoseClaim);
        inv.AddBatch(new("released-dose", "medicine.fever-dose", "clinic", .1m, new(.999m, "fictional clinic release")));
        Until(s, () => Region(s).Treatments.Single().Effort > 0); var loaded = PhysicalSave.Load(SaveManager.Save(s, "clinic care"));
        Until(s, () => Region(s).Treatments.Single().Completed); Until(loaded, () => Region(loaded).Treatments.Single().Completed);
        Assert.Equal(State(s), State(loaded)); Assert.NotNull(inv.FindBatch("bad-dose"));
    }
    [Theory]
    [InlineData("medicine.precursor", .96, "sealed fictional stock")]
    [InlineData("medicine.precursor", .99, "opened unknown stock")]
    [InlineData("chemical.sorbent", .94, "identified dry sorbent")]
    public void PurityAndProvenanceRejectUnsuitableRefiningInputs(string material, double purity, string grade)
    {
        var (_, s, _) = ScavengingScenario.Create(); var inv = s.World.Module<InventoryModule>();
        inv.AddBatch(new("test-stock", "medicine.precursor", "salvage", .1m, new(material == "medicine.precursor" ? (decimal)purity : .98m, material == "medicine.precursor" ? grade : "sealed fictional stock")));
        inv.AddBatch(new("test-sorbent", "chemical.sorbent", "salvage", .1m, new(material == "chemical.sorbent" ? (decimal)purity : .95m, "identified dry sorbent")));
        s.Queue(new QueueProductionCommand(ScavengingScenario.MedicinePlan("bad-refine", "refine-fever-stock"))); s.FastForward(100);
        Assert.Equal(ProductionStatus.WaitingForInputs, s.World.Module<ProductionModule>().Find("bad-refine")!.Status);
        Assert.DoesNotContain(inv.Batches, b => b.MaterialId == "medicine.refined"); PhysicalSave.ValidateWorld(s.World);
    }
    [Fact]
    public void HighPurityAndCorrectLabelDoNotEraseKnownContamination()
    {
        var (_, s, _) = ScavengingScenario.Create(); var inv = s.World.Module<InventoryModule>();
        inv.AddBatch(new("contaminated-stock", "medicine.precursor", "salvage", .1m, new(.995m, "sealed fictional stock", new Dictionary<string, decimal> { ["unknown"] = .005m })));
        inv.AddBatch(new("sorbent", "chemical.sorbent", "salvage", .1m, new(.95m, "identified dry sorbent")));
        s.Queue(new QueueProductionCommand(ScavengingScenario.MedicinePlan("contaminated", "refine-fever-stock"))); s.FastForward(50);
        Assert.Equal(ProductionStatus.WaitingForInputs, s.World.Module<ProductionModule>().Find("contaminated")!.Status);
        Assert.NotNull(inv.FindBatch("contaminated-stock")); PhysicalSave.ValidateWorld(s.World);
    }
    [Fact]
    public void StructuralIncidentCanBeAssessedAndLaterDecayCanCloseAccess()
    {
        var (_, s, _) = ScavengingScenario.Create();
        AlterRegion(s, l => l.Definition.Id == "pharmacy" ? l with { Definition = l.Definition with { InitialIntegrity = 0, Salvage = l.Definition.Salvage.Select(o => o with { MinimumIntegrity = 0 }).ToArray() }, Integrity = 0 } : l);
        Dispatch(s, Pharmacy() with { Approach = ExpeditionApproach.Quick });
        Until(s, () => Region(s).Find("pharmacy-run")!.Status == ExpeditionStatus.Decision, respond: false);
        Assert.Equal(ExpeditionDecision.Structure, Region(s).Find("pharmacy-run")!.Decision);
        s.Queue(new RespondToExpeditionCommand("pharmacy-run", ExpeditionResponse.Wait)); s.Advance();
        Assert.Equal(5, Region(s).Find("pharmacy-run")!.DelayTicks);
        Until(s, () => !Region(s).Find("pharmacy-run")!.Active);
        Assert.Equal(3, Region(s).Find("pharmacy-run")!.Receipts.Count);
        var (_, closed, _) = ScavengingScenario.Create(); Dispatch(closed, Pharmacy());
        Until(closed, () => Region(closed).Find("pharmacy-run")!.Status == ExpeditionStatus.Outbound);
        AlterRegion(closed, l => l.Definition.Id == "pharmacy" ? l with { Integrity = .1m } : l);
        Until(closed, () => !Region(closed).Find("pharmacy-run")!.Active); Assert.Empty(Region(closed).Find("pharmacy-run")!.Receipts);
    }
    [Fact]
    public void CompetingPlayerTeamsCannotDuplicateTheSameRemoteComputerUnit()
    {
        var (_, s, _) = ScavengingScenario.Create();
        Dispatch(s, new("parts-first", "repair-shop", [3], [new("board-a", 1.2m)]));
        Dispatch(s, new("parts-second", "repair-shop", [2], [new("board-a", 1.2m)]));
        Until(s, () => Region(s).Expeditions.All(e => !e.Active));
        Assert.Single(s.World.Module<ComputingModule>().Components);
        Assert.Single(Region(s).Expeditions.SelectMany(e => e.Receipts));
    }
    [Fact]
    public void GenericProcessWaterCannotReplaceSterilePharmacyWater()
    {
        var (_, s, _) = ScavengingScenario.Create(); var inv = s.World.Module<InventoryModule>();
        inv.AddBatch(new("refined", "medicine.refined", "clinic", .08m, new(.999m, "fictional refined stock")));
        inv.AddBatch(new("excipient", "medicine.excipient", "clinic", .12m, new(.999m, "sealed formulation stock")));
        s.Queue(new QueueProductionCommand(ScavengingScenario.MedicinePlan("no-water", "formulate-fever-dose"))); s.FastForward(100);
        Assert.Equal(ProductionStatus.WaitingForInputs, s.World.Module<ProductionModule>().Find("no-water")!.Status);
        Assert.DoesNotContain(inv.Batches, b => b.MaterialId == "medicine.fever-dose"); PhysicalSave.ValidateWorld(s.World);
    }
    [Fact]
    public void ExposureWhileWaitingCanMakeReturnedWorkersUnavailable()
    {
        var (_, s, _) = ScavengingScenario.Create();
        AlterRegion(s, l => l.Definition.Id == "homes" ? l with { Definition = l.Definition with { RivalPressure = 1, ExposurePerTick = .2m, InitialIntegrity = 1 }, Integrity = 1 } : l);
        Dispatch(s, new("exposed", "homes", [2], [new("bottle-glass", 1)], UseProtection: false));
        Until(s, () => Region(s).Find("exposed")!.Status == ExpeditionStatus.Decision, respond: false);
        s.FastForward(6); s.Queue(new RecallExpeditionCommand("exposed")); s.Advance(); Until(s, () => !Region(s).Find("exposed")!.Active);
        Assert.True(Region(s).HealthOf(2)!.Sick); Assert.False(s.World.Module<WorkforceModule>().Find(2)!.Present);
    }
    [Fact]
    public void CorruptedStockAndMissingCapacityClaimsAreRejectedOnLoad()
    {
        var (_, s, _) = ScavengingScenario.Create(); Dispatch(s, Pharmacy());
        var snapshot = (ScavengingSnapshot)Region(s).ToSnapshot();
        var bad = snapshot with { Locations = snapshot.Locations.Select(l => l.Definition.Id == "pharmacy" ? l with { Remaining = new Dictionary<string, decimal>(l.Remaining) { ["stock-sealed"] = 100 } } : l).ToArray() };
        s.World.Register(ScavengingModule.FromSnapshot(bad)); Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(s, "corrupt-stock")));
        s.World.Register(ScavengingModule.FromSnapshot(snapshot)); s.World.Module<InventoryModule>().ReleaseReservations("scavenger.expedition/pharmacy-run/space/0");
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(s, "missing-space")));
    }
    [Fact]
    public void CancellingClinicCareReleasesDoseMedicAndEquipmentWithoutCuringPatient()
    {
        var (_, s, _) = ScavengingScenario.Create(); var inv = s.World.Module<InventoryModule>();
        inv.AddBatch(new("dose", "medicine.fever-dose", "clinic", .1m, new(.999m, "fictional clinic release")));
        s.Queue(new QueueTreatmentCommand(new("cancel-care", 1))); Until(s, () => Region(s).Treatments.Any(t => t.DoseClaim is not null));
        s.Queue(new CancelTreatmentCommand("cancel-care")); s.Advance(); PhysicalSave.ValidateWorld(s.World);
        Assert.True(Region(s).HealthOf(1)!.Sick); Assert.NotNull(inv.FindBatch("dose"));
        Assert.Null(s.World.Module<WorkforceModule>().ReservationFor(4)); Assert.Null(s.World.Module<EquipmentModule>().OwnerOf("clinic-station"));
        var loaded = PhysicalSave.Load(SaveManager.Save(s, "cancelled clinic")); Assert.Equal(State(s), State(loaded));
    }
    [Fact]
    public void OlderIndustrialSavesDoNotAcquireRegionalLoot()
    {
        var (_, s, _) = IndustrialScenario.Create(); var loaded = PhysicalSave.Load(SaveManager.Save(s, "older scenario"));
        Assert.False(loaded.World.Has<ScavengingModule>()); Assert.Equal(State(s), State(loaded));
    }
}
