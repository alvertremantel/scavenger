using Haldane.Kernel.Serialization;

namespace Scavenger.Sim.Industry;

public sealed record EquipmentClaim(string StationId, string Owner);
public sealed class EquipmentModule : IPersistentModule
{
    public const string Persist = "scavenger.equipment-claims";
    public string PersistId => Persist;
    public int SchemaVersion => 1;
    private readonly Dictionary<string, string> _claims = new(StringComparer.Ordinal);
    public IReadOnlyList<EquipmentClaim> Claims => _claims.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new EquipmentClaim(c.Key, c.Value)).ToList();
    public string? OwnerOf(string stationId) => _claims.GetValueOrDefault(stationId);
    public bool TryReserve(string stationId, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return !_claims.ContainsValue(owner) && _claims.TryAdd(stationId, owner);
    }
    public void Release(string stationId, string owner)
    { if (OwnerOf(stationId) == owner) _claims.Remove(stationId); }
    public object ToSnapshot() => new EquipmentSnapshot(Claims);
    public static EquipmentModule FromSnapshot(EquipmentSnapshot snapshot)
    {
        var module = new EquipmentModule();
        foreach (var claim in snapshot.Claims)
            if (!module.TryReserve(claim.StationId, claim.Owner)) throw new ArgumentException("Duplicate equipment claim.", nameof(snapshot));
        return module;
    }
}
public sealed record EquipmentSnapshot(IReadOnlyList<EquipmentClaim> Claims);
