using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace Scavenger.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        if (args.Length is not (2 or 4) || args[0] != "--screenshot"
            || (args.Length == 4 && (args[2] != "--ticks" || !int.TryParse(args[3], out int n) || n is < 0 or > 100000)))
        { Console.Error.WriteLine("Usage: Scavenger.Desktop [--screenshot path.png [--ticks 0..100000]]"); return 2; }
        int ticks = args.Length == 4 ? int.Parse(args[3]) : 30;
        using var session = HeadlessUnitTestSession.StartNew(typeof(RenderAppBuilder));
        session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            if (ticks > 0) { window.Session.BeginDemo(); if (ticks > 1) window.Session.Advance(ticks - 1); }
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
            string path = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, PngBitmapEncoderOptions.Default);
            window.Close();
            Console.WriteLine($"Rendered tick {ticks} to {path}");
        }, CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
