namespace TreasureKeeperGlobal.Core;

// Stores only the current snapshots, never a growing history of old inventories/catalogs.
// Callers replace snapshots rather than mutating their rows in place.
public sealed class InventoryCountCache
{
    private IReadOnlyList<BagSlot>? snapshot;
    private readonly Dictionary<uint, long> counts = [];

    public IReadOnlyDictionary<uint, long> Get(IReadOnlyList<BagSlot> slots)
    {
        if (ReferenceEquals(snapshot, slots)) return counts;
        snapshot = slots;
        counts.Clear();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            counts[slot.ItemId] = counts.GetValueOrDefault(slot.ItemId) + slot.Quantity;
        }
        return counts;
    }
}

public sealed class CatalogIndexCache
{
    private TreasureItem[][] snapshots = [];
    private readonly Dictionary<uint, TreasureItem> items = [];

    public IReadOnlyDictionary<uint, TreasureItem> Get(TreasureCatalog catalog)
    {
        var changed = snapshots.Length != catalog.Vaults.Length;
        if (!changed)
            for (var i = 0; i < snapshots.Length; i++)
                if (!ReferenceEquals(snapshots[i], catalog.Vaults[i].Items)) { changed = true; break; }
        if (!changed) return items;
        if (snapshots.Length != catalog.Vaults.Length) snapshots = new TreasureItem[catalog.Vaults.Length][];
        items.Clear();
        for (var i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = catalog.Vaults[i].Items;
            foreach (var item in snapshots[i]) items.TryAdd(item.Id, item);
        }
        return items;
    }
}

public sealed class ObservationOrderCache
{
    private readonly Dictionary<uint, DiscoveredLoot> snapshot = [];
    private DiscoveredLoot[] ordered = [];

    public DiscoveredLoot[] Get(IReadOnlyDictionary<uint, DiscoveredLoot>? records)
    {
        if (records is null || records.Count == 0) { snapshot.Clear(); ordered = []; return ordered; }
        var changed = snapshot.Count != records.Count;
        if (!changed)
            foreach (var pair in records)
                if (!snapshot.TryGetValue(pair.Key, out var previous) || !ReferenceEquals(previous, pair.Value)) { changed = true; break; }
        if (!changed) return ordered;
        snapshot.Clear();
        foreach (var pair in records) snapshot.Add(pair.Key, pair.Value);
        ordered = snapshot.Values.Where(r => r is not null).OrderByDescending(r => r.LastSeenUtc).ToArray();
        return ordered;
    }
}
