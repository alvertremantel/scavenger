using Haldane.Kernel.Events;

namespace Scavenger.Sim.Physical;

public static class CompoundEventKinds
{
    public const string ConstructionPlanned = "scavenger.construction.planned";
    public const string ConstructionBlocked = "scavenger.construction.blocked";
    public const string ConstructionCompleted = "scavenger.construction.completed";
    public const string ConstructionCancelled = "scavenger.construction.cancelled";
    public const string LayoutChanged = "scavenger.layout.changed";
    public const string UtilityBlocked = "scavenger.utility.blocked";
    public const string UtilityRestored = "scavenger.utility.restored";
    public const string ConditionChanged = "scavenger.condition.changed";
    public static void RegisterAll(EventCatalogue catalogue)
    {
        catalogue.Register(ConstructionPlanned, "Construction planned");
        catalogue.Register(ConstructionBlocked, "Construction blocked", EventSeverity.Warning, true);
        catalogue.Register(ConstructionCompleted, "Construction complete", EventSeverity.Positive, true);
        catalogue.Register(ConstructionCancelled, "Construction cancelled");
        catalogue.Register(LayoutChanged, "Layout changed");
        catalogue.Register(UtilityBlocked, "Utility outage", EventSeverity.Warning, true);
        catalogue.Register(UtilityRestored, "Utility restored", EventSeverity.Positive);
        catalogue.Register(ConditionChanged, "Fixture condition changed");
    }
}
