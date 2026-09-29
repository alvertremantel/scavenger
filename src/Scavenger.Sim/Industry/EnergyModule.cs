using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public sealed record GeneratorPlan(string BuildingId, string FuelStoreId, string FuelMaterialId, decimal KgPerKwh);
public sealed record GeneratorSnapshot(GeneratorPlan Plan, bool Enabled, decimal ConsumedFuelKg);
public sealed class FuelGenerator
{
    internal FuelGenerator(GeneratorPlan plan) { Plan = plan; }
    public GeneratorPlan Plan { get; }
    public bool Enabled { get; internal set; } = true;
    public decimal ConsumedFuelKg { get; internal set; }
}
public sealed record BatteryPlan(string BuildingId, decimal CapacityKwh, decimal InitialKwh, decimal ChargeKw,
    decimal DischargeKw, decimal ChargeEfficiency = .9m, decimal DischargeEfficiency = .9m);
public sealed record BatterySnapshot(BatteryPlan Plan, decimal StoredKwh, bool Enabled);
public sealed class BatteryBank
{
    internal BatteryBank(BatteryPlan plan) { Plan = plan; StoredKwh = plan.InitialKwh; }
    public BatteryPlan Plan { get; }
    public decimal StoredKwh { get; internal set; }
    public bool Enabled { get; internal set; } = true;
}
public sealed record OperatingLoad(string BuildingId, decimal PowerKw, decimal WaterLph);
public sealed record EnergyFrame(long Tick, decimal RequestedKw, decimal ServedKw, decimal GeneratedKwh,
    decimal UsedKwh, decimal ChargedKwh, decimal DischargedKwh, decimal LossKwh, decimal FuelKg);
public sealed record SourceBudget(string BuildingId, decimal Kw);
public sealed record EnergySnapshot(IReadOnlyList<GeneratorSnapshot> Generators, IReadOnlyList<BatterySnapshot> Batteries,
    IReadOnlyList<OperatingLoad> IdleLoads, IReadOnlyList<OperatingLoad> Requests, IReadOnlyList<SourceBudget> Budgets,
    decimal GeneratedKwh, decimal UsedKwh, decimal LossKwh, EnergyFrame? LastFrame);

