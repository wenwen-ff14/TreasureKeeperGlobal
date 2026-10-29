namespace TreasureKeeperGlobal.Core;

public readonly record struct LootSlot(int Inventory, int Slot, uint ItemId, long Quantity);
public readonly record struct LootSlotChange(LootSlot Before, LootSlot After);
public readonly record struct LootGain(uint ItemId, long Quantity);
public sealed record DiscoveredLoot(uint ItemId, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

public sealed class LootDiscoveryTracker
{
    private readonly Dictionary<(int Inventory, int Slot), LootSlot> slots = [];
    private readonly Dictionary<uint, long> ownedDelta = [];
    private readonly Dictionary<uint, long> bagDelta = [];
    private readonly List<LootGain> gains = [];
    public bool Ready { get; private set; }
    public const int Crystals = 2001;

    public static bool IsObservedInventory(int inventory) => inventory is >= 0 and <= 3 or Crystals;
    public static bool ValidSlot(LootSlot slot) => slot.Slot >= 0 &&
        slot.Slot < (slot.Inventory == Crystals ? 18 : 35) && IsObservedInventory(slot.Inventory) &&
        slot.Quantity >= 0 && (slot.ItemId != 0 || slot.Quantity == 0);

    public void Seed(IEnumerable<LootSlot> snapshot)
    {
        Reset();
        foreach (var slot in snapshot)
        {
            if (!ValidSlot(slot) || !slots.TryAdd((slot.Inventory, slot.Slot), slot)) { Reset(); return; }
        }
        // All four normal bags and the crystal container must be present, including empty slots.
        Ready = slots.Count == 158;
        if (!Ready) Reset();
    }

    public LootGain[] Apply(IReadOnlyList<LootSlotChange> changes, bool record)
    {
        if (!Ready) return [];
        ownedDelta.Clear(); bagDelta.Clear(); gains.Clear();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            Add(ownedDelta, change.Before.ItemId, -change.Before.Quantity);
            Add(ownedDelta, change.After.ItemId, change.After.Quantity);
            if (change.Before.Inventory != change.After.Inventory || change.Before.Slot != change.After.Slot ||
                !ValidSlot(change.Before) || !ValidSlot(change.After)) continue;
            var key = (change.After.Inventory, change.After.Slot);
            var previous = slots.GetValueOrDefault(key);
            // Initial load notifications and duplicated events already equal the seeded/new state.
            // A mismatching old state is resynchronized without guessing that it was newly received.
            slots[key] = change.After;
            if (previous != change.Before || previous == change.After) continue;
            Add(bagDelta, change.Before.ItemId, -change.Before.Quantity);
            Add(bagDelta, change.After.ItemId, change.After.Quantity);
        }
        if (!record) return [];
        // Transfers from equipment, armoury or other containers have zero total ownership gain.
        foreach (var pair in bagDelta)
        {
            var quantity = Math.Min(pair.Value, ownedDelta.GetValueOrDefault(pair.Key));
            if (pair.Key > 1 && quantity > 0) gains.Add(new(pair.Key, quantity));
        }
        return gains.ToArray();
    }

    private static void Add(Dictionary<uint, long> values, uint id, long quantity)
    {
        if (id != 0) values[id] = values.GetValueOrDefault(id) + quantity;
    }

    public void Reset() { slots.Clear(); ownedDelta.Clear(); bagDelta.Clear(); gains.Clear(); Ready = false; }
}

public static class LootDiscoveryCatalog
{
    public const string Source = "自動補錄（庫內取得）";
    public static TreasureVault? ResolveVault(IReadOnlyList<TreasureVault> vaults, uint territory)
    {
        for (var i = 0; i < vaults.Count; i++)
            if (!vaults[i].IsOutdoor && vaults[i].ContainsTerritory(territory)) return vaults[i];
        return null;
    }

    public static TreasureVault Merge(TreasureVault vault, TreasureItem item)
    {
        TreasureItem? existing = null;
        foreach (var candidate in vault.Items)
            if (candidate.Id == item.Id) { existing = candidate; break; }
        if (existing is not null && existing.Sources.Contains(Source)) return vault;
        var tagged = existing is null ? item with { Sources = [Source] } : existing with { Sources = [.. existing.Sources, Source] };
        var items = new TreasureItem[vault.Items.Length + (existing is null ? 1 : 0)];
        var next = 0;
        foreach (var candidate in vault.Items)
            if (candidate.Id != item.Id) items[next++] = candidate;
        items[next] = tagged;
        Array.Sort(items, static (left, right) => left.Id.CompareTo(right.Id));
        return vault with { Items = items };
    }

    public static DiscoveredLoot Observe(DiscoveredLoot? old, uint itemId, DateTimeOffset now) =>
        old is null || old.ItemId != itemId ? new(itemId, now, now) : old with { LastSeenUtc = now > old.LastSeenUtc ? now : old.LastSeenUtc };
}
