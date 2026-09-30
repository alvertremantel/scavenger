using System.Globalization;
using System.Text;
using System.Text.Json;
using Haldane.CareerSim.Work;
using Haldane.Kernel.Simulation;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Scavenging;
using Scavenger.Sim.Region;
using Scavenger.Sim.Skills;

namespace Scavenger.Cli;

/// <summary>Text interaction head. All actions use the same session and commands as Desktop.</summary>
public sealed class GameConsole(GameSession session, TextWriter output)
{
    public GameSession Session { get; } = session;
    public bool Quit { get; private set; }
    public string? LastActivityId { get; private set; }
    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        string? script = null, load = null; ulong seed = GeneratedScenario.DefaultSeed;
        bool generated = true, regional = true;
        try
        {
            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--play": break;
                    case "--script": script = args[++i]; break;
                    case "--load": load = args[++i]; break;
                    case "--seed": seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--authored": generated = false; break;
                    case "--industry": generated = regional = false; break;
                    default: throw new ArgumentException($"Unknown play option {args[i]}.");
                }
            var game = new GameConsole(new GameSession(regional, generated, seed), output);
            if (load is not null) game.Session.LoadJson(File.ReadAllText(load));
            using var reader = script is null ? null : File.OpenText(script);
            var source = reader ?? input;
            output.WriteLine("Scavenger headless game. Type help. Reads are free; accepted actions advance one tick.");
            game.Execute("status");
            bool failed = false;
            while (!game.Quit)
            {
                if (script is null && !Console.IsInputRedirected) output.Write($"[{game.Session.World.Clock.Tick}]> ");
                string? line = source.ReadLine(); if (line is null) break;
                if (script is not null || Console.IsInputRedirected) output.WriteLine("> " + line);
                if (!game.Execute(line)) { failed = true; if (script is not null) break; }
            }
            return failed ? 1 : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or FormatException or OverflowException or IOException or InvalidOperationException or JsonException or NotSupportedException)
        { error.WriteLine(ex.Message); error.WriteLine("Usage: --play [--seed N] [--authored | --industry] [--load path] [--script path]"); return 2; }
    }
    public bool Execute(string line)
    {
        try
        {
            var a = Tokenize(line); if (a.Count == 0) return true;
            string Arg(int n) => a.Count > n ? a[n] : throw new ArgumentException("Missing argument; type help.");
            int Int(int n) => int.Parse(Arg(n), CultureInfo.InvariantCulture);
            int? Operator(int n) => Arg(n) is "auto" or "-" ? null : Int(n);
            decimal Kg(int n) => decimal.Parse(Arg(n), CultureInfo.InvariantCulture);
            string id = Session.NextId(a[0].ToLowerInvariant());
            var world = Session.World; var inventory = world.Module<InventoryModule>();
            bool Submit(ICommand command) { var result = Session.Submit(command); output.WriteLine((result.Accepted ? "OK " : "REJECTED ") + result.Message);
                if (result.Accepted && ActivityReport.Capture(Session.World).Any(j => j.Id == id)) LastActivityId = id;
                return result.Accepted; }
            switch (a[0].ToLowerInvariant())
            {
                case "quit": case "exit": Quit = true; return true;
                case "help": output.WriteLine(Help); return true;
                case "status":
                    output.WriteLine($"Tick {world.Clock.Tick}, day {world.Clock.Day + 1}; {Session.Report.TotalTrackedKg:0.###} kg tracked. {Session.PendingDecision}");
                    foreach (var w in Session.Report.Workers) output.WriteLine($"{w.Id} {w.Name}: {w.Assignment ?? "idle"}; {w.WorkReason}");
                    if (world.Has<EnergyModule>()) { var p = world.Module<EnergyModule>().LastFrame; output.WriteLine($"Tank {inventory.Batches.Where(b => b.StockpileId == "generator-tank").Sum(b => b.QuantityKg):0.###} kg; power {p?.ServedKw}/{p?.RequestedKw} kW"); }
                    return true;
                case "skills":
                    foreach (var member in world.Module<WorkforceModule>().Staff.Where(w => a.Count < 2 || w.Id == Int(1)))
                    {
                        output.WriteLine($"{member.Id} {member.Name}");
                        foreach (var skill in world.Module<SkillsModule>().Members.Where(s => s.WorkerId == member.Id && (a.Count < 3 || s.SkillId == Arg(2))))
                            output.WriteLine($"  {skill.SkillId}: L{skill.Level} rating {skill.Rating}; theory L{skill.KnowledgeLevel}; {skill.Gate}; lifetime earned XP {skill.LifetimeExperience:0.##}");
                    } return true;
                case "books":
                    foreach (var b in inventory.Batches.Where(b => SkillCatalogue.IsBook(b.MaterialId) && (a.Count < 2 || b.MaterialId.Contains(Arg(1), StringComparison.OrdinalIgnoreCase))))
                        output.WriteLine($"{b.Id}: {SkillCatalogue.Book(b.MaterialId)!.Name} | {b.StockpileId} | {(inventory.AvailableKg(b.Id) == SkillBook.MassKg ? "available" : "reserved")}"); return true;
                case "study":
                    string skillId = Arg(2); var progress = world.Module<SkillsModule>().Find(Int(1), skillId) ?? throw new ArgumentException("Unknown member/skill.");
                    string? bookId = a.Count > 3 ? Arg(3) : inventory.Batches.FirstOrDefault(b => SkillCatalogue.Book(b.MaterialId) is { } book && book.SkillId == skillId && book.Level == progress.NextLevel && inventory.AvailableKg(b.Id) == SkillBook.MassKg)?.Id;
                    return Submit(new StartLearningCommand(new(id, Int(1), skillId, LearningKind.Study, bookId)));
                case "practice": return Submit(new StartLearningCommand(new(id, Int(1), Arg(2), LearningKind.Practice, MentorId: a.Count > 3 ? Int(3) : null)));
                case "learning": foreach (var job in world.Module<SkillsModule>().Jobs) output.WriteLine($"{job.Plan.Id}: member {job.Plan.WorkerId} {job.Plan.Kind} {job.Plan.SkillId} L{job.TargetLevel} {job.Status}; effort {job.Effort:0.##}; {job.BlockedReason}"); return true;
                case "recover":
                    var suggestion = OperationsGuide.Suggest(world, Arg(1), id); output.WriteLine(suggestion.Message);
                    return suggestion.Plan is not null && Submit(new DispatchExpeditionCommand(suggestion.Plan));
                case "crew": foreach (var w in Session.Report.Workers) output.WriteLine($"{w.Id} {w.Name}: {w.Assignment ?? "idle"}; {string.Join(", ", w.Skills.Select(s => $"{s.Key} {s.Value}"))}"); return true;
                case "advance": case "wait":
                    int ticks = a.Count > 1 ? Int(1) : 1;
                    if (ticks is < 0 or > 100000) throw new ArgumentException("Ticks must be 0–100000.");
                    var result = Session.Advance(ticks, a.Count < 3 || Arg(2) != "all");
                    foreach (var e in result.Events) output.WriteLine($"[{e.Tick}] {e.Description}");
                    output.WriteLine($"{Session.Status} ({result.TicksAdvanced} ticks)"); return true;
                case "until":
                    string taskId = Arg(1) == "last" ? LastActivityId ?? throw new ArgumentException("No task started in this console; use jobs to select an id.") : Arg(1); int budget = a.Count > 2 ? Int(2) : 2000;
                    if (budget is < 1 or > 100000) throw new ArgumentException("Limit must be 1–100000 ticks.");
                    if (!ActivityReport.Capture(world).Any(j => j.Id == taskId)) throw new ArgumentException("Unknown task id; use jobs.");
                    for (int n = 0; n < budget && ActivityReport.Capture(world).Any(j => j.Id == taskId && j.Active); n++)
                    {
                        if (Session.PendingDecision is { } pending) { output.WriteLine(pending); break; }
                        var frame = Session.Advance(1, true);
                        foreach (var e in frame.Events) output.WriteLine($"[{e.Tick}] {e.Description}");
                        if (Session.LastAdvanceNeedsAttention) { output.WriteLine(Session.Status); break; }
                    }
                    var task = ActivityReport.Capture(world).First(j => j.Id == taskId);
                    output.WriteLine($"Tick {world.Clock.Tick}: {task.Id} {task.Status}; {task.Reason}"); return true;
                case "stock":
                    foreach (var b in inventory.Batches.Where(b => a.Count < 2 || $"{b.Id} {b.MaterialId} {b.StockpileId}".Contains(Arg(1), StringComparison.OrdinalIgnoreCase)))
                        output.WriteLine($"{b.Id} | {b.MaterialId} | {b.StockpileId} | {inventory.AvailableKg(b.Id):0.###}/{b.QuantityKg} kg | {b.Quality.Grade}"); return true;
                case "stores": foreach (var s in inventory.Stockpiles) output.WriteLine($"{s.Id}: {inventory.StoredKg(s.Id)}/{s.CapacityKg} kg; free {inventory.FreeCapacityKg(s.Id)} kg"); return true;
                case "sites": case "find":
                    foreach (var l in world.Module<ScavengingModule>().Locations)
                    {
                        string search = a.Count > 1 ? Arg(1) : "";
                        var stock = l.Definition.Salvage.Where(s => l.Remaining(s.Id) > 0 && $"{l.Definition.Id} {l.Definition.Name} {s.Object} {s.MaterialId}".Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (stock.Length == 0) continue;
                        output.WriteLine($"{l.Definition.Id}: {l.Definition.Name} | travel {l.Definition.TravelTicks} ticks | integrity {l.Integrity:P0}");
                        if (a[0].Equals("find", StringComparison.OrdinalIgnoreCase)) foreach (var s in stock) output.WriteLine($"  {s.Id} | {s.Object} | {l.Remaining(s.Id):0.###} kg | {s.Qualification.SkillId} {s.Qualification.MinimumRating}");
                    } return true;
                case "site":
                    var site = world.Module<ScavengingModule>().Location(Arg(1)) ?? throw new ArgumentException("Unknown site.");
                    output.WriteLine($"{site.Definition.Name}: {site.Definition.Description}\nTravel {site.Definition.TravelTicks}; integrity {site.Integrity:P0}; rivals {site.Definition.RivalPressure:P0}");
                    foreach (var s in site.Definition.Salvage) output.WriteLine($"{s.Id}: {s.Object} | {s.MaterialId} | {site.Remaining(s.Id):0.###} kg | {s.Qualification.SkillId} {s.Qualification.MinimumRating} | minimum integrity {s.MinimumIntegrity:P0}{(s.ComponentId is not null ? " | whole unit" : "")}"); return true;
                case "expedition":
                    var manifest = Arg(3).Split(',').Select(item => { var p = item.Split('='); if (p.Length != 2) throw new ArgumentException("Use salvage-id=kg,..."); return new SalvageRequest(p[0], decimal.Parse(p[1], CultureInfo.InvariantCulture)); }).ToArray();
                    return Submit(new DispatchExpeditionCommand(new(id, Arg(1), Arg(2).Split(',').Select(int.Parse).ToArray(), manifest,
                        a.Count > 6 ? Arg(6) : "salvage", a.Count > 4 ? Enum.Parse<ExpeditionApproach>(Arg(4), true) : ExpeditionApproach.Careful, a.Count < 6 || bool.Parse(Arg(5)))));
                case "teams": foreach (var e in world.Module<ScavengingModule>().Expeditions) output.WriteLine($"{e.Plan.Id}: {e.Status} {e.ElapsedTravel} travel; cargo {e.Cargo.Sum(c => c.QuantityKg)} kg; {e.Reason}"); return true;
                case "respond": return Submit(new RespondToExpeditionCommand(Arg(1), Enum.Parse<ExpeditionResponse>(Arg(2), true)));
                case "recall": return Submit(new RecallExpeditionCommand(Arg(1)));
                case "recipes": foreach (var r in world.Module<ProductionModule>().Recipes) output.WriteLine($"{r.Id}: {r.Name} | {r.Qualification.SkillId} {r.Qualification.MinimumRating} | {string.Join(", ", r.Inputs.Select(i => $"{i.QuantityKg} kg {i.MaterialId}"))} | {r.Effort} effort | stations {string.Join(',', world.Module<CompoundModule>().Buildings.Where(b => world.Module<CompoundModule>().Definition(b.DefinitionId)!.Capabilities.Contains(r.Capability)).Select(b => b.Id))}"); return true;
                case "produce": return Submit(new QueueProductionCommand(new(id, Arg(1), Arg(2), Operator(3), a.Count > 4 ? Int(4) : 2, a.Count > 6 ? Arg(6).Split(',') : ["salvage", "clinic"], a.Count > 5 ? Arg(5) : "salvage", "waste", Automatic: Arg(3) == "auto")));
                case "maintain": return Submit(new AddStockOrderCommand(new(id,
                    new ProductionPlan("template-" + id, Arg(1), Arg(2), Operator(3), a.Count > 6 ? Int(6) : 2, ["salvage", "clinic"], "salvage", "waste", Automatic: Arg(3) == "auto"), Arg(4), Kg(5))));
                case "recovery": Session.BeginDemo(); output.WriteLine(Session.Status); return true;
                case "orders": foreach (var o in world.Module<OrdersModule>().Orders) output.WriteLine($"{o.Plan.Id}: {(o.Enabled ? "on" : "paused")} {o.Plan.TargetKg} kg {o.Plan.MaterialId}; {o.BlockedReason}"); return true;
                case "order": return Submit(new SetStockOrderEnabledCommand(Arg(1), bool.Parse(Arg(2))));
                case "treat": return Submit(new QueueTreatmentCommand(new(id, Int(1), a.Count > 2 ? Int(2) : 4)));
                case "transfer": return Submit(new StoreTransferCommand(new(id, Arg(1), Arg(2), Int(4), Kg(3))));
                case "refuel":
                    var fuel = inventory.Batches.FirstOrDefault(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage" && inventory.AvailableKg(b.Id) > 0) ?? throw new ArgumentException("No available depot fuel.");
                    return Submit(new StoreTransferCommand(new(id, fuel.Id, "generator-tank", a.Count > 2 ? Int(2) : 2, Math.Min(a.Count > 1 ? Kg(1) : 6, inventory.AvailableKg(fuel.Id)))));
                case "buildings": foreach (var b in Session.Report.Buildings) output.WriteLine($"{b.Id}: {b.Name} ({b.Footprint.X},{b.Footprint.Y}) {b.Condition:P0} {b.BlockedReason}"); return true;
                case "plans": foreach (var d in world.Module<CompoundModule>().Definitions.Where(d => d.Buildable)) output.WriteLine($"{d.Id}: {d.Name} | {d.RequiredSkill} | {string.Join(", ", d.Materials.Select(m => $"{m.QuantityKg} kg {m.MaterialId}"))}"); return true;
                case "build": return Submit(new PlanBuildCommand(new(id, Arg(1), new(Int(2), Int(3)), a.Count > 4 ? Int(4) : 3, a.Count > 5 ? Int(5) : 2, "salvage")));
                case "repair": var repair = Session.Repair(Arg(1)); output.WriteLine(repair.Message); if (repair.Accepted) LastActivityId = id; return repair.Accepted;
                case "door": return Submit(new SetDoorOpenCommand(new(Int(1), Int(2)), bool.Parse(Arg(3))));
                case "assign": return Submit(new AssignStaffCommand(Int(1), Arg(2)));
                case "release": return Submit(new UnassignStaffCommand(Int(1)));
                case "power": return Submit(new SetComputerPoweredCommand(Arg(1), bool.Parse(Arg(2))));
                case "generator": return Submit(new SetGeneratorEnabledCommand(Arg(1), bool.Parse(Arg(2))));
                case "battery": return Submit(new SetBatteryEnabledCommand(Arg(1), bool.Parse(Arg(2))));
                case "conduit": return Submit(new SetConduitEnabledCommand(Enum.Parse<UtilityKind>(Arg(1), true), new(Int(2), Int(3)), bool.Parse(Arg(4))));
                case "room": return Submit(new DesignateRoomCommand(new(Arg(1), Arg(2), Arg(3), new(Int(4), Int(5), Int(6), Int(7)))));
                case "energy": output.WriteLine(JsonSerializer.Serialize(IndustryReport.Capture(world).Energy, SaveManager.Options)); return true;
                case "services": foreach (var service in Session.Report.Utilities) output.WriteLine($"{service.BuildingId}: {(service.Operational ? "operational" : service.BlockedReason)} | power {service.PowerAllocatedKw}/{service.PowerRequiredKw} kW | water {service.WaterAllocatedLph}/{service.WaterRequiredLph} L/h"); return true;
                case "projects": foreach (var project in Session.Report.Projects) output.WriteLine($"{project.Id}: {project.CompletedEffort:0.##}/{project.RequiredEffort} | {project.StationId} | workers {string.Join(',', project.WorkerIds)} | {project.BlockedReason}"); return true;
                case "computers": output.WriteLine(JsonSerializer.Serialize(IndustryReport.Capture(world).Machines, SaveManager.Options)); return true;
                case "assets": foreach (var asset in world.Module<ComputingModule>().Assets) output.WriteLine($"{asset.Id}: {asset.Name}; integrity {asset.Integrity:P0}; {asset.Knowledge}"); return true;
                case "components": foreach (var c in world.Module<ComputingModule>().Components) output.WriteLine($"{c.Id}: {c.DefinitionId} {c.Condition:P0}; {(c.Diagnosed ? c.Fault.ToString() : "unexamined")} installed {c.InstalledOwner} claimed {c.ClaimedBy}"); return true;
                case "computer": return Submit(new QueueComputerTaskCommand(new(id, Enum.Parse<ComputerTaskKind>(Arg(1), true), Arg(2), Operator(3), a.Count > 6 ? Int(6) : 2,
                    a.Count > 4 && Arg(4) != "-" ? Arg(4).Split(',') : null, a.Count > 5 && Arg(5) != "-" ? Arg(5) : null, a.Count > 7 ? Arg(7) : null)));
                case "cancel": return Arg(1) switch
                    { "build" => Submit(new CancelBuildCommand(Arg(2))), "produce" => Submit(new CancelProductionCommand(Arg(2))), "haul" => Submit(new CancelHaulCommand(Arg(2))), "computer" => Submit(new CancelComputerTaskCommand(Arg(2))), "treat" => Submit(new CancelTreatmentCommand(Arg(2))), "learning" => Submit(new CancelLearningCommand(Arg(2))), _ => throw new ArgumentException("Unknown task kind.") };
                case "jobs":
                    foreach (var j in ActivityReport.Capture(world)) output.WriteLine($"{j.Kind} {j.Id}: {j.Status} {j.Effort:0.##}/{j.Required:0.##}; {j.Reason}"); return true;
                case "events": foreach (var e in world.Events.Entries.TakeLast(a.Count > 1 ? Int(1) : 30)) output.WriteLine($"[{e.Tick}] {e.Description}"); return true;
                case "save": WriteFile(Arg(1), Session.SaveJson()); output.WriteLine("Saved " + Arg(1)); return true;
                case "load": Session.LoadJson(File.ReadAllText(Arg(1))); output.WriteLine(Session.Status); return true;
                case "report": var json = JsonSerializer.Serialize(new { Physical = Session.Report, Industry = IndustryReport.Capture(world), Skills = world.Module<SkillsModule>().ToSnapshot(), Region = world.TryModule<ScavengingModule>(out var region) ? region.ToSnapshot() : null }, SaveManager.Options); if (a.Count > 1) WriteFile(Arg(1), json); else output.WriteLine(json); return true;
                case "map": WriteFile(Arg(1), RegionMapExport.Svg(world.Module<RegionModule>(), a.Count > 2 ? Enum.Parse<MapLayer>(Arg(2), true) : MapLayer.Terrain)); return true;
                default: throw new ArgumentException($"Unknown command '{a[0]}'; type help.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException or InvalidOperationException or JsonException or NotSupportedException)
        { output.WriteLine("ERROR " + ex.Message); return false; }
    }
    private static void WriteFile(string path, string text) { string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text); }
    public static IReadOnlyList<string> Tokenize(string line)
    {
        var words = new List<string>(); var current = new StringBuilder(); char quote = '\0'; bool started = false;
        foreach (char c in line)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; else current.Append(c); started = true; }
            else if (c is '\'' or '"') { quote = c; started = true; }
            else if (c == '#' && !started) break;
            else if (char.IsWhiteSpace(c)) { if (started) { words.Add(current.ToString()); current.Clear(); started = false; } }
            else { current.Append(c); started = true; }
        }
        if (quote != '\0') throw new ArgumentException("Unclosed quoted argument.");
        if (started) words.Add(current.ToString()); return words;
    }
    public const string Help = """
        Read: status | crew | stock [filter] | stores | sites [filter] | find <text> | site <id>
              teams | recipes | plans | buildings | projects | jobs | orders | energy | services | computers | components | assets | events [N]
        Learning: skills [worker] [skill] | books [skill] | learning
                  study <worker> <skill> [book-batch] | practice <worker> <skill> [mentor]
                  find "book/<skill>/<level>" (known regional volumes) | recover pharmacy|sorbent|fuel|materials
        Time: until <task-id|last> [max-ticks] | advance [ticks] [all] (default stops at decisions/completions/warnings)
        Recover: expedition <site> <worker,worker> <salvage=kg,salvage=kg> [Careful|Quick] [protection:true|false] [store]
                 respond <expedition-id> Wait|Retreat | recall <id>
        Compound: produce <recipe> <station> <operator|auto> [carrier] [product-store] [source,source] | recovery | order <id> true|false
                  maintain <recipe> <station> <operator|auto> <material> <target-kg> [carrier]
          transfer <batch> <store> <kg> <carrier> | refuel [kg] [carrier] | treat <patient> [medic]
                  build <plan> <x> <y> [builder] [carrier] | repair <fixture> | door <x> <y> true|false
                  assign <worker> <project> | release <worker>
          generator <id> true|false | battery <id> true|false | conduit Power|Water|Data <x> <y> true|false
          room <id> "name" "purpose" <x> <y> <width> <height>
        Computing: power <machine> true|false | computer <TaskKind> <machine> <technician|-> [component,component|-] [asset|-] [carrier] [target]
        Cancel: cancel build|produce|haul|computer|treat|learning <id>
        Files: save <path> | load <path> | report [path] | map <path.svg> [layer]
        quit | help. Quote arguments with spaces. # starts a comment. No time passes on rejected actions.
        """;
}
