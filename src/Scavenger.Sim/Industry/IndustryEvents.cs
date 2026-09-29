using Haldane.Kernel.Events;
using Haldane.Kernel;

namespace Scavenger.Sim.Industry;

public static class IndustryEvents
{
    public const string Planned = "scavenger.industry.planned";
    public const string Blocked = "scavenger.industry.blocked";
    public const string Completed = "scavenger.industry.completed";
    public const string Cancelled = "scavenger.industry.cancelled";
    public const string ComputerAlert = "scavenger.computer.alert";
    public const string PowerChanged = "scavenger.energy.changed";
    public static void RegisterAll(EventCatalogue catalogue)
    {
        catalogue.Register(Planned, "Industry order accepted");
        catalogue.Register(Blocked, "Industry task blocked", EventSeverity.Warning, true);
        catalogue.Register(Completed, "Industry task completed", EventSeverity.Positive, true);
        catalogue.Register(Cancelled, "Industry task cancelled");
        catalogue.Register(ComputerAlert, "Computer maintenance alert", EventSeverity.Warning, true);
        catalogue.Register(PowerChanged, "Power supply changed", EventSeverity.Warning, true);
    }
    internal static GameEvent Event(World world, string kind, string description, EventSeverity severity = EventSeverity.Info)
        => new(world.Clock.Tick, kind, severity, description);
}
