using Haldane.Kernel.Simulation;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;

namespace Scavenger.Sim.Scavenging;

/// <summary>Recorded player orders for the two-destination early clinic progression.</summary>
public static class ScavengingWalkthrough
{
    public static void QueueNext(Scheduler scheduler)
    {
        var world = scheduler.World; var module = world.Module<ScavengingModule>();
        foreach (var e in module.Expeditions.Where(e => e.Status == ExpeditionStatus.Decision))
            scheduler.Queue(new RespondToExpeditionCommand(e.Plan.Id, ExpeditionResponse.Wait));
        if (module.Find("demo-pharmacy") is null)
        {
            scheduler.Queue(new DispatchExpeditionCommand(new("demo-pharmacy", "pharmacy", [2, 4],
                [new("stock-sealed", .1m), new("excipient", .12m), new("sterile-water", .8m)]))); return;
        }
        if (module.Find("demo-pharmacy")!.Active) return;
        if (module.Find("demo-hardware") is null)
        {
            scheduler.Queue(new DispatchExpeditionCommand(new("demo-hardware", "hardware", [2, 4], [new("filter-sorbent", .1m)]))); return;
        }
        if (module.Find("demo-hardware")!.Active) return;
        var production = world.Module<ProductionModule>();
        if (production.Find("demo-refine") is null)
        { scheduler.Queue(new QueueProductionCommand(ScavengingScenario.MedicinePlan("demo-refine", "refine-fever-stock"))); return; }
        if (production.Find("demo-refine")!.Status != ProductionStatus.Completed) return;
        if (production.Find("demo-formulate") is null)
        { scheduler.Queue(new QueueProductionCommand(ScavengingScenario.MedicinePlan("demo-formulate", "formulate-fever-dose"))); return; }
        if (production.Find("demo-formulate")!.Status != ProductionStatus.Completed) return;
        if (module.Treatments.All(t => t.Plan.Id != "demo-treatment")) scheduler.Queue(new QueueTreatmentCommand(new("demo-treatment", 1)));
    }
}
