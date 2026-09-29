using Haldane.Kernel;
using Haldane.SupplyChain;

namespace Scavenger.Sim.Industry;

public sealed record OrderRow(string Id, string Material, decimal TargetKg, decimal CoveredKg, bool Enabled, string? Reason);
public sealed record MachineRow(string Id, bool Powered, string? HardwareReason, int RamGiB, int VramGiB,
    decimal StorageGiB, decimal UsedStorageGiB, decimal RequestedKw, IReadOnlyList<string> Components);
public sealed record IndustryReport(IReadOnlyList<OrderRow> Orders, ProductionSnapshot Production, ComputingSnapshot Computing,
    EnergySnapshot Energy, IReadOnlyList<MachineRow> Machines, decimal FuelAvailableKg, decimal InstalledComponentKg,
    int LabelledTrainingRecords, int LabelledHeldOutRecords)
{
    public static IndustryReport Capture(World world)
    {
        var orders = world.Module<OrdersModule>(); var computing = world.Module<ComputingModule>(); var energy = world.Module<EnergyModule>();
        return new(orders.Orders.Select(o => new OrderRow(o.Plan.Id, o.Plan.MaterialId, o.Plan.TargetKg, orders.CoveredKg(world, o), o.Enabled, o.BlockedReason)).ToArray(),
            (ProductionSnapshot)world.Module<ProductionModule>().ToSnapshot(), (ComputingSnapshot)computing.ToSnapshot(), (EnergySnapshot)energy.ToSnapshot(),
            computing.Computers.Select(c => new MachineRow(c.Id, c.Powered, computing.HardwareReason(c.Id), computing.RamGiB(c.Id), computing.VramGiB(c.Id),
                computing.StorageGiB(c.Id), computing.UsedStorageGiB(c.Id), computing.RequestedPower(world, c.Id), c.ComponentIds.ToArray())).ToArray(),
            world.Module<InventoryModule>().Batches.Where(b => b.MaterialId == "fuel.diesel").Sum(b => b.QuantityKg), computing.InstalledMassKg,
            computing.Samples.Count(s => !s.HeldOut && s.LabelledBy is not null), computing.Samples.Count(s => s.HeldOut && s.LabelledBy is not null));
    }
}
