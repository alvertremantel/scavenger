using Scavenger.Cli;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Skills;
using Scavenger.Sim.Scavenging;
using Haldane.SupplyChain;

namespace Scavenger.Cli.Tests;

public class GameConsoleTests
{
    [Fact]
    public void QuotedSearchesCommentsAndMalformedInputAreHandledWithoutAdvancingTime()
    {
        var writer = new StringWriter(); var game = new GameConsole(new GameSession(), writer);
        Assert.Equal(new[] { "find", "book/construction/7" }, GameConsole.Tokenize("find \"book/construction/7\" # comment"));
        foreach (string command in new[] { "help", "status", "skills 2", "find \"book/construction/7\"", "jobs", "stock", "books" }) Assert.True(game.Execute(command), writer.ToString());
        Assert.False(game.Execute("expedition")); Assert.False(game.Execute("advance invalid")); Assert.False(game.Execute("find \"unterminated"));
        Assert.Equal(0, game.Session.World.Clock.Tick); Assert.Contains("Construction", writer.ToString());
    }
    [Fact]
    public void ConsoleCanRecoverStudySaveResumePromoteAndBuild()
    {
        var writer = new StringWriter(); var game = new GameConsole(new GameSession(), writer);
        var region = game.Session.World.Module<ScavengingModule>();
        var site = region.Locations.First(l => l.Definition.Salvage.Any(s => s.MaterialId == "book/construction/7"));
        var book = site.Definition.Salvage.First(s => s.MaterialId == "book/construction/7");
        Assert.True(game.Execute($"expedition {site.Definition.Id} 2 {book.Id}=0.4"));
        Finish("expedition-0-1");
        Assert.True(game.Execute("study 3 construction"));
        string id = game.Session.World.Module<SkillsModule>().Jobs.Single().Plan.Id;
        Assert.True(game.Execute("advance 20"));
        string path = Path.Combine(Path.GetTempPath(), "scavenger-cli-" + Guid.NewGuid() + " test.json");
        try
        {
            Assert.True(game.Execute($"save \"{path}\"")); Assert.True(game.Execute($"load \"{path}\"")); Finish(id);
            Assert.True(game.Session.World.Module<SkillsModule>().Find(3, "construction")!.TheoryReady);
            Assert.True(game.Execute("practice 3 construction"));
            Assert.NotNull(game.LastActivityId);
            id = game.Session.World.Module<SkillsModule>().Jobs.Single(j => j.Active).Plan.Id;
            Finish(id);
            Assert.Equal(7, game.Session.World.Module<SkillsModule>().Find(3, "construction")!.Level);
            Assert.True(game.Execute("build wall 9 8"));
            Assert.True(game.Execute("jobs")); Assert.Contains("advanced construction", writer.ToString());
        }
        finally { File.Delete(path); }
        void Finish(string task)
        {
            for (int i = 0; i < 15 && ActivityReport.Capture(game.Session.World).Any(j => j.Id == task && j.Active); i++)
            {
                foreach (var e in game.Session.World.Module<ScavengingModule>().Expeditions.Where(e => e.Status == ExpeditionStatus.Decision)) Assert.True(game.Execute($"respond {e.Plan.Id} Wait"));
                Assert.True(game.Execute($"until {task} 300"));
            }
            Assert.False(ActivityReport.Capture(game.Session.World).Single(j => j.Id == task).Active, writer.ToString());
        }
    }
    [Fact]
    public void RedirectedInteractiveInputReportsRejectionsAndContinuesAndScriptStopsAtFailure()
    {
        var output = new StringWriter(); var errors = new StringWriter();
        int code = GameConsole.Run(["--play", "--industry"], new StringReader("practice 2 construction\nstatus\nquit\n"), output, errors);
        Assert.Equal(1, code); Assert.Contains("REJECTED", output.ToString()); Assert.Contains("> status", output.ToString());
        string path = Path.Combine(Path.GetTempPath(), "scavenger-script-" + Guid.NewGuid());
        try
        {
            File.WriteAllText(path, "practice 2 construction\nadvance 999\n"); output.GetStringBuilder().Clear();
            Assert.Equal(1, GameConsole.Run(["--script", path, "--industry"], TextReader.Null, output, errors));
            Assert.DoesNotContain("> advance", output.ToString());
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void UntilLastPausesForPowerProblemsRatherThanAdvancingWeeksOfBlockedWork()
    {
        var writer = new StringWriter(); var game = new GameConsole(new GameSession(regional: false), writer);
        var inventory = game.Session.World.Module<InventoryModule>(); var snapshot = (InventorySnapshot)inventory.ToSnapshot();
        game.Session.World.Register(InventoryModule.FromSnapshot(snapshot with { Batches = snapshot.Batches.Select(b => b.StockpileId == "generator-tank" ? new MaterialBatch(b.Id, b.MaterialId, b.StockpileId, .001m, b.Quality) : b).ToArray() }));
        Assert.True(game.Execute("produce wash-hdpe washer 4 2"));
        Assert.True(game.Execute("until last 2000"));
        Assert.InRange(game.Session.World.Clock.Tick, 1, 100);
        Assert.True(game.Session.LastAdvanceNeedsAttention);
        Assert.Contains("power", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void WholeVolumeTransferCannotBeSplitAndBusyStudyCannotBeDispatched()
    {
        var game = new GameConsole(new GameSession(regional: false), new StringWriter());
        game.Session.World.Module<InventoryModule>().AddBatch(new("manual", "book/construction/1", "salvage", .4m, new(1, "readable")));
        Assert.False(game.Execute("transfer manual workshop 0.2 2")); Assert.True(game.Execute("study 2 construction manual"));
        Assert.False(game.Execute("transfer manual workshop 0.4 3")); Assert.Equal(1, game.Session.World.Clock.Tick);
        Assert.True(game.Execute("cancel learning study-0-1")); Assert.True(game.Execute("transfer manual workshop 0.4 3"));
    }
}
