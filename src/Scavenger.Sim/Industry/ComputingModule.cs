using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

/// <summary>Physical, individually owned components and simulated offline computer work. Never executes recovered software.</summary>
public sealed class ComputingModule : IPersistentModule
{
    public const string Persist = "scavenger.computing";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, ComponentDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComputerComponent> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompoundComputer> _computers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DigitalAsset> _assets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComputerJob> _jobs = new(StringComparer.Ordinal);
    private readonly List<AssetCopy> _copies = [];
    private readonly HashSet<LearnedKnowledge> _knowledge = [];
    private readonly List<TelemetryLink> _links = [];
    private readonly List<TelemetrySample> _samples = [];
    private readonly Dictionary<string, ModelAdapter> _adapters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _alerts = new(StringComparer.Ordinal);
    private long _nextSample = 1;
    public IReadOnlyList<ComponentDefinition> Definitions => _definitions.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<ComputerComponent> Components => _components.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<CompoundComputer> Computers => _computers.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<DigitalAsset> Assets => _assets.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<ComputerJob> Jobs => _jobs.Values.OrderBy(j => j.Plan.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<AssetCopy> Copies => _copies.OrderBy(c => c.StorageId, StringComparer.Ordinal).ThenBy(c => c.AssetId, StringComparer.Ordinal).ToList();
    public IReadOnlyList<LearnedKnowledge> Knowledge => _knowledge.OrderBy(k => k.WorkerId).ThenBy(k => k.Capability, StringComparer.Ordinal).ToList();
    public IReadOnlyList<TelemetryLink> Links => _links.OrderBy(l => l.ComputerId, StringComparer.Ordinal).ThenBy(l => l.BuildingId, StringComparer.Ordinal).ToList();
    public IReadOnlyList<TelemetrySample> Samples => _samples.AsReadOnly();
    public IReadOnlyList<ModelAdapter> Adapters => _adapters.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    public ComputerComponent? Component(string id) => _components.GetValueOrDefault(id);
    public ComponentDefinition? Definition(string id) => _definitions.GetValueOrDefault(id);
    public CompoundComputer? Computer(string id) => _computers.GetValueOrDefault(id);
    public DigitalAsset? Asset(string id) => _assets.GetValueOrDefault(id);
    public ComputerJob? Job(string id) => _jobs.GetValueOrDefault(id);
    public ModelAdapter? Adapter(string id) => _adapters.GetValueOrDefault(id);
    public decimal InstalledMassKg => Components.Where(c => c.InstalledOwner is not null).Sum(c => c.MassKg);
    public void AddDefinition(ComponentDefinition definition) => _definitions.Add(definition.Id, definition);
    public void AddComputer(string id, string buildingId)
    { ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(buildingId); _computers.Add(id, new CompoundComputer(id, buildingId)); }
    public void AddComponent(World world, string id, string definitionId, string storeId, decimal condition = .9m, ComponentFault fault = ComponentFault.None)
    {
        var definition = Definition(definitionId) ?? throw new ArgumentException("Unknown component model.", nameof(definitionId));
        if (condition is < 0 or > 1 || !Enum.IsDefined(fault) || _components.ContainsKey(id)) throw new ArgumentException("Invalid component unit.");
        var component = new ComputerComponent(id, definitionId, definition.MassKg, condition, fault);
        var inventory = world.Module<InventoryModule>(); inventory.AddMaterial(new MaterialDefinition(component.MaterialId, $"{definition.Name} [{id}]"));
        inventory.AddBatch(new MaterialBatch($"component/{id}", component.MaterialId, storeId, component.MassKg, new BatchQuality(1, "unexamined component")));
        _components.Add(id, component);
    }
    public void AddAsset(DigitalAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Id) || string.IsNullOrWhiteSpace(asset.Name) || asset.SizeGiB <= 0 || asset.Integrity is < 0 or > 1
            || asset.Capabilities.Count == 0 || asset.Capabilities.Concat(asset.Dependencies).Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid digital asset.", nameof(asset));
        _assets.Add(asset.Id, asset with { Capabilities = Array.AsReadOnly(asset.Capabilities.ToArray()), Dependencies = Array.AsReadOnly(asset.Dependencies.ToArray()) });
    }
    public static ComputerProfile Profile(ComputerTaskKind kind) => kind switch
    {
        ComputerTaskKind.DiagnoseComponents => new(4, 0, 0, 0, 0),
        ComputerTaskKind.RepairComponent => new(4, 0, 0, 0, 0),
        ComputerTaskKind.Assemble => new(8, 0, 0, 0, 0),
        ComputerTaskKind.RemoveComponent => new(4, 0, 0, 0, 0),
        ComputerTaskKind.ServiceComponent => new(4, 0, 0, 0, 0),
        ComputerTaskKind.InstallAsset => new(4, 1, 0, .5m, .06m, Machine: true),
        ComputerTaskKind.Study => new(6, .5m, 0, .2m, .03m, Machine: true),
        ComputerTaskKind.InstallTelemetry => new(6, 0, 0, 0, 0),
        ComputerTaskKind.LabelTelemetry => new(4, 1, 0, .5m, .04m, Machine: true),
        ComputerTaskKind.TrainAdapter => new(48, 16, 10, 1, .20m, .25m, true, HandsOn: false, Knowledge: "adapter-training"),
        ComputerTaskKind.ValidateAdapter => new(12, 12, 6, 1, .17m, Machine: true, HandsOn: false, Knowledge: "adapter-training"),
        ComputerTaskKind.RuleMonitor => new(1, .5m, 0, .2m, .02m, Machine: true, Continuous: true, HandsOn: false),
        ComputerTaskKind.ModelMonitor => new(1, 8, 4, .5m, .12m, Machine: true, Continuous: true, HandsOn: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public string? Explain(World world, ComputerTaskPlan plan)
    {
        if (!Enum.IsDefined(plan.Kind) || string.IsNullOrWhiteSpace(plan.Id) || Job(plan.Id) is not null || Computer(plan.ComputerId) is null) return "Choose a unique computer task and an existing computer.";
        var profile = Profile(plan.Kind);
        if (!profile.Continuous && (plan.TechnicianId is not { } id || world.Module<WorkforceModule>().Find(id) is not { } worker
            || !worker.Skills.Meets(new(profile.Machine ? "computing" : "electronics", 40))))
            return "This task needs a qualified electronics/computing technician at rating 40.";
        var components = plan.Components ?? [];
        if (components.Distinct(StringComparer.Ordinal).Count() != components.Count || components.Any(c => Component(c) is null)) return "Choose distinct existing component units.";
        if (plan.Kind is ComputerTaskKind.DiagnoseComponents or ComputerTaskKind.Assemble && components.Count == 0) return "Select component units for this task.";
        if (plan.Kind is ComputerTaskKind.RepairComponent or ComputerTaskKind.RemoveComponent or ComputerTaskKind.ServiceComponent && components.Count != 1) return "This task operates on one component unit.";
        var installed = components.Select(id => Component(id)!.InstalledOwner).Where(o => o is not null).Distinct().ToArray();
        if (installed.Length > 1 || installed.Any(o => o != plan.ComputerId && !Links.Any(l => l.ComputerId == plan.ComputerId && o == $"interface/{l.BuildingId}")))
            return "Installed units must share one access site belonging to the selected computer.";
        if (plan.Kind is ComputerTaskKind.InstallAsset or ComputerTaskKind.Study && (plan.AssetId is null || Asset(plan.AssetId) is null)) return "Choose a recovered digital asset.";
        if (plan.Kind == ComputerTaskKind.TrainAdapter && (plan.AssetId is null || Asset(plan.AssetId) is not { } asset || !asset.Capabilities.Contains("model-4b"))) return "Choose the complete recovered 4B model asset.";
        if (plan.Kind is ComputerTaskKind.ValidateAdapter or ComputerTaskKind.ModelMonitor && (plan.AssetId is null || Adapter(plan.AssetId) is null)) return "Choose an existing model adapter.";
        if (plan.Kind == ComputerTaskKind.InstallTelemetry)
        {
            if (plan.TargetBuildingId is null || world.Module<CompoundModule>().FindBuilding(plan.TargetBuildingId) is null
                || components.Count is < 1 or > 2 || !components.Any(c => Definition(Component(c)!.DefinitionId)!.Kind == ComponentKind.Sensor)
                || components.Count(c => Definition(Component(c)!.DefinitionId)!.Kind == ComponentKind.Sensor) != 1
                || components.Any(c => Definition(Component(c)!.DefinitionId)!.Kind is not (ComponentKind.Sensor or ComponentKind.Controller))) return "Choose a target fixture, one sensor, and optionally one controller.";
            if (_links.Any(l => l.BuildingId == plan.TargetBuildingId)) return "This fixture already has a telemetry installation.";
        }
        string station = StationFor(plan); return WorksiteLogistics.ExplainNew(world, plan.Id, station, plan.CarrierId, SourceStores(world));
    }
    private string StationFor(ComputerTaskPlan plan)
    {
        if (plan.Kind == ComputerTaskKind.InstallTelemetry) return plan.TargetBuildingId!;
        if (plan.Kind is ComputerTaskKind.DiagnoseComponents or ComputerTaskKind.RepairComponent or ComputerTaskKind.ServiceComponent
            && (plan.Components ?? []).All(id => Component(id)!.InstalledOwner is null)) return "electronics-bench";
        if ((plan.Components ?? []).Select(id => Component(id)!.InstalledOwner).FirstOrDefault(o => o is not null) is { } owner && owner.StartsWith("interface/", StringComparison.Ordinal))
            return owner["interface/".Length..];
        return Computer(plan.ComputerId)!.BuildingId;
    }
    private static IReadOnlyList<string> SourceStores(World world) => world.Module<InventoryModule>().Stockpiles.Where(s => s.Position is not null).Select(s => s.Id).ToArray();
    public ComputerJob Accept(World world, ComputerTaskPlan plan)
    {
        if (Explain(world, plan) is { } reason) throw new InvalidOperationException(reason);
        var frozen = plan with { Components = Array.AsReadOnly((plan.Components ?? []).ToArray()) };
        var site = WorksiteLogistics.Create(world, plan.Id, StationFor(plan), plan.CarrierId, SourceStores(world), 128);
        var job = new ComputerJob(frozen, site); _jobs.Add(plan.Id, job); return job;
    }
    public IReadOnlyList<MaterialRequirement> Requirements(ComputerJob job)
    {
        var result = new List<MaterialRequirement>();
        foreach (string id in job.Plan.Components ?? [])
        {
            var component = Component(id)!;
            if (job.Plan.Kind != ComputerTaskKind.RemoveComponent && component.InstalledOwner is null)
                result.Add(new MaterialRequirement(component.MaterialId, component.MassKg));
        }
        if (job.Plan.Kind == ComputerTaskKind.InstallAsset)
        {
            var source = Component(Asset(job.Plan.AssetId!)!.SourceMediaId)!;
            if (source.InstalledOwner is null && !result.Any(r => r.MaterialId == source.MaterialId)) result.Add(new(source.MaterialId, source.MassKg));
        }
        if (job.Plan.Kind == ComputerTaskKind.RepairComponent)
        {
            var component = Component(job.Plan.Components![0])!;
            result.Add(component.Fault == ComponentFault.BadConnector ? new("metal.copper", .05m, .95m) : new("part.polymer", .1m, .97m));
        }
        if (job.Plan.Kind == ComputerTaskKind.ServiceComponent) result.Add(new("water.clean", .05m, 1));
        return result;
    }
    public string? Compatibility(IReadOnlyList<string> componentIds, bool requireDiagnosis = true)
    {
        if (componentIds.Distinct(StringComparer.Ordinal).Count() != componentIds.Count || componentIds.Any(id => Component(id) is null)) return "Invalid component selection.";
        var units = componentIds.Select(id => Component(id)!).ToArray();
        if (units.Any(c => Definition(c.DefinitionId)!.Kind is ComponentKind.PortableMedia or ComponentKind.Sensor or ComponentKind.Controller)) return "Portable media and industrial interfaces are separate peripherals.";
        foreach (var role in new[] { ComponentKind.Motherboard, ComponentKind.Processor, ComponentKind.PowerSupply, ComponentKind.Cooler, ComponentKind.Storage })
            if (units.Count(c => Definition(c.DefinitionId)!.Kind == role) != 1) return $"Assembly needs exactly one {role}.";
        if (units.Count(c => Definition(c.DefinitionId)!.Kind == ComponentKind.GraphicsCard) > 1) return "This chassis supports one graphics card.";
        if (requireDiagnosis && units.Any(c => !c.Diagnosed)) return "Components need diagnosis before assembly.";
        if (requireDiagnosis && units.Any(c => c.Condition < .5m || c.Fault != ComponentFault.None)) return "An installed component needs repair or replacement.";
        ComponentDefinition Get(ComponentKind role) => Definition(units.Single(c => Definition(c.DefinitionId)!.Kind == role).DefinitionId)!;
        var board = Get(ComponentKind.Motherboard); var cpu = Get(ComponentKind.Processor); var supply = Get(ComponentKind.PowerSupply);
        var cooling = Get(ComponentKind.Cooler); var storage = Get(ComponentKind.Storage);
        var ram = units.Where(c => Definition(c.DefinitionId)!.Kind == ComponentKind.Memory).Select(c => Definition(c.DefinitionId)!).ToArray();
        if (board.Socket != cpu.Socket || cooling.Socket != cpu.Socket) return "CPU, motherboard and cooler sockets do not match.";
        if (ram.Length == 0 || ram.Length > board.MemorySlots || ram.Any(r => r.MemoryType != board.MemoryType)) return "RAM type or slot count is incompatible.";
        if (board.StorageInterface != storage.StorageInterface) return "The storage interface is incompatible.";
        int gpuWatts = units.Where(c => Definition(c.DefinitionId)!.Kind == ComponentKind.GraphicsCard).Sum(c => Definition(c.DefinitionId)!.GpuWatts);
        if (gpuWatts > 0 && !supply.PciePower) return "The PSU lacks the required GPU power connector.";
        if (cooling.CoolingWatts < cpu.CpuWatts) return "The cooler cannot handle the CPU's authored thermal load.";
        var psuUnit = units.Single(c => Definition(c.DefinitionId)!.Kind == ComponentKind.PowerSupply);
        if (supply.PsuWatts * psuUnit.Condition < (cpu.CpuWatts + gpuWatts + 50) * 1.2m) return "The PSU lacks condition-adjusted peak capacity and headroom.";
        return null;
    }
    public string? HardwareReason(string computerId) => Compatibility(Computer(computerId)!.ComponentIds);
    public int RamGiB(string computerId) => Computer(computerId)!.ComponentIds.Sum(id => Definition(Component(id)!.DefinitionId)!.RamGiB);
    public int VramGiB(string computerId) => Computer(computerId)!.ComponentIds.Sum(id => Definition(Component(id)!.DefinitionId)!.VramGiB);
    private IReadOnlyList<string> Drives(string computerId) => Computer(computerId)!.ComponentIds.Where(id => Definition(Component(id)!.DefinitionId)!.Kind == ComponentKind.Storage).ToArray();
    public decimal StorageGiB(string computerId) => Drives(computerId).Sum(id => Definition(Component(id)!.DefinitionId)!.StorageGiB);
    public decimal UsedStorageGiB(string computerId)
    {
        var drives = Drives(computerId);
        return Copies.Where(c => drives.Contains(c.StorageId)).Sum(c => Asset(c.AssetId)!.SizeGiB)
            + Adapters.Where(a => drives.Contains(a.StorageId)).Sum(a => a.SizeGiB)
            + Samples.Count(s => drives.Contains(s.StorageId)) * .000001m;
    }
    public bool HasCapability(string computerId, string capability) => Copies.Any(c => Drives(computerId).Contains(c.StorageId)
        && c.Verified && c.Integrity >= .9m && Asset(c.AssetId)!.Capabilities.Contains(capability));
    public decimal PromisedStorageGiB(string computerId) => Jobs.Where(j => j.Active && j.ResourceClaim && j.Plan.ComputerId == computerId)
        .Sum(j => j.Plan.Kind == ComputerTaskKind.InstallAsset ? Asset(j.Plan.AssetId!)!.SizeGiB : Profile(j.Plan.Kind).OutputGiB);
    public bool Knows(int workerId, string capability) => _knowledge.Contains(new LearnedKnowledge(workerId, capability));
    private IEnumerable<TelemetrySample> AccessibleSamples(string computerId) => Samples.Where(s => s.ComputerId == computerId && Drives(computerId).Contains(s.StorageId));
    public string? Prerequisites(World world, ComputerJob job)
    {
        var plan = job.Plan; var computer = Computer(plan.ComputerId)!; var profile = Profile(plan.Kind); var components = plan.Components ?? [];
        if (ClaimedComponents(job).Any(id => Component(id)!.ClaimedBy is { } owner && owner != job.Owner)) return "A component is reserved by another task.";
        if (plan.Kind is ComputerTaskKind.Assemble or ComputerTaskKind.RemoveComponent or ComputerTaskKind.RepairComponent or ComputerTaskKind.ServiceComponent && components.Any(c => Component(c)!.InstalledOwner is not null))
        {
            if (computer.Powered) return "Power the computer off before physical component work.";
            if (Jobs.Any(j => j.Active && j.ResourceClaim && j.Plan.ComputerId == plan.ComputerId)) return "Cancel active machine tasks before hardware maintenance.";
            if (components.Any(c => Component(c)!.InstalledOwner is { } owner && owner != computer.Id
                && !Links.Any(l => l.ComputerId == computer.Id && owner == $"interface/{l.BuildingId}"))) return "The unit is installed in a different machine or interface.";
        }
        if (plan.Kind == ComputerTaskKind.Assemble)
        {
            if (computer.Powered) return "Power off before assembly.";
            if (Jobs.Any(j => j.Active && j.ResourceClaim && j.Plan.ComputerId == plan.ComputerId)) return "Cancel active machine tasks before changing the assembly.";
            if (components.Any(id => Component(id)!.InstalledOwner is not null)) return "Select loose units; existing installed units are included automatically.";
            return Compatibility(computer.ComponentIds.Concat(components).ToArray());
        }
        if (plan.Kind == ComputerTaskKind.RemoveComponent && components.Any(id => Component(id)!.InstalledOwner != computer.Id)) return "This unit is not installed in the selected computer.";
        if (plan.Kind == ComputerTaskKind.RepairComponent)
        {
            var component = Component(components[0])!;
            if (!component.Diagnosed) return "Diagnose the component before choosing a repair.";
            if (component.Fault is ComponentFault.DeadSilicon or ComponentFault.MediaCorruption) return "This fault requires replacement or independent data recovery; generic repair cannot restore it.";
            if (component.Fault == ComponentFault.None) return "The unit has no specified repairable fault.";
        }
        if (plan.Kind == ComputerTaskKind.ServiceComponent)
        {
            var component = Component(components[0])!;
            if (!component.Diagnosed || component.Fault != ComponentFault.None) return "Routine service needs a diagnosed, fault-free component; repair or replace specified faults first.";
            if (component.Condition >= .95m) return "This component does not need routine service.";
        }
        if (plan.Kind == ComputerTaskKind.InstallTelemetry)
        {
            if (components.Any(id => !Component(id)!.Diagnosed || Component(id)!.Fault != ComponentFault.None || Component(id)!.InstalledOwner is not null)) return "Telemetry needs diagnosed, healthy, loose interface units.";
            if (HardwareReason(computer.Id) is { } hardware) return hardware;
        }
        if (!profile.Machine) return null;
        if (!computer.Powered) return "Computer is switched off.";
        if (HardwareReason(computer.Id) is { } hardwareReason) return hardwareReason;
        bool bootstrapping = plan.Kind == ComputerTaskKind.InstallAsset && Asset(plan.AssetId!)!.Capabilities.Contains("os");
        if (!bootstrapping && !HasCapability(computer.Id, "os")) return "A verified operating system is required.";
        if (profile.Knowledge is { } knowledge && (plan.TechnicianId is not { } technician || !Knows(technician, knowledge))) return $"Study recovered knowledge '{knowledge}' before this task.";
        if (plan.Kind == ComputerTaskKind.InstallAsset)
        {
            var asset = Asset(plan.AssetId!)!; var source = Component(asset.SourceMediaId)!;
            if (!source.Diagnosed || source.Fault != ComponentFault.None || source.Condition < .5m) return "Diagnose and recover usable source media.";
            if (!asset.HasManifest || asset.Integrity < .9m) return "Archive integrity or trusted checksum manifest is insufficient.";
            if (asset.Dependencies.Any(d => !HasCapability(computer.Id, d))) return "Required offline software dependencies are missing.";
            if (Copies.Any(c => Drives(computer.Id).Contains(c.StorageId) && c.AssetId == asset.Id && c.Verified && c.Integrity >= .9m)) return "This verified archive is already installed.";
        }
        if (plan.Kind == ComputerTaskKind.Study && !Copies.Any(c => c.AssetId == plan.AssetId && Drives(computer.Id).Contains(c.StorageId) && c.Verified && c.Integrity >= .9m)) return "The manual needs a verified local copy.";
        if (plan.Kind is ComputerTaskKind.LabelTelemetry or ComputerTaskKind.RuleMonitor && !HasCapability(computer.Id, "telemetry")) return "Install the offline telemetry tools.";
        if (plan.Kind == ComputerTaskKind.RuleMonitor && !HasCapability(computer.Id, "rule-control")) return "Install the recovered rule-control software.";
        if (plan.Kind is ComputerTaskKind.TrainAdapter or ComputerTaskKind.ValidateAdapter or ComputerTaskKind.ModelMonitor)
        {
            if (new[] { "gpu-runtime", "quant-runtime", "model-4b", "tokenizer" }.Any(c => !HasCapability(computer.Id, c))) return "The GPU runtime, quantized model runtime, weights, and tokenizer/config must all be installed.";
            if (plan.Kind == ComputerTaskKind.TrainAdapter && AccessibleSamples(computer.Id).Count(s => !s.HeldOut && s.LabelledBy is not null) < 24) return "Need at least 24 labelled training telemetry samples.";
            if (plan.Kind == ComputerTaskKind.ValidateAdapter)
            {
                var adapter = Adapter(plan.AssetId!)!;
                if (!Drives(computer.Id).Contains(adapter.StorageId)) return "The adapter is not on a mounted drive.";
                if (AccessibleSamples(computer.Id).Count(s => s.HeldOut && s.LabelledBy is not null && !adapter.TrainingIds.Contains(s.Id)) < 8) return "Need eight labelled held-out samples outside the training set.";
            }
            if (plan.Kind == ComputerTaskKind.ModelMonitor && (Adapter(plan.AssetId!) is not { ValidationScore: >= .75m } checkedAdapter || !Drives(computer.Id).Contains(checkedAdapter.StorageId))) return "Deploy only an adapter that passed held-out simulation validation.";
        }
        if (plan.Kind == ComputerTaskKind.LabelTelemetry && !AccessibleSamples(computer.Id).Any(s => s.LabelledBy is null)) return "No unlabelled telemetry is accessible.";
        if (plan.Kind is ComputerTaskKind.RuleMonitor or ComputerTaskKind.ModelMonitor && !_links.Any(l => l.ComputerId == computer.Id)) return "Install at least one physical telemetry interface.";
        var others = Jobs.Where(j => j.Active && j.ResourceClaim && j.Plan.ComputerId == computer.Id && j.Plan.Id != plan.Id).ToArray();
        if (others.Sum(j => Profile(j.Plan.Kind).RamGiB) + profile.RamGiB > RamGiB(computer.Id)) return "Insufficient unclaimed system RAM for this workload profile.";
        if (others.Sum(j => Profile(j.Plan.Kind).VramGiB) + profile.VramGiB > VramGiB(computer.Id)) return "Insufficient unclaimed VRAM for this workload/context profile.";
        if (others.Sum(j => Profile(j.Plan.Kind).CpuUnits) + profile.CpuUnits > 4) return "CPU scheduling capacity is occupied.";
        decimal outputSize = plan.Kind == ComputerTaskKind.InstallAsset ? Asset(plan.AssetId!)!.SizeGiB : profile.OutputGiB;
        decimal promised = others.Sum(j => j.Plan.Kind == ComputerTaskKind.InstallAsset ? Asset(j.Plan.AssetId!)!.SizeGiB : Profile(j.Plan.Kind).OutputGiB);
        if (UsedStorageGiB(computer.Id) + promised + outputSize > StorageGiB(computer.Id)) return "Insufficient unclaimed persistent storage.";
        return null;
    }
    internal void Prepare(World world, List<GameEvent> events)
    {
        foreach (var job in Jobs.Where(j => j.Status == ComputerTaskStatus.WaitingForInputs))
        {
            string? reason = Prerequisites(world, job);
            if (reason is not null) { Block(world, job, reason, events); continue; }
            var requirements = Requirements(job); reason = WorksiteLogistics.DeliverInputs(world, job.Site, requirements, events);
            if (!WorksiteLogistics.Ready(world, job.Site, requirements)) { Block(world, job, reason, events); continue; }
            var profile = Profile(job.Plan.Kind);
            if (job.Plan.TechnicianId is { } id) reason = WorksiteLogistics.ExplainLabor(world, id,
                new(profile.Machine ? "computing" : "electronics", 40)); else reason = null;
            if (reason is null && job.Plan.TechnicianId is not null && world.Module<EquipmentModule>().OwnerOf(job.Site.StationId) is not null) reason = "Access station is claimed by another task.";
            if (reason is not null) { Block(world, job, reason, events); continue; }
            if (job.Plan.Kind == ComputerTaskKind.RepairComponent && Component(job.Plan.Components![0])!.InstalledOwner is null
                && !world.Module<InventoryModule>().TryReserveCapacity($"{job.Owner}/repair-output", job.Site.LocalStoreId,
                    Component(job.Plan.Components[0])!.MassKg + requirements.Where(r => r.MaterialId != Component(job.Plan.Components[0])!.MaterialId).Sum(r => r.QuantityKg)))
            { Block(world, job, "No local capacity for the repaired component.", events); continue; }
            if (job.Plan.Kind == ComputerTaskKind.RemoveComponent && !world.Module<InventoryModule>().TryReserveCapacity($"{job.Owner}/removed-output", job.Site.LocalStoreId, Component(job.Plan.Components![0])!.MassKg))
            { Block(world, job, "No local capacity for the removed component.", events); continue; }
            if (job.Plan.Kind == ComputerTaskKind.ServiceComponent)
            {
                var part = Component(job.Plan.Components![0])!; var inventory = world.Module<InventoryModule>();
                bool space = inventory.TryReserveCapacity($"{job.Owner}/service-waste", job.Site.LocalStoreId, .05m);
                if (space && part.InstalledOwner is null) space = inventory.TryReserveCapacity($"{job.Owner}/service-unit", job.Site.LocalStoreId, part.MassKg);
                if (!space)
                {
                    inventory.ReleaseReservations($"{job.Owner}/service-waste"); inventory.ReleaseReservations($"{job.Owner}/service-unit");
                    Block(world, job, "No local capacity for servicing receipts.", events); continue;
                }
            }
            WorksiteLogistics.ReserveInputs(world, job.Site, requirements, job.Owner);
            foreach (string component in ClaimedComponents(job)) Component(component)!.ClaimedBy = job.Owner;
            if (job.Plan.TechnicianId is { } worker)
            {
                if (!world.Module<WorkforceModule>().TryReserve(worker, job.Owner) || !world.Module<EquipmentModule>().TryReserve(job.Site.StationId, job.Owner))
                    throw new InvalidOperationException("Computer task claims changed during serial acquisition.");
            }
            job.ResourceClaim = profile.Machine; job.Status = ComputerTaskStatus.Walking;
            if (job.Plan.Kind == ComputerTaskKind.TrainAdapter) job.TrainingIds = Array.AsReadOnly(AccessibleSamples(job.Plan.ComputerId).Where(s => !s.HeldOut && s.LabelledBy is not null).Take(32).Select(s => s.Id).ToArray());
            Block(world, job, null, events);
        }
    }
    private IReadOnlyList<string> ClaimedComponents(ComputerJob job)
    {
        var result = (job.Plan.Components ?? []).ToList();
        if (job.Plan.Kind == ComputerTaskKind.InstallAsset) result.Add(Asset(job.Plan.AssetId!)!.SourceMediaId);
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }
    public decimal RequestedPower(World world, string computerId)
    {
        var computer = Computer(computerId)!; if (!computer.Powered || HardwareReason(computerId) is not null) return 0;
        var supply = computer.ComponentIds.Select(id => Definition(Component(id)!.DefinitionId)!).Single(d => d.Kind == ComponentKind.PowerSupply);
        return (.04m + Jobs.Where(j => j.Working && j.ResourceClaim && j.Plan.ComputerId == computerId).Sum(j => Profile(j.Plan.Kind).ActiveDcKw)) / supply.Efficiency;
    }
    private bool InterfaceConnected(World world, TelemetryLink link)
    {
        var computer = Computer(link.ComputerId)!; if (!computer.Powered || HardwareReason(computer.Id) is not null
            || world.Module<UtilitiesModule>().ServiceFor(computer.BuildingId) is not { Operational: true }) return false;
        var sensor = Component(link.SensorId)!;
        if (sensor.Condition < .5m || sensor.Fault != ComponentFault.None) return false;
        var compound = world.Module<CompoundModule>(); var networks = world.Module<UtilitiesModule>().ConnectedComponents(compound, UtilityKind.Data);
        return networks.TryGetValue(compound.FindBuilding(computer.BuildingId)!.Origin, out int a)
            && networks.TryGetValue(compound.FindBuilding(link.BuildingId)!.Origin, out int b) && a == b;
    }
    public bool CanControl(World world, string buildingId) => Links.Any(l => l.BuildingId == buildingId && l.ControllerId is { } controller
        && Component(controller) is { Condition: >= .5m, Fault: ComponentFault.None } && InterfaceConnected(world, l)
        && HasCapability(l.ComputerId, "rule-control") && Jobs.Any(j => j.Plan.ComputerId == l.ComputerId
            && j.Plan.Kind == ComputerTaskKind.RuleMonitor && j.Status == ComputerTaskStatus.Running && j.ResourceClaim));
    internal void Work(World world, List<GameEvent> events)
    {
        foreach (var job in Jobs.Where(j => j.Working))
        {
            var plan = job.Plan; var profile = Profile(plan.Kind); var computer = Computer(plan.ComputerId)!;
            string? reason = Prerequisites(world, job);
            if (profile.Machine && (!computer.Powered || HardwareReason(computer.Id) is not null
                || world.Module<UtilitiesModule>().ServiceFor(computer.BuildingId) is not { Operational: true })) reason = "Computer hardware or electrical service is unavailable.";
            if (!profile.Machine && world.Module<UtilitiesModule>().ServiceFor(job.Site.StationId) is not { Operational: true }) reason = "Electronics station utilities are unavailable.";
            if (reason is not null)
            {
                if (job.Status == ComputerTaskStatus.Running && job.BlockedReason != reason && profile.Machine && !profile.Continuous)
                { job.Progress = job.Checkpoint; job.Restarts++; }
                Block(world, job, reason, events); continue;
            }
            bool needsPerson = plan.TechnicianId is not null && (profile.HandsOn || job.SetupSteps < 2);
            if (needsPerson)
            {
                var worker = world.Module<WorkforceModule>().Find(plan.TechnicianId!.Value)!;
                if (!worker.Present || world.Module<WorkforceModule>().ReservationFor(worker.Id) != job.Owner) reason = "Technician is absent or its labor claim is missing.";
                else
                {
                    var move = Navigation.StepTowards(world.Module<CompoundModule>(), worker.Id, world.Module<CompoundModule>().FindBuilding(job.Site.StationId)!.InteractionCell);
                    if (!move.Arrived) reason = move.BlockedReason ?? "Technician is walking to the station.";
                }
                if (reason is not null) { Block(world, job, reason, events); continue; }
                job.SetupSteps++;
                if (!profile.HandsOn && job.SetupSteps >= 2) ReleaseLabor(world, job);
            }
            job.Status = ComputerTaskStatus.Running; Block(world, job, null, events);
            if (profile.Continuous)
            {
                Collect(world, job, events); WearComputer(computer); continue;
            }
            decimal rate = profile.Machine && !profile.HandsOn ? .75m
                : .25m + world.Module<WorkforceModule>().Find(plan.TechnicianId!.Value)!.Skills.Rating(profile.Machine ? "computing" : "electronics") / 100m;
            if (plan.Kind is ComputerTaskKind.TrainAdapter or ComputerTaskKind.ValidateAdapter)
                rate *= computer.ComponentIds.Select(id => Definition(Component(id)!.DefinitionId)!).Single(d => d.Kind == ComponentKind.GraphicsCard).ComputeRating / 50m;
            job.Progress = Math.Min(profile.Effort, job.Progress + rate);
            if (plan.Kind == ComputerTaskKind.TrainAdapter && job.Progress - job.Checkpoint >= 6) job.Checkpoint = job.Progress;
            if (profile.Machine) WearComputer(computer);
            if (job.Progress < profile.Effort) continue;
            Complete(world, job); Release(world, job); job.Status = ComputerTaskStatus.Completed;
            events.Add(IndustryEvents.Event(world, IndustryEvents.Completed, $"Computer task '{plan.Id}' ({plan.Kind}) completed.", EventSeverity.Positive));
        }
    }
    private void WearComputer(CompoundComputer computer)
    { foreach (string id in computer.ComponentIds) Component(id)!.Condition = Math.Max(0, Component(id)!.Condition - .000002m); }
    private void Collect(World world, ComputerJob job, List<GameEvent> events)
    {
        var computer = Computer(job.Plan.ComputerId)!; string drive = Drives(computer.Id).First();
        foreach (var link in Links.Where(l => l.ComputerId == computer.Id))
        {
            if (_samples.Count >= 10000 || UsedStorageGiB(computer.Id) + PromisedStorageGiB(computer.Id) + .000001m > StorageGiB(computer.Id))
            { Block(world, job, "Telemetry archive is full or its space is reserved; archive management is required.", events); return; }
            if (!InterfaceConnected(world, link)) { Block(world, job, "A physical telemetry data link is disconnected.", events); continue; }
            var fixture = world.Module<CompoundModule>().FindBuilding(link.BuildingId)!;
            decimal observed = decimal.Round(fixture.Condition, 2);
            _samples.Add(new TelemetrySample(_nextSample, world.Clock.Tick, computer.Id, drive, fixture.Id, observed,
                world.Module<UtilitiesModule>().ServiceFor(fixture.Id)?.PowerAllocatedKw ?? 0, _nextSample % 5 == 0)); _nextSample++;
            decimal forecast = observed;
            if (job.Plan.Kind == ComputerTaskKind.ModelMonitor)
            {
                // Authored, deterministic trend predictor for the simulated adapter, not host-side ML inference.
                var history = AccessibleSamples(computer.Id).Where(s => s.BuildingId == fixture.Id).GroupBy(s => s.Tick)
                    .Select(g => g.Last()).TakeLast(16).ToArray();
                if (history.Length >= 2 && history[^1].Tick > history[0].Tick)
                    forecast = Math.Clamp(observed + (history[^1].ObservedCondition - history[0].ObservedCondition)
                        * 24 / (history[^1].Tick - history[0].Tick), 0, 1);
            }
            string alert = $"{computer.Id}/{fixture.Id}/{job.Plan.Kind}";
            if (forecast < .8m && _alerts.Add(alert))
                events.Add(IndustryEvents.Event(world, IndustryEvents.ComputerAlert, job.Plan.Kind == ComputerTaskKind.ModelMonitor
                    ? $"{computer.Id}: model-assisted simulation forecasts {fixture.Id} condition {forecast:P0} in six hours; inspect and schedule human maintenance."
                    : $"{computer.Id}: rule monitoring flags {fixture.Id} below 80% condition for human maintenance.", EventSeverity.Warning));
            if (forecast >= .8m) _alerts.Remove(alert);
        }
    }
    private void Complete(World world, ComputerJob job)
    {
        var plan = job.Plan; var inventory = world.Module<InventoryModule>(); var computer = Computer(plan.ComputerId)!;
        switch (plan.Kind)
        {
            case ComputerTaskKind.DiagnoseComponents:
                foreach (string id in plan.Components!) Component(id)!.Diagnosed = true;
                break;
            case ComputerTaskKind.Assemble:
                foreach (string owner in job.Site.InputOwners) inventory.TakeReservedStock(owner);
                job.Site.ClearInputs();
                foreach (string id in plan.Components!) { Component(id)!.InstalledOwner = computer.Id; computer.Install(id); }
                break;
            case ComputerTaskKind.RepairComponent:
                var component = Component(plan.Components![0])!;
                decimal added = Requirements(job).Where(r => r.MaterialId != component.MaterialId).Sum(r => r.QuantityKg);
                if (component.InstalledOwner is null)
                    Conversions.Complete(inventory, job.Site.InputOwners, [new($"{job.Owner}/repair-output", new(component.MaterialId, component.MassKg + added, new BatchQuality(1, "repaired component")))]);
                else foreach (string owner in job.Site.InputOwners) inventory.TakeReservedStock(owner);
                job.Site.ClearInputs(); component.MassKg += added; component.Fault = ComponentFault.None; component.Condition = .95m;
                break;
            case ComputerTaskKind.RemoveComponent:
                var removed = Component(plan.Components![0])!;
                inventory.ReceiveReservedCargo($"{job.Owner}/removed-output", new(removed.MaterialId, removed.MassKg, new BatchQuality(1, "removed component")));
                removed.InstalledOwner = null; computer.Remove(removed.Id);
                break;
            case ComputerTaskKind.ServiceComponent:
                var serviced = Component(plan.Components![0])!;
                var receipts = new List<ConversionDelivery> { new($"{job.Owner}/service-waste", new("waste.washwater", .05m, new BatchQuality(.95m, "electronics service effluent"))) };
                if (serviced.InstalledOwner is null) receipts.Add(new($"{job.Owner}/service-unit", new(serviced.MaterialId, serviced.MassKg, new BatchQuality(1, "serviced component"))));
                Conversions.Complete(inventory, job.Site.InputOwners, receipts); job.Site.ClearInputs();
                serviced.Condition = Math.Min(.95m, serviced.Condition + .1m);
                break;
            case ComputerTaskKind.InstallAsset:
                var asset = Asset(plan.AssetId!)!; string storage = Drives(computer.Id).First();
                _copies.RemoveAll(c => c.AssetId == asset.Id && c.StorageId == storage);
                _copies.Add(new AssetCopy(asset.Id, computer.Id, storage, asset.Integrity, asset.HasManifest && asset.Integrity >= .9m));
                break;
            case ComputerTaskKind.Study:
                if (Asset(plan.AssetId!)!.Knowledge is { } knowledge) _knowledge.Add(new LearnedKnowledge(plan.TechnicianId!.Value, knowledge));
                break;
            case ComputerTaskKind.InstallTelemetry:
                foreach (string owner in job.Site.InputOwners) inventory.TakeReservedStock(owner); job.Site.ClearInputs();
                string sensor = plan.Components!.Single(id => Definition(Component(id)!.DefinitionId)!.Kind == ComponentKind.Sensor);
                string? controller = plan.Components!.FirstOrDefault(id => Definition(Component(id)!.DefinitionId)!.Kind == ComponentKind.Controller);
                foreach (string id in plan.Components!) Component(id)!.InstalledOwner = $"interface/{plan.TargetBuildingId}";
                _links.Add(new TelemetryLink(computer.Id, plan.TargetBuildingId!, sensor, controller));
                break;
            case ComputerTaskKind.LabelTelemetry:
                var eligible = AccessibleSamples(computer.Id).Where(s => s.LabelledBy is null).Take(64).Select(s => s.Id).ToHashSet();
                for (int i = 0; i < _samples.Count; i++) if (eligible.Contains(_samples[i].Id)) _samples[i] = _samples[i] with { LabelledBy = plan.TechnicianId };
                break;
            case ComputerTaskKind.TrainAdapter:
                string adapterId = $"adapter/{plan.Id}";
                _adapters.Add(adapterId, new ModelAdapter(adapterId, computer.Id, Drives(computer.Id).First(), plan.AssetId!, Array.AsReadOnly(job.TrainingIds.ToArray()), []));
                break;
            case ComputerTaskKind.ValidateAdapter:
                var adapter = Adapter(plan.AssetId!)!;
                var heldOut = AccessibleSamples(computer.Id).Where(s => s.HeldOut && s.LabelledBy is not null && !adapter.TrainingIds.Contains(s.Id)).Take(16).Select(s => s.Id).ToArray();
                // A transparent authored game score, never a claim that a real learned model was evaluated.
                decimal score = Math.Min(.92m, .6m + Math.Min(adapter.TrainingIds.Count, 32) * .005m + Math.Min(heldOut.Length, 16) * .005m);
                _adapters[adapter.Id] = adapter with { ValidationIds = Array.AsReadOnly(heldOut), ValidationScore = score };
                break;
        }
    }
    private static void ReleaseLabor(World world, ComputerJob job)
    {
        if (job.Plan.TechnicianId is { } id) world.Module<WorkforceModule>().Release(id, job.Owner);
        world.Module<EquipmentModule>().Release(job.Site.StationId, job.Owner);
    }
    private void Release(World world, ComputerJob job)
    {
        ReleaseLabor(world, job); WorksiteLogistics.ReleaseInputs(world, job.Site);
        foreach (string id in ClaimedComponents(job)) if (Component(id)!.ClaimedBy == job.Owner) Component(id)!.ClaimedBy = null;
        world.Module<InventoryModule>().ReleaseReservations($"{job.Owner}/repair-output"); world.Module<InventoryModule>().ReleaseReservations($"{job.Owner}/removed-output"); job.ResourceClaim = false;
        world.Module<InventoryModule>().ReleaseReservations($"{job.Owner}/service-waste"); world.Module<InventoryModule>().ReleaseReservations($"{job.Owner}/service-unit");
    }
    public IReadOnlyList<GameEvent> Cancel(World world, ComputerJob job)
    {
        if (!job.Active) throw new InvalidOperationException("Task is already finished.");
        var events = new List<GameEvent>(); WorksiteLogistics.CancelUnloaded(world, job.Site, events); Release(world, job);
        job.Status = ComputerTaskStatus.Cancelled; job.BlockedReason = null;
        events.Add(IndustryEvents.Event(world, IndustryEvents.Cancelled, $"Computer task '{job.Plan.Id}' cancelled; components and media remain physical.")); return events;
    }
    private static void Block(World world, ComputerJob job, string? reason, List<GameEvent> events)
    {
        if (reason is not null && job.BlockedReason != reason)
            events.Add(IndustryEvents.Event(world, IndustryEvents.Blocked, $"Computer task '{job.Plan.Id}': {reason}", reason.Contains("walking", StringComparison.OrdinalIgnoreCase) || reason.Contains("travelling", StringComparison.OrdinalIgnoreCase) || reason.StartsWith("Carrier", StringComparison.Ordinal) ? EventSeverity.Info : EventSeverity.Warning));
        job.BlockedReason = reason;
    }
    public object ToSnapshot() => new ComputingSnapshot(Definitions, Components.Select(c => new ComponentSnapshot(c.Id, c.DefinitionId, c.MassKg, c.Condition, c.Fault, c.Diagnosed, c.InstalledOwner, c.ClaimedBy)).ToArray(),
        Computers.Select(c => new ComputerSnapshot(c.Id, c.BuildingId, c.Powered, c.ComponentIds.ToArray())).ToArray(), Assets, Copies, Knowledge, Links, Samples, Adapters,
        Jobs.Select(j => j.Snapshot()).ToArray(), _nextSample, _alerts.Order(StringComparer.Ordinal).ToArray());
    public static ComputingModule FromSnapshot(ComputingSnapshot snapshot)
    {
        var module = new ComputingModule(); foreach (var definition in snapshot.Definitions) module.AddDefinition(definition);
        foreach (var c in snapshot.Components)
        {
            if (string.IsNullOrWhiteSpace(c.Id) || c.MassKg <= 0 || c.Condition is < 0 or > 1 || !Enum.IsDefined(c.Fault)) throw new ArgumentException("Invalid component unit.", nameof(snapshot));
            module._components.Add(c.Id, new ComputerComponent(c.Id, c.DefinitionId, c.MassKg, c.Condition, c.Fault)
            { Diagnosed = c.Diagnosed, InstalledOwner = c.InstalledOwner, ClaimedBy = c.ClaimedBy });
        }
        foreach (var c in snapshot.Computers)
        {
            module.AddComputer(c.Id, c.BuildingId); module.Computer(c.Id)!.Powered = c.Powered;
            foreach (string id in c.Components) module.Computer(c.Id)!.Install(id);
        }
        foreach (var asset in snapshot.Assets) module.AddAsset(asset);
        module._copies.AddRange(snapshot.Copies); foreach (var knowledge in snapshot.Knowledge) module._knowledge.Add(knowledge);
        module._links.AddRange(snapshot.Links); module._samples.AddRange(snapshot.Samples);
        foreach (var adapter in snapshot.Adapters) module._adapters.Add(adapter.Id, adapter with { TrainingIds = Array.AsReadOnly(adapter.TrainingIds.ToArray()), ValidationIds = Array.AsReadOnly(adapter.ValidationIds.ToArray()) });
        foreach (var s in snapshot.Jobs)
        {
            if (!Enum.IsDefined(s.Plan.Kind) || !Enum.IsDefined(s.Status) || s.Progress < 0 || s.Checkpoint < 0 || s.Checkpoint > s.Progress || s.Restarts < 0 || s.SetupSteps < 0)
                throw new ArgumentException("Invalid computer task state.", nameof(snapshot));
            var job = new ComputerJob(s.Plan with { Components = Array.AsReadOnly((s.Plan.Components ?? []).ToArray()) }, Worksite.Restore(s.Site))
            { Status = s.Status, Progress = s.Progress, Checkpoint = s.Checkpoint, Restarts = s.Restarts, SetupSteps = s.SetupSteps, ResourceClaim = s.ResourceClaim,
                BlockedReason = s.BlockedReason, TrainingIds = Array.AsReadOnly(s.TrainingIds.ToArray()) };
            module._jobs.Add(s.Plan.Id, job);
        }
        if (snapshot.NextSample < 1 || snapshot.Samples.Any(s => s.Id >= snapshot.NextSample)
            || snapshot.Knowledge.Distinct().Count() != snapshot.Knowledge.Count || snapshot.Alerts.Distinct(StringComparer.Ordinal).Count() != snapshot.Alerts.Count)
            throw new ArgumentException("Invalid telemetry sequence or duplicate knowledge/alerts.", nameof(snapshot));
        module._nextSample = snapshot.NextSample; foreach (string alert in snapshot.Alerts) module._alerts.Add(alert); return module;
    }
}
