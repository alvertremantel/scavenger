using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Industry;

public sealed class ProductionModule : IPersistentModule
{
    public const string Persist = "scavenger.production";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, RecipeDefinition> _recipes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProductionJob> _jobs = new(StringComparer.Ordinal);
    public IReadOnlyList<RecipeDefinition> Recipes => _recipes.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    public IReadOnlyList<ProductionJob> Jobs => _jobs.Values.OrderBy(j => j.Plan.Id, StringComparer.Ordinal).ToList();
    public RecipeDefinition? Recipe(string id) => _recipes.GetValueOrDefault(id);
    public ProductionJob? Find(string id) => _jobs.GetValueOrDefault(id);
    public void AddRecipe(RecipeDefinition recipe) => _recipes.Add(recipe.Id, recipe);
    public string? Explain(World world, ProductionPlan plan)
    {
        if (Find(plan.Id) is not null) return "Production id is already used.";
        if (Recipe(plan.RecipeId) is not { } recipe) return "Unknown process recipe.";
        if (WorksiteLogistics.ExplainNew(world, plan.Id, plan.StationId, plan.CarrierId, plan.SourceStores) is { } reason) return reason;
        var compound = world.Module<CompoundModule>(); var station = compound.FindBuilding(plan.StationId)!;
        if (!compound.Definition(station.DefinitionId)!.Capabilities.Contains(recipe.Capability)) return "Station lacks the required processing capability.";
        var inventory = world.Module<InventoryModule>();
        if (inventory.FindStockpile(plan.ProductStoreId)?.Position is null || inventory.FindStockpile(plan.WasteStoreId)?.Position is null)
            return "Products and wastes need physical destination stores.";
        if (recipe.Inputs.Any(i => inventory.FindMaterial(i.MaterialId) is null) || recipe.Outputs.Any(o => inventory.FindMaterial(o.MaterialId) is null))
            return "Recipe refers to an unknown material.";
        if (plan.Automatic)
        { if (!recipe.Automatable || plan.OperatorId is not null) return "This process requires physical operation, or an automatic plan incorrectly assigns a person."; }
        else if (plan.OperatorId is not { } id || world.Module<WorkforceModule>().Find(id) is not { } worker || !worker.Skills.Meets(recipe.Qualification))
            return "A qualified process operator is required.";
        return null;
    }
    public ProductionJob Accept(World world, ProductionPlan plan)
    {
        if (Explain(world, plan) is { } reason) throw new InvalidOperationException(reason);
        var recipe = Recipe(plan.RecipeId)!;
        var frozen = plan with { SourceStores = Array.AsReadOnly(plan.SourceStores.ToArray()) };
        var job = new ProductionJob(frozen, WorksiteLogistics.Create(world, plan.Id, plan.StationId, plan.CarrierId,
            plan.SourceStores, recipe.Inputs.Sum(i => i.QuantityKg) + recipe.Outputs.Sum(o => o.QuantityKg)));
        for (int i = 0; i < recipe.Outputs.Count; i++)
        {
            var output = recipe.Outputs[i]; string destination = output.Waste ? plan.WasteStoreId : plan.ProductStoreId;
            world.Module<LogisticsModule>().AddRoute(new TransportRoute($"process-output-route/{plan.Id}/{i}", job.Site.LocalStoreId,
                destination, 1, 6, new("hauling", 20), usesWorldMovement: true));
        }
        _jobs.Add(plan.Id, job); return job;
    }
    internal void Prepare(World world, List<GameEvent> events)
    {
        foreach (var job in Jobs.Where(j => j.Active))
        {
            if (job.Status == ProductionStatus.Delivering) { DispatchProducts(world, job, events); continue; }
            if (job.Status != ProductionStatus.WaitingForInputs) continue;
            var recipe = Recipe(job.Plan.RecipeId)!;
            string? reason = WorksiteLogistics.DeliverInputs(world, job.Site, recipe.Inputs, events);
            if (!WorksiteLogistics.Ready(world, job.Site, recipe.Inputs)) { Block(world, job, reason, events); continue; }
            if (job.Plan.Automatic && !world.Module<ComputingModule>().CanControl(world, job.Plan.StationId)) reason = "Verified control computer, data link, sensor and actuator are required.";
            else if (job.Plan.OperatorId is { } worker) reason = WorksiteLogistics.ExplainLabor(world, worker, recipe.Qualification);
            else reason = null;
            if (reason is null && world.Module<EquipmentModule>().OwnerOf(job.Plan.StationId) is not null) reason = "Processing equipment is claimed by another job.";
            if (reason is not null) { Block(world, job, reason, events); continue; }
            WorksiteLogistics.ReserveInputs(world, job.Site, recipe.Inputs, job.Owner);
            var products = CreateProducts(world, job);
            var inventory = world.Module<InventoryModule>(); var acquired = new List<string>();
            bool capacity = true;
            foreach (var product in products)
                foreach (var (owner, store) in new[] { (product.LocalClaim, job.Site.LocalStoreId), (product.DestinationClaim, product.DestinationId) })
                {
                    if (!inventory.TryReserveCapacity(owner, store, product.Cargo.QuantityKg)) { capacity = false; break; }
                    acquired.Add(owner);
                }
            if (!capacity)
            {
                foreach (string owner in acquired) inventory.ReleaseReservations(owner);
                WorksiteLogistics.ReleaseInputs(world, job.Site);
                Block(world, job, "Insufficient promised product or waste receiving space.", events); continue;
            }
            if (job.Plan.OperatorId is { } operatorId && !world.Module<WorkforceModule>().TryReserve(operatorId, job.Owner)) throw new InvalidOperationException("Operator claim changed.");
            if (!world.Module<EquipmentModule>().TryReserve(job.Plan.StationId, job.Owner)) throw new InvalidOperationException("Equipment claim changed.");
            job.SetProducts(products); job.Status = ProductionStatus.Walking; Block(world, job, null, events);
        }
    }
    private IReadOnlyList<ProductShipment> CreateProducts(World world, ProductionJob job)
    {
        var inventory = world.Module<InventoryModule>(); var recipe = Recipe(job.Plan.RecipeId)!;
        var inputs = job.Site.InputOwners.Select(owner => inventory.StockReservedBy(owner)!).Select(c =>
            new MaterialCargo(inventory.FindBatch(c.BatchId)!.MaterialId, c.QuantityKg, inventory.FindBatch(c.BatchId)!.Quality)).ToArray();
        return recipe.Outputs.Select((o, i) =>
        {
            BatchQuality quality;
            if (o.Quality is not null) quality = o.Quality;
            else
            {
                var batches = inputs.Where(c => c.MaterialId == o.PreserveMaterialId).ToArray(); decimal mass = batches.Sum(c => c.QuantityKg);
                var contamination = batches.SelectMany(b => b.Quality.Contaminants.Keys).Distinct(StringComparer.Ordinal)
                    .ToDictionary(k => k, k => batches.Sum(b => b.Quality.Contaminants.GetValueOrDefault(k) * b.QuantityKg) / mass, StringComparer.Ordinal);
                quality = new BatchQuality(batches.Sum(b => b.Quality.Purity * b.QuantityKg) / mass, "reprocessed / untested", contamination);
            }
            return new ProductShipment(i, new MaterialCargo(o.MaterialId, o.QuantityKg, quality), o.Waste ? job.Plan.WasteStoreId : job.Plan.ProductStoreId,
                $"{job.Owner}/local-output/{i}", $"{job.Owner}/destination/{i}", $"process-output-route/{job.Plan.Id}/{i}");
        }).ToArray();
    }
    internal void Work(World world, List<GameEvent> events)
    {
        foreach (var job in Jobs.Where(j => j.Status is ProductionStatus.Walking or ProductionStatus.Processing))
        {
            var compound = world.Module<CompoundModule>(); var station = compound.FindBuilding(job.Plan.StationId)!; var recipe = Recipe(job.Plan.RecipeId)!;
            string? reason = null;
            if (world.Module<UtilitiesModule>().ServiceFor(station.Id) is not { Operational: true }) reason = world.Module<UtilitiesModule>().ServiceFor(station.Id)?.BlockedReason ?? "Equipment utilities unavailable.";
            if (job.Plan.Automatic && !world.Module<ComputingModule>().CanControl(world, station.Id)) reason = "Automatic control is unavailable.";
            if (job.Plan.OperatorId is { } id)
            {
                var worker = world.Module<WorkforceModule>().Find(id)!;
                if (!worker.Present || !worker.Skills.Meets(recipe.Qualification) || world.Module<WorkforceModule>().ReservationFor(id) != job.Owner) reason = "Operator is absent, unqualified, or missing its claim.";
                if (reason is null)
                {
                    var move = Navigation.StepTowards(compound, id, station.InteractionCell);
                    if (!move.Arrived) reason = move.BlockedReason ?? "Operator is walking to the station.";
                }
            }
            if (reason is not null) { Block(world, job, reason, events); continue; }
            job.Status = ProductionStatus.Processing; Block(world, job, null, events);
            int skill = job.Plan.OperatorId is { } operatorId ? world.Module<WorkforceModule>().Find(operatorId)!.Skills.Rating(recipe.Qualification.SkillId) : 50;
            job.EffortCompleted = Math.Min(recipe.Effort, job.EffortCompleted + .25m + skill / 100m);
            station.Condition = Math.Max(0, station.Condition - recipe.WearPerTick);
            if (job.EffortCompleted < recipe.Effort) continue;
            var receipt = Conversions.Complete(world.Module<InventoryModule>(), job.Site.InputOwners,
                job.Products.Select(p => new ConversionDelivery(p.LocalClaim, p.Cargo)).ToArray());
            job.Site.ClearInputs();
            for (int i = 0; i < receipt.Produced.Count; i++)
            {
                job.UpdateProduct(i, job.Products[i] with { BatchId = receipt.Produced[i].Id });
                if (!world.Module<InventoryModule>().TryReserveStock($"{job.Owner}/pending-product/{i}", receipt.Produced[i].Id, receipt.Produced[i].QuantityKg))
                    throw new InvalidOperationException("Committed product cannot be held for dispatch.");
            }
            ReleaseWork(world, job); job.Status = ProductionStatus.Delivering;
            events.Add(IndustryEvents.Event(world, IndustryEvents.Completed, $"{recipe.Name}: products and byproducts are ready at the station.", EventSeverity.Positive));
        }
    }
    private static void DispatchProducts(World world, ProductionJob job, List<GameEvent> events)
    {
        var inventory = world.Module<InventoryModule>(); var logistics = world.Module<LogisticsModule>();
        for (int i = 0; i < job.Products.Count; i++)
        {
            var product = job.Products[i]; if (product.HaulId is not null) continue;
            // The promise is handed to the haul in one serial operation; failed acceptance restores it.
            inventory.ReleaseReservations(product.DestinationClaim);
            string stockOwner = $"{job.Owner}/pending-product/{i}";
            inventory.ReleaseReservations(stockOwner);
            string id = $"process-output/{job.Plan.Id}/{i}";
            var command = new QueueHaulCommand(new(id, product.BatchId!, product.RouteId, job.Plan.CarrierId, product.Cargo.QuantityKg));
            if (!command.CanExecute(world))
            {
                if (!inventory.TryReserveCapacity(product.DestinationClaim, product.DestinationId, product.Cargo.QuantityKg)) throw new InvalidOperationException("Output capacity promise could not be restored.");
                if (!inventory.TryReserveStock(stockOwner, product.BatchId!, product.Cargo.QuantityKg)) throw new InvalidOperationException("Pending product claim could not be restored.");
                Block(world, job, "Waiting for the product carrier or an open output route.", events); return;
            }
            events.AddRange(command.Execute(world)); job.UpdateProduct(i, product with { HaulId = id });
            Block(world, job, "Products and wastes are travelling to storage.", events); return;
        }
        if (job.Products.All(p => p.HaulId is { } id && logistics.FindJob(id)?.Status == HaulStatus.Delivered))
        {
            job.Status = ProductionStatus.Completed; job.BlockedReason = null;
            events.Add(IndustryEvents.Event(world, IndustryEvents.Completed, $"Production '{job.Plan.Id}' delivered all products and wastes.", EventSeverity.Positive));
        }
    }
    internal static void ReleaseWork(World world, ProductionJob job)
    {
        if (job.Plan.OperatorId is { } id) world.Module<WorkforceModule>().Release(id, job.Owner);
        world.Module<EquipmentModule>().Release(job.Plan.StationId, job.Owner);
    }
    public IReadOnlyList<GameEvent> Cancel(World world, ProductionJob job)
    {
        if (!job.Active) throw new InvalidOperationException("Production is already finished.");
        var events = new List<GameEvent>(); WorksiteLogistics.ReleaseInputs(world, job.Site); WorksiteLogistics.CancelUnloaded(world, job.Site, events);
        foreach (var product in job.Products)
        {
            world.Module<InventoryModule>().ReleaseReservations(product.LocalClaim); world.Module<InventoryModule>().ReleaseReservations(product.DestinationClaim);
            world.Module<InventoryModule>().ReleaseReservations($"{job.Owner}/pending-product/{product.Index}");
            if (product.HaulId is { } id) { var cancel = new CancelHaulCommand(id); if (cancel.CanExecute(world)) events.AddRange(cancel.Execute(world)); }
        }
        ReleaseWork(world, job); job.Status = ProductionStatus.Cancelled; job.BlockedReason = null;
        events.Add(IndustryEvents.Event(world, IndustryEvents.Cancelled, $"Production '{job.Plan.Id}' cancelled; actual material remains where it is.")); return events;
    }
    private static void Block(World world, ProductionJob job, string? reason, List<GameEvent> events)
    {
        if (reason is not null && job.BlockedReason != reason)
            events.Add(IndustryEvents.Event(world, IndustryEvents.Blocked, $"Production '{job.Plan.Id}': {reason}",
                reason.Contains("walking", StringComparison.OrdinalIgnoreCase) || reason.Contains("travelling", StringComparison.OrdinalIgnoreCase) || reason.StartsWith("Carrier", StringComparison.Ordinal) ? EventSeverity.Info : EventSeverity.Warning));
        job.BlockedReason = reason;
    }
    public decimal PromisedKg(World world, string materialId, string destinationId)
    {
        decimal promised = 0;
        foreach (var job in Jobs.Where(j => j.Active))
        {
            var recipe = Recipe(job.Plan.RecipeId)!;
            if (job.Status == ProductionStatus.Delivering)
                promised += job.Products.Where(p => p.Cargo.MaterialId == materialId && p.DestinationId == destinationId && p.HaulId is null).Sum(p => p.Cargo.QuantityKg);
            else
                promised += recipe.Outputs.Where(o => o.MaterialId == materialId && (o.Waste ? job.Plan.WasteStoreId : job.Plan.ProductStoreId) == destinationId).Sum(o => o.QuantityKg);
        }
        return promised;
    }
    public object ToSnapshot() => new ProductionSnapshot(Recipes, Jobs.Select(j => j.Snapshot()).ToArray());
    public static ProductionModule FromSnapshot(ProductionSnapshot snapshot)
    {
        var module = new ProductionModule(); foreach (var recipe in snapshot.Recipes) module.AddRecipe(recipe);
        foreach (var s in snapshot.Jobs)
        {
            if (!Enum.IsDefined(s.Status) || s.EffortCompleted < 0) throw new ArgumentException("Invalid production state.", nameof(snapshot));
            var job = new ProductionJob(s.Plan with { SourceStores = Array.AsReadOnly(s.Plan.SourceStores.ToArray()) }, Worksite.Restore(s.Site))
            { Status = s.Status, EffortCompleted = s.EffortCompleted, BlockedReason = s.BlockedReason };
            job.SetProducts(s.Products); module._jobs.Add(s.Plan.Id, job);
        }
        return module;
    }
}
