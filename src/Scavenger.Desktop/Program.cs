using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Region;

namespace Scavenger.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        ulong seed = GeneratedScenario.DefaultSeed; SettlementMotif? motif = null; MapLayer layer = MapLayer.Terrain;
        int ticks = 0, width = 1440, height = 1000; string view = "home"; bool walkthrough = false;
        if (args.Length < 2 || args[0] != "--screenshot") return Usage();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--seed" && i + 1 < args.Length && ulong.TryParse(args[++i], out seed)) continue;
            if (args[i] == "--layout" && i + 1 < args.Length)
            { motif = args[++i] switch { "compact" => SettlementMotif.Compact, "avenue" => SettlementMotif.Avenue, "twin-centers" => SettlementMotif.TwinCenters, _ => null }; if (motif is not null) continue; }
            if (args[i] == "--layer" && i + 1 < args.Length && Enum.TryParse(args[++i], true, out layer) && Enum.IsDefined(layer)) continue;
            if (args[i] == "--walkthrough") { walkthrough = true; continue; }
            if (args[i] == "--ticks" && i + 1 < args.Length && int.TryParse(args[++i], out ticks) && ticks is >= 0 and <= 100000) continue;
            if (args[i] == "--width" && i + 1 < args.Length && int.TryParse(args[++i], out width) && width is >= 1050 and <= 2560) continue;
            if (args[i] == "--height" && i + 1 < args.Length && int.TryParse(args[++i], out height) && height is >= 780 and <= 1600) continue;
            if (args[i] == "--view" && i + 1 < args.Length)
            { view = args[++i]; if (new[] { "home", "compound", "production", "hardware", "archives", "compute", "energy", "transfers", "recipes", "scavenging", "clinic", "yields", "region", "region-setup", "materials", "stores", "workers", "jobs", "services", "events" }.Contains(view)) continue; }
            return Usage();
        }
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        session.Dispatch(() =>
        {
            var window = new MainWindow(regional: !walkthrough) { Width = width, Height = height };
            if (!walkthrough) { window.Session.NewRegion(seed, motif); window.Region.SetLayer(layer); }
            window.Show();
            if (walkthrough)
            {
                for (int i = 0; i < ticks; i++) { IndustrialWalkthrough.QueueNext(window.Session.Scheduler); window.Session.Scheduler.Advance(); }
                window.Session.Advance(0);
            }
            else if (ticks > 0) { window.Session.BeginDemo(); if (ticks > 1) window.Session.Advance(ticks - 1); }
            int industry = Array.IndexOf(new[] { "production", "hardware", "archives", "compute", "energy", "transfers", "recipes" }, view);
            int register = Array.IndexOf(new[] { "materials", "stores", "workers", "jobs", "services", "events" }, view);
            window.Navigate(industry >= 0 ? $"industry/{industry}" : register >= 0 ? $"register/{register}" : view == "yields" ? "scavenging" : view == "region-setup" ? "region" : view);
            if (view == "yields") window.Scavenging.ShowCargoDetails();
            if (view == "region-setup") window.Region.Tabs.SelectedIndex = 1;
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
            string path = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, PngBitmapEncoderOptions.Default);
            window.Close();
            Console.WriteLine($"Rendered tick {ticks} to {path}");
        }, CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }
    private static int Usage()
    { Console.Error.WriteLine("Usage: Scavenger.Desktop [--screenshot path.png [--ticks 0..100000] [--view home|compound|production|hardware|archives|compute|energy|transfers|recipes|scavenging|clinic|yields|region|region-setup|materials|stores|workers|jobs|services|events] [--seed N] [--layout compact|avenue|twin-centers] [--layer Terrain|Districts|Elevation|Moisture|FloodRisk] [--width 1050..2560] [--height 780..1600] [--walkthrough]]"); return 2; }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
