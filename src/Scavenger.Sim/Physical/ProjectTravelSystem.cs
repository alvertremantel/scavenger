using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Simulation;

namespace Scavenger.Sim.Physical;

public sealed class ProjectTravelSystem : ISystem
{
    public string Name => "Workstation access";
    public IReadOnlyList<Type> Dependencies => [typeof(UtilitySystem), typeof(ConstructionSystem)];
    public IReadOnlyList<GameEvent> Tick(World world, int ticks)
    {
        var compound = world.Module<CompoundModule>();
        var workforce = world.Module<WorkforceModule>();
        foreach (var project in world.Module<ProjectBoardModule>().Projects.Where(p => !p.Completed).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var binding = compound.BindingFor(project.Id);
            var station = binding is null ? null : compound.FindBuilding(binding.BuildingId);
            if (station is null) continue;
            foreach (int id in project.AssignedStaffIds.Order())
                if (workforce.Find(id) is { Present: true } && workforce.ReservationFor(id) is null)
                    Navigation.StepTowards(compound, id, station.InteractionCell);
        }
        return [];
    }
}

public sealed class CompoundWorkContext : IWorkContext
{
    public IReadOnlyList<Type> Dependencies => [typeof(ProjectTravelSystem), typeof(UtilitySystem)];
    public bool CanContribute(World world, Project project, StaffMember worker) => Explain(world, project, worker) is null;

    public static string? Explain(World world, Project project, StaffMember worker)
    {
        var compound = world.Module<CompoundModule>();
        if (!worker.Present) return "Worker is absent.";
        if (world.Module<WorkforceModule>().ReservationFor(worker.Id) is not null) return "Worker is reserved by another task.";
        if (project.RequiredSkill is { } skill && !worker.Skills.Meets(skill)) return "Worker lacks the required specialty.";
        var binding = compound.BindingFor(project.Id);
        var station = binding is null ? null : compound.FindBuilding(binding.BuildingId);
        if (station is null) return "Project has no physical workstation.";
        var definition = compound.Definition(station.DefinitionId)!;
        if (!definition.Capabilities.Contains(binding!.Capability)) return "Station lacks this work capability.";
        if (world.TryModule<Industry.EquipmentModule>(out var equipment) && equipment.OwnerOf(station.Id) is not null) return "Station is reserved by another task.";
        if (world.Module<UtilitiesModule>().ServiceFor(station.Id) is not { Operational: true } service)
            return world.Module<UtilitiesModule>().ServiceFor(station.Id)?.BlockedReason ?? "Station utility service has not been assessed.";
        if (compound.PositionOf(worker.Id) is not { } position) return "Worker has no physical position.";
        if (position != station.InteractionCell)
            return Navigation.FindPath(compound, position, station.InteractionCell).Count == 0
                ? "Workstation is unreachable." : "Worker is walking to the workstation.";
        return null;
    }
}
