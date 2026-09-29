using System.Globalization;
using System.Text.Json;
using Haldane.Kernel.Serialization;
using Scavenger.Sim;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
bool json = args.Contains("--json"), roundtrip = args.Contains("--roundtrip"), legacy = args.Contains("--legacy"), industry = args.Contains("--industry"), scavenging = args.Contains("--scavenging");
int ticks = scavenging ? 400 : industry ? 2400 : legacy ? 8 : 120;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--json" or "--roundtrip" or "--legacy" or "--industry" or "--scavenging") continue;
    if (args[i] == "--ticks" && i + 1 < args.Length && int.TryParse(args[++i], out ticks) && ticks is >= 1 and <= 100_000) continue;
    Console.Error.WriteLine("Usage: dotnet run --project src/Scavenger.Cli -- [--json] [--roundtrip] [--ticks N] [--legacy | --industry | --scavenging]");
    return 2;
}
if (new[] { legacy, industry, scavenging }.Count(v => v) > 1) { Console.Error.WriteLine("Choose one scenario."); return 2; }
var (_, scheduler, _) = scavenging ? ScavengingScenario.Create() : industry ? IndustrialScenario.Create() : legacy ? CompoundScenario.Create() : PhysicalCompoundScenario.Create();
if (!json) Console.WriteLine(legacy ? "Timed logistics experiment.\n" : "Physical compound: walking, door access, construction deliveries, and workstation utilities.\n");
for (int step = 0; step < ticks; step++)
{
    if (scavenging) ScavengingWalkthrough.QueueNext(scheduler);
    else if (industry) IndustrialWalkthrough.QueueNext(scheduler);
    else if (legacy) CompoundScenario.QueueDemoCommands(scheduler);
    else PhysicalCompoundScenario.QueueDemoCommands(scheduler);
    var events = scheduler.Advance();
    if (!json)
        foreach (var gameEvent in events)
            Console.WriteLine($"[{gameEvent.Tick,2}] {gameEvent.Description}");
    if (roundtrip && step == (legacy ? 1 : 10))
    {
        string saved = SaveManager.Save(scheduler, "Compound in progress");
        scheduler = legacy ? CompoundScenario.Load(saved) : PhysicalSave.Load(saved);
        if (!json) Console.WriteLine("     Saved and reloaded the running compound.");
    }
}
var report = CompoundReport.Capture(scheduler.World);
if (json) Console.WriteLine(scavenging ? JsonSerializer.Serialize(new { Physical = PhysicalReport.Capture(scheduler.World), Industry = IndustryReport.Capture(scheduler.World), Region = scheduler.World.Module<ScavengingModule>().ToSnapshot() }, SaveManager.Options) : industry ? JsonSerializer.Serialize(new { Physical = PhysicalReport.Capture(scheduler.World), Industry = IndustryReport.Capture(scheduler.World) }, SaveManager.Options) : legacy ? JsonSerializer.Serialize(report, SaveManager.Options)
    : JsonSerializer.Serialize(PhysicalReport.Capture(scheduler.World), SaveManager.Options));
else
{
    Console.WriteLine($"\nTick {report.Tick}: {report.StoredKg} kg in stores; {report.InTransitKg} kg in transit.");
    Console.WriteLine($"{"Store",-20} {"Stored kg",10} {"Incoming kg",12} {"Free kg",10}");
    foreach (var store in report.Stores)
        Console.WriteLine($"{store.Name,-20} {store.StoredKg,10} {store.IncomingKg,12} {store.FreeKg,10}");
    foreach (var haul in report.Hauls)
        Console.WriteLine(legacy ? $"{haul.Id}: {haul.Status}; {haul.ElapsedTicks}/{haul.TravelTicks} travel ticks."
            : $"{haul.Id}: {haul.Status}; {haul.ElapsedTicks} walking steps (route estimate {haul.TravelTicks}).");
    if (!legacy)
    {
        var physical = PhysicalReport.Capture(scheduler.World);
        Console.WriteLine($"Total tracked mass, including built structures: {physical.TotalTrackedKg} kg.");
        foreach (var job in physical.Construction) Console.WriteLine($"{job.Id}: {job.Status}; {job.CompletedEffort}/{job.RequiredEffort} effort.");
        foreach (var worker in physical.Workers) Console.WriteLine($"{worker.Name}: ({worker.Position.X},{worker.Position.Y}); {worker.Assignment ?? "idle"}.");
    }
}
if ((industry || scavenging) && !json)
{
    var industrial = IndustryReport.Capture(scheduler.World);
    foreach (var machine in industrial.Machines) Console.WriteLine($"{machine.Id}: {machine.Components.Count} parts, RAM {machine.RamGiB} GiB, VRAM {machine.VramGiB} GiB, disk {machine.UsedStorageGiB:0.000}/{machine.StorageGiB} GiB.");
    foreach (var job in industrial.Computing.Jobs) Console.WriteLine($"{job.Plan.Kind}: {job.Status}; {job.Progress:0.##} effort. {job.BlockedReason}");
    Console.WriteLine($"Generated {industrial.Energy.GeneratedKwh:0.000} kWh; used {industrial.Energy.UsedKwh:0.000} kWh; losses {industrial.Energy.LossKwh:0.000} kWh. Fuel available {industrial.FuelAvailableKg:0.00} kg.");
}
if (scavenging && !json)
{
    var region = scheduler.World.Module<ScavengingModule>();
    foreach (var e in region.Expeditions) Console.WriteLine($"{e.Plan.Id}: {e.Status}, recovered {e.Receipts.Sum(c => c.QuantityKg)} kg. {e.Reason}");
    foreach (var h in region.Health) Console.WriteLine($"Worker {h.WorkerId}: {(h.Sick ? "ill" : "well")}, exposure {h.Exposure:0.000}.");
    Console.WriteLine($"Regional stock {region.RegionalKg:0.###} kg; rival removals {region.RivalRemovedKg:0.###} kg; spent supplies {region.SpentSuppliesKg:0.###} kg.");
}
return 0;
