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
            var window = new MainWindow(); window.Show();
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
            var window = new MainWindow { SavePath = path }; window.Show();
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
                Assert.Equal(200, window.Session.Report.TotalTrackedKg);
                Assert.Single(window.Session.Report.Construction);
                File.WriteAllText(path, "invalid");
                window.LoadSlot();
                Assert.Equal(1, window.Session.Report.Tick);
                window.Session.Advance(100);
                Assert.Equal(ConstructionStatus.Completed, window.Session.Report.Construction[0].Status);
                Assert.NotEmpty(saved);
            }
            finally { window.Close(); File.Delete(path); File.Delete(path + ".tmp"); }
        }, CancellationToken.None);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control
        => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(MainWindow window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
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
