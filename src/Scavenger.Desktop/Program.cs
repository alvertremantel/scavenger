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
        int ticks = 30, view = 0; bool walkthrough = false;
        if (args.Length < 2 || args[0] != "--screenshot") return Usage();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--seed" && i + 1 < args.Length && ulong.TryParse(args[++i], out seed)) continue;
            if (args[i] == "--layout" && i + 1 < args.Length)
            { motif = args[++i] switch { "compact" => SettlementMotif.Compact, "avenue" => SettlementMotif.Avenue, "twin-centers" => SettlementMotif.TwinCenters, _ => null }; if (motif is not null) continue; }
            if (args[i] == "--layer" && i + 1 < args.Length && Enum.TryParse(args[++i], true, out layer) && Enum.IsDefined(layer)) continue;
            if (args[i] == "--walkthrough") { walkthrough = true; continue; }
            if (args[i] == "--ticks" && i + 1 < args.Length && int.TryParse(args[++i], out ticks) && ticks is >= 0 and <= 100000) continue;
            if (args[i] == "--view" && i + 1 < args.Length)
            { view = Array.IndexOf(new[] { "production", "hardware", "archives", "compute", "energy", "scavenging", "region" }, args[++i]); if (view >= 0) continue; }
            return Usage();
        }
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        session.Dispatch(() =>
        {
            var window = new MainWindow(regional: !walkthrough);
            if (!walkthrough) { window.Session.NewRegion(seed, motif); window.Region.SetLayer(layer); }
            window.Show();
            if (walkthrough)
            {
                for (int i = 0; i < ticks; i++) { IndustrialWalkthrough.QueueNext(window.Session.Scheduler); window.Session.Scheduler.Advance(); }
                window.Session.Advance(0);
            }
            else if (ticks > 0) { window.Session.BeginDemo(); if (ticks > 1) window.Session.Advance(ticks - 1); }
            window.WorkspaceTabs.SelectedIndex = view == 6 ? 1 : 0;
            if (view != 6)
            {
                window.OperationsTabs.SelectedIndex = view == 5 ? 0 : 1;
                if (view != 5) window.Industry.Tabs.SelectedIndex = view;
            }
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
    { Console.Error.WriteLine("Usage: Scavenger.Desktop [--screenshot path.png [--ticks 0..100000] [--view production|hardware|archives|compute|energy|scavenging|region] [--seed N] [--layout compact|avenue|twin-centers] [--layer Terrain|Districts|Elevation|Moisture|FloodRisk] [--walkthrough]]"); return 2; }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
