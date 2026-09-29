using Haldane.CareerSim.Work;
using Haldane.Kernel;
using Haldane.Kernel.Serialization;
using Haldane.SupplyChain;
using Scavenger.Sim.Industry;
using Scavenger.Sim.Physical;

namespace Scavenger.Sim.Scavenging;

public static class ScavengingSave
{
    public static void RegisterAll(SaveRegistry registry)
    {
        registry.RegisterModule<ScavengingSnapshot>(ScavengingModule.Persist, (s, v) =>
        { if (v != 1) throw new NotSupportedException($"Unsupported scavenging schema {v}."); return ScavengingModule.FromSnapshot(s); });
        registry.RegisterCommand<ExpeditionPlan>(DispatchExpeditionCommand.Persist, s => new DispatchExpeditionCommand(s));
        registry.RegisterCommand<IdPayload>(RecallExpeditionCommand.Persist, s => new RecallExpeditionCommand(s.Id));
        registry.RegisterCommand<ResponsePayload>(RespondToExpeditionCommand.Persist, s => new RespondToExpeditionCommand(s.Id, s.Response));
        registry.RegisterCommand<IdPayload>(CancelTreatmentCommand.Persist, s => new CancelTreatmentCommand(s.Id));
        registry.RegisterCommand<TreatmentPlan>(QueueTreatmentCommand.Persist, s => new QueueTreatmentCommand(s));
    }
    private static void Require(bool valid, string message)
    { if (!valid) throw new InvalidOperationException(message); }
    public static void Validate(World world)
    {
        var module = world.Module<ScavengingModule>(); var inventory = world.Module<InventoryModule>();
        var workforce = world.Module<WorkforceModule>(); var board = world.Module<ProjectBoardModule>();
        var computing = world.Module<ComputingModule>(); var equipment = world.Module<EquipmentModule>();
        var stockOwners = new HashSet<string>(StringComparer.Ordinal); var spaceOwners = new HashSet<string>(StringComparer.Ordinal);
        var labor = new Dictionary<int, string>(); var componentIds = new HashSet<string>(StringComparer.Ordinal); var assetIds = new HashSet<string>(StringComparer.Ordinal);
        Require(module.Health.Count == workforce.Staff.Count && module.Health.All(h => workforce.Find(h.WorkerId) is { } worker
            && (!h.Sick || !worker.Present)), "Regional health records must match the roster and sick workers must be unavailable.");
        foreach (var location in module.Locations)
        {
            var snapshot = location.Snapshot(); var d = location.Definition;
            Require(location.Integrity is >= 0 and <= 1 && location.Integrity <= d.InitialIntegrity, "Invalid location integrity.");
            Require(snapshot.Remaining.Count == d.Salvage.Count && snapshot.RemovedByRivals.Keys.All(id => d.Salvage.Any(s => s.Id == id)), "Unknown regional stock entries.");
            foreach (var s in d.Salvage)
            {
                Require(snapshot.Remaining.ContainsKey(s.Id) && location.Remaining(s.Id) >= 0 && snapshot.RemovedByRivals.GetValueOrDefault(s.Id) >= 0, "Invalid remaining/rival salvage quantities.");
                decimal recovered = module.Expeditions.Where(e => e.Plan.LocationId == d.Id).Sum(e => e.Cargo.Concat(e.Receipts).Where(c => c.SalvageId == s.Id).Sum(c => c.QuantityKg));
                Require(location.Remaining(s.Id) + snapshot.RemovedByRivals.GetValueOrDefault(s.Id) + recovered == s.InitialKg, "Regional salvage mass ledger does not balance.");
                if (s.ComponentId is { } id)
                {
                    Require(componentIds.Add(id) && computing.Definition(s.ComponentModel!) is { } model && model.MassKg == s.InitialKg
                        && s.MaterialId == $"item/{id}" && location.Remaining(s.Id) is var remaining && (remaining == 0 || remaining == s.InitialKg)
                        && (snapshot.RemovedByRivals.GetValueOrDefault(s.Id) == 0 || snapshot.RemovedByRivals.GetValueOrDefault(s.Id) == s.InitialKg), "Duplicate, fractional or invalid remote component.");
                    bool delivered = module.Expeditions.Any(e => e.Plan.LocationId == d.Id && e.Receipts.Any(c => c.SalvageId == s.Id));
                    var assets = s.Assets ?? [];
                    Require(assets.Count == 0 || computing.Definition(s.ComponentModel!)!.Kind == ComponentKind.PortableMedia
                        && assets.Sum(a => a.SizeGiB) <= computing.Definition(s.ComponentModel!)!.StorageGiB, "Remote archives need a carrier with enough storage.");
                    foreach (var asset in assets)
                        Require(assetIds.Add(asset.Id) && asset.SourceMediaId == id && (delivered == (computing.Asset(asset.Id) is not null)), "Remote archive ownership/identity is missing or duplicated.");
                    Require(delivered == (computing.Component(id) is not null) && (!delivered || computing.Component(id)!.DefinitionId == s.ComponentModel), "Recovered unit has missing or premature compound ownership.");
                }
                else Require(inventory.FindMaterial(s.MaterialId) is not null && (s.Assets?.Count ?? 0) == 0, "Remote salvage refers to an unknown material.");
            }
        }
        foreach (var e in module.Expeditions)
        {
            var location = module.Location(e.Plan.LocationId);
            Require(location is not null && !string.IsNullOrWhiteSpace(e.Plan.Id) && Enum.IsDefined(e.Plan.Approach)
                && e.Plan.Team.Count is >= 1 and <= 4 && e.Plan.Team.Distinct().Count() == e.Plan.Team.Count
                && e.Plan.Team.All(id => module.HealthOf(id) is not null && workforce.Find(id)!.Skills.Meets(new("hauling", 20)))
                && inventory.FindStockpile(e.Plan.ReceivingStoreId)?.Position is not null
                && e.Plan.Manifest.Count > 0 && e.Plan.Manifest.Select(m => m.SalvageId).Distinct().Count() == e.Plan.Manifest.Count
                && e.ManifestKg <= e.Plan.Team.Count * 12m, "Invalid expedition references/team/manifest.");
            foreach (var request in e.Plan.Manifest)
            {
                var spec = location!.Definition.Salvage.FirstOrDefault(s => s.Id == request.SalvageId);
                Require(spec is not null && request.QuantityKg > 0 && request.QuantityKg <= spec.InitialKg
                    && (spec.ComponentId is null || request.QuantityKg == spec.InitialKg)
                    && e.Plan.Team.Any(id => workforce.Find(id)!.Skills.Meets(spec.Qualification)), "Invalid recovery manifest qualification or mass.");
            }
            decimal toolKg = e.Plan.Manifest.Any(m => location!.Definition.Salvage.Single(s => s.Id == m.SalvageId).NeedsPrybar) ? 1 : 0;
            Require(e.ManifestKg + toolKg <= e.Plan.Team.Count * 12m, "Manifest and reusable kit exceed team carrying capacity.");
            int travel = e.Plan.Approach == ExpeditionApproach.Careful ? location!.Definition.TravelTicks : Math.Max(1, location!.Definition.TravelTicks * 3 / 4);
            Require(e.ElapsedTravel <= travel && e.SearchIndex <= e.Plan.Manifest.Count && e.DelayTicks <= 6
                && (e.Status == ExpeditionStatus.Decision) == (e.Decision is not null)
                && (e.Departed || e.Status is ExpeditionStatus.Gathering or ExpeditionStatus.LeavingCompound or ExpeditionStatus.Completed)
                && (e.Status != ExpeditionStatus.Completed || e.Cargo.Count == 0)
                && (e.Status == ExpeditionStatus.Completed || e.Receipts.Count == 0)
                && (e.Status is not (ExpeditionStatus.Gathering or ExpeditionStatus.LeavingCompound or ExpeditionStatus.Outbound) || e.Cargo.Count == 0 && e.SearchIndex == 0)
                && (!e.Recalled || e.Status is ExpeditionStatus.Returning or ExpeditionStatus.Unloading or ExpeditionStatus.Completed), "Invalid expedition phase/progression.");
            var collected = e.Cargo.Concat(e.Receipts).ToArray();
            Require(collected.Select(c => c.SalvageId).Distinct().Count() == collected.Length, "Duplicate expedition cargo/receipt.");
            foreach (var cargo in collected)
            {
                int index = e.Plan.Manifest.ToList().FindIndex(r => r.SalvageId == cargo.SalvageId);
                var spec = location.Definition.Salvage.FirstOrDefault(s => s.Id == cargo.SalvageId);
                Require(index >= 0 && index < e.SearchIndex && spec is not null && cargo.MaterialId == spec.MaterialId
                    && cargo.QuantityKg > 0 && cargo.QuantityKg <= e.Plan.Manifest[index].QuantityKg
                    && (spec.ComponentId is null || cargo.QuantityKg == spec.InitialKg)
                    && SameQuality(cargo.Quality, spec.Quality), "Cargo differs from its source object, quality or manifest.");
            }
            for (int i = 0; i < e.Plan.Manifest.Count; i++)
            {
                var cargo = e.Cargo.FirstOrDefault(c => c.SalvageId == e.Plan.Manifest[i].SalvageId);
                bool pending = e.Active && !e.Recalled && (e.Status is ExpeditionStatus.Gathering or ExpeditionStatus.LeavingCompound or ExpeditionStatus.Outbound or ExpeditionStatus.Searching or ExpeditionStatus.Decision) && i >= e.SearchIndex;
                decimal expected = cargo?.QuantityKg ?? (pending ? e.Plan.Manifest[i].QuantityKg : 0);
                var claim = inventory.CapacityReservedBy(e.SpaceOwner(i));
                Require(expected > 0 ? claim is not null && claim.StockpileId == e.Plan.ReceivingStoreId && claim.QuantityKg == expected && spaceOwners.Add(claim.Owner)
                    : claim is null, "Invalid expedition receiving capacity promise.");
            }
            foreach (int id in e.Plan.Team)
            {
                if (e.Active) Require(labor.TryAdd(id, e.Owner) && workforce.ReservationFor(id) == e.Owner && workforce.Find(id)!.Present
                    && !board.IsAssigned(id), "Expedition labor is unavailable, duplicated or missing.");
                else Require(workforce.ReservationFor(id) != e.Owner, "Completed expedition retains labor.");
            }
            var expectedKit = new Dictionary<string, decimal>(StringComparer.Ordinal);
            if (e.Active)
            {
                if (e.Plan.Manifest.Any(m => location.Definition.Salvage.Single(s => s.Id == m.SalvageId).NeedsPrybar)) expectedKit["tool.prybar"] = 1;
                if (e.Plan.UseProtection && !e.Departed) expectedKit["supply.protection"] = .02m * e.Plan.Team.Count;
            }
            var kit = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (string owner in e.KitClaims)
            {
                var claim = inventory.StockReservedBy(owner); var batch = claim is null ? null : inventory.FindBatch(claim.BatchId);
                Require(batch is not null && batch.StockpileId == e.Plan.ReceivingStoreId && owner.StartsWith(e.Owner + "/kit/", StringComparison.Ordinal)
                    && stockOwners.Add(owner) && (batch.MaterialId != "supply.protection" || batch.Quality.Purity >= .95m), "Invalid expedition kit claim.");
                kit[batch!.MaterialId] = kit.GetValueOrDefault(batch.MaterialId) + claim!.QuantityKg;
            }
            Require(kit.Count == expectedKit.Count && expectedKit.All(k => kit.GetValueOrDefault(k.Key) == k.Value), "Expedition kit is incomplete or excessive.");
        }
        var patients = new HashSet<int>();
        foreach (var treatment in module.Treatments)
        {
            var p = treatment.Plan;
            Require(!string.IsNullOrWhiteSpace(p.Id) && p.PatientId != p.MedicId && module.HealthOf(p.PatientId) is not null
                && workforce.Find(p.MedicId) is { } medic && medic.Skills.Meets(new("medicine", 30)), "Invalid clinic task references.");
            Require(!(treatment.Completed && treatment.Cancelled), "Clinic task cannot be both completed and cancelled.");
            if (treatment.Cancelled) Require(treatment.DoseClaim is null && workforce.ReservationFor(p.MedicId) != treatment.Owner, "Cancelled care retains claims.");
            else if (treatment.Completed) Require(treatment.Effort == 4 && treatment.DoseClaim is null && workforce.ReservationFor(p.MedicId) != treatment.Owner, "Completed care retains claims or incomplete effort.");
            else
            {
                Require(patients.Add(p.PatientId) && module.HealthOf(p.PatientId)!.Sick && !module.IsAway(p.PatientId), "Invalid or duplicated clinic patient.");
                if (treatment.DoseClaim is { } owner)
                {
                    var claim = inventory.StockReservedBy(owner); var batch = claim is null ? null : inventory.FindBatch(claim.BatchId);
                    Require(owner == treatment.Owner + "/dose" && batch is not null && batch.StockpileId == "clinic" && ScavengingModule.ReleasedMedicine(batch)
                        && claim!.QuantityKg == .1m && stockOwners.Add(owner) && labor.TryAdd(p.MedicId, treatment.Owner)
                        && workforce.ReservationFor(p.MedicId) == treatment.Owner && workforce.Find(p.MedicId)!.Present && !board.IsAssigned(p.MedicId)
                        && equipment.OwnerOf("clinic-station") == treatment.Owner, "Invalid clinic dose, medic or equipment claim.");
                }
                else Require(treatment.Effort == 0 && workforce.ReservationFor(p.MedicId) != treatment.Owner, "Waiting clinic task retains effort/labor without a dose.");
            }
        }
        decimal spent = module.Expeditions.Where(e => e.Departed && e.Plan.UseProtection).Sum(e => .02m * e.Plan.Team.Count)
            + module.Treatments.Count(t => t.Completed) * .1m;
        Require(spent == module.SpentSuppliesKg, "Consumed expedition/clinic supplies ledger does not balance.");
        foreach (var claim in inventory.StockReservations.Where(c => RegionalOwner(c.Owner))) Require(stockOwners.Contains(claim.Owner), "Orphan regional stock claim.");
        foreach (var claim in inventory.CapacityReservations.Where(c => RegionalOwner(c.Owner))) Require(spaceOwners.Contains(claim.Owner), "Orphan regional capacity claim.");
        foreach (var claim in workforce.Reservations.Where(c => RegionalOwner(c.Owner))) Require(labor.GetValueOrDefault(claim.StaffId) == claim.Owner, "Orphan regional labor claim.");
    }
    private static bool RegionalOwner(string owner) => owner.StartsWith("scavenger.expedition/", StringComparison.Ordinal) || owner.StartsWith("scavenger.clinic/", StringComparison.Ordinal);
    private static bool SameQuality(BatchQuality a, BatchQuality b) => a.Purity == b.Purity && a.Grade == b.Grade
        && a.Contaminants.Count == b.Contaminants.Count && a.Contaminants.All(c => b.Contaminants.TryGetValue(c.Key, out var v) && c.Value == v);
}
