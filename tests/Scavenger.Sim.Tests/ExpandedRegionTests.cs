using System.Text.Json;
using Haldane.Kernel.Serialization;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Region;

namespace Scavenger.Sim.Tests;

public class ExpandedRegionTests
{
    [Theory]
    [InlineData(SettlementMotif.Compact)] [InlineData(SettlementMotif.Avenue)] [InlineData(SettlementMotif.TwinCenters)]
    public void EveryLayoutRepeatsHasAMainTownAndPopulatedDetachedSuburbs(SettlementMotif motif)
    {
        for (ulong seed = 0; seed < 12; seed++)
        {
            var generated = RegionGenerator.Generate(seed, motif: motif); var map = generated.Geography;
            Assert.Equal(motif, map.Motif); Assert.Equal(2, map.GeneratorVersion);
            Assert.Equal(142, map.Width); Assert.InRange(map.SquareMiles, 49.7m, 50m);
            Assert.Single(map.Centers, c => c.Role == SettlementRole.MainTown);
            Assert.Equal(motif == SettlementMotif.TwinCenters ? 1 : 0, map.Centers.Count(c => c.Role == SettlementRole.SecondaryCenter));
            foreach (var suburb in map.Centers.Where(c => c.Role == SettlementRole.Suburb))
            {
                Assert.Contains(map.Sites, s => s.TypeId == "homes" && s.Parcel.Manhattan(suburb.Point) <= 10);
                Assert.True(suburb.Point.Manhattan(map.Centers[0].Point) >= 30);
            }
            if (seed == 0) Assert.Equal(JsonSerializer.Serialize(map.ToSnapshot()), JsonSerializer.Serialize(RegionGenerator.Generate(seed, motif: motif).Geography.ToSnapshot()));
        }
    }
    [Fact]
    public void MotifsChangeActualTownFootprintsAndAllOccurWithoutAnOverride()
    {
        var motifs = Enumerable.Range(0, 18).Select(n => RegionGenerator.Generate((ulong)n).Geography.Motif).ToHashSet();
        Assert.Equal(3, motifs.Count);
        var avenue = RegionGenerator.Generate(42, motif: SettlementMotif.Avenue).Geography;
        var compact = RegionGenerator.Generate(42, motif: SettlementMotif.Compact).Geography;
        decimal Aspect(RegionModule map)
        {
            var core = Enumerable.Range(0, map.Cells.Count).Where(i => map.Cells[i].District == LandUse.TownCenter).Select(i => new RegionPoint(i % map.Width, i / map.Width)).ToArray();
            int w = core.Max(p => p.X) - core.Min(p => p.X) + 1, h = core.Max(p => p.Y) - core.Min(p => p.Y) + 1;
            return Math.Max(w, h) / (decimal)Math.Min(w, h);
        }
        Assert.True(Aspect(avenue) > 2.5m); Assert.True(Aspect(compact) < 2m);
        Assert.NotEqual(JsonSerializer.Serialize(avenue.Roads), JsonSerializer.Serialize(compact.Roads));
    }
    [Fact]
    public void FractionalTerrainContainsRichCoverAndSmoothFieldsInsteadOfOneLabelPerCell()
    {
        var map = RegionGenerator.Generate(42).Geography;
        Assert.Equal(10, map.Cells.SelectMany(c => c.Cover!).Select(s => s.Kind).Distinct().Count());
        Assert.True(map.Cells.Count(c => c.Cover!.Count > 1) > 1000);
        Assert.Contains(map.Cells, c => c.Fraction(TerrainKind.River) is > 0 and < .5m);
        Assert.Contains(map.Roads, r => map.Cell(r.Point).WaterFraction is > 0 and < .5m && r.Kind == RoadKind.Bridge);
        Assert.Contains(map.Cells, c => c.Fraction(TerrainKind.River) == .44m || c.Fraction(TerrainKind.Woodland) == .44m);
        foreach (var cell in map.Cells)
        {
            Assert.Equal(1m, cell.Cover!.Sum(s => s.Fraction)); Assert.All(cell.Cover!, s => Assert.Equal(0, s.Fraction % .04m));
            Assert.InRange(cell.Moisture, 0, 1); Assert.InRange(cell.FloodRisk, 0, 1); Assert.InRange(cell.Roughness, 0, 1);
        }
        Assert.True(map.Cells.Max(c => c.ElevationMeters) - map.Cells.Min(c => c.ElevationMeters) > 100);
        for (int y = 1; y < map.Height; y++) for (int x = 1; x < map.Width; x++)
        {
            var c = map.Cell(new(x, y));
            Assert.InRange(Math.Abs(c.ElevationMeters - map.Cell(new(x - 1, y)).ElevationMeters), 0, 60);
            Assert.InRange(Math.Abs(c.ElevationMeters - map.Cell(new(x, y - 1)).ElevationMeters), 0, 60);
        }
        var boundary = map.Cells.First(c => c.Cover!.Count > 1);
        Assert.NotEqual(RegionPalette.Color(boundary, MapLayer.Terrain), RegionPalette.Color(boundary, MapLayer.FloodRisk));
    }
    [Fact]
    public void BadFractionsAndUnbridgedPartialWaterAreRejectedAndCollectionsCannotMutateSavedState()
    {
        var map = RegionGenerator.Generate(42).Geography; var snapshot = (RegionSnapshot)map.ToSnapshot();
        var cells = snapshot.Cells.ToArray(); cells[0] = cells[0] with { Cover = [new(TerrainKind.Meadow, .44m), new(TerrainKind.Hills, .32m)] };
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Cells = cells }));
        cells[0] = cells[0] with { Cover = [new(TerrainKind.Meadow, .5m), new(TerrainKind.Meadow, .5m)] };
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Cells = cells }));
        var bridge = snapshot.Roads.First(r => map.Cell(r.Point).WaterFraction is > 0 and < .5m);
        Assert.Throws<InvalidOperationException>(() => new RegionModule(snapshot with { Roads = snapshot.Roads.Select(r => r == bridge ? r with { Kind = RoadKind.Street } : r).ToArray() }));
        var original = snapshot.Cells[0].Cover!.ToArray(); var mutable = original.ToArray(); cells = snapshot.Cells.ToArray(); cells[0] = cells[0] with { Cover = mutable };
        var copy = new RegionModule(snapshot with { Cells = cells }); mutable[0] = new(TerrainKind.River, 1);
        Assert.Equal(original, copy.Cells[0].Cover);
    }
    [Fact]
    public void PreviousGeneratedSavesLoadAndResetWithoutChangingSizeOrAddingTerrain()
    {
        var (_, scheduler, _) = GeneratedScenario.Create(42, legacy: true); string json = SaveManager.Save(scheduler, "version one region");
        // Remove optional new properties to emulate the literal schema written before this change.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        void Strip(System.Text.Json.Nodes.JsonNode? value)
        {
            if (value is System.Text.Json.Nodes.JsonObject obj)
            {
                foreach (string key in new[] { "cover", "elevationMeters", "moisture", "floodRisk", "roughness", "motif", "centers" }) obj.Remove(key);
                foreach (var child in obj.ToArray()) Strip(child.Value);
            }
            else if (value is System.Text.Json.Nodes.JsonArray array) foreach (var child in array) Strip(child);
        }
        Strip(node); var session = new GameSession(); session.LoadJson(node.ToJsonString());
        var map = session.World.Module<RegionModule>(); Assert.Equal(64, map.Width); Assert.Equal(1, map.GeneratorVersion);
        Assert.All(map.Cells, c => Assert.Null(c.Cover));
        string state = JsonSerializer.Serialize(map.ToSnapshot()); session.Advance(5); session.Reset();
        Assert.Equal(state, JsonSerializer.Serialize(session.World.Module<RegionModule>().ToSnapshot()));
        session.NewRegion(42, SettlementMotif.TwinCenters); string fresh = session.SaveJson(); session.Advance(3); session.LoadJson(fresh); session.Reset();
        Assert.Equal(SettlementMotif.TwinCenters, session.World.Module<RegionModule>().Motif); Assert.Equal(142, session.World.Module<RegionModule>().Width);
    }
}