/// <summary>Finite fuel, network-local electrical storage, operational setpoints, and interval receipts.</summary>
public sealed class EnergyModule : IPersistentModule
{
    public const string Persist = "scavenger.energy";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, FuelGenerator> _generators = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BatteryBank> _batteries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OperatingLoad> _idle = new(StringComparer.Ordinal), _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, decimal> _budgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _fuelClaims = new(StringComparer.Ordinal);
    public IReadOnlyList<FuelGenerator> Generators => _generators.Values.OrderBy(g => g.Plan.BuildingId, StringComparer.Ordinal).ToList();
    public IReadOnlyList<BatteryBank> Batteries => _batteries.Values.OrderBy(b => b.Plan.BuildingId, StringComparer.Ordinal).ToList();
    public IReadOnlyList<OperatingLoad> Requests => _requests.Values.OrderBy(r => r.BuildingId, StringComparer.Ordinal).ToList();
    public decimal GeneratedKwh { get; internal set; }
    public decimal UsedKwh { get; internal set; }
    public decimal LossKwh { get; internal set; }
    public decimal SpentFuelKg => Generators.Sum(g => g.ConsumedFuelKg);
    public EnergyFrame? LastFrame { get; internal set; }
    public FuelGenerator? Generator(string id) => _generators.GetValueOrDefault(id);
    public BatteryBank? Battery(string id) => _batteries.GetValueOrDefault(id);
    public static decimal HoursPerTick(World world) => 24m / world.Clock.Time.TicksPerDay;
    public void AddGenerator(GeneratorPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.BuildingId) || string.IsNullOrWhiteSpace(plan.FuelStoreId)
            || string.IsNullOrWhiteSpace(plan.FuelMaterialId) || plan.KgPerKwh <= 0) throw new ArgumentException("Invalid generator fuel specification.", nameof(plan));
        _generators.Add(plan.BuildingId, new FuelGenerator(plan));
    }
    public void AddBattery(BatteryPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.BuildingId) || plan.CapacityKwh <= 0 || plan.InitialKwh < 0 || plan.InitialKwh > plan.CapacityKwh
            || plan.ChargeKw <= 0 || plan.DischargeKw <= 0 || plan.ChargeEfficiency is <= 0 or > 1 || plan.DischargeEfficiency is <= 0 or > 1)
            throw new ArgumentException("Invalid battery specification.", nameof(plan));
        _batteries.Add(plan.BuildingId, new BatteryBank(plan));
    }
    public void AddIdleLoad(OperatingLoad load)
    {
        if (string.IsNullOrWhiteSpace(load.BuildingId) || load.PowerKw < 0 || load.WaterLph < 0) throw new ArgumentException("Invalid idle load.", nameof(load));
        _idle.Add(load.BuildingId, load); _requests.Add(load.BuildingId, load);
    }
    public OperatingLoad Demand(World world, string id)
    {
        if (_requests.TryGetValue(id, out var request)) return request;
        var definition = world.Module<CompoundModule>().Definition(world.Module<CompoundModule>().FindBuilding(id)!.DefinitionId)!;
        return new OperatingLoad(id, definition.PowerDemandKw, definition.WaterDemandLph);
    }
    public decimal GeneratorBudget(string id) => _budgets.GetValueOrDefault(id);
    internal void Prepare(World world)
    {
        if (_fuelClaims.Count > 0) throw new InvalidOperationException("Previous energy interval still owns fuel claims.");
        _requests.Clear(); foreach (var load in _idle.Values) _requests[load.BuildingId] = load;
        foreach (var job in world.Module<ProductionModule>().Jobs.Where(j => j.Status is ProductionStatus.Walking or ProductionStatus.Processing))
        {
            var recipe = world.Module<ProductionModule>().Recipe(job.Plan.RecipeId)!;
            var prior = Demand(world, job.Plan.StationId);
            _requests[job.Plan.StationId] = prior with { PowerKw = prior.PowerKw + recipe.PowerKw, WaterLph = prior.WaterLph + recipe.WaterLph };
        }
        foreach (var computer in world.Module<ComputingModule>().Computers)
        {
            var prior = Demand(world, computer.BuildingId);
            _requests[computer.BuildingId] = prior with { PowerKw = prior.PowerKw + world.Module<ComputingModule>().RequestedPower(world, computer.Id) };
        }
        foreach (var job in world.Module<ComputingModule>().Jobs.Where(j => j.Working && !ComputingModule.Profile(j.Plan.Kind).Machine))
        {
            var prior = Demand(world, job.Site.StationId);
            _requests[job.Site.StationId] = prior with { PowerKw = prior.PowerKw + .05m };
        }
        _budgets.Clear(); var inventory = world.Module<InventoryModule>(); var compound = world.Module<CompoundModule>();
        foreach (var source in Generators)
        {
            var building = compound.FindBuilding(source.Plan.BuildingId)!; var definition = compound.Definition(building.DefinitionId)!;
            decimal available = inventory.Batches.Where(b => b.StockpileId == source.Plan.FuelStoreId && b.MaterialId == source.Plan.FuelMaterialId).Sum(b => inventory.AvailableKg(b.Id));
            decimal kg = source.Enabled ? Math.Min(available, definition.PowerSupplyKw * building.Condition * HoursPerTick(world) * source.Plan.KgPerKwh) : 0;
            _budgets[source.Plan.BuildingId] = decimal.Round(kg / (HoursPerTick(world) * source.Plan.KgPerKwh), 12, MidpointRounding.ToZero);
            if (kg <= 0) continue;
            var claims = new List<string>(); int index = 0; decimal remaining = kg;
            foreach (var batch in inventory.Batches.Where(b => b.StockpileId == source.Plan.FuelStoreId && b.MaterialId == source.Plan.FuelMaterialId))
            {
                decimal amount = Math.Min(remaining, inventory.AvailableKg(batch.Id)); if (amount <= 0) continue;
                string owner = $"scavenger.energy/{source.Plan.BuildingId}/{index++}";
                if (!inventory.TryReserveStock(owner, batch.Id, amount)) throw new InvalidOperationException("Fuel availability changed.");
                claims.Add(owner); remaining -= amount; if (remaining == 0) break;
            }
            _fuelClaims.Add(source.Plan.BuildingId, claims);
        }
    }
    public IReadOnlyDictionary<int, decimal> BatteryBudgets(World world, IReadOnlyDictionary<CellPosition, int> networks)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var battery in Batteries)
            if (battery.Enabled && world.Module<CompoundModule>().FindBuilding(battery.Plan.BuildingId) is { Condition: > 0 } building
                && networks.TryGetValue(building.Origin, out int network))
                result[network] = result.GetValueOrDefault(network) + decimal.Round(Math.Min(battery.Plan.DischargeKw * building.Condition,
                    battery.StoredKwh * battery.Plan.DischargeEfficiency / HoursPerTick(world)), 12, MidpointRounding.ToZero);
        return result;
    }
    internal void Account(World world)
    {
        var compound = world.Module<CompoundModule>(); var utilities = world.Module<UtilitiesModule>(); var inventory = world.Module<InventoryModule>();
        var networks = utilities.ConnectedComponents(compound, UtilityKind.Power); decimal hours = HoursPerTick(world);
        decimal generated = 0, used = utilities.Services.Sum(s => s.PowerAllocatedKw) * hours, charged = 0, discharged = 0, loss = 0, fuel = 0;
        var actualGeneration = Generators.ToDictionary(g => g.Plan.BuildingId, _ => 0m, StringComparer.Ordinal);
        foreach (int network in networks.Values.Distinct().Order())
        {
            bool networkDischarged = false;
            var generators = Generators.Where(g => networks.GetValueOrDefault(compound.FindBuilding(g.Plan.BuildingId)!.Origin, -1) == network).ToArray();
            var batteries = Batteries.Where(b => b.Enabled && compound.FindBuilding(b.Plan.BuildingId) is { Condition: > 0 } building
                && networks.GetValueOrDefault(building.Origin, -1) == network).ToArray();
            decimal needed = utilities.Services.Where(s => s.PowerNetwork == network).Sum(s => s.PowerAllocatedKw);
            foreach (var generator in generators)
            {
                decimal amount = Math.Min(needed, GeneratorBudget(generator.Plan.BuildingId));
                actualGeneration[generator.Plan.BuildingId] += amount; needed -= amount;
            }
            foreach (var battery in batteries)
            {
                decimal capacity = Math.Min(battery.Plan.DischargeKw * compound.FindBuilding(battery.Plan.BuildingId)!.Condition, battery.StoredKwh * battery.Plan.DischargeEfficiency / hours);
                decimal delivered = Math.Min(needed, capacity) * hours; if (delivered <= 0) continue;
                decimal taken = Math.Min(battery.StoredKwh, delivered / battery.Plan.DischargeEfficiency);
                battery.StoredKwh -= taken; loss += taken - delivered; discharged += delivered; needed -= delivered / hours; networkDischarged = true;
            }
            if (needed > .000000000001m) throw new InvalidOperationException("Allocated power exceeds available generation and battery discharge.");
            decimal spare = generators.Sum(g => GeneratorBudget(g.Plan.BuildingId) - actualGeneration[g.Plan.BuildingId]);
            foreach (var battery in batteries)
            {
                // No charge/discharge cycle within one interval: charge only when generation alone served the network.
                if (networkDischarged || spare <= 0) continue;
                decimal inputKw = Math.Min(spare, Math.Min(battery.Plan.ChargeKw * compound.FindBuilding(battery.Plan.BuildingId)!.Condition,
                    (battery.Plan.CapacityKwh - battery.StoredKwh) / (hours * battery.Plan.ChargeEfficiency)));
                decimal inputKwh = inputKw * hours, retained = inputKwh * battery.Plan.ChargeEfficiency;
                battery.StoredKwh += retained; charged += retained; loss += inputKwh - retained; spare -= inputKw;
                decimal remainingCharge = inputKw;
                foreach (var generator in generators)
                {
                    decimal amount = Math.Min(remainingCharge, GeneratorBudget(generator.Plan.BuildingId) - actualGeneration[generator.Plan.BuildingId]);
                    actualGeneration[generator.Plan.BuildingId] += amount; remainingCharge -= amount;
                }
            }
        }
        foreach (var source in Generators)
        {
            decimal sourceEnergy = actualGeneration[source.Plan.BuildingId] * hours;
            decimal remainingFuel = sourceEnergy * source.Plan.KgPerKwh;
            generated += sourceEnergy; fuel += remainingFuel; source.ConsumedFuelKg += remainingFuel;
            foreach (string owner in _fuelClaims.GetValueOrDefault(source.Plan.BuildingId) ?? [])
            {
                var claim = inventory.StockReservedBy(owner)!; decimal amount = Math.Min(remainingFuel, claim.QuantityKg);
                inventory.ReleaseReservations(owner);
                if (amount > 0)
                {
                    if (!inventory.TryReserveStock(owner, claim.BatchId, amount)) throw new InvalidOperationException("Fuel claim cannot be consumed.");
                    inventory.TakeReservedStock(owner); remainingFuel -= amount;
                }
            }
            if (remainingFuel > .000000000001m) throw new InvalidOperationException("Generation has no corresponding fuel claim.");
        }
        _fuelClaims.Clear(); GeneratedKwh += generated; UsedKwh += used; LossKwh += loss;
        LastFrame = new EnergyFrame(world.Clock.Tick, utilities.Services.Sum(s => s.PowerRequiredKw), utilities.Services.Sum(s => s.PowerAllocatedKw),
            generated, used, charged, discharged, loss, fuel);
    }
    public object ToSnapshot()
    {
        if (_fuelClaims.Count > 0) throw new InvalidOperationException("Save only between completed energy intervals.");
        return new EnergySnapshot(Generators.Select(g => new GeneratorSnapshot(g.Plan, g.Enabled, g.ConsumedFuelKg)).ToArray(),
            Batteries.Select(b => new BatterySnapshot(b.Plan, b.StoredKwh, b.Enabled)).ToArray(),
            _idle.Values.OrderBy(l => l.BuildingId, StringComparer.Ordinal).ToArray(), Requests,
            _budgets.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => new SourceBudget(b.Key, b.Value)).ToArray(), GeneratedKwh, UsedKwh, LossKwh, LastFrame);
    }
    public static EnergyModule FromSnapshot(EnergySnapshot snapshot)
    {
        var module = new EnergyModule();
        if (snapshot.GeneratedKwh < 0 || snapshot.UsedKwh < 0 || snapshot.LossKwh < 0) throw new ArgumentException("Negative energy ledger.", nameof(snapshot));
        foreach (var source in snapshot.Generators)
        {
            module.AddGenerator(source.Plan); if (source.ConsumedFuelKg < 0) throw new ArgumentException("Negative fuel use.", nameof(snapshot));
            module.Generator(source.Plan.BuildingId)!.Enabled = source.Enabled; module.Generator(source.Plan.BuildingId)!.ConsumedFuelKg = source.ConsumedFuelKg;
        }
        foreach (var battery in snapshot.Batteries)
        {
            module.AddBattery(battery.Plan);
            if (battery.StoredKwh < 0 || battery.StoredKwh > battery.Plan.CapacityKwh) throw new ArgumentException("Battery charge outside its capacity.", nameof(snapshot));
            module.Battery(battery.Plan.BuildingId)!.StoredKwh = battery.StoredKwh; module.Battery(battery.Plan.BuildingId)!.Enabled = battery.Enabled;
        }
        foreach (var load in snapshot.IdleLoads) module.AddIdleLoad(load);
        module._requests.Clear();
        foreach (var request in snapshot.Requests)
        { if (request.PowerKw < 0 || request.WaterLph < 0) throw new ArgumentException("Invalid workload demand.", nameof(snapshot)); module._requests.Add(request.BuildingId, request); }
        foreach (var budget in snapshot.Budgets)
        { if (budget.Kw < 0) throw new ArgumentException("Invalid generation budget.", nameof(snapshot)); module._budgets.Add(budget.BuildingId, budget.Kw); }
        module.GeneratedKwh = snapshot.GeneratedKwh; module.UsedKwh = snapshot.UsedKwh; module.LossKwh = snapshot.LossKwh; module.LastFrame = snapshot.LastFrame;
        return module;
    }
}
