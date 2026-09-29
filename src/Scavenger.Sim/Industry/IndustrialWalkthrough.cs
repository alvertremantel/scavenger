using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

/// <summary>Optional deterministic CLI demonstration of player commands; grants no resources or capabilities.</summary>
public static class IndustrialWalkthrough
{
    public static readonly IReadOnlyList<string> Software = Array.AsReadOnly(new[] { "offline-os", "python-runtime", "gpu-driver", "quant-runtime",
        "model-4b", "model-tokenizer", "training-textbook", "telemetry-tools", "rule-control" });
    public static void QueueNext(Scheduler scheduler)
    {
        var world = scheduler.World; var computing = world.Module<ComputingModule>(); var inventory = world.Module<InventoryModule>();
        if (inventory.StoredKg("generator-tank") < 5)
        {
            var diesel = inventory.Batches.FirstOrDefault(b => b.MaterialId == "fuel.diesel" && b.StockpileId == "salvage" && inventory.AvailableKg(b.Id) >= 6);
            if (diesel is not null)
            {
                var refuel = new QueuePhysicalHaulCommand(new($"demo-refuel-{world.Clock.Tick}", diesel.Id, "refuel-route", 2, 6));
                if (refuel.CanExecute(world)) scheduler.Queue(refuel);
            }
        }
        IndustrialScenario.QueueRecoveryOrders(scheduler);
        if (!Step("demo-diagnosis", ComputerTaskKind.DiagnoseComponents, IndustrialScenario.StarterKit.Concat(["usb-archive", "sensor-a", "controller-a"]).ToArray())) return;
        if (!Step("demo-fan-repair", ComputerTaskKind.RepairComponent, ["fan-a"])) return;
        if (!Step("demo-assembly", ComputerTaskKind.Assemble, IndustrialScenario.StarterKit)) return;
        if (!computing.Computer("rig-1")!.Powered) scheduler.Queue(new SetComputerPoweredCommand("rig-1", true));
        foreach (string asset in Software) if (!Step($"demo-install-{asset}", ComputerTaskKind.InstallAsset, asset: asset)) return;
        if (!Step("demo-study", ComputerTaskKind.Study, asset: "training-textbook")) return;
        if (!Step("demo-interface", ComputerTaskKind.InstallTelemetry, ["sensor-a", "controller-a"], target: "dryer")) return;
        if (!Step("demo-rules", ComputerTaskKind.RuleMonitor)) return;
        if (world.Module<ProductionModule>().Find("demo-auto-dry") is null) scheduler.Queue(new QueueProductionCommand(IndustrialScenario.ProcessPlan("demo-auto-dry", "dry-hdpe", automatic: true)));
        if (computing.Samples.Count < 48) return;
        if (!Step("demo-labels", ComputerTaskKind.LabelTelemetry)) return;
        if (!Step("demo-train", ComputerTaskKind.TrainAdapter, asset: "model-4b")) return;
        if (!Step("demo-validation", ComputerTaskKind.ValidateAdapter, asset: "adapter/demo-train")) return;
        Step("demo-model-monitor", ComputerTaskKind.ModelMonitor, asset: "adapter/demo-train");

        bool Step(string id, ComputerTaskKind kind, IReadOnlyList<string>? parts = null, string? asset = null, string? target = null)
        {
            if (computing.Job(id) is { } job) return job.Status == ComputerTaskStatus.Completed || (ComputingModule.Profile(kind).Continuous && job.Status == ComputerTaskStatus.Running);
            var command = new QueueComputerTaskCommand(new(id, kind, "rig-1", ComputingModule.Profile(kind).Continuous ? null : 1, 2, parts, asset, target));
            if (command.CanExecute(world)) scheduler.Queue(command); return false;
        }
    }
}
