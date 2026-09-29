using System.Text.Json.Serialization;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Industry;

public enum ComponentKind { Motherboard, Processor, Memory, Storage, PowerSupply, GraphicsCard, Cooler, PortableMedia, Sensor, Controller }
public enum ComponentFault { None, BrokenFanClip, BadConnector, DeadSilicon, MediaCorruption }
public sealed record ComponentDefinition
{
    [JsonConstructor]
    public ComponentDefinition(string id, string name, ComponentKind kind, decimal massKg, string socket = "", string memoryType = "",
        string storageInterface = "", int memorySlots = 0, int ramGiB = 0, int vramGiB = 0, decimal storageGiB = 0,
        int psuWatts = 0, bool pciePower = false, decimal efficiency = 1, int coolingWatts = 0,
        int cpuWatts = 0, int gpuWatts = 0, int computeRating = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(kind) || massKg <= 0 || memorySlots < 0 || ramGiB < 0 || vramGiB < 0 || storageGiB < 0
            || psuWatts < 0 || coolingWatts < 0 || cpuWatts < 0 || gpuWatts < 0 || efficiency is <= 0 or > 1 || computeRating is < 1 or > 100)
            throw new ArgumentException("Invalid component specification.");
        Id = id; Name = name; Kind = kind; MassKg = massKg; Socket = socket; MemoryType = memoryType; StorageInterface = storageInterface;
        MemorySlots = memorySlots; RamGiB = ramGiB; VramGiB = vramGiB; StorageGiB = storageGiB; PsuWatts = psuWatts;
        PciePower = pciePower; Efficiency = efficiency; CoolingWatts = coolingWatts; CpuWatts = cpuWatts; GpuWatts = gpuWatts; ComputeRating = computeRating;
    }
    public string Id { get; }
    public string Name { get; }
    public ComponentKind Kind { get; }
    public decimal MassKg { get; }
    public string Socket { get; }
    public string MemoryType { get; }
    public string StorageInterface { get; }
    public int MemorySlots { get; }
    public int RamGiB { get; }
    public int VramGiB { get; }
    public decimal StorageGiB { get; }
    public int PsuWatts { get; }
    public bool PciePower { get; }
    public decimal Efficiency { get; }
    public int CoolingWatts { get; }
    public int CpuWatts { get; }
    public int GpuWatts { get; }
    public int ComputeRating { get; }
}
public sealed class ComputerComponent
{
    internal ComputerComponent(string id, string definitionId, decimal massKg, decimal condition, ComponentFault fault)
    { Id = id; DefinitionId = definitionId; MassKg = massKg; Condition = condition; Fault = fault; }
    public string Id { get; }
    public string DefinitionId { get; }
    public string MaterialId => $"item/{Id}";
    public decimal MassKg { get; internal set; }
    public decimal Condition { get; internal set; }
    public ComponentFault Fault { get; internal set; }
    public bool Diagnosed { get; internal set; }
    public string? InstalledOwner { get; internal set; }
    public string? ClaimedBy { get; internal set; }
}
public sealed record ComponentSnapshot(string Id, string DefinitionId, decimal MassKg, decimal Condition, ComponentFault Fault,
    bool Diagnosed, string? InstalledOwner, string? ClaimedBy);
public sealed record ComputerSnapshot(string Id, string BuildingId, bool Powered, IReadOnlyList<string> Components);
public sealed class CompoundComputer
{
    internal CompoundComputer(string id, string buildingId) { Id = id; BuildingId = buildingId; }
    public string Id { get; }
    public string BuildingId { get; }
    public bool Powered { get; internal set; }
    private readonly List<string> _components = [];
    public IReadOnlyList<string> ComponentIds => _components.AsReadOnly();
    internal void Install(string id) => _components.Add(id);
    internal void Remove(string id) => _components.Remove(id);
}
public sealed record DigitalAsset(string Id, string Name, decimal SizeGiB, string SourceMediaId, decimal Integrity,
    IReadOnlyList<string> Capabilities, IReadOnlyList<string> Dependencies, string? Knowledge = null, bool HasManifest = true);
