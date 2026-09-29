using System.Text.Json;
using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Tests;

public class CompoundTests
{
    private static string State(Scheduler scheduler)
        => JsonSerializer.Serialize(SaveManager.Capture(scheduler, "test").World, SaveManager.Options);

    private static void Run(Scheduler scheduler, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            CompoundScenario.QueueDemoCommands(scheduler);
            scheduler.Advance();
        }
    }

    [Fact]
    public void ExperimentDeliversMaterialAndKeepsItsQualityAndLocationVisible()
    {
        var (world, scheduler, _) = CompoundScenario.Create();
        Run(scheduler, 8);
        var report = CompoundReport.Capture(world);
        Assert.Equal(20, report.StoredKg + report.InTransitKg);
        Assert.Equal(HaulStatus.Delivered, Assert.Single(report.Hauls).Status);
        var arrived = Assert.Single(report.Batches, b => b.StockpileId == "workshop");
        Assert.Equal(6, arrived.QuantityKg);
        Assert.Equal(0.94m, arrived.Quality.Purity);
        Assert.Equal(0.04m, arrived.Quality.Contaminants["soil"]);
        Assert.Equal(new CellPosition(8, 2), Assert.Single(report.Stores, s => s.Id == "workshop").Position);
        Assert.Single(world.Events.Entries, e => e.Kind == SupplyChainEventKinds.HaulBlocked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void SavingAtAnyStageAndContinuingProducesTheSameWorldAndCommandHistory(int saveTick)
    {
        var (_, uninterrupted, _) = CompoundScenario.Create();
        Run(uninterrupted, saveTick);
        var loaded = CompoundScenario.Load(SaveManager.Save(uninterrupted, "mid-experiment"));
        Assert.Equal(State(uninterrupted), State(loaded));
        Run(uninterrupted, 12 - saveTick);
        Run(loaded, 12 - saveTick);
        Assert.Equal(State(uninterrupted), State(loaded));
        Assert.Equal(uninterrupted.History.Count, loaded.History.Count);
    }

    [Fact]
    public void DeserializedCommandHistoryReplaysIntoAnIdenticalCompound()
    {
        var (_, original, _) = CompoundScenario.Create(seed: 7);
        Run(original, 12);
        var loaded = SaveManager.Load(SaveManager.Save(original, "replay"), SupplyChainSave.CreateRegistry());
        var (_, replay, _) = CompoundScenario.Create(seed: 7);
        var commands = loaded.CommandLog.ToLookup(c => c.Tick);
        for (int tick = 0; tick < 12; tick++)
        {
            foreach (var command in commands[replay.World.Clock.Tick]) replay.Queue(command.Command);
            replay.Advance();
        }
        Assert.Equal(State(original), State(replay));
    }

    [Fact]
    public void SameSeedAndCommandsAreDeterministic()
    {
        var (_, first, _) = CompoundScenario.Create(seed: 99);
        var (_, second, _) = CompoundScenario.Create(seed: 99);
        Run(first, 12);
        Run(second, 12);
        Assert.Equal(State(first), State(second));
    }

    [Fact]
    public void CatalogueCanStopFastForwardOnDeliveryWithoutPausingOnCalendarNoise()
    {
        var (world, scheduler, catalogue) = CompoundScenario.Create();
        CompoundScenario.QueueDemoCommands(scheduler);
        var result = scheduler.FastForward(20, new FastForwardStopConditions
        {
            MinSeverityToStop = null, InterruptCatalogue = catalogue,
        });
        Assert.True(result.StoppedEarly);
        Assert.Equal(4, result.TicksAdvanced);
        Assert.Equal(4, world.Clock.Tick);
        Assert.Contains(result.Events, e => e.Kind == SupplyChainEventKinds.HaulDelivered);
    }

    [Fact]
    public void UnqualifiedGeneralistCannotTakeTheMechanicsProject()
    {
        var (world, _, _) = CompoundScenario.Create();
        Assert.False(new AssignStaffCommand(2, "inspect-pump").CanExecute(world));
        Assert.True(new AssignStaffCommand(1, "inspect-pump").CanExecute(world));
    }

    [Fact]
    public void LowerEngineLayersDoNotReferenceTheGameOrSupplyChain()
    {
        Assert.DoesNotContain(typeof(World).Assembly.GetReferencedAssemblies(), a => a.Name == "Haldane.SupplyChain" || a.Name == "Scavenger.Sim");
        Assert.DoesNotContain(typeof(WorkforceModule).Assembly.GetReferencedAssemblies(), a => a.Name == "Haldane.SupplyChain" || a.Name == "Scavenger.Sim");
        Assert.DoesNotContain(typeof(InventoryModule).Assembly.GetReferencedAssemblies(), a => a.Name == "Scavenger.Sim");
    }
}
