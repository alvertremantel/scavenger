using Haldane.CareerSim.Work;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Tests;

public class PlayabilityTests
{
    [Theory]
    [InlineData(42UL)]
    [InlineData(20260929UL)]
    [InlineData(9UL)]
    public void SupplySuggestionsSupportClinicLoopAndSeveralDaysInGeneratedWorlds(ulong seed)
    {
        var session = new GameSession(seed: seed);
        decimal mass = session.Report.TotalTrackedKg;
        foreach (string purpose in new[] { "pharmacy", "sorbent" })
        {
            long tick = session.World.Clock.Tick;
            var suggestion = OperationsGuide.Suggest(session.World, purpose, session.NextId("expedition"));
            Assert.Equal(tick, session.World.Clock.Tick); Assert.Equal(mass, session.Report.TotalTrackedKg);
            Assert.NotNull(suggestion.Plan);
            Assert.True(session.Submit(new DispatchExpeditionCommand(suggestion.Plan!)).Accepted, suggestion.Message);
            Until(() => !session.World.Module<ScavengingModule>().Expeditions.Any(e => e.Active));
        }
        foreach (string recipe in new[] { "refine-fever-stock", "formulate-fever-dose" })
        {
            Assert.Null(OperationsGuide.ExplainMedicine(session.World, recipe));
            string id = session.NextId("medicine");
            Assert.True(session.Submit(new QueueProductionCommand(ScavengingScenario.MedicinePlan(id, recipe))).Accepted);
            Assert.Contains("already queued", OperationsGuide.ExplainMedicine(session.World, recipe));
            Until(() => session.World.Module<ProductionModule>().Find(id)!.Status == ProductionStatus.Completed);
        }
        Assert.True(session.Submit(new QueueTreatmentCommand(new(session.NextId("clinic"), 1))).Accepted);
        Until(() => !session.World.Module<ScavengingModule>().HealthOf(1)!.Sick);
        var fuel = OperationsGuide.Suggest(session.World, "fuel", session.NextId("expedition"));
        Assert.NotNull(fuel.Plan); Assert.True(session.Submit(new DispatchExpeditionCommand(fuel.Plan!)).Accepted);
        session.LoadJson(session.SaveJson());
        Until(() => !session.World.Module<ScavengingModule>().Expeditions.Any(e => e.Active));
        var inv = session.World.Module<InventoryModule>();
        var batch = inv.Batches.First(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage" && inv.AvailableKg(b.Id) >= 6);
        Assert.True(session.Submit(new QueuePhysicalHaulCommand(new(session.NextId("refuel"), batch.Id, "refuel-route", 2, 6))).Accepted, session.Status);
        Until(() => session.World.Module<LogisticsModule>().Jobs.All(j => !j.Active));
        Assert.True(session.Build("workbench", new(8,8)).Accepted);
        Until(() => session.World.Module<ConstructionModule>().Jobs.All(j => !j.Active));
        Assert.True(session.Repair("clinic-station").Accepted);
        Until(() => session.World.Module<ConstructionModule>().Jobs.All(j => !j.Active));
        session.BeginDemo(); Assert.Equal(4, session.World.Module<OrdersModule>().Orders.Count);
        Until(() => inv.Batches.Any(b => b.MaterialId == "part.polymer"));
        Until(() => session.World.Clock.Tick >= 4 * session.World.Clock.Time.TicksPerDay);
        Assert.False(session.World.Module<ScavengingModule>().HealthOf(1)!.Sick);
        Assert.InRange(Math.Abs(mass - session.Report.TotalTrackedKg), 0, .000000001m);
        PhysicalSave.ValidateWorld(session.World);
        void Until(Func<bool> done)
        {
            for (int i = 0; i < 1800 && !done(); i++)
            {
                foreach (var e in session.World.Module<ScavengingModule>().Expeditions.Where(e => e.Status == ExpeditionStatus.Decision))
                    Assert.True(session.Submit(new RespondToExpeditionCommand(e.Plan.Id, ExpeditionResponse.Wait)).Accepted);
                var stock = session.World.Module<InventoryModule>();
                var refill = stock.Batches.FirstOrDefault(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage" && stock.AvailableKg(b.Id) >= .001m);
                if (stock.Batches.Where(b => b.StockpileId == "generator-tank" && b.MaterialId == "fuel.diesel").Sum(b => stock.AvailableKg(b.Id)) < 3 && refill is not null)
                {
                    var command = new QueuePhysicalHaulCommand(new(session.NextId("refuel"), refill.Id, "refuel-route", 2, Math.Min(6, stock.AvailableKg(refill.Id))));
                    if (command.CanExecute(session.World)) session.Submit(command);
                }
                else if (refill is null && stock.Batches.Where(b => b.StockpileId == "generator-tank" && b.MaterialId == "fuel.diesel").Sum(b => stock.AvailableKg(b.Id)) < 3)
                {
                    var scavenging = session.World.Module<ScavengingModule>();
                    bool fuelAway = scavenging.Expeditions.Any(e => e.Active && e.Plan.Manifest.Any(m =>
                        scavenging.Location(e.Plan.LocationId)!.Definition.Salvage.Any(s => s.Id == m.SalvageId && s.MaterialId == "fuel.diesel")));
                    if (!fuelAway && OperationsGuide.Suggest(session.World, "fuel", session.NextId("expedition")).Plan is { } recovery)
                        Assert.True(session.Submit(new DispatchExpeditionCommand(recovery)).Accepted);
                }
                session.Advance(1, true);
            }
            Assert.True(done(), session.Status + "\n" + string.Join("\n", session.World.Module<ProductionModule>().Jobs.Select(j => $"{j.Plan.RecipeId} {j.Status}: {j.BlockedReason}"))
                + "\nFuel: " + string.Join(",", session.World.Module<InventoryModule>().Batches.Where(b => b.MaterialId == "fuel.diesel").Select(b => $"{b.StockpileId}={b.QuantityKg}")));

        }
    }
    [Fact]
    public void SupplyPlanningRejectsCloserContaminatedStockAndNeverAdvancesTheWorld()
    {
        var session = new GameSession(generated: false);
        var module = session.World.Module<ScavengingModule>();
        var pharmacy = ScavengingScenario.Region().Single(l => l.Id == "pharmacy");
        module.AddLocation(pharmacy with { Id = "nearby-damp-stock", TravelTicks = 1,
            Salvage = pharmacy.Salvage.Select(s => s.Id == "stock-sealed" ? s with { Quality = new(.90m, s.Quality.Grade) } : s).ToArray() });
        var before = Haldane.Kernel.Serialization.SaveManager.Capture(session.Scheduler, "planning").World;
        string state = System.Text.Json.JsonSerializer.Serialize(before, Haldane.Kernel.Serialization.SaveManager.Options);
        var suggestion = OperationsGuide.Suggest(session.World, "pharmacy");
        Assert.Equal("pharmacy", suggestion.Plan!.LocationId);
        string after = System.Text.Json.JsonSerializer.Serialize(Haldane.Kernel.Serialization.SaveManager.Capture(session.Scheduler, "planning").World, Haldane.Kernel.Serialization.SaveManager.Options);
        Assert.Equal(state, after);
        Assert.True(session.StartClinic().Accepted);
        Assert.Null(OperationsGuide.Suggest(session.World, "pharmacy").Plan);
        Assert.True(session.ReleaseProject(4).Accepted);
        Assert.NotNull(OperationsGuide.Suggest(session.World, "pharmacy").Plan);
    }
    [Fact]
    public void FuelSuggestionsFindUsableMaterialsAcrossSiteTypesAndRecoverPartialCans()
    {
        var session = new GameSession(generated: false);
        var module = session.World.Module<ScavengingModule>();
        var hardware = module.Location("hardware")!.Definition;
        var can = hardware.Salvage.Single(s => s.Id == "diesel-cans");
        module.AddLocation(hardware with { Id = "nearby-wet-fuel", TravelTicks = 1,
            Salvage = [can with { Id = "diesel", Quality = new(.8m, "water-contaminated fuel") }] });
        module.AddLocation(hardware with { Id = "farm-service-shed", TravelTicks = 2,
            Salvage = [can with { Id = "farm-diesel", InitialKg = 2 }] });
        var suggestion = OperationsGuide.Suggest(session.World, "fuel");
        Assert.NotNull(suggestion.Plan);
        Assert.Equal("farm-service-shed", suggestion.Plan!.LocationId);
        Assert.Equal(new SalvageRequest("farm-diesel", 2), Assert.Single(suggestion.Plan.Manifest));
        Assert.Null(module.Explain(session.World, suggestion.Plan));
        Assert.Equal(2, module.Location("farm-service-shed")!.Remaining("farm-diesel"));
    }
    [Fact]
    public void PendingFieldDecisionStopsEvenWhenNoNewEventIsEmitted()
    {
        // An authored test location forces an incident without mutating internal job state.
        var session = new GameSession(generated: false);
        var module = session.World.Module<ScavengingModule>();
        var definition = ScavengingScenario.Region().Single(l => l.Id == "homes");
        module.AddLocation(definition with { Id = "incident", InitialIntegrity = .5m, RivalPressure = 1 });
        Assert.True(session.Submit(new DispatchExpeditionCommand(new("incident-run", "incident", [2], [new("bottle-glass", 1)]))).Accepted);
        for (int i = 0; i < 100 && session.PendingDecision is null; i++) session.Advance();
        Assert.NotNull(session.PendingDecision);
        long tick = session.World.Clock.Tick;
        decimal exposure = session.World.Module<ScavengingModule>().Health.Sum(h => h.Exposure);
        var result = session.Advance(96, true);
        Assert.True(result.StoppedEarly); Assert.Equal(0, result.TicksAdvanced);
        Assert.Equal(tick, session.World.Clock.Tick); Assert.Equal(exposure, session.World.Module<ScavengingModule>().Health.Sum(h => h.Exposure));
        Assert.Equal(1, session.Advance().TicksAdvanced); // deliberate manual step remains possible
    }
    [Fact]
    public void NormalTimeStopsAtLowTankReserveBeforePowerFails()
    {
        var session = new GameSession(generated: false);
        var result = session.Advance(1000, true);
        var inventory = session.World.Module<InventoryModule>();
        decimal tank = inventory.Batches.Where(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "generator-tank").Sum(b => inventory.AvailableKg(b.Id));
        Assert.True(result.StoppedEarly); Assert.InRange(tank, 2.9m, 3m);
        Assert.Contains("tank is low", session.Status);
        Assert.True(session.World.Module<UtilitiesModule>().ServiceFor("clinic-station")!.Operational);
    }
    [Fact]
    public void ClinicProjectCanBeReleasedAndRecoveryOrdersCanStartAfterDayOne()
    {
        var session = new GameSession(generated: false);
        Assert.True(session.StartClinic().Accepted);
        Assert.True(session.World.Module<ProjectBoardModule>().IsAssigned(4));
        Assert.True(session.ReleaseProject(4).Accepted);
        Assert.False(session.World.Module<ProjectBoardModule>().IsAssigned(4));
        session.Advance(100);
        // An existing custom target must not prevent the remaining recovery chain.
        Assert.True(session.Submit(new AddStockOrderCommand(new("custom-wash",
            IndustrialScenario.ProcessPlan("template", "wash-hdpe"), "polymer.hdpe.washed", 3))).Accepted);
        session.BeginDemo();
        Assert.Equal(4, session.World.Module<OrdersModule>().Orders.Count);
        Assert.Equal("custom-wash", Assert.Single(session.World.Module<OrdersModule>().Orders, o => o.Plan.Template.RecipeId == "wash-hdpe").Plan.Id);
        session.BeginDemo(); Assert.Equal(4, session.World.Module<OrdersModule>().Orders.Count);
        session.LoadJson(session.SaveJson()); PhysicalSave.ValidateWorld(session.World);
    }
}