public sealed record AssetCopy(string AssetId, string ComputerId, string StorageId, decimal Integrity, bool Verified);
public sealed record LearnedKnowledge(int WorkerId, string Capability);
public sealed record TelemetryLink(string ComputerId, string BuildingId, string SensorId, string? ControllerId);
public sealed record TelemetrySample(long Id, long Tick, string ComputerId, string StorageId, string BuildingId, decimal ObservedCondition,
    decimal PowerKw, bool HeldOut, int? LabelledBy = null);
public sealed record ModelAdapter(string Id, string ComputerId, string StorageId, string ModelAssetId, IReadOnlyList<long> TrainingIds,
    IReadOnlyList<long> ValidationIds, decimal? ValidationScore = null, decimal SizeGiB = .25m);
public enum ComputerTaskKind { DiagnoseComponents, RepairComponent, Assemble, RemoveComponent, InstallAsset, Study,
    InstallTelemetry, LabelTelemetry, TrainAdapter, ValidateAdapter, RuleMonitor, ModelMonitor, ServiceComponent }
public enum ComputerTaskStatus { WaitingForInputs, Walking, Running, Completed, Cancelled }
public sealed record ComputerTaskPlan(string Id, ComputerTaskKind Kind, string ComputerId, int? TechnicianId = null,
    int CarrierId = 2, IReadOnlyList<string>? Components = null, string? AssetId = null, string? TargetBuildingId = null);
public sealed record ComputerProfile(decimal Effort, decimal RamGiB, decimal VramGiB, decimal CpuUnits, decimal ActiveDcKw,
    decimal OutputGiB = 0, bool Machine = false, bool Continuous = false, bool HandsOn = true, string? Knowledge = null);
public sealed class ComputerJob
{
    internal ComputerJob(ComputerTaskPlan plan, Worksite site) { Plan = plan; Site = site; }
    public ComputerTaskPlan Plan { get; }
    public Worksite Site { get; }
    public string Owner => $"scavenger.computer/{Plan.Id}";
    public ComputerTaskStatus Status { get; internal set; }
    public decimal Progress { get; internal set; }
    public decimal Checkpoint { get; internal set; }
    public int Restarts { get; internal set; }
    public int SetupSteps { get; internal set; }
    public bool ResourceClaim { get; internal set; }
    public string? BlockedReason { get; internal set; }
    public IReadOnlyList<long> TrainingIds { get; internal set; } = [];
    public bool Active => Status is not ComputerTaskStatus.Completed and not ComputerTaskStatus.Cancelled;
    public bool Working => Status is ComputerTaskStatus.Walking or ComputerTaskStatus.Running;
    internal ComputerJobSnapshot Snapshot() => new(Plan, Site.Snapshot(), Status, Progress, Checkpoint, Restarts, SetupSteps,
        ResourceClaim, BlockedReason, TrainingIds);
}
public sealed record ComputerJobSnapshot(ComputerTaskPlan Plan, WorksiteSnapshot Site, ComputerTaskStatus Status, decimal Progress,
    decimal Checkpoint, int Restarts, int SetupSteps, bool ResourceClaim, string? BlockedReason, IReadOnlyList<long> TrainingIds);
public sealed record ComputingSnapshot(IReadOnlyList<ComponentDefinition> Definitions, IReadOnlyList<ComponentSnapshot> Components,
    IReadOnlyList<ComputerSnapshot> Computers, IReadOnlyList<DigitalAsset> Assets, IReadOnlyList<AssetCopy> Copies,
    IReadOnlyList<LearnedKnowledge> Knowledge, IReadOnlyList<TelemetryLink> Links, IReadOnlyList<TelemetrySample> Samples,
    IReadOnlyList<ModelAdapter> Adapters, IReadOnlyList<ComputerJobSnapshot> Jobs, long NextSample,
    IReadOnlyList<string> Alerts);
