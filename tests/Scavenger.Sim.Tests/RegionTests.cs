using System.Text.Json;
using System.Xml.Linq;
using Haldane.Kernel.Serialization;
using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Region;
using Scavenger.Sim.Scavenging;

namespace Scavenger.Sim.Tests;

public class RegionTests
{
    private static string State(Scheduler s) => JsonSerializer.Serialize(new {
        Physical = PhysicalReport.Capture(s.World), Industry = IndustryReport.Capture(s.World),
        Geography = s.World.Module<RegionModule>().ToSnapshot(), Scavenging = s.World.Module<ScavengingModule>().ToSnapshot(),
        Rng = RngState(s) }, SaveManager.Options);
    private static string RngState(Scheduler s) => JsonSerializer.Serialize(new {
        s.World.Rng.MasterSeed, Streams = s.World.Rng.ActiveStreams.Order(StringComparer.Ordinal).Select(name => {
            var state = s.World.Rng.Stream(name).Snapshot(); return new { name, state.S0, state.S1, state.S2, state.S3 };
        }) });
    private static string Generated(GeneratedRegion r) => JsonSerializer.Serialize(new { Geography = r.Geography.ToSnapshot(), r.Locations }, SaveManager.Options);
    [Theory]
    [InlineData(0UL)] [InlineData(1UL)] [InlineData(42UL)] [InlineData(20260929UL)] [InlineData(ulong.MaxValue)]
    public void SeedsRepeatExactlyAndEverySiteHasARealShortestRoadRoute(ulong seed)
    {
        var a = RegionGenerator.Generate(seed); var b = RegionGenerator.Generate(seed);
        Assert.Equal(Generated(a), Generated(b));
        var map = a.Geography; Assert.InRange(map.SquareMiles, 49.7m, 50m);
        Assert.Equal(20, map.Sites.Select(s => s.TypeId).Distinct().Count());
        Assert.InRange(map.Sites.Count, 81, 141);
        var road = map.Roads.Select(r => r.Point).ToHashSet();
        var distances = new Dictionary<RegionPoint, int>(); var queue = new Queue<RegionPoint>();
        var start = ((RegionSnapshot)map.ToSnapshot()).CompoundAccess; queue.Enqueue(start); distances[start] = 0;
        while (queue.TryDequeue(out var p))
        {
            foreach (var next in new[] { new RegionPoint(p.X - 1, p.Y), new(p.X + 1, p.Y), new(p.X, p.Y - 1), new(p.X, p.Y + 1) })
                if (road.Contains(next) && distances.TryAdd(next, distances[p] + 1)) queue.Enqueue(next);
        }
        Assert.Equal(road.Count, distances.Count);
        Assert.Equal(map.Sites.Count + 1, map.Sites.Select(s => s.Parcel).Append(map.Compound).Distinct().Count());
        foreach (var site in map.Sites)
        {
            var route = map.Route(site.LocationId);
            Assert.Equal(map.Compound, route[0]); Assert.Equal(site.Parcel, route[^1]);
            Assert.Equal(distances[site.Access] + 2, route.Count - 1);
            Assert.Equal((route.Count - 1) * map.CellMeters, site.RoadDistanceMeters);
            Assert.All(route.Zip(route.Skip(1)), pair => Assert.Equal(1, pair.First.Manhattan(pair.Second)));
            Assert.All(route.Skip(1).SkipLast(1), p => Assert.Contains(p, road));
            Assert.DoesNotContain(site.Parcel, road);
            Assert.Contains(map.Cell(site.Parcel).District, RegionalContent.Type(site.TypeId)!.Districts);
            Assert.Equal(map.TravelTicks(site), a.Locations.Single(l => l.Id == site.LocationId).TravelTicks);
        }
        Assert.True(map.Roads.Count(r => r.Kind == RoadKind.Bridge) >= 3);
        Assert.All(map.Roads, r => Assert.Equal(map.Cell(r.Point).NeedsBridge, r.Kind == RoadKind.Bridge));
    }
    [Fact]
    public void ManyWorldsRemainCoherentWithoutLosingOrDuplicatingContent()
    {
        for (ulong seed = 0; seed < 48; seed++)
        {
            var generated = RegionGenerator.Generate(seed); var map = generated.Geography;
            foreach (var type in RegionalContent.Types)
                Assert.InRange(map.Sites.Count(s => s.TypeId == type.Id), type.MinimumCount, type.MaximumCount * 2);
            var all = generated.Locations.SelectMany(l => l.Salvage).ToArray();
            var parts = all.Where(s => s.ComponentId is not null).ToArray();
            Assert.Equal(parts.Length, parts.Select(s => s.ComponentId).Distinct().Count());
            Assert.All(parts, p => Assert.Equal(IndustrialScenario.ComponentModels().Single(d => d.Id == p.ComponentModel).MassKg, p.InitialKg));
            var assets = parts.SelectMany(p => p.Assets ?? []).ToArray();
            Assert.Equal(assets.Length, assets.Select(a => a.Id).Distinct().Count());
            Assert.All(parts, p => Assert.All(p.Assets ?? [], a => Assert.Equal(p.ComponentId, a.SourceMediaId)));
            // No commodity lottery puts medical stock or laboratory glass into domestic recycling.
            foreach (var site in map.Sites.Where(s => s.TypeId is "homes" or "grocery" or "recycling"))
                Assert.DoesNotContain(generated.Locations.Single(l => l.Id == site.LocationId).Salvage, s => s.MaterialId is "medicine.precursor" or "glass.borosilicate" or "water.sterile");
            Assert.Contains(generated.Locations.Single(l => l.Id == "pharmacy-001").Salvage, s => s.Id == "stock-sealed" && s.Quality.Grade == "sealed fictional stock");
            Assert.Contains(generated.Locations.Single(l => l.Id == "hardware-001").Salvage, s => s.Id == "filter-sorbent" && s.Quality.Purity >= .95m);
        }
    }
    [Fact]
    public void DifferentSeedsChangeGeographyAndInstancesWithinOneTypeAreDistinct()
    {
        var regions = Enumerable.Range(1, 8).Select(n => RegionGenerator.Generate((ulong)n)).ToArray();
        Assert.Equal(8, regions.Select(Generated).Distinct().Count());
        Assert.True(regions.Select(r => JsonSerializer.Serialize(r.Geography.Cells)).Distinct().Count() > 1);
        var houses = regions[0].Locations.Where(l => l.Id.StartsWith("homes-")).ToArray();
        Assert.True(houses.Select(l => l.InitialIntegrity).Distinct().Count() > 3);
        Assert.True(houses.Select(l => JsonSerializer.Serialize(l.Salvage)).Distinct().Count() > 3);
        Assert.True(regions.SelectMany(r => r.Geography.Sites).Where(s => s.TypeId == "repair-shop").Select(s => s.Variant).Distinct().Count() >= 2);
    }
    [Theory]
    [InlineData(1)] [InlineData(24)] [InlineData(96)] [InlineData(192)] [InlineData(100000)]
    public void RoadTravelUsesClockDurationAndNeverRoundsDown(int ticksPerDay)
    {
        var generated = RegionGenerator.Generate(42, ticksPerDay); var map = generated.Geography;
        var scavenging = new ScavengingModule(); foreach (var location in generated.Locations) scavenging.AddLocation(location);
        Assert.Equal(Math.Max(1, 4000 * 24 / ticksPerDay), map.WalkingMetersPerTick);
        foreach (var site in map.Sites)
        {
            int ticks = map.TravelTicks(site);
            Assert.True(ticks * map.WalkingMetersPerTick >= site.RoadDistanceMeters);
            Assert.True((ticks - 1) * map.WalkingMetersPerTick < site.RoadDistanceMeters);
        }
    }
    [Theory]
    [InlineData(0UL)] [InlineData(42UL)] [InlineData(20260929UL)]
    public void GeneratedClinicLoopConservesMassAndResumesAndReplaysExactly(ulong seed)
    {
        var (_, original, _) = GeneratedScenario.Create(seed); decimal mass = PhysicalReport.Capture(original.World).TotalTrackedKg;
        for (int tick = 0; tick < 500 && original.World.Module<ScavengingModule>().HealthOf(1)!.Sick; tick++)
        {
            ScavengingWalkthrough.QueueNext(original); original.Advance();
            if (tick == 15) original = PhysicalSave.Load(SaveManager.Save(original, "generated trip in progress"));
            PhysicalSave.ValidateWorld(original.World);
        }
        Assert.False(original.World.Module<ScavengingModule>().HealthOf(1)!.Sick);
        Assert.Equal(mass, PhysicalReport.Capture(original.World).TotalTrackedKg);
        var loaded = PhysicalSave.Load(SaveManager.Save(original, "generated recovery")); Assert.Equal(State(original), State(loaded));
        var (_, replay, _) = GeneratedScenario.Create(seed); var commands = loaded.History.ToLookup(c => c.Tick);
        while (replay.World.Clock.Tick < original.World.Clock.Tick)
        { foreach (var c in commands[replay.World.Clock.Tick]) replay.Queue(c.Command); replay.Advance(); }
        Assert.Equal(State(original), State(replay));
    }
    [Fact]
    public void GeneratedPartsAndArchivesKeepTheirNamespacedIdentityOnRecovery()
    {
        var (_, s, _) = GeneratedScenario.Create(42); var m = s.World.Module<ScavengingModule>();
        var l = m.Locations.First(l => l.Definition.Salvage.Any(o => (o.Assets?.Count ?? 0) > 0 && o.Fault == ComponentFault.None));
        var usb = l.Definition.Salvage.First(o => (o.Assets?.Count ?? 0) > 0 && o.Fault == ComponentFault.None);
        s.Queue(new DispatchExpeditionCommand(new("usb", l.Definition.Id, [2, 3], [new(usb.Id, usb.InitialKg)]))); s.Advance();
        Assert.NotNull(m.Find("usb"));
        for (int i = 0; i < 200 && m.Find("usb")!.Active; i++)
        {
            if (m.Find("usb")!.Status == ExpeditionStatus.Decision) s.Queue(new RespondToExpeditionCommand("usb", ExpeditionResponse.Wait));
            s.Advance();
        }
        Assert.False(m.Find("usb")!.Active);
        var computers = s.World.Module<ComputingModule>(); Assert.NotNull(computers.Component(usb.ComponentId!));
        Assert.NotNull(computers.Asset($"{l.Definition.Id}/model-4b"));
        Assert.Contains(computers.Asset($"{l.Definition.Id}/model-4b")!.Capabilities, c => c == "model-4b");
        PhysicalSave.ValidateWorld(s.World);
        Assert.Equal(State(s), State(PhysicalSave.Load(SaveManager.Save(s, "namespaced carrier"))));
    }
    [Fact]
    public void GeographyRejectsDisconnectedRoadsInvalidParcelsAndFalseTravelOnLoad()
    {
        var generated = RegionGenerator.Generate(42); var snapshot = (RegionSnapshot)generated.Geography.ToSnapshot();
        var site = snapshot.Sites[0];
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Sites = snapshot.Sites.Select(s => s == site ? s with { Parcel = snapshot.Compound } : s).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Sites = snapshot.Sites.Select(s => s == site ? s with { RoadDistanceMeters = 1 } : s).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Roads = snapshot.Roads.Concat([snapshot.Roads[0]]).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Roads = snapshot.Roads.Select(r => r.Kind == RoadKind.Bridge ? r with { Kind = RoadKind.Street } : r).ToArray() }));
        var allRoads = snapshot.Roads.Select(r => r.Point).ToHashSet();
        IEnumerable<RegionPoint> Adjacent(RegionPoint p) => [new(p.X - 1, p.Y), new(p.X + 1, p.Y), new(p.X, p.Y - 1), new(p.X, p.Y + 1)];
        var deadEnd = allRoads.First(p => Adjacent(p).Count(allRoads.Contains) == 1);
        var link = Adjacent(deadEnd).Single(allRoads.Contains);
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Roads = snapshot.Roads.Where(r => r.Point != link).ToArray() }));
        var (_, s, _) = GeneratedScenario.Create(42); var salvage = (ScavengingSnapshot)s.World.Module<ScavengingModule>().ToSnapshot();
        s.World.Register(ScavengingModule.FromSnapshot(salvage with { Locations = salvage.Locations.Select(l => l.Definition.Id == site.LocationId ? l with { Definition = l.Definition with { TravelTicks = l.Definition.TravelTicks + 1 } } : l).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => PhysicalSave.Load(SaveManager.Save(s, "false travel")));
    }
    [Fact]
    public void GenerationUsesNoGameplayRandomStreamAndSvgIsWellFormedWithAllSites()
    {
        var (_, fixture, _) = ScavengingScenario.Create(42); var (_, generated, _) = GeneratedScenario.Create(42);
        Assert.Equal(RngState(fixture), RngState(generated));
        var map = generated.World.Module<RegionModule>(); var document = XDocument.Parse(RegionMapExport.Svg(map));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(map.Sites.Count, document.Descendants(ns + "g").Count(e => e.Attribute("data-site") is not null));
        Assert.All(map.Sites, s => Assert.Contains(s.LocationId, document.ToString()));
    }
    [Fact]
    public void SessionSeedResetAndOldSavesKeepTheCorrectScenario()
    {
        var session = new GameSession(); Assert.True(session.World.Has<RegionModule>());
        session.NewRegion(55); string fresh = State(session.Scheduler); session.Advance(5); session.Reset(); Assert.Equal(fresh, State(session.Scheduler));
        var (_, old, _) = ScavengingScenario.Create(); session.LoadJson(SaveManager.Save(old, "old five-site region"));
        Assert.False(session.World.Has<RegionModule>()); Assert.Equal(5, session.World.Module<ScavengingModule>().Locations.Count);
        session.Reset(); Assert.False(session.World.Has<RegionModule>());
    }
}
