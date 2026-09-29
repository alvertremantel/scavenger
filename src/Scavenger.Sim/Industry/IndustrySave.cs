using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public static class IndustrySave
{
    public static void RegisterAll(SaveRegistry registry)
    {
        registry.RegisterModule<EquipmentSnapshot>(EquipmentModule.Persist, (s, v) => { Version(v); return EquipmentModule.FromSnapshot(s); });
        registry.RegisterModule<ProductionSnapshot>(ProductionModule.Persist, (s, v) => { Version(v); return ProductionModule.FromSnapshot(s); });
        registry.RegisterModule<OrdersSnapshot>(OrdersModule.Persist, (s, v) => { Version(v); return OrdersModule.FromSnapshot(s); });
        registry.RegisterModule<EnergySnapshot>(EnergyModule.Persist, (s, v) => { Version(v); return EnergyModule.FromSnapshot(s); });
        registry.RegisterModule<ComputingSnapshot>(ComputingModule.Persist, (s, v) => { Version(v); return ComputingModule.FromSnapshot(s); });
        registry.RegisterCommand<ProductionPlan>(QueueProductionCommand.Persist, s => new QueueProductionCommand(s));
        registry.RegisterCommand<IdPayload>(CancelProductionCommand.Persist, s => new CancelProductionCommand(s.Id));
        registry.RegisterCommand<StockOrderPlan>(AddStockOrderCommand.Persist, s => new AddStockOrderCommand(s));
        registry.RegisterCommand<EnabledPayload>(SetStockOrderEnabledCommand.Persist, s => new SetStockOrderEnabledCommand(s.Id, s.Enabled));
        registry.RegisterCommand<ComputerTaskPlan>(QueueComputerTaskCommand.Persist, s => new QueueComputerTaskCommand(s));
        registry.RegisterCommand<IdPayload>(CancelComputerTaskCommand.Persist, s => new CancelComputerTaskCommand(s.Id));
        registry.RegisterCommand<EnabledPayload>(SetComputerPoweredCommand.Persist, s => new SetComputerPoweredCommand(s.Id, s.Enabled));
        registry.RegisterCommand<EnabledPayload>(SetGeneratorEnabledCommand.Persist, s => new SetGeneratorEnabledCommand(s.Id, s.Enabled));
        registry.RegisterCommand<EnabledPayload>(SetBatteryEnabledCommand.Persist, s => new SetBatteryEnabledCommand(s.Id, s.Enabled));
        registry.RegisterCommand<ComponentDamagePayload>(DamageComponentCommand.Persist, s => new DamageComponentCommand(s.Id, s.Condition, s.Fault));
    }
    private static void Version(int version) { if (version != 1) throw new NotSupportedException($"Unsupported industrial schema {version}."); }
    public static void Validate(World world)
    {
        var production = world.Module<ProductionModule>(); var computing = world.Module<ComputingModule>(); var energy = world.Module<EnergyModule>();
        var inventory = world.Module<InventoryModule>(); var logistics = world.Module<LogisticsModule>(); var compound = world.Module<CompoundModule>();
        var workforce = world.Module<WorkforceModule>(); var equipment = world.Module<EquipmentModule>();
        var labor = new Dictionary<string, (int Worker, string Station)>(StringComparer.Ordinal);
        var allowedStock = new HashSet<string>(StringComparer.Ordinal); var allowedCapacity = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in production.Recipes)
            if (recipe.Inputs.Any(i => inventory.FindMaterial(i.MaterialId) is null) || recipe.Outputs.Any(o => inventory.FindMaterial(o.MaterialId) is null)) throw new InvalidOperationException("Recipe has an unknown material.");
        foreach (var job in production.Jobs)
        {
            var recipe = production.Recipe(job.Plan.RecipeId) ?? throw new InvalidOperationException("Unknown recipe in a job.");
            ValidateSite(world, job.Site);
            if (job.Plan.Id != job.Site.Id || job.Plan.StationId != job.Site.StationId || job.Plan.CarrierId != job.Site.CarrierId
                || job.EffortCompleted > recipe.Effort || !job.Plan.SourceStores.SequenceEqual(job.Site.Sources)
                || inventory.FindStockpile(job.Plan.ProductStoreId) is null || inventory.FindStockpile(job.Plan.WasteStoreId) is null
                || !compound.Definition(compound.FindBuilding(job.Plan.StationId)!.DefinitionId)!.Capabilities.Contains(recipe.Capability)) throw new InvalidOperationException("Invalid production plan/site references.");
            if (job.Plan.Automatic ? !recipe.Automatable || job.Plan.OperatorId is not null
                : job.Plan.OperatorId is not { } operatorId || workforce.Find(operatorId) is not { } qualifiedOperator || !qualifiedOperator.Skills.Meets(recipe.Qualification))
                throw new InvalidOperationException("Invalid production operator/automation references.");
            bool working = job.Status is ProductionStatus.Walking or ProductionStatus.Processing;
            if (working)
            {
                ValidateInputs(world, job.Site, recipe.Inputs, job.Owner, allowedStock, recipe.AcceptsInputQuality);
                if (equipment.OwnerOf(job.Plan.StationId) != job.Owner) throw new InvalidOperationException("Production equipment claim is missing.");
                if (job.Plan.OperatorId is { } worker) labor.Add(job.Owner, (worker, job.Plan.StationId));
            }
            else if (job.Site.InputOwners.Count > 0) throw new InvalidOperationException("Inactive production retains inputs.");
            if (job.Products.Count > 0 && job.Products.Count != recipe.Outputs.Count) throw new InvalidOperationException("Production output specification is incomplete.");
            if ((working || job.Status is ProductionStatus.Delivering or ProductionStatus.Completed) && job.Products.Count == 0) throw new InvalidOperationException("Production is missing its output claims/receipts.");
            for (int i = 0; i < job.Products.Count; i++)
            {
                var product = job.Products[i]; var spec = recipe.Outputs[i];
                if (product.Index != i || product.Cargo.MaterialId != spec.MaterialId || product.Cargo.QuantityKg != spec.QuantityKg
                    || product.DestinationId != (spec.Waste ? job.Plan.WasteStoreId : job.Plan.ProductStoreId)
                    || product.LocalClaim != $"{job.Owner}/local-output/{i}" || product.DestinationClaim != $"{job.Owner}/destination/{i}") throw new InvalidOperationException("Output promise differs from its recipe.");
                if (working)
                {
                    Capacity(inventory, product.LocalClaim, job.Site.LocalStoreId, product.Cargo.QuantityKg, allowedCapacity);
                    if (product.BatchId is not null || product.HaulId is not null) throw new InvalidOperationException("Uncommitted process has output receipts.");
                }
                if (job.Active && product.HaulId is null) Capacity(inventory, product.DestinationClaim, product.DestinationId, product.Cargo.QuantityKg, allowedCapacity);
                if (job.Status == ProductionStatus.Delivering && product.HaulId is null)
                {
                    string owner = $"{job.Owner}/pending-product/{i}"; var stock = inventory.StockReservedBy(owner);
                    if (stock is null || stock.BatchId != product.BatchId || stock.QuantityKg != product.Cargo.QuantityKg
                        || inventory.FindBatch(stock.BatchId)?.StockpileId != job.Site.LocalStoreId || !allowedStock.Add(owner)) throw new InvalidOperationException("Committed product has no local dispatch claim.");
                }
                if (product.HaulId is { } haulId)
                {
                    var haul = logistics.FindJob(haulId);
                    if (haul is null || haul.Request.BatchId != product.BatchId || haul.Request.QuantityKg != product.Cargo.QuantityKg
                        || logistics.FindRoute(haul.Request.RouteId)?.DestinationId != product.DestinationId) throw new InvalidOperationException("Output shipment reference is inconsistent.");
                    if (job.Status == ProductionStatus.Completed && haul.Status != HaulStatus.Delivered) throw new InvalidOperationException("Completed production still has undelivered output.");
                }
            }
            if (job.Status is ProductionStatus.Delivering or ProductionStatus.Completed && job.EffortCompleted != recipe.Effort) throw new InvalidOperationException("Production finished without completing effort.");
        }
        foreach (var job in computing.Jobs)
        {
            var computer = computing.Computer(job.Plan.ComputerId) ?? throw new InvalidOperationException("Unknown task computer.");
            var parts = job.Plan.Components ?? []; var taskProfile = ComputingModule.Profile(job.Plan.Kind);
            if (parts.Distinct(StringComparer.Ordinal).Count() != parts.Count
                || (job.Plan.Kind is ComputerTaskKind.DiagnoseComponents or ComputerTaskKind.Assemble && parts.Count == 0)
                || (job.Plan.Kind is ComputerTaskKind.RepairComponent or ComputerTaskKind.RemoveComponent or ComputerTaskKind.ServiceComponent && parts.Count != 1)
                || (!taskProfile.Continuous && (job.Plan.TechnicianId is not { } id || workforce.Find(id) is not { } qualifiedTechnician
                    || !qualifiedTechnician.Skills.Meets(new(taskProfile.Machine ? "computing" : "electronics", 40))))
                || (job.Plan.Kind is ComputerTaskKind.InstallAsset or ComputerTaskKind.Study or ComputerTaskKind.TrainAdapter && computing.Asset(job.Plan.AssetId ?? "") is null)
                || (job.Plan.Kind is ComputerTaskKind.ValidateAdapter or ComputerTaskKind.ModelMonitor && computing.Adapter(job.Plan.AssetId ?? "") is null)
                || (job.Plan.Kind == ComputerTaskKind.InstallTelemetry && (compound.FindBuilding(job.Plan.TargetBuildingId ?? "") is null || parts.Count is < 1 or > 2)))
                throw new InvalidOperationException("Invalid computer task shape or required references.");
            if (job.Plan.Id != job.Site.Id || job.Plan.CarrierId != job.Site.CarrierId || job.Progress > ComputingModule.Profile(job.Plan.Kind).Effort
                || (job.Plan.Components ?? []).Any(id => computing.Component(id) is null)) throw new InvalidOperationException("Invalid computer task references/progress.");
            ValidateSite(world, job.Site);
            if (job.Working)
            {
                ValidateInputs(world, job.Site, computing.Requirements(job), job.Owner, allowedStock);
                var profile = ComputingModule.Profile(job.Plan.Kind);
                if (job.ResourceClaim != profile.Machine) throw new InvalidOperationException("Computer resource claim differs from its workload.");
                if (job.Plan.TechnicianId is { } technician && (profile.HandsOn || job.SetupSteps < 2)) labor.Add(job.Owner, (technician, job.Site.StationId));
                if (job.Plan.Kind == ComputerTaskKind.RepairComponent && computing.Component(job.Plan.Components![0])!.InstalledOwner is null)
                {
                    var part = computing.Component(job.Plan.Components[0])!;
                    decimal quantity = part.MassKg + computing.Requirements(job).Where(r => r.MaterialId != part.MaterialId).Sum(r => r.QuantityKg);
                    Capacity(inventory, $"{job.Owner}/repair-output", job.Site.LocalStoreId, quantity, allowedCapacity);
                }
                if (job.Plan.Kind == ComputerTaskKind.RemoveComponent) Capacity(inventory, $"{job.Owner}/removed-output", job.Site.LocalStoreId, computing.Component(job.Plan.Components![0])!.MassKg, allowedCapacity);
                if (job.Plan.Kind == ComputerTaskKind.ServiceComponent)
                {
                    Capacity(inventory, $"{job.Owner}/service-waste", job.Site.LocalStoreId, .05m, allowedCapacity);
                    if (computing.Component(job.Plan.Components![0])!.InstalledOwner is null)
                        Capacity(inventory, $"{job.Owner}/service-unit", job.Site.LocalStoreId, computing.Component(job.Plan.Components[0])!.MassKg, allowedCapacity);
                }
            }
            else if (job.ResourceClaim || job.Site.InputOwners.Count > 0) throw new InvalidOperationException("Inactive computer task retains resource claims.");
            if (job.Status == ComputerTaskStatus.Completed && job.Progress != taskProfile.Effort) throw new InvalidOperationException("Completed computer task has incomplete effort.");
            if (job.TrainingIds.Any(id => computing.Samples.All(s => s.Id != id || s.HeldOut || s.LabelledBy is null))) throw new InvalidOperationException("Training contains unavailable/unlabelled/held-out records.");
        }
        foreach (var component in computing.Components)
        {
            var definition = computing.Definition(component.DefinitionId);
            if (definition is null || component.MassKg < definition.MassKg || inventory.FindMaterial(component.MaterialId) is null) throw new InvalidOperationException("Unknown or underweight component.");
            var loose = inventory.Batches.Where(b => b.MaterialId == component.MaterialId).ToArray();
            var cargo = logistics.Jobs.Where(j => j.Cargo?.MaterialId == component.MaterialId).ToArray();
            int installed = component.InstalledOwner is null ? 0 : 1;
            if (loose.Length + cargo.Length + installed != 1 || loose.Any(b => b.QuantityKg != component.MassKg) || cargo.Any(j => j.Cargo!.QuantityKg != component.MassKg))
                throw new InvalidOperationException("Whole component identity/mass has duplicate, missing, or fractional ownership.");
            if (component.InstalledOwner is { } owner && !computing.Computers.Any(c => c.Id == owner && c.ComponentIds.Contains(component.Id))
                && !computing.Links.Any(l => owner == $"interface/{l.BuildingId}" && (l.SensorId == component.Id || l.ControllerId == component.Id))) throw new InvalidOperationException("Installed component has no matching owner.");
            if (component.ClaimedBy is { } claim && !computing.Jobs.Any(j => j.Working && j.Owner == claim
                && ((j.Plan.Components ?? []).Contains(component.Id) || (j.Plan.Kind == ComputerTaskKind.InstallAsset && computing.Asset(j.Plan.AssetId!)?.SourceMediaId == component.Id)))) throw new InvalidOperationException("Orphaned component task claim.");
        }
        foreach (var computer in computing.Computers)
        {
            if (compound.FindBuilding(computer.BuildingId) is null || computer.ComponentIds.Distinct(StringComparer.Ordinal).Count() != computer.ComponentIds.Count
                || computer.ComponentIds.Any(id => computing.Component(id)?.InstalledOwner != computer.Id)) throw new InvalidOperationException("Invalid machine component references.");
            var claims = computing.Jobs.Where(j => j.Active && j.ResourceClaim && j.Plan.ComputerId == computer.Id).ToArray();
            if (claims.Sum(j => ComputingModule.Profile(j.Plan.Kind).RamGiB) > computing.RamGiB(computer.Id)
                || claims.Sum(j => ComputingModule.Profile(j.Plan.Kind).VramGiB) > computing.VramGiB(computer.Id)
                || claims.Sum(j => ComputingModule.Profile(j.Plan.Kind).CpuUnits) > 4 || computing.UsedStorageGiB(computer.Id) + computing.PromisedStorageGiB(computer.Id) > computing.StorageGiB(computer.Id)) throw new InvalidOperationException("Computer resources are overbooked.");
        }
        foreach (var asset in computing.Assets)
            if (computing.Component(asset.SourceMediaId) is not { } source || computing.Definition(source.DefinitionId)?.Kind != ComponentKind.PortableMedia) throw new InvalidOperationException("Archive has no source media.");
        foreach (var media in computing.Components.Where(c => computing.Definition(c.DefinitionId)!.Kind == ComponentKind.PortableMedia))
            if (computing.Assets.Where(a => a.SourceMediaId == media.Id).Sum(a => a.SizeGiB) > computing.Definition(media.DefinitionId)!.StorageGiB) throw new InvalidOperationException("Recovered media contents exceed its storage.");
        if (computing.Copies.Select(c => (c.AssetId, c.StorageId)).Distinct().Count() != computing.Copies.Count) throw new InvalidOperationException("Duplicate installed asset copy.");
        foreach (var copy in computing.Copies)
            if (computing.Asset(copy.AssetId) is null || computing.Computer(copy.ComputerId) is null || computing.Component(copy.StorageId) is not { } drive
                || computing.Definition(drive.DefinitionId)!.Kind != ComponentKind.Storage || copy.Integrity is < 0 or > 1 || (copy.Verified && copy.Integrity < .9m)) throw new InvalidOperationException("Invalid archive copy or integrity.");
        foreach (var link in computing.Links)
            if (computing.Computer(link.ComputerId) is null || compound.FindBuilding(link.BuildingId) is null
                || computing.Component(link.SensorId)?.InstalledOwner != $"interface/{link.BuildingId}" || computing.Definition(computing.Component(link.SensorId)!.DefinitionId)?.Kind != ComponentKind.Sensor
                || (link.ControllerId is { } controller && (computing.Component(controller)?.InstalledOwner != $"interface/{link.BuildingId}" || computing.Definition(computing.Component(controller)!.DefinitionId)?.Kind != ComponentKind.Controller))) throw new InvalidOperationException("Invalid physical telemetry installation.");
        if (computing.Samples.Select(s => s.Id).Distinct().Count() != computing.Samples.Count || computing.Links.Select(l => l.BuildingId).Distinct().Count() != computing.Links.Count) throw new InvalidOperationException("Duplicate telemetry sample or interface id.");
        foreach (var knowledge in computing.Knowledge) if (workforce.Find(knowledge.WorkerId) is null || !computing.Assets.Any(a => a.Knowledge == knowledge.Capability)) throw new InvalidOperationException("Learned knowledge lacks a worker or recovered source.");
        foreach (var sample in computing.Samples)
            if (sample.Id < 1 || sample.Tick < 0 || sample.Tick > world.Clock.Tick || computing.Computer(sample.ComputerId) is null || computing.Component(sample.StorageId) is not { } sampleDrive || computing.Definition(sampleDrive.DefinitionId)?.Kind != ComponentKind.Storage
                || compound.FindBuilding(sample.BuildingId) is null || sample.ObservedCondition is < 0 or > 1 || sample.PowerKw < 0
                || (sample.LabelledBy is { } worker && workforce.Find(worker) is null)) throw new InvalidOperationException("Invalid dataset provenance.");
        foreach (var adapter in computing.Adapters)
            if (computing.Computer(adapter.ComputerId) is null || computing.Asset(adapter.ModelAssetId) is not { } model || !model.Capabilities.Contains("model-4b") || computing.Component(adapter.StorageId) is not { } adapterDrive || computing.Definition(adapterDrive.DefinitionId)?.Kind != ComponentKind.Storage || adapter.SizeGiB <= 0
                || adapter.TrainingIds.Count < 24 || adapter.TrainingIds.Distinct().Count() != adapter.TrainingIds.Count
                || adapter.TrainingIds.Any(id => computing.Samples.All(s => s.Id != id || s.HeldOut || s.LabelledBy is null))
                || adapter.ValidationIds.Distinct().Count() != adapter.ValidationIds.Count || adapter.ValidationIds.Any(id => adapter.TrainingIds.Contains(id) || computing.Samples.All(s => s.Id != id || !s.HeldOut || s.LabelledBy is null))
                || (adapter.ValidationScore is not null && (adapter.ValidationScore is < 0 or > 1 || adapter.ValidationIds.Count < 8))) throw new InvalidOperationException("Model adapter lacks independent training/validation provenance.");
        foreach (var order in world.Module<OrdersModule>().Orders)
            if (production.Recipe(order.Plan.Template.RecipeId) is not { } recipe || !recipe.Outputs.Any(o => !o.Waste && o.MaterialId == order.Plan.MaterialId)
                || inventory.FindStockpile(order.Plan.Template.ProductStoreId) is null) throw new InvalidOperationException("Invalid standing order.");
        foreach (var job in world.Module<ConstructionModule>().Jobs.Where(j => j.Status is ConstructionStatus.WalkingToSite or ConstructionStatus.Building && j.Request.RepairTargetId is not null))
            labor.Add(job.Owner, (job.Request.BuilderId, job.Request.RepairTargetId!));
        if (world.TryModule<Scavenging.ScavengingModule>(out var scavenging))
            foreach (var treatment in scavenging.Treatments.Where(t => t.Active && t.DoseClaim is not null))
                labor.Add(treatment.Owner, (treatment.Plan.MedicId, "clinic-station"));
        foreach (var (owner, claim) in labor)
            if (workforce.ReservationFor(claim.Worker) != owner || world.Module<ProjectBoardModule>().IsAssigned(claim.Worker)
                || equipment.OwnerOf(claim.Station) != owner) throw new InvalidOperationException("Industrial labor or access claim is inconsistent.");
        foreach (var claim in equipment.Claims)
            if (!labor.Any(l => l.Key == claim.Owner && l.Value.Station == claim.StationId)
                && !production.Jobs.Any(j => j.Plan.Automatic && j.Owner == claim.Owner && j.Plan.StationId == claim.StationId && j.Status is ProductionStatus.Walking or ProductionStatus.Processing)) throw new InvalidOperationException("Orphaned equipment claim.");
        foreach (var claim in workforce.Reservations.Where(c => c.Owner.StartsWith("scavenger.process/", StringComparison.Ordinal) || c.Owner.StartsWith("scavenger.computer/", StringComparison.Ordinal)))
            if (!labor.TryGetValue(claim.Owner, out var expected) || expected.Worker != claim.StaffId) throw new InvalidOperationException("Orphaned industry labor claim.");
        foreach (var claim in inventory.StockReservations.Where(c => IndustryOwner(c.Owner))) if (!allowedStock.Contains(claim.Owner)) throw new InvalidOperationException("Orphaned industrial stock claim.");
        foreach (var claim in inventory.CapacityReservations.Where(c => IndustryOwner(c.Owner))) if (!allowedCapacity.Contains(claim.Owner)) throw new InvalidOperationException("Orphaned industrial output promise.");
        foreach (var generator in energy.Generators)
            if (compound.FindBuilding(generator.Plan.BuildingId) is null || inventory.FindStockpile(generator.Plan.FuelStoreId) is null || inventory.FindMaterial(generator.Plan.FuelMaterialId) is null) throw new InvalidOperationException("Invalid fuel generator references.");
        foreach (var battery in energy.Batteries) if (compound.FindBuilding(battery.Plan.BuildingId) is null) throw new InvalidOperationException("Invalid battery references.");
        foreach (var request in energy.Requests) if (compound.FindBuilding(request.BuildingId) is null) throw new InvalidOperationException("Invalid electrical setpoint references.");
        decimal balance = energy.Batteries.Sum(b => b.Plan.InitialKwh) + energy.GeneratedKwh - energy.UsedKwh - energy.LossKwh - energy.Batteries.Sum(b => b.StoredKwh);
        if (Math.Abs(balance) > .000000000001m) throw new InvalidOperationException("Electrical energy ledger does not balance.");
        if (inventory.StockReservations.Any(c => c.Owner.StartsWith("scavenger.energy/", StringComparison.Ordinal))) throw new InvalidOperationException("Unfinished fuel claims between ticks.");
    }
    private static bool IndustryOwner(string owner) => owner.StartsWith("scavenger.process/", StringComparison.Ordinal) || owner.StartsWith("scavenger.computer/", StringComparison.Ordinal);
    private static void Capacity(InventoryModule inventory, string owner, string store, decimal quantity, HashSet<string> valid)
    { if (inventory.CapacityReservedBy(owner) is not { } claim || claim.StockpileId != store || claim.QuantityKg != quantity || !valid.Add(owner)) throw new InvalidOperationException("Output capacity promise is inconsistent."); }
    private static void ValidateInputs(World world, Worksite site, IReadOnlyList<MaterialRequirement> requirements, string owner, HashSet<string> valid, Func<MaterialBatch, bool>? accepts = null)
    {
        var inventory = world.Module<InventoryModule>(); var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (string input in site.InputOwners)
        {
            var claim = inventory.StockReservedBy(input); var batch = claim is null ? null : inventory.FindBatch(claim.BatchId);
            if (batch is null || batch.StockpileId != site.LocalStoreId || !input.StartsWith(owner + "/input/", StringComparison.Ordinal)
                || !requirements.Any(r => r.MaterialId == batch.MaterialId && batch.Quality.Purity >= r.MinimumPurity) || !(accepts?.Invoke(batch) ?? true) || !valid.Add(input)) throw new InvalidOperationException("Invalid local industrial input claim.");
            totals[batch.MaterialId] = totals.GetValueOrDefault(batch.MaterialId) + claim!.QuantityKg;
        }
        if (requirements.Any(r => totals.GetValueOrDefault(r.MaterialId) != r.QuantityKg) || totals.Keys.Any(k => !requirements.Any(r => r.MaterialId == k))) throw new InvalidOperationException("Industrial inputs are incomplete or excessive.");
    }
    private static void ValidateSite(World world, Worksite site)
    {
        var compound = world.Module<CompoundModule>(); var inventory = world.Module<InventoryModule>(); var logistics = world.Module<LogisticsModule>();
        if (compound.FindBuilding(site.StationId) is not { } station || inventory.FindStockpile(site.LocalStoreId)?.Position != station.InteractionCell
            || site.Sources.Any(s => inventory.FindStockpile(s)?.Position is null) || world.Module<WorkforceModule>().Find(site.CarrierId) is null) throw new InvalidOperationException("Invalid physical worksite.");
        foreach (string id in site.Deliveries)
            if (logistics.FindJob(id) is not { } job || job.Request.WorkerId != site.CarrierId || logistics.FindRoute(job.Request.RouteId)?.DestinationId != site.LocalStoreId) throw new InvalidOperationException("Worksite delivery ownership is inconsistent.");
    }
}
