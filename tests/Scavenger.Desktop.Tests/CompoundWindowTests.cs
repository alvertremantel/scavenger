using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haldane.SupplyChain;
using Scavenger.Desktop;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;
using Scavenger.Sim.Region;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Scavenger.Desktop.Tests;

public class CompoundWindowTests
{
    [Fact]
    public async Task ActualWindowAcceptsButtonsMapPlansDoorInputAndRendersSkiaFrame()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow(regional: false); window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.Map.Bounds.Width > 400);
                Assert.True(window.Map.Bounds.Height > 400);
                Click(window, Find<Button>(window, "StepButton"));
                Assert.Equal(1, window.Session.Report.Tick);
                window.ToolSelector.SelectedIndex = 5; // Workbench
                ClickCell(window, new CellPosition(8, 8));
                var job = Assert.Single(window.Session.Report.Construction);
                Assert.Equal(new CellPosition(8, 8), job.Origin);
                Assert.Equal(2, window.Session.Report.Tick);
                Assert.NotEmpty(window.Session.Report.Paths);
                window.ToolSelector.SelectedIndex = 0;
                ClickCell(window, new CellPosition(13, 5));
                Click(window, Find<Button>(window, "DoorButton"));
                Assert.Equal(Terrain.ClosedDoor, window.Session.World.Module<CompoundModule>().TileAt(new CellPosition(13, 5)).Terrain);
                Assert.Equal(new CellPosition(13, 5), window.Map.SelectedCell);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.True(frame.PixelSize.Width >= 1050);
                Assert.True(frame.PixelSize.Height >= 780);
                using var bytes = new MemoryStream(); frame.Save(bytes, PngBitmapEncoderOptions.Default);
                Assert.True(bytes.Length > 15000); // Real text, map, and tables, rather than the headless stub.
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AllBuildToolsResolveAndUiSaveLoadRestoresLiveSimulation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"scavenger-ui-{Guid.NewGuid():N}.json");
            var window = new MainWindow(regional: false) { SavePath = path }; window.Show();
            try
            {
                foreach (var tool in window.ToolSelector.Items.Cast<MapTool>().Where(t => t.DefinitionId is not null))
                    Assert.NotNull(window.Session.World.Module<CompoundModule>().Definition(tool.DefinitionId!));
                Click(window, Find<Button>(window, "DemoButton"));
                Assert.Equal(1, window.Session.Report.Tick);
                window.SaveSlot();
                string saved = window.Session.SaveJson();
                window.Session.Advance(30);
                Click(window, Find<Button>(window, "LoadButton"));
                Assert.Equal(1, window.Session.Report.Tick);
                // Save metadata is time-dependent; compare authoritative state through its projection.
                Assert.Equal(345.55m, window.Session.Report.TotalTrackedKg);
                Assert.Equal(4, window.Session.World.Module<OrdersModule>().Orders.Count);
                File.WriteAllText(path, "invalid");
                window.LoadSlot();
                Assert.Equal(1, window.Session.Report.Tick);
                window.Session.Advance(100);
                Assert.NotEmpty(window.Session.World.Module<ProductionModule>().Jobs);
                Assert.NotEmpty(saved);
            }
            finally { window.Close(); File.Delete(path); File.Delete(path + ".tmp"); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task IndustrialControlsCompleteRecoveryAssemblyAndVerifiedOfflineAutomation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow(regional: false); window.Show();
            try
            {
                Click(window, Find<Button>(window, "RecoveryOrdersButton"));
                AdvanceUntil(window, () => window.Session.World.Module<InventoryModule>().Batches.Where(b => b.MaterialId == "part.polymer" && b.StockpileId == "workshop").Sum(b => b.QuantityKg) >= 1.6m);
                window.Industry.Tabs.SelectedIndex = 1; // Hardware
                SelectParts(window, IndustrialScenario.StarterKit.Concat(["usb-archive", "sensor-a", "controller-a"]));
                Click(window, Find<Button>(window, "DiagnoseButton")); Assert.True(window.Session.World.Module<ComputingModule>().Jobs.Count > 0, window.Session.Status); WaitTask(ComputerTaskKind.DiagnoseComponents);
                SelectParts(window, ["fan-a"]); Click(window, Find<Button>(window, "RepairComponentButton")); WaitTask(ComputerTaskKind.RepairComponent);
                SelectParts(window, IndustrialScenario.StarterKit); Click(window, Find<Button>(window, "AssembleButton")); WaitTask(ComputerTaskKind.Assemble);
                Click(window, Find<Button>(window, "ComputerPowerButton"));
                window.Industry.Tabs.SelectedIndex = 2; // Archives
                foreach (string asset in IndustrialWalkthrough.Software)
                {
                    SelectChoice(window, "AssetSelector", asset);
                    Click(window, Find<Button>(window, "InstallAssetButton"));
                    AdvanceUntil(window, () => window.Session.World.Module<ComputingModule>().Copies.Any(c => c.AssetId == asset));
                }
                SelectChoice(window, "AssetSelector", "training-textbook"); Click(window, Find<Button>(window, "StudyButton")); WaitTask(ComputerTaskKind.Study);
                window.Industry.Tabs.SelectedIndex = 1; SelectParts(window, ["sensor-a", "controller-a"]);
                window.Industry.Tabs.SelectedIndex = 3; SelectChoice(window, "TelemetryTargetSelector", "dryer");
                Click(window, Find<Button>(window, "TelemetryButton")); WaitTask(ComputerTaskKind.InstallTelemetry);
                Click(window, Find<Button>(window, "RuleMonitorButton")); window.Session.Advance(50);
                Assert.True(window.Session.World.Module<ComputingModule>().CanControl(window.Session.World, "dryer"));
                Click(window, Find<Button>(window, "LabelTelemetryButton")); WaitTask(ComputerTaskKind.LabelTelemetry);
                Click(window, Find<Button>(window, "TrainAdapterButton")); WaitTask(ComputerTaskKind.TrainAdapter);
                Click(window, Find<Button>(window, "ValidateAdapterButton")); WaitTask(ComputerTaskKind.ValidateAdapter);
                Click(window, Find<Button>(window, "ModelMonitorButton")); window.Session.Advance(2);
                Assert.Contains(window.Session.World.Module<ComputingModule>().Jobs, j => j.Plan.Kind == ComputerTaskKind.ModelMonitor && j.Status == ComputerTaskStatus.Running);
                window.Industry.Tabs.SelectedIndex = 4; Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                using var bytes = new MemoryStream(); frame.Save(bytes, PngBitmapEncoderOptions.Default); Assert.True(bytes.Length > 15000);
                PhysicalSave.ValidateWorld(window.Session.World);
                void WaitTask(ComputerTaskKind kind) => AdvanceUntil(window, () => window.Session.World.Module<ComputingModule>().Jobs.Any(j => j.Plan.Kind == kind && j.Status == ComputerTaskStatus.Completed));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task ExpeditionControlsRecoverTwoDestinationsMakeMedicineTreatPatientAndRender()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow(generated: false) { Width = 1050, Height = 780 }; window.Show();
            try
            {
                SelectChoice(window, "ExpeditionLocation", "pharmacy");
                SelectSalvage("stock-sealed", "excipient", "sterile-water");
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Assert.Single(window.Session.World.Module<ScavengingModule>().Expeditions);
                FinishExpeditions();
                SelectChoice(window, "ExpeditionLocation", "hardware"); SelectSalvage("filter-sorbent");
                Find<NumericUpDown>(window, "SalvageQuantity").Value = .1m;
                Click(window, Find<Button>(window, "DispatchExpeditionButton")); FinishExpeditions();
                Click(window, Find<Button>(window, "RefineMedicineButton"));
                AdvanceUntil(window, () => window.Session.World.Module<InventoryModule>().Batches.Any(b => b.StockpileId == "clinic" && b.MaterialId == "medicine.refined"));
                Click(window, Find<Button>(window, "FormulateMedicineButton"));
                AdvanceUntil(window, () => window.Session.World.Module<InventoryModule>().Batches.Any(b => b.StockpileId == "clinic" && b.MaterialId == "medicine.fever-dose"));
                Click(window, Find<Button>(window, "TreatPatientButton"));
                AdvanceUntil(window, () => !window.Session.World.Module<ScavengingModule>().HealthOf(1)!.Sick);
                PhysicalSave.ValidateWorld(window.Session.World);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                using var bytes = new MemoryStream(); frame.Save(bytes, PngBitmapEncoderOptions.Default); Assert.True(bytes.Length > 15000);
                void SelectSalvage(params string[] ids)
                {
                    var selector = window.Scavenging.SalvageSelector; selector.SelectedItems!.Clear();
                    foreach (var c in selector.Items.Cast<Choice>().Where(c => ids.Contains(c.Id))) selector.SelectedItems.Add(c);
                    Dispatcher.UIThread.RunJobs();
                }
                void FinishExpeditions()
                {
                    for (int i = 0; i < 100 && window.Session.World.Module<ScavengingModule>().Expeditions.Any(e => e.Active); i++)
                    {
                        foreach (var e in window.Session.World.Module<ScavengingModule>().Expeditions.Where(e => e.Status == ExpeditionStatus.Decision).ToArray())
                        { SelectChoice(window, "ActiveExpedition", e.Plan.Id); Click(window, Find<Button>(window, "WaitExpeditionButton")); }
                        window.Session.Advance(4);
                    }
                    Assert.DoesNotContain(window.Session.World.Module<ScavengingModule>().Expeditions, e => e.Active);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task RegionalMapSeedInputSelectionExpeditionAndSaveLoadWorkThroughRealUi()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                Assert.Equal(1, window.WorkspaceTabs.SelectedIndex);
                Assert.True(window.Region.Map.Bounds.Width > 400);
                Find<TextBox>(window, "RegionSeed").Text = "42";
                Click(window, Find<Button>(window, "GenerateRegionButton"));
                var geography = window.Session.World.Module<RegionModule>(); Assert.Equal(42UL, geography.Seed);
                var site = geography.Site("pharmacy-001")!;
                var point = window.Region.Map.TranslatePoint(window.Region.Map.ParcelCenter(site.Parcel), window)!.Value;
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs(); Assert.Equal(site.LocationId, window.Region.Map.SelectedSite);
                using (var frame = window.CaptureRenderedFrame())
                { Assert.NotNull(frame); using var bytes = new MemoryStream(); frame.Save(bytes, PngBitmapEncoderOptions.Default); Assert.True(bytes.Length > 15000); }
                Click(window, Find<Button>(window, "PlanRegionalExpeditionButton"));
                Assert.Equal(0, window.WorkspaceTabs.SelectedIndex); Assert.Equal(0, window.OperationsTabs.SelectedIndex);
                var location = Find<ComboBox>(window, "ExpeditionLocation"); Assert.Equal(site.LocationId, ((Choice)location.SelectedItem!).Id);
                var salvage = window.Scavenging.SalvageSelector;
                salvage.SelectedItems!.Add(salvage.Items.Cast<Choice>().Single(c => c.Id == "stock-sealed"));
                Find<NumericUpDown>(window, "SalvageQuantity").Value = .1m;
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Assert.Single(window.Session.World.Module<ScavengingModule>().Expeditions);
                string saved = window.Session.SaveJson(); window.Session.Advance(4); window.Session.LoadJson(saved);
                Assert.Equal(42UL, window.Region.Map.Region!.Seed); PhysicalSave.ValidateWorld(window.Session.World);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task FractionalTerrainLayoutLayersAndZoomAreInspectableThroughActualMapInput()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                Find<TextBox>(window, "RegionSeed").Text = "42";
                SelectChoice(window, "RegionMotif", "TwinCenters"); Click(window, Find<Button>(window, "GenerateRegionButton"));
                var map = window.Session.World.Module<RegionModule>(); Assert.Equal(SettlementMotif.TwinCenters, map.Motif);
                var cell = Enumerable.Range(0, map.Cells.Count).Where(i => map.Cells[i].Cover!.Count > 1)
                    .Select(i => new RegionPoint(i % map.Width, i / map.Width)).First(p => map.Sites.All(s => s.Parcel.Manhattan(p) > 2));
                var point = window.Region.Map.TranslatePoint(window.Region.Map.ParcelCenter(cell), window)!.Value;
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.Equal(cell, window.Region.Map.SelectedCell); Assert.Null(window.Region.Map.SelectedSite);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Mean elevation") == true);
                Find<ComboBox>(window, "RegionLayer").SelectedItem = MapLayer.FloodRisk; Dispatcher.UIThread.RunJobs();
                Assert.Equal(MapLayer.FloodRisk, window.Region.Map.Layer);
                double before = window.Region.Map.ParcelCenter(new(cell.X + 1, cell.Y)).X - window.Region.Map.ParcelCenter(cell).X;
                Click(window, Find<Button>(window, "RegionZoom"));
                double after = window.Region.Map.ParcelCenter(new(cell.X + 1, cell.Y)).X - window.Region.Map.ParcelCenter(cell).X;
                Assert.Equal(before * 2, after, 6);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                using var bytes = new MemoryStream(); frame.Save(bytes, PngBitmapEncoderOptions.Default); Assert.True(bytes.Length > 15000);
                Click(window, Find<Button>(window, "RegionFit"));
                Assert.Equal(before, window.Region.Map.ParcelCenter(new(cell.X + 1, cell.Y)).X - window.Region.Map.ParcelCenter(cell).X, 6);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void SelectParts(MainWindow window, IEnumerable<string> ids)
    {
        var selector = window.Industry.ComponentSelector; selector.SelectedItems!.Clear();
        foreach (var choice in selector.Items.Cast<Choice>().Where(c => ids.Contains(c.Id))) selector.SelectedItems.Add(choice);
        Dispatcher.UIThread.RunJobs();
    }
    [Fact]
    public async Task MinimumWindowCanDispatchBulkAndWholeComponentTransfers()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow(regional: false) { Width = 1050, Height = 780 }; window.Show();
            try
            {
                window.Industry.Tabs.SelectedIndex = 5;
                SelectChoice(window, "TransferBatchSelector", "salvage-metal.steel");
                Click(window, Find<Button>(window, "TransferButton"));
                var first = Assert.Single(window.Session.World.Module<LogisticsModule>().Jobs); Assert.Equal(1, first.Request.QuantityKg);
                AdvanceUntil(window, () => first.Status == HaulStatus.Delivered);
                SelectChoice(window, "TransferBatchSelector", "component/board-a"); Click(window, Find<Button>(window, "TransferButton"));
                var second = window.Session.World.Module<LogisticsModule>().Jobs.Single(j => j.Request.Id != first.Request.Id);
                Assert.Equal(1.2m, second.Request.QuantityKg); AdvanceUntil(window, () => second.Status == HaulStatus.Delivered);
                SelectChoice(window, "TransferBatchSelector", "component/cpu-a");
                Find<CheckBox>(window, "TransferWholeUnit").IsChecked = false;
                long tick = window.Session.Report.Tick; Click(window, Find<Button>(window, "TransferButton"));
                Assert.Equal(tick, window.Session.Report.Tick); Assert.Contains("complete unit", window.Session.Status);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); PhysicalSave.ValidateWorld(window.Session.World);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void SelectChoice(MainWindow window, string name, string id)
    {
        Dispatcher.UIThread.RunJobs(); var selector = Find<ComboBox>(window, name);
        selector.SelectedItem = selector.Items.Cast<Choice>().Single(c => c.Id == id);
        Dispatcher.UIThread.RunJobs();
    }
    private static void AdvanceUntil(MainWindow window, Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) window.Session.Advance(20);
        Assert.True(done(), window.Session.Status + "\n" + string.Join("\n", window.Session.World.Module<ComputingModule>().Jobs.Select(j => $"{j.Plan.Kind}: {j.Status} {j.BlockedReason}")));
    }
    private static T Find<T>(MainWindow window, string name) where T : Control
        => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(MainWindow window, Control control)
    {
        control.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using var rendered = window.CaptureRenderedFrame();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
    private static void ClickCell(MainWindow window, CellPosition cell)
    {
        Dispatcher.UIThread.RunJobs();
        var point = window.Map.TranslatePoint(window.Map.CellCenter(cell), window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
