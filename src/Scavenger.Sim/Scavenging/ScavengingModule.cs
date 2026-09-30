using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Events;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Scavenging;

/// <summary>Regional finite stock, exclusive expeditions, recovered cargo and fictional exposure/clinic care.</summary>
public sealed class ScavengingModule : IPersistentModule
{
    public static CellPosition DeparturePoint { get; } = new(0, 4);
    public const string Persist = "scavenger.scavenging";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, ScavengeLocation> _locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expedition> _expeditions = new(StringComparer.Ordinal);
    private readonly Dictionary<int, WorkerHealth> _health = new();
    private readonly Dictionary<string, ClinicTreatment> _treatments = new(StringComparer.Ordinal);
    public IReadOnlyList<ScavengeLocation> Locations => _locations.Values.OrderBy(l => l.Definition.Id, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<Expedition> Expeditions => _expeditions.Values.OrderBy(e => e.Plan.Id, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<WorkerHealth> Health => _health.Values.OrderBy(h => h.WorkerId).ToArray();
    public IReadOnlyList<ClinicTreatment> Treatments => _treatments.Values.OrderBy(t => t.Plan.Id, StringComparer.Ordinal).ToArray();
    public decimal SpentSuppliesKg { get; private set; }
    public decimal RegionalKg => Locations.Sum(l => l.Definition.Salvage.Sum(s => l.Remaining(s.Id)));
    public decimal RivalRemovedKg => Locations.Sum(l => l.RemovedByRivals.Values.Sum());
    public decimal CargoKg => Expeditions.Sum(e => e.Cargo.Sum(c => c.QuantityKg));
    public ScavengeLocation? Location(string id) => _locations.GetValueOrDefault(id);
    public Expedition? Find(string id) => _expeditions.GetValueOrDefault(id);
    public WorkerHealth? HealthOf(int id) => _health.GetValueOrDefault(id);
    public bool IsAway(int id) => Expeditions.Any(e => e.Active && e.Departed && e.Status != ExpeditionStatus.Unloading && e.Plan.Team.Contains(id));
    public void AddLocation(LocationDefinition definition)
    {
        CheckDefinition(definition);
        _locations.Add(definition.Id, new ScavengeLocation(definition));
    }
    public void AddResident(int id, bool sick = false) => _health.Add(id, new WorkerHealth(id, sick ? 1 : 0, sick));
    internal static void CheckDefinition(LocationDefinition d)
    {
        if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Description)
            || d.TravelTicks is < 1 or > 1_000_000 || d.InitialIntegrity is < 0 or > 1 || d.DecayPerDay is < 0 or > 1
            || d.RivalPressure is < 0 or > 1 || d.RivalKgPerDay < 0 || d.ExposurePerTick is < 0 or > 1
            || d.Salvage.Count == 0 || d.Salvage.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != d.Salvage.Count)
            throw new ArgumentException("Invalid regional location definition.");
        foreach (var s in d.Salvage)
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Object) || string.IsNullOrWhiteSpace(s.MaterialId)
                || s.InitialKg <= 0 || s.EffortPerKg <= 0 || s.MinimumIntegrity is < 0 or > 1 || s.Quality is null || s.Qualification is null
                || (Skills.SkillCatalogue.IsBook(s.MaterialId) && (Skills.SkillCatalogue.Book(s.MaterialId) is null || s.InitialKg != Skills.SkillBook.MassKg || s.ComponentId is not null))
                || (s.ComponentId is null) != (s.ComponentModel is null) || !Enum.IsDefined(s.Fault) || s.ComponentCondition is < 0 or > 1
                || (s.Assets ?? []).Any(a => a.SourceMediaId != s.ComponentId || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name)
                    || a.SizeGiB <= 0 || a.Integrity is < 0 or > 1 || a.Capabilities.Count == 0 || a.Capabilities.Concat(a.Dependencies).Any(string.IsNullOrWhiteSpace)))
                throw new ArgumentException("Invalid object salvage definition.");
    }
    private static int Skill(World world, Expedition e, string skill) => e.Plan.Team.Max(id => world.Module<WorkforceModule>().Find(id)!.Skills.Rating(skill));
    public decimal AccessIntegrity(World world, ExpeditionPlan plan, SalvageDefinition entry)
    {
        decimal mitigation = plan.Approach == ExpeditionApproach.Careful
            ? plan.Team.Max(id => world.Module<WorkforceModule>().Find(id)!.Skills.Rating("construction")) / 500m : 0;
        return Math.Max(0, entry.MinimumIntegrity - mitigation);
    }
    private IReadOnlyList<MaterialRequirement> Kit(ScavengeLocation location, ExpeditionPlan plan)
    {
        var kit = new List<MaterialRequirement>();
        if (plan.Manifest.Any(m => location.Definition.Salvage.Single(s => s.Id == m.SalvageId).NeedsPrybar)) kit.Add(new("tool.prybar", 1));
        if (plan.UseProtection) kit.Add(new("supply.protection", .02m * plan.Team.Count, .95m));
        return kit;
    }
    private static bool EligibleKit(MaterialBatch batch, MaterialRequirement r, string store)
        => batch.StockpileId == store && batch.MaterialId == r.MaterialId && batch.Quality.Purity >= r.MinimumPurity;
    public string? Explain(World world, ExpeditionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Id) || Find(plan.Id) is not null) return "Choose an unused expedition id.";
        var location = Location(plan.LocationId); if (location is null) return "Choose a known regional destination.";
        if (!Enum.IsDefined(plan.Approach) || plan.Team.Count is < 1 or > 4 || plan.Team.Distinct().Count() != plan.Team.Count)
            return "Choose one to four distinct workers and a valid approach.";
        foreach (int id in plan.Team)
        {
            if (HealthOf(id) is not { Sick: false }) return "A team member is ill or has no regional health record.";
            if (WorksiteLogistics.ExplainLabor(world, id, new SkillRequirement("hauling", 20)) is { } reason) return reason;
        }
        if (plan.Manifest.Count == 0 || plan.Manifest.Count > location.Definition.Salvage.Count
            || plan.Manifest.Select(m => m.SalvageId).Distinct(StringComparer.Ordinal).Count() != plan.Manifest.Count)
            return "Choose a distinct, nonempty recovery manifest.";
        foreach (var request in plan.Manifest)
        {
            var entry = location.Definition.Salvage.FirstOrDefault(s => s.Id == request.SalvageId);
            if (entry is null || request.QuantityKg <= 0 || request.QuantityKg > location.Remaining(request.SalvageId)) return "Requested salvage is unknown, depleted, or smaller than the requested quantity.";
            if ((entry.ComponentId is not null || Skills.SkillCatalogue.IsBook(entry.MaterialId)) && request.QuantityKg != entry.InitialKg) return "Computer units and educational volumes must be recovered whole.";
            if (!plan.Team.Any(id => world.Module<WorkforceModule>().Find(id)!.Skills.Meets(entry.Qualification)))
                return $"{entry.Object} needs {entry.Qualification.SkillId} {entry.Qualification.MinimumRating}.";
            if (location.Integrity < AccessIntegrity(world, plan, entry)) return $"{entry.Object} is structurally inaccessible to this team/approach.";
        }
        if (plan.Manifest.Sum(m => m.QuantityKg) + Kit(location, plan).Where(r => r.MaterialId == "tool.prybar").Sum(r => r.QuantityKg) > plan.Team.Count * 12m) return "Each worker can bring back at most 12 kg, including reusable tools; reduce the manifest or enlarge the team.";
        var inventory = world.Module<InventoryModule>(); var compound = world.Module<CompoundModule>();
        if (inventory.FindStockpile(plan.ReceivingStoreId)?.Position is not { } receiving || !compound.IsWalkable(receiving)) return "Choose a walkable compound receiving store.";
        if (Navigation.FindPath(compound, receiving, DeparturePoint).Count == 0) return "There is no walkable route from the receiving point to the regional exit.";
        if (plan.Team.Any(id => Navigation.FindPath(compound, compound.PositionOf(id)!, receiving).Count == 0)) return "The team cannot walk to the receiving/departure point.";
        if (inventory.FreeCapacityKg(plan.ReceivingStoreId) < plan.Manifest.Sum(m => m.QuantityKg)) return "The receiving store has insufficient unclaimed capacity.";
        foreach (var requirement in Kit(location, plan))
            if (inventory.Batches.Where(b => EligibleKit(b, requirement, plan.ReceivingStoreId)).Sum(b => inventory.AvailableKg(b.Id)) < requirement.QuantityKg)
                return $"Bring {requirement.QuantityKg} kg {requirement.MaterialId} to the receiving store before dispatch.";
        return null;
    }
    public Expedition Dispatch(World world, ExpeditionPlan plan)
    {
        if (Explain(world, plan) is { } rejection) throw new InvalidOperationException(rejection);
        var e = new Expedition(plan); var workforce = world.Module<WorkforceModule>(); var inventory = world.Module<InventoryModule>();
        // Preflight is read-only; serial command execution makes this transaction indivisible.
        foreach (int id in e.Plan.Team) if (!workforce.TryReserve(id, e.Owner)) throw new InvalidOperationException("Labor changed during dispatch.");
        for (int i = 0; i < e.Plan.Manifest.Count; i++)
            if (!inventory.TryReserveCapacity(e.SpaceOwner(i), plan.ReceivingStoreId, e.Plan.Manifest[i].QuantityKg)) throw new InvalidOperationException("Space changed during dispatch.");
        foreach (var requirement in Kit(Location(plan.LocationId)!, plan))
        {
            decimal left = requirement.QuantityKg;
            foreach (var batch in inventory.Batches.Where(b => EligibleKit(b, requirement, plan.ReceivingStoreId)))
            {
                decimal take = Math.Min(left, inventory.AvailableKg(batch.Id)); if (take == 0) continue;
                string owner = $"{e.Owner}/kit/{e.KitClaims.Count}";
                if (!inventory.TryReserveStock(owner, batch.Id, take)) throw new InvalidOperationException("Kit changed during dispatch.");
                e.KitClaims.Add(owner); left -= take; if (left == 0) break;
            }
        }
        _expeditions.Add(plan.Id, e); return e;
    }
    public IReadOnlyList<GameEvent> Recall(World world, Expedition e)
    {
        if (!e.Active) throw new InvalidOperationException("Expedition is already home.");
        e.Recalled = true; e.Decision = null; e.DelayTicks = 0; e.Reason = "Recalled; returning with recovered cargo.";
        ReleaseUnusedSpace(world, e);
        if (!e.Departed) Finish(world, e);
        else if (e.Status is not (ExpeditionStatus.Returning or ExpeditionStatus.Unloading))
        {
            int distance = e.Status == ExpeditionStatus.Outbound ? e.ElapsedTravel : TravelTicks(Location(e.Plan.LocationId)!, e);
            e.ElapsedTravel = Math.Max(0, TravelTicks(Location(e.Plan.LocationId)!, e) - distance);
            e.Status = ExpeditionStatus.Returning;
        }
        return [ScavengingEvents.Event(world, ScavengingEvents.Changed, $"Recall {e.Plan.Id}; recovered cargo stays with the team.")];
    }
    public IReadOnlyList<GameEvent> Respond(World world, Expedition e, ExpeditionResponse response)
    {
        if (e.Status != ExpeditionStatus.Decision || !Enum.IsDefined(response)) throw new InvalidOperationException("No valid pending decision.");
        if (response == ExpeditionResponse.Retreat) return Recall(world, e);
        e.DelayTicks = e.Decision == ExpeditionDecision.Structure ? Math.Max(2, 6 - Skill(world, e, "navigation") / 25) : 4;
        e.Reason = e.Decision == ExpeditionDecision.Structure ? "Assessing and finding a safe approach." : "Waiting for the rival crew to leave; stocks may change.";
        e.Status = ExpeditionStatus.Searching; e.Decision = null;
        return [ScavengingEvents.Event(world, ScavengingEvents.Changed, $"{e.Plan.Id}: {e.Reason}")];
    }
    public int TravelTicksFor(Expedition expedition) => TravelTicks(Location(expedition.Plan.LocationId)!, expedition);
    private static int TravelTicks(ScavengeLocation l, Expedition e) => e.Plan.Approach == ExpeditionApproach.Careful ? l.Definition.TravelTicks : Math.Max(1, l.Definition.TravelTicks * 3 / 4);
    public IReadOnlyList<GameEvent> Tick(World world)
    {
        var events = new List<GameEvent>();
        if (world.Clock.Tick % world.Clock.Time.TicksPerDay == 0) ChangeRegion(world);
        foreach (var e in Expeditions.Where(e => e.Active)) TickExpedition(world, e, events);
        foreach (var treatment in Treatments.Where(t => t.Active)) TickTreatment(world, treatment, events);
        return events;
    }
    private void ChangeRegion(World world)
    {
        foreach (var location in Locations)
        {
            location.Integrity = Math.Max(0, location.Integrity - location.Definition.DecayPerDay);
            decimal budget = location.Definition.RivalKgPerDay;
            // Rival priorities are authored object order, not dictionary order. No magical respawn.
            foreach (var entry in location.Definition.Salvage)
            {
                if (budget <= 0) break;
                if (location.Integrity < entry.MinimumIntegrity) continue;
                decimal take = Math.Min(budget, location.Remaining(entry.Id));
                if ((entry.ComponentId is not null || Skills.SkillCatalogue.IsBook(entry.MaterialId)) && take < entry.InitialKg) continue;
                if (take == 0) continue;
                location.Remove(entry.Id, take); location.RemovedByRivals[entry.Id] = location.RemovedByRivals.GetValueOrDefault(entry.Id) + take;
                budget -= take;
            }
        }
    }
    private void Expose(World world, Expedition e, ScavengeLocation location)
    {
        decimal rate = location.Definition.ExposurePerTick * (e.Plan.UseProtection ? .2m : 1) * (e.Plan.Approach == ExpeditionApproach.Quick ? 1.5m : 1);
        foreach (int id in e.Plan.Team)
        {
            int safety = world.Module<WorkforceModule>().Find(id)!.Skills.Rating("survival");
            _health[id] = _health[id] with { Exposure = _health[id].Exposure + rate * (1 - safety / 400m) };
        }
    }
    private void TickExpedition(World world, Expedition e, List<GameEvent> events)
    {
        var location = Location(e.Plan.LocationId)!; var compound = world.Module<CompoundModule>(); var inventory = world.Module<InventoryModule>();
        var point = inventory.FindStockpile(e.Plan.ReceivingStoreId)!.Position!;
        switch (e.Status)
        {
            case ExpeditionStatus.Gathering:
                bool gathered = true;
                foreach (int id in e.Plan.Team)
                {
                    var move = Navigation.StepTowards(compound, id, point); gathered &= move.Arrived;
                    e.Reason = move.BlockedReason;
                }
                if (!gathered) { e.Reason ??= "Team is walking to the departure point."; return; }
                e.Status = ExpeditionStatus.LeavingCompound; e.Reason = "Team is walking to the regional exit.";
                break;
            case ExpeditionStatus.LeavingCompound:
                bool atExit = true;
                foreach (int id in e.Plan.Team)
                {
                    var move = Navigation.StepTowards(compound, id, DeparturePoint); atExit &= move.Arrived;
                    e.Reason = move.BlockedReason;
                }
                if (!atExit) { e.Reason ??= "Team is walking to the regional exit."; return; }
                foreach (string owner in e.KitClaims.ToArray())
                    if (inventory.FindBatch(inventory.StockReservedBy(owner)!.BatchId)!.MaterialId == "supply.protection")
                    { SpentSuppliesKg += inventory.TakeReservedStock(owner).QuantityKg; e.KitClaims.Remove(owner); }
                e.Departed = true; e.Status = ExpeditionStatus.Outbound; e.Reason = "Travelling to the site.";
                events.Add(ScavengingEvents.Event(world, ScavengingEvents.Changed, $"{e.Plan.Id}: team departed for {location.Definition.Name}."));
                break;
            case ExpeditionStatus.Outbound:
                foreach (int id in e.Plan.Team) Skills.SkillsModule.Credit(world, id, "navigation", .25m);
                if (++e.ElapsedTravel < TravelTicks(location, e)) return;
                e.ElapsedTravel = 0; e.Status = ExpeditionStatus.Searching; e.Reason = null;
                var rng = world.Rng.Stream("scavenging.incidents");
                decimal structureChance = (1 - location.Integrity) * (e.Plan.Approach == ExpeditionApproach.Careful ? .5m : 1);
                if (rng.Chance((double)structureChance)) e.Decision = ExpeditionDecision.Structure;
                else if (rng.Chance((double)location.Definition.RivalPressure)) e.Decision = ExpeditionDecision.Rivals;
                if (e.Decision is not null)
                {
                    e.Status = ExpeditionStatus.Decision;
                    e.Reason = e.Decision == ExpeditionDecision.Structure ? "Unsafe fallen structure: assess a route or retreat." : "Another scavenger crew is here: wait or retreat.";
                    events.Add(ScavengingEvents.Event(world, ScavengingEvents.Decision, $"{e.Plan.Id}: {e.Reason}", EventSeverity.Warning));
                }
                break;
            case ExpeditionStatus.Decision:
                Expose(world, e, location); // The team still occupies the contaminated site while awaiting instructions.
                break;
            case ExpeditionStatus.Searching:
                Expose(world, e, location);
                if (e.DelayTicks > 0) { e.DelayTicks--; return; }
                if (e.SearchIndex >= e.Plan.Manifest.Count) { ReleaseUnusedSpace(world, e); StartReturn(e); return; }
                var request = e.Plan.Manifest[e.SearchIndex]; var entry = location.Definition.Salvage.Single(s => s.Id == request.SalvageId);
                if (location.Integrity < AccessIntegrity(world, e.Plan, entry) || location.Remaining(entry.Id) == 0)
                {
                    e.Reason = $"Skipped {entry.Object}: depleted or inaccessible.";
                    events.Add(ScavengingEvents.Event(world, ScavengingEvents.Changed, $"{e.Plan.Id}: {e.Reason}"));
                    inventory.ReleaseReservations(e.SpaceOwner(e.SearchIndex)); e.SearchIndex++; e.SearchEffort = 0; return;
                }
                decimal take = Math.Min(request.QuantityKg, location.Remaining(entry.Id));
                decimal effort = take * entry.EffortPerKg * (e.Plan.Approach == ExpeditionApproach.Careful ? 1.25m : 1);
                string? secondary = FieldSpecialty(entry.MaterialId);
                decimal rate = Math.Max(.1m, Skill(world, e, entry.Qualification.SkillId) / 100m)
                    * (1 + Skill(world, e, "scavenging") / 500m + (secondary is null ? 0 : Skill(world, e, secondary) / 500m));
                decimal contribution = Math.Min(rate, Math.Max(0, effort - e.SearchEffort));
                e.SearchEffort += contribution;
                int specialist = e.Plan.Team.OrderByDescending(id => world.Module<WorkforceModule>().Find(id)!.Skills.Rating(entry.Qualification.SkillId)).ThenBy(id => id).First();
                Skills.SkillsModule.Credit(world, specialist, entry.Qualification.SkillId, contribution);
                foreach (int id in e.Plan.Team)
                {
                    if (entry.Qualification.SkillId != "scavenging") Skills.SkillsModule.Credit(world, id, "scavenging", contribution / e.Plan.Team.Count / 2);
                    Skills.SkillsModule.Credit(world, id, "survival", .1m);
                    if (secondary is not null && secondary != entry.Qualification.SkillId) Skills.SkillsModule.Credit(world, id, secondary, contribution / e.Plan.Team.Count / 2);
                }
                e.Reason = $"Recovering {entry.Object}.";
                if (e.SearchEffort < effort) return;
                location.Remove(entry.Id, take);
                // Shrink the receiving promise to actual cargo before ownership changes.
                inventory.ReleaseReservations(e.SpaceOwner(e.SearchIndex));
                if (!inventory.TryReserveCapacity(e.SpaceOwner(e.SearchIndex), e.Plan.ReceivingStoreId, take)) throw new InvalidOperationException("Receiving promise was lost.");
                e.CargoList.Add(new(entry.Id, entry.MaterialId, take, entry.Quality)); e.SearchIndex++; e.SearchEffort = 0;
                events.Add(ScavengingEvents.Event(world, ScavengingEvents.Changed, $"{e.Plan.Id}: recovered {take} kg from {entry.Object}."));
                break;
            case ExpeditionStatus.Returning:
                e.ElapsedTravel = Math.Min(TravelTicks(location, e), e.ElapsedTravel + 1);
                if (e.ElapsedTravel < TravelTicks(location, e)) return;
                e.Status = ExpeditionStatus.Unloading; e.Reason = "Returning cargo to the receiving point.";
                break;
            case ExpeditionStatus.Unloading:
                bool atStore = true;
                foreach (int id in e.Plan.Team)
                {
                    var move = Navigation.StepTowards(compound, id, point); atStore &= move.Arrived;
                    if (move.BlockedReason is not null) e.Reason = move.BlockedReason;
                }
                if (!atStore) return;
                foreach (var cargo in e.Cargo.ToArray())
                {
                    int index = e.Plan.Manifest.ToList().FindIndex(r => r.SalvageId == cargo.SalvageId);
                    var spec = location.Definition.Salvage.Single(s => s.Id == cargo.SalvageId);
                    if (spec.ComponentId is { } unit)
                    {
                        inventory.ReleaseReservations(e.SpaceOwner(index));
                        world.Module<ComputingModule>().AddComponent(world, unit, spec.ComponentModel!, e.Plan.ReceivingStoreId, spec.ComponentCondition, spec.Fault);
                        foreach (var asset in spec.Assets ?? []) world.Module<ComputingModule>().AddAsset(asset);
                    }
                    else inventory.ReceiveReservedCargo(e.SpaceOwner(index), new(cargo.MaterialId, cargo.QuantityKg, cargo.Quality));
                    e.ReceiptsList.Add(cargo); e.CargoList.Remove(cargo);
                }
                Finish(world, e);
                events.Add(ScavengingEvents.Event(world, ScavengingEvents.Completed, $"{e.Plan.Id}: team returned with {e.Receipts.Sum(c => c.QuantityKg)} kg." + string.Concat(e.Plan.Team.Where(id => _health[id].Sick).Select(id => $" {world.Module<WorkforceModule>().Find(id)!.Name} is ill and needs clinic care.")), EventSeverity.Positive));
                break;
        }
    }
    private static string? FieldSpecialty(string materialId) => materialId switch
    {
        "food.dry" => "cooking", "nutrient.fertilizer" => "agriculture", "fabric" or "supply.protection" => "tailoring",
        "metal.steel" or "metal.copper" or "metal.aluminum" => "metalworking", _ => null
    };
    private static void ReleaseUnusedSpace(World world, Expedition e)
    {
        for (int i = 0; i < e.Plan.Manifest.Count; i++)
            if (!e.Cargo.Any(c => c.SalvageId == e.Plan.Manifest[i].SalvageId)) world.Module<InventoryModule>().ReleaseReservations(e.SpaceOwner(i));
    }
    private static void StartReturn(Expedition e)
    { e.Status = ExpeditionStatus.Returning; e.ElapsedTravel = 0; e.Reason = "Travelling home with cargo."; }
    private void Finish(World world, Expedition e)
    {
        var inventory = world.Module<InventoryModule>(); var workforce = world.Module<WorkforceModule>();
        for (int i = 0; i < e.Plan.Manifest.Count; i++) inventory.ReleaseReservations(e.SpaceOwner(i));
        foreach (string owner in e.KitClaims) inventory.ReleaseReservations(owner); e.KitClaims.Clear();
        foreach (int id in e.Plan.Team)
        {
            workforce.Release(id, e.Owner);
            if (_health[id].Exposure >= 1)
            { _health[id] = _health[id] with { Sick = true }; workforce.Find(id)!.Present = false; }
        }
        e.Status = ExpeditionStatus.Completed; e.Decision = null; e.Reason = e.Recalled ? "Recalled; recovered cargo unloaded." : "Cargo unloaded; team released.";
    }
    public static bool ReleasedMedicine(MaterialBatch b) => b.MaterialId == "medicine.fever-dose" && b.Quality.Purity >= .999m
        && b.Quality.Grade == "fictional clinic release" && b.Quality.Contaminants.Count == 0;
    public string? ExplainTreatment(World world, TreatmentPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Id) || _treatments.ContainsKey(plan.Id)) return "Choose an unused clinic task id.";
        if (plan.PatientId == plan.MedicId || HealthOf(plan.PatientId) is not { Sick: true } || IsAway(plan.PatientId)
            || _treatments.Values.Any(t => t.Active && t.Plan.PatientId == plan.PatientId)) return "Choose an ill, returned patient who is not already under treatment.";
        if (WorksiteLogistics.ExplainLabor(world, plan.MedicId, new("medicine", 30)) is { } reason) return reason;
        if (world.Module<CompoundModule>().FindBuilding("clinic-station") is null) return "The clinic is missing.";
        return null;
    }
    public void Treat(World world, TreatmentPlan plan)
    {
        if (ExplainTreatment(world, plan) is { } reason) throw new InvalidOperationException(reason);
        var t = new ClinicTreatment(plan);
        _treatments.Add(plan.Id, t);
    }
    public IReadOnlyList<GameEvent> CancelTreatment(World world, ClinicTreatment t)
    {
        if (!t.Active) throw new InvalidOperationException("Clinic task is already finished.");
        if (t.DoseClaim is { } owner) world.Module<InventoryModule>().ReleaseReservations(owner);
        t.DoseClaim = null; world.Module<WorkforceModule>().Release(t.Plan.MedicId, t.Owner);
        world.Module<EquipmentModule>().Release("clinic-station", t.Owner); t.Cancelled = true; t.Reason = "Cancelled; the patient remains ill and the dose remains in the clinic.";
        return [ScavengingEvents.Event(world, ScavengingEvents.Changed, t.Reason)];
    }
    private void TickTreatment(World world, ClinicTreatment t, List<GameEvent> events)
    {
        var inventory = world.Module<InventoryModule>(); var compound = world.Module<CompoundModule>();
        var clinic = compound.FindBuilding("clinic-station")!;
        if (t.DoseClaim is null)
        {
            var dose = inventory.Batches.FirstOrDefault(b => b.StockpileId == "clinic" && ReleasedMedicine(b) && inventory.AvailableKg(b.Id) >= .1m);
            if (dose is null) { t.Reason = "Deliver 0.1 kg of released fictional medicine to the clinic; purity alone is insufficient."; return; }
            if (WorksiteLogistics.ExplainLabor(world, t.Plan.MedicId, new("medicine", 30)) is { } laborReason) { t.Reason = laborReason; return; }
            if (world.Module<EquipmentModule>().OwnerOf(clinic.Id) is not null) { t.Reason = "Clinic equipment is in use."; return; }
            if (!world.Module<WorkforceModule>().TryReserve(t.Plan.MedicId, t.Owner) || !world.Module<EquipmentModule>().TryReserve(clinic.Id, t.Owner)) throw new InvalidOperationException("Clinic claims changed.");
            t.DoseClaim = $"{t.Owner}/dose";
            if (!inventory.TryReserveStock(t.DoseClaim, dose.Id, .1m)) throw new InvalidOperationException("Dose became unavailable.");
        }
        bool arrived = true;
        foreach (int id in new[] { t.Plan.MedicId, t.Plan.PatientId })
        {
            var move = Navigation.StepTowards(compound, id, clinic.InteractionCell); arrived &= move.Arrived;
            t.Reason = move.BlockedReason;
        }
        if (!arrived) { t.Reason ??= "Patient and medic are walking to the clinic."; return; }
        if (world.Module<UtilitiesModule>().ServiceFor(clinic.Id) is not { Operational: true }) { t.Reason = "The clinic needs working power, water and equipment."; return; }
        decimal priorEffort = t.Effort;
        t.Effort = Math.Min(4, t.Effort + world.Module<WorkforceModule>().Find(t.Plan.MedicId)!.Skills.Rating("medicine") / 100m);
        Skills.SkillsModule.Credit(world, t.Plan.MedicId, "medicine", t.Effort - priorEffort);
        t.Reason = null;
        if (t.Effort < 4) return;
        SpentSuppliesKg += inventory.TakeReservedStock(t.DoseClaim).QuantityKg; t.DoseClaim = null;
        _health[t.Plan.PatientId] = new(t.Plan.PatientId); world.Module<WorkforceModule>().Find(t.Plan.PatientId)!.Present = true;
        world.Module<WorkforceModule>().Release(t.Plan.MedicId, t.Owner); world.Module<EquipmentModule>().Release(clinic.Id, t.Owner); t.Completed = true;
        events.Add(ScavengingEvents.Event(world, ScavengingEvents.Completed, $"{world.Module<WorkforceModule>().Find(t.Plan.PatientId)!.Name} recovered after clinic treatment.", EventSeverity.Positive));
    }
    public object ToSnapshot() => new ScavengingSnapshot(Locations.Select(l => l.Snapshot()).ToArray(), Expeditions.Select(e => e.Snapshot()).ToArray(), Health, Treatments.Select(t => t.Snapshot()).ToArray(), SpentSuppliesKg);
    public static ScavengingModule FromSnapshot(ScavengingSnapshot snapshot)
    {
        if (snapshot.SpentSuppliesKg < 0) throw new ArgumentException("Invalid spent supplies ledger.");
        var module = new ScavengingModule { SpentSuppliesKg = snapshot.SpentSuppliesKg };
        foreach (var l in snapshot.Locations) { module.AddLocation(l.Definition); module.Location(l.Definition.Id)!.Restore(l); }
        foreach (var h in snapshot.Health)
        { if (h.Exposure < 0 || h.Sick && h.Exposure < 1) throw new ArgumentException("Invalid worker exposure."); module._health.Add(h.WorkerId, h); }
        foreach (var s in snapshot.Expeditions)
        {
            if (!Enum.IsDefined(s.Status) || s.ElapsedTravel < 0 || s.SearchIndex < 0 || s.SearchIndex > s.Plan.Manifest.Count
                || s.SearchEffort < 0 || s.DelayTicks < 0 || s.Decision is { } decision && !Enum.IsDefined(decision)) throw new ArgumentException("Invalid expedition state.");
            var e = new Expedition(s.Plan) { Status = s.Status, ElapsedTravel = s.ElapsedTravel, SearchIndex = s.SearchIndex,
                SearchEffort = s.SearchEffort, DelayTicks = s.DelayTicks, Decision = s.Decision, Reason = s.Reason, Recalled = s.Recalled, Departed = s.Departed };
            e.CargoList.AddRange(s.Cargo); e.ReceiptsList.AddRange(s.Receipts); e.KitClaims.AddRange(s.KitClaims); module._expeditions.Add(s.Plan.Id, e);
        }
        foreach (var s in snapshot.Treatments)
        {
            if (s.Effort is < 0 or > 4) throw new ArgumentException("Invalid clinic effort.");
            module._treatments.Add(s.Plan.Id, new ClinicTreatment(s.Plan) { DoseClaim = s.DoseClaim, Effort = s.Effort, Completed = s.Completed, Reason = s.Reason, Cancelled = s.Cancelled });
        }
        return module;
    }
}
