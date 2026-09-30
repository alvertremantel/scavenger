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
using Scavenger.Sim.Skills;

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
                SelectChoice(window, "CompoundContext", "tasks");
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
                window.Navigate("industry/0"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
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
                if (Environment.GetEnvironmentVariable("SCAVENGER_UI_ARTIFACT_DIR") is { Length: > 0 } auditPath)
                {
                    Directory.CreateDirectory(auditPath);
                    foreach (string route in new[] { "home", "compound", "industry/0", "industry/1", "industry/2", "industry/3", "industry/4", "register/0" })
                    {
                        window.Navigate(route); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        using var audit = window.CaptureRenderedFrame()!;
                        audit.Save(Path.Combine(auditPath, "developed-" + route.Replace("/", "-") + ".png"), PngBitmapEncoderOptions.Default);
                    }
                }
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
                window.Navigate("scavenging"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                SelectChoice(window, "ExpeditionLocation", "pharmacy");
                SelectSalvage("stock-sealed", "excipient", "sterile-water");
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Assert.Single(window.Session.World.Module<ScavengingModule>().Expeditions);
                FinishExpeditions();
                window.Navigate("scavenging"); SelectChoice(window, "ExpeditionLocation", "hardware"); SelectSalvage("filter-sorbent");
                Find<Expander>(window, "BulkQuantities").IsExpanded = true;
                Find<NumericUpDown>(window, "SalvageQuantity").Value = .1m;
                Click(window, Find<Button>(window, "DispatchExpeditionButton")); FinishExpeditions();
                window.Navigate("clinic"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
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
                    window.Navigate("scavenging"); Click(window, Find<Button>(window, "PlanStep2Button"));
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
                Assert.Equal(0, window.WorkspaceTabs.SelectedIndex);
                window.Navigate("region"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                window.Region.Tabs.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
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
                Assert.Equal(1, window.WorkspaceTabs.SelectedIndex); Assert.Equal(0, window.Scavenging.Tabs.SelectedIndex);
                var location = Find<ComboBox>(window, "ExpeditionLocation"); Assert.Equal(site.LocationId, ((Choice)location.SelectedItem!).Id);
                Click(window, Find<Button>(window, "PlanStep2Button"));
                var salvage = window.Scavenging.SalvageSelector;
                salvage.SelectedItems!.Add(salvage.Items.Cast<Choice>().Single(c => c.Id == "stock-sealed"));
                Find<Expander>(window, "BulkQuantities").IsExpanded = true;
                Find<NumericUpDown>(window, "SalvageQuantity").Value = .1m;
                Assert.True(Find<Button>(window, "DispatchExpeditionButton").IsEnabled, Find<TextBlock>(window, "ExpeditionReadiness").Text);
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
                window.Navigate("region"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                window.Region.Tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
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
    [Fact]
    public async Task RegionWheelAnchorsTheCursorAndCapturedDragsDoNotSelectSites()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                window.Navigate("region"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var map = window.Region.Map;
                var parcel = new RegionPoint(60, 64);
                Point OnWindow(Point local) => map.TranslatePoint(local, window)!.Value;
                double Length(Point p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);
                var anchor = map.ParcelCenter(parcel);
                window.MouseWheel(OnWindow(anchor), new Vector(0, 3), RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Math.Pow(1.2, 3), map.ZoomLevel, 8);
                Assert.InRange(Length(map.ParcelCenter(parcel) - anchor), 0, .000001);
                var selected = map.SelectedSite;
                int selections = 0; map.SiteClicked += _ => selections++; map.CellClicked += _ => selections++;
                var start = new Point(map.Bounds.Width / 2, map.Bounds.Height / 2);
                var delta = new Vector(75, 36);
                var before = map.ParcelCenter(parcel);
                window.MouseDown(OnWindow(start), MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(OnWindow(start + delta), RawInputModifiers.LeftMouseButton);
                window.MouseUp(OnWindow(start + delta), MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.InRange(Length(map.ParcelCenter(parcel) - before - delta), 0, .000001);
                Assert.Equal(selected, map.SelectedSite); Assert.Equal(0, selections);
                // Capture continues outside the map; releasing there must never inspect a parcel.
                window.MouseDown(OnWindow(start), MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(OnWindow(new Point(-25, start.Y)), RawInputModifiers.LeftMouseButton);
                window.MouseUp(OnWindow(new Point(-25, start.Y)), MouseButton.Left, RawInputModifiers.None);
                Assert.Equal(0, selections);
                Click(window, Find<Button>(window, "RegionFit")); Assert.Equal(1, map.ZoomLevel);
                var site = map.Region!.Sites.First();
                window.MouseDown(OnWindow(map.ParcelCenter(site.Parcel)), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(OnWindow(map.ParcelCenter(site.Parcel)), MouseButton.Left, RawInputModifiers.None);
                Assert.Equal(site.LocationId, map.SelectedSite); Assert.Equal(1, selections);
                window.MouseWheel(OnWindow(start), new Vector(0, 100), RawInputModifiers.None);
                Assert.Equal(16, map.ZoomLevel);
                Click(window, Find<Button>(window, "RegionZoomOut")); Assert.Equal(8, map.ZoomLevel);
                window.MouseWheel(OnWindow(start), new Vector(0, -100), RawInputModifiers.None);
                Assert.Equal(1, map.ZoomLevel);
                Assert.InRange(Length(map.ParcelCenter(parcel) - anchor), 0, .000001);
                // Inspect a rendered view after mouse navigation rather than only its geometry.
                window.MouseWheel(OnWindow(start), new Vector(0, 5), RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                if (Environment.GetEnvironmentVariable("SCAVENGER_UI_ARTIFACT_DIR") is { Length: > 0 } path)
                { Directory.CreateDirectory(path); frame.Save(Path.Combine(path, "playable-region-navigation.png"), PngBitmapEncoderOptions.Default); }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task DashboardLinksAndKeyboardReachFourWorkspacesWithoutAdvancingAndRetainSelections()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, window.WorkspaceTabs.SelectedIndex);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "3 / 4");
                decimal mass = window.Session.Report.TotalTrackedKg;
                Click(window, Find<Button>(window, "AttentionclinicButton"));
                Assert.Equal(2, window.WorkspaceTabs.SelectedIndex); Assert.Equal("clinic", window.CompoundWorkspace.Mode);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Mira · Ill / unavailable", StringComparison.Ordinal) == true);
                Click(window, Find<Button>(window, "NavDashboardButton"));
                Click(window, Find<Button>(window, "Homeindustry4Button"));
                Assert.Equal(2, window.WorkspaceTabs.SelectedIndex); Assert.Equal(4, window.Industry.Tabs.SelectedIndex);
                Click(window, Find<Button>(window, "NavRegistersButton"));
                window.OperationsTabs.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
                Click(window, Find<Button>(window, "NavDashboardButton"));
                Click(window, Find<Button>(window, "NavRegistersButton")); Assert.Equal(2, window.OperationsTabs.SelectedIndex);
                window.KeyPress(Key.D2, RawInputModifiers.Control, default, null); window.KeyRelease(Key.D2, RawInputModifiers.Control, default, null); Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, window.WorkspaceTabs.SelectedIndex);
                window.KeyPress(Key.Tab, RawInputModifiers.Control, default, null); window.KeyRelease(Key.Tab, RawInputModifiers.Control, default, null); Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, window.WorkspaceTabs.SelectedIndex);
                window.KeyPress(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift, default, null); window.KeyRelease(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift, default, null); Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, window.WorkspaceTabs.SelectedIndex);
                Assert.Equal(0, window.Session.Report.Tick); Assert.Equal(mass, window.Session.Report.TotalTrackedKg);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task RegisterSearchFiltersLiveRecordsAndSurvivesNavigationAndSimulationRefresh()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow(regional: false); window.Show();
            try
            {
                window.Navigate("register/0"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var filter = Find<TextBox>(window, "RegisterFilter"); filter.Text = "DIESEL"; Dispatcher.UIThread.RunJobs();
                string[] Rows() => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                Assert.Contains(Rows(), t => t.Contains("fuel.diesel / depot-diesel"));
                Assert.DoesNotContain(Rows(), t => t.Contains("water.clean / process-water"));
                window.Session.Advance(); Dispatcher.UIThread.RunJobs(); Assert.Equal("DIESEL", filter.Text);
                Click(window, Find<Button>(window, "NavDashboardButton")); Click(window, Find<Button>(window, "NavRegistersButton"));
                Assert.Equal("DIESEL", Find<TextBox>(window, "RegisterFilter").Text);
                filter.Text = "no-matching-record"; Dispatcher.UIThread.RunJobs(); Assert.Contains("No records in this view.", Rows());
                filter.Text = ""; Dispatcher.UIThread.RunJobs(); Assert.Contains(Rows(), t => t.Contains("water.clean / process-water"));
                Assert.Equal(1, window.Session.Report.Tick);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task MinimumWindowRendersEveryWorkspaceWithPersistentCommandBarAndVisibleMapAction()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                foreach (string nav in new[] { "Dashboard", "Region", "Compound", "Registers" })
                {
                    Click(window, Find<Button>(window, "Nav" + nav + "Button"));
                    foreach (string name in new[] { "StepButton", "DecisionButton", "SaveButton", "LoadButton" })
                    {
                        var c = Find<Button>(window, name); var p = c.TranslatePoint(default, window)!.Value;
                        Assert.InRange(p.X, 0, window.Width - c.Bounds.Width); Assert.InRange(p.Y, 0, window.Height - c.Bounds.Height);
                    }
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    Assert.Equal(new PixelSize(1050, 780), frame.PixelSize);
                    if (nav == "Region")
                    {
                        var button = Find<Button>(window, "PlanRegionalExpeditionButton"); var p = button.TranslatePoint(default, window)!.Value;
                        Assert.InRange(p.Y, 0, window.Height - button.Bounds.Height);
                        Assert.True(window.Region.Map.Bounds.Height >= 200);
                    }
                }
                Assert.Equal(0, window.Session.Report.Tick);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Theory]
    [InlineData(1050, 780)]
    [InlineData(1440, 1000)]
    public async Task FourWorkspacesKeepMapsVisibleAcrossManagementContextsAndDrawersPreservePlans(int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = width, Height = height }; window.Show();
            try
            {
                Assert.Equal(4, window.WorkspaceTabs.Items.Count);
                window.Navigate("scavenging"); Click(window, Find<Button>(window, "PlanPharmacyButton"));
                var selected = window.Scavenging.TeamSelector.SelectedItems!.Cast<Choice>().Select(c => c.Id).ToArray();
                decimal? quantity = Find<NumericUpDown>(window, "CargoKg_excipient").Value;
                double before = window.Region.Map.Bounds.Width;
                Click(window, Find<Button>(window, "RegionCloseSidebarButton"));
                Assert.False(window.Region.Workspace.SidebarOpen); Assert.True(window.Region.Map.Bounds.Width > before + 300);
                Click(window, Find<Button>(window, "RegionOpenSidebarButton"));
                Assert.Equal("plan", window.Region.Workspace.Mode);
                Assert.Equal(selected, window.Scavenging.TeamSelector.SelectedItems!.Cast<Choice>().Select(c => c.Id));
                Assert.Equal(quantity, Find<NumericUpDown>(window, "CargoKg_excipient").Value);
                foreach (string route in new[] { "clinic", "industry/0", "industry/1", "industry/2", "industry/3", "industry/4", "industry/5", "industry/6" })
                {
                    window.Navigate(route); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.Equal(2, window.WorkspaceTabs.SelectedIndex);
                    Assert.True(window.Map.Bounds.Width > 600); Assert.True(window.Map.Bounds.Height > 500);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    Capture(route.Replace('/', '-'));
                }
                window.Navigate("industry/1");
                Click(window, Find<Button>(window, "FindComputerPartsButton"));
                Assert.Equal("scout", window.Region.Workspace.Mode);
                Assert.Equal("computer", Find<TextBox>(window, "ScoutSearch").Text);
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name?.StartsWith("ScoutSite_") == true);
                window.Navigate("clinic"); window.Session.Advance(0);
                Assert.Equal("clinic", window.CompoundWorkspace.Mode); // Refreshes must not steal the open context.
                window.Navigate("compound");
                foreach (string mode in new[] { "build", "crew", "tasks", "inspect" })
                { SelectChoice(window, "CompoundContext", mode); Capture("compound-" + mode); }
                Assert.Equal(0, window.Session.Report.Tick);
                void Capture(string name)
                {
                    if (Environment.GetEnvironmentVariable("SCAVENGER_UI_ARTIFACT_DIR") is not { Length: > 0 } path) return;
                    Directory.CreateDirectory(path); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(path, $"management-{width}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task ScoutingSearchConnectsRemainingMaterialsMapInspectionAndTheExpeditionDestination()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                window.Navigate("region"); Click(window, Find<Button>(window, "RegionScoutButton"));
                Find<TextBox>(window, "ScoutSearch").Text = "glass.borosilicate"; Dispatcher.UIThread.RunJobs();
                var result = window.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("ScoutSite_") == true);
                string id = result.Name!["ScoutSite_".Length..];
                Assert.Contains(window.Session.World.Module<ScavengingModule>().Location(id)!.Definition.Salvage, s => s.MaterialId == "glass.borosilicate");
                Click(window, result); Assert.Equal("inspect", window.Region.Workspace.Mode); Assert.Equal(id, window.Region.Map.SelectedSite);
                Assert.Equal(window.Region.Map.Region!.Site(id)!.Parcel, window.Region.Map.SelectedCell);
                Click(window, Find<Button>(window, "PlanRegionalExpeditionButton"));
                Assert.Equal("plan", window.Region.Workspace.Mode);
                Assert.Equal(id, ((Choice)Find<ComboBox>(window, "ExpeditionLocation").SelectedItem!).Id);
                Assert.True(window.Region.Map.Bounds.Width > 600); Assert.True(window.Region.Map.Bounds.Height > 500);
                Assert.Equal(0, window.Session.Report.Tick);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task FixtureAndFieldTeamClicksOpenContextualActionsAndTaskCardsCanCancelWork()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780 }; window.Show();
            try
            {
                window.Navigate("compound"); ClickCell(window, new CellPosition(18,12));
                Assert.Equal("clinic", window.CompoundWorkspace.Mode);
                var washer = window.Session.World.Module<CompoundModule>().FindBuilding("washer")!;
                ClickCell(window, washer.Origin);
                Assert.Equal("production", window.CompoundWorkspace.Mode);
                Assert.Equal("wash-hdpe", ((Choice)Find<ComboBox>(window, "RecipeSelector").SelectedItem!).Id);
                Click(window, Find<Button>(window, "QueueProcessButton"));
                var batch = Assert.Single(window.Session.World.Module<ProductionModule>().Jobs);
                Click(window, Find<Button>(window, "CompoundTasksButton"));
                Click(window, Find<Button>(window, "CancelProduction_" + batch.Plan.Id));
                Assert.Equal(ProductionStatus.Cancelled, batch.Status);
                window.Navigate("scavenging"); Click(window, Find<Button>(window, "PlanPharmacyButton"));
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                var team = Assert.Single(window.Session.World.Module<ScavengingModule>().Expeditions);
                var map = window.Region.Map;
                Assert.Equal(team.Plan.Id, Assert.Single(map.Teams).Id);
                var point = map.TranslatePoint(map.TeamCenter(team.Plan.Id), window)!.Value;
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("teams", window.Region.Workspace.Mode);
                Assert.Equal(team.Plan.LocationId, map.SelectedSite);
                Assert.Equal(team.Plan.Id, ((Choice)Find<ComboBox>(window, "ActiveExpedition").SelectedItem!).Id);
                PhysicalSave.ValidateWorld(window.Session.World);
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
                window.Navigate("industry/5"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
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
    [Fact]
    public async Task PlainClicksToggleCrewAndCustomCargoAndIndividualAmountsReachTheOrder()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow(generated: false) { Width = 1050, Height = 780 }; window.Show();
            try
            {
                window.Navigate("scavenging");
                Click(window, Find<Button>(window, "PlanPharmacyButton"));
                Click(window, Find<Button>(window, "PlanStep1Button"));
                var team = window.Scavenging.TeamSelector;
                var ivo = team.GetRealizedContainers().OfType<ListBoxItem>().Single(c => c.Content is Choice choice && choice.Id == "2");
                Click(window, ivo);
                Assert.Contains(team.SelectedItems!.Cast<Choice>(), c => c.Id == "2");
                Assert.Contains(team.SelectedItems!.Cast<Choice>(), c => c.Id == "4");
                Click(window, ivo);
                Assert.DoesNotContain(team.SelectedItems!.Cast<Choice>(), c => c.Id == "2");
                Click(window, Find<Button>(window, "PlanStep2Button"));
                var cargo = window.Scavenging.SalvageSelector;
                var stock = cargo.GetRealizedContainers().OfType<ListBoxItem>().Single(c => c.Content is Choice choice && choice.Id == "stock-sealed");
                Click(window, stock); Assert.DoesNotContain(cargo.SelectedItems!.Cast<Choice>(), c => c.Id == "stock-sealed");
                Click(window, stock); Assert.Contains(cargo.SelectedItems!.Cast<Choice>(), c => c.Id == "stock-sealed");
                Find<NumericUpDown>(window, "CargoKg_stock_sealed").Value = .2m;
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                var order = Assert.Single(window.Session.World.Module<ScavengingModule>().Expeditions).Plan;
                Assert.Equal(.2m, order.Manifest.Single(m => m.SalvageId == "stock-sealed").QuantityKg);
                Assert.Equal(.12m, order.Manifest.Single(m => m.SalvageId == "excipient").QuantityKg);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task GeneratedWorldIsPlayableForSeveralDaysUsingOnlyPlayerControls()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = 1050, Height = 780, SavePath = Path.Combine(Path.GetTempPath(), "playability-" + Guid.NewGuid() + ".json") }; window.Show();
            try
            {
                decimal mass = window.Session.Report.TotalTrackedKg;
                window.Navigate("compound");
                long freshTick = window.Session.World.Clock.Tick;
                Click(window, Find<Button>(window, "ClinicButton"));
                Assert.Equal(freshTick, window.Session.World.Clock.Tick);
                Assert.False(window.Session.World.Module<Haldane.CareerSim.Work.ProjectBoardModule>().IsAssigned(4));
                Assert.False(Find<Button>(window, "RefineMedicineButton").IsEnabled);
                window.Navigate("home");
                Click(window, Find<Button>(window, "HomeplanpharmacyButton"));
                Assert.Equal(.1m, Find<NumericUpDown>(window, "CargoKg_stock_sealed").Value);
                Assert.Equal(.12m, Find<NumericUpDown>(window, "CargoKg_excipient").Value);
                Assert.Equal(.8m, Find<NumericUpDown>(window, "CargoKg_sterile_water").Value);
                Capture("pharmacy-plan");
                var before = window.Session.Report.Workers.ToDictionary(w => w.Id, w => w.Position);
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Assert.Equal(1, window.Scavenging.Tabs.SelectedIndex); Capture("active-team");
                Assert.Contains(window.Session.Report.Workers, w => before[w.Id] != w.Position);
                Until(() => window.Session.World.Module<ScavengingModule>().Expeditions.All(e => !e.Active));
                Click(window, Find<Button>(window, "OpenPlannerButton"));
                Click(window, Find<Button>(window, "PlanSorbentButton"));
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Until(() => window.Session.World.Module<ScavengingModule>().Expeditions.All(e => !e.Active));
                window.Navigate("clinic");
                Click(window, Find<Button>(window, "RefineMedicineButton"));
                Until(() => Stock("medicine.refined", "clinic") >= .08m);
                Click(window, Find<Button>(window, "FormulateMedicineButton"));
                Until(() => Stock("medicine.fever-dose", "clinic") >= .1m);
                Click(window, Find<Button>(window, "TreatPatientButton"));
                Until(() => !window.Session.World.Module<ScavengingModule>().HealthOf(1)!.Sick);
                Capture("clinic");
                window.Navigate("scavenging");
                Click(window, Find<Button>(window, "PlanFuelButton"));
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Click(window, Find<Button>(window, "SaveButton")); long savedTick = window.Session.World.Clock.Tick;
                Click(window, Find<Button>(window, "StepButton")); Click(window, Find<Button>(window, "LoadButton"));
                Assert.Equal(savedTick, window.Session.World.Clock.Tick);
                Until(() => window.Session.World.Module<ScavengingModule>().Expeditions.All(e => !e.Active));
                window.Navigate("industry/4"); Click(window, Find<Button>(window, "RefuelButton"));
                Assert.Contains(window.Session.World.Module<LogisticsModule>().Jobs, j => j.Active && j.Request.Id.StartsWith("refuel-"));
                Until(() => window.Session.World.Module<LogisticsModule>().Jobs.All(j => !j.Active));
                window.Navigate("compound"); window.ToolSelector.SelectedIndex = 4;
                ClickCell(window, new CellPosition(8,8));
                Until(() => window.Session.World.Module<ConstructionModule>().Jobs.All(j => !j.Active));
                Assert.Contains(window.Session.Report.Buildings, b => b.Footprint.Contains(new CellPosition(8,8)));
                // The starting clinic wears during its real medical batches; repair uses depot materials.
                window.ToolSelector.SelectedIndex = 0;
                ClickCell(window, new CellPosition(18,12));
                SelectChoice(window, "CompoundContext", "inspect");
                var repair = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Repair"));
                Click(window, repair);
                Until(() => window.Session.World.Module<ConstructionModule>().Jobs.All(j => !j.Active));
                Assert.Equal(1m, window.Session.World.Module<CompoundModule>().FindBuilding("clinic-station")!.Condition);
                Capture("compound");
                window.Navigate("industry/0"); Click(window, Find<Button>(window, "RecoveryOrdersButton"));
                Assert.Equal(4, window.Session.World.Module<OrdersModule>().Orders.Count);
                Until(() => Stock("part.polymer", "workshop") > 0);
                window.Navigate("scavenging"); Click(window, Find<Button>(window, "PlanFuelButton"));
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                Until(() => window.Session.World.Module<ScavengingModule>().Expeditions.All(e => !e.Active));
                window.Navigate("industry/4"); Click(window, Find<Button>(window, "RefuelButton"));
                Until(() => window.Session.World.Module<LogisticsModule>().Jobs.All(j => !j.Active));
                long lastDay = window.Session.World.Clock.Tick + 3 * window.Session.World.Clock.Time.TicksPerDay;
                Until(() => window.Session.World.Clock.Tick >= lastDay && window.Session.World.Module<UtilitiesModule>().ServiceFor("clinic-station")!.Operational, daySteps: true);
                Assert.True(window.Session.World.Clock.Tick >= lastDay);
                Assert.DoesNotContain(window.Session.World.Module<ScavengingModule>().Health, h => h.Sick);
                Assert.True(window.Session.World.Module<UtilitiesModule>().ServiceFor("clinic-station")!.Operational, $"Tick {window.Session.World.Clock.Tick}: " + window.Session.World.Module<UtilitiesModule>().ServiceFor("clinic-station")!.BlockedReason);
                Assert.True(window.Session.World.Module<EnergyModule>().LastFrame!.ServedKw > 0);
                Assert.InRange(Math.Abs(mass - window.Session.Report.TotalTrackedKg), 0, .000000001m);
                PhysicalSave.ValidateWorld(window.Session.World);
                window.Navigate("home"); Capture("developed-home");
                Click(window, Find<Button>(window, "SaveButton")); Click(window, Find<Button>(window, "LoadButton"));
                Assert.InRange(Math.Abs(mass - window.Session.Report.TotalTrackedKg), 0, .000000001m);

                decimal Stock(string material, string store) => window.Session.World.Module<InventoryModule>().Batches.Where(b => b.MaterialId == material && b.StockpileId == store).Sum(b => b.QuantityKg);
                void Until(Func<bool> done, bool daySteps = false)
                {
                    for (int i = 0; i < (daySteps ? 30 : 400) && !done(); i++)
                    {
                        if (window.Session.PendingDecision is not null)
                        {
                            window.Navigate("fieldteams");
                            long waiting = window.Session.World.Clock.Tick;
                            Click(window, Find<Button>(window, "DayButton")); Assert.Equal(waiting, window.Session.World.Clock.Tick);
                            Click(window, Find<Button>(window, "WaitExpeditionButton"));
                        }
                        var inventory = window.Session.World.Module<InventoryModule>();
                        bool fuelLow = Stock("fuel.diesel", "generator-tank") < 3;
                        bool depotReady = inventory.Batches.Any(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage" && inventory.AvailableKg(b.Id) >= .001m);

                        if (fuelLow && depotReady
                            && window.Session.World.Module<Haldane.CareerSim.Work.WorkforceModule>().ReservationFor(2) is null)
                        {
                            int workspace = window.WorkspaceTabs.SelectedIndex, industry = window.Industry.Tabs.SelectedIndex, field = window.Scavenging.Tabs.SelectedIndex;
                            window.Navigate("industry/4"); Click(window, Find<Button>(window, "RefuelButton"));
                            window.WorkspaceTabs.SelectedIndex = workspace; window.Industry.Tabs.SelectedIndex = industry; window.Scavenging.Tabs.SelectedIndex = field;
                        }
                        else if (fuelLow && !depotReady && !window.Session.World.Module<ScavengingModule>().Expeditions.Any(e => e.Active && e.Plan.Manifest.Any(m => window.Session.World.Module<ScavengingModule>().Location(e.Plan.LocationId)!.Definition.Salvage.Any(s => s.Id == m.SalvageId && s.MaterialId == "fuel.diesel"))))
                        {
                            int workspace = window.WorkspaceTabs.SelectedIndex, industry = window.Industry.Tabs.SelectedIndex, field = window.Scavenging.Tabs.SelectedIndex;
                            window.Navigate("scavenging"); Click(window, Find<Button>(window, "PlanFuelButton"));
                            if (Find<Button>(window, "DispatchExpeditionButton").IsEnabled) Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                            window.WorkspaceTabs.SelectedIndex = workspace; window.Industry.Tabs.SelectedIndex = industry; window.Scavenging.Tabs.SelectedIndex = field;
                        }
                        Click(window, Find<Button>(window, daySteps ? "DayButton" : "AdvanceButton"));
                    }
                    Assert.True(done(), window.Session.Status + "\n" + string.Join("\n", window.Session.World.Module<ProductionModule>().Jobs.Select(j => $"{j.Plan.RecipeId}: {j.Status} {j.BlockedReason}")));
                }
                void Capture(string name)
                {
                    if (Environment.GetEnvironmentVariable("SCAVENGER_UI_ARTIFACT_DIR") is not { Length: > 0 } path) return;
                    Directory.CreateDirectory(path); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(path, "playable-" + name + ".png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); File.Delete(window.SavePath); }
        }, CancellationToken.None);
    }
    [Theory]
    [InlineData(1050, 780)]
    [InlineData(1440, 1000)]
    public async Task CrewCanFindRecoverStudyCancelResumeAndPractiseWholeBooksThroughMouseControls(int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { Width = width, Height = height }; window.Show();
            try
            {
                window.Navigate("compound"); Click(window, Find<Button>(window, "CompoundCrewButton"));
                SelectChoice(window, "LearningMember", "3"); SelectChoice(window, "LearningSkill", "construction");
                Assert.False(Find<Button>(window, "StartSkillStudyButton").IsEnabled);
                Assert.False(Find<Button>(window, "StartSkillPracticeButton").IsEnabled);
                long tick = window.Session.World.Clock.Tick;
                Click(window, Find<Button>(window, "FindSkillBookButton"));
                Assert.Equal(tick, window.Session.World.Clock.Tick);
                Assert.Equal("book/construction/7", Find<TextBox>(window, "ScoutSearch").Text);
                Click(window, Find<Button>(window, "ScoutSite_apartments-005"));
                Click(window, Find<Button>(window, "PlanRegionalExpeditionButton"));
                Click(window, Find<Button>(window, "PlanStep1Button"));
                window.Scavenging.TeamSelector.SelectedItems!.Clear();
                window.Scavenging.TeamSelector.SelectedItems.Add(window.Scavenging.TeamSelector.Items.Cast<Choice>().Single(c => c.Id == "2"));
                Click(window, Find<Button>(window, "PlanStep2Button"));
                var cargo = window.Scavenging.SalvageSelector;
                cargo.SelectedItems!.Clear();
                cargo.SelectedItems.Add(cargo.Items.Cast<Choice>().Single(c => c.Id == "manual-1-construction-7"));
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<NumericUpDown>(), n => n.Name == "CargoKg_manual_1_construction_7");
                Click(window, Find<Button>(window, "DispatchExpeditionButton"));
                var expedition = window.Session.World.Module<ScavengingModule>().Expeditions.Single();
                Assert.Equal(.4m, expedition.Plan.Manifest.Single().QuantityKg);
                AdvanceUntil(window, () => !expedition.Active);
                window.Navigate("crew");
                Click(window, Find<Button>(window, "StartSkillStudyButton"));
                window.Session.Advance(22);
                var skills = window.Session.World.Module<SkillsModule>();
                var job = skills.Jobs.Single(j => j.Active);
                Assert.True(skills.Find(3, "construction")!.StudyEffort > 0);
                Click(window, Find<Button>(window, "CancelLearning_" + job.Plan.Id));
                decimal partial = skills.Find(3, "construction")!.StudyEffort;
                Assert.Null(window.Session.World.Module<Haldane.CareerSim.Work.WorkforceModule>().ReservationFor(3));
                Click(window, Find<Button>(window, "StartSkillStudyButton"));
                Assert.True(skills.Find(3, "construction")!.StudyEffort >= partial);
                AdvanceUntil(window, () => skills.Jobs.All(j => !j.Active));
                Assert.Equal(6, skills.Find(3, "construction")!.Level); Assert.True(skills.Find(3, "construction")!.TheoryReady);
                Assert.True(Find<Button>(window, "StartSkillPracticeButton").IsEnabled);
                Capture("theory");
                Click(window, Find<Button>(window, "StartSkillPracticeButton"));
                AdvanceUntil(window, () => skills.Jobs.All(j => !j.Active));
                Assert.Equal(7, skills.Find(3, "construction")!.Level);
                Assert.Equal(70, window.Session.World.Module<Haldane.CareerSim.Work.WorkforceModule>().Find(3)!.Skills.Rating("construction"));
                Assert.True(window.Map.Bounds.Width > 400); Assert.True(window.Map.Bounds.Height >= 320);
                Capture("promoted"); PhysicalSave.ValidateWorld(window.Session.World);
                void Capture(string phase)
                {
                    Find<ComboBox>(window, "LearningMember").BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    if (Environment.GetEnvironmentVariable("SCAVENGER_UI_ARTIFACT_DIR") is { } directory)
                    { Directory.CreateDirectory(directory); frame!.Save(Path.Combine(directory, $"skills-{phase}-{width}x{height}.png"), PngBitmapEncoderOptions.Default); }
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    private static void SelectChoice(MainWindow window, string name, string id)
    {
        Dispatcher.UIThread.RunJobs();
        if (name == "ExpeditionLocation") Click(window, Find<Button>(window, "PlanStep0Button"));
        if (name is "TechnicianSelector" or "ComputerSelector") Find<Expander>(window, "ComputerSetup").IsExpanded = true;
        var selector = Find<ComboBox>(window, name);
        selector.SelectedItem = selector.Items.Cast<Choice>().Single(c => c.Id == id);
        Dispatcher.UIThread.RunJobs();
    }
    private static void AdvanceUntil(MainWindow window, Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) window.Session.Advance(20);
        Assert.True(done(), window.Session.Status + "\n" + string.Join("\n", window.Session.World.Module<ComputingModule>().Jobs.Select(j => $"{j.Plan.Kind}: {j.Status} {j.BlockedReason}")));
    }
    private static T Find<T>(MainWindow window, string name) where T : Control
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        return window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    }
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
