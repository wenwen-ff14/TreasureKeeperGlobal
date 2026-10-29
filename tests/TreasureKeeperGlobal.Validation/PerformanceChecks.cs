using System.Runtime.CompilerServices;
using TreasureKeeperGlobal.Core;

internal static class PerformanceChecks
{
    internal static void Run(Action<bool, string> check, TreasureCatalog catalog)
    {
        var saves = new DeferredSave();
        var count = 0;
        Action save = () => count++;
        saves.Flush(99_000, save);
        check(count == 0, "Idle configuration never writes");
        saves.MarkDirty(1000);
        saves.MarkDirty(2900);
        saves.Flush(2999, save);
        check(count == 0 && saves.Dirty, "Repeated observations wait for the first bounded save deadline");
        saves.Flush(3000, save);
        check(count == 1 && !saves.Dirty, "Continuous observations cannot postpone a due save");
        saves.Flush(9000, save);
        check(count == 1, "Successful write clears dirty state");
        saves.MarkDirty(10_000); saves.Flush(10_001, save, true);
        check(count == 2 && !saves.Dirty, "New item, logout and dispose can force pending observations to disk");
        saves.MarkDirty(20_000);
        try { saves.Flush(22_000, () => throw new IOException("simulated")); }
        catch (IOException) { }
        check(saves.Dirty, "Failed save retains pending data");
        saves.Flush(22_001, save);
        check(count == 2, "Failed saves do not retry on every framework frame");
        saves.Flush(24_000, save);
        check(count == 3 && !saves.Dirty, "Failed save can recover on the next deadline");
        saves.MarkDirty(30_000); saves.Saved(); saves.Flush(40_000, save);
        check(count == 3, "Explicit setting save also satisfies pending discovery save");

        var inventory = new InventoryCountCache();
        BagSlot[] first = [new(0, 0, 100, int.MaxValue, false), new(0, 1, 100, 20, false)];
        var counts = inventory.Get(first);
        check(counts[100] == (long)int.MaxValue + 20, "Cached totals use 64-bit accumulation");
        check(ReferenceEquals(counts, inventory.Get(first)), "Identical inventory snapshot reuses bounded totals");
        check(inventory.Get([new(0, 0, 101, 7, false)]).Count == 1 && !counts.ContainsKey(100) && counts[101] == 7,
            "Changed snapshot clears stale counts and shows new items");
        check(inventory.Get([]).Count == 0, "Logout snapshot clears cached inventory");
        var oldSnapshot = PopulateThenReplace(inventory);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!oldSnapshot.IsAlive, "Inventory cache releases replaced snapshots for collection");

        var index = new CatalogIndexCache();
        var local = catalog with { Vaults = catalog.Vaults.ToArray() };
        var lookup = index.Get(local);
        check(lookup.Count == local.Vaults.SelectMany(v => v.Items).DistinctBy(i => i.Id).Count(), "Preview lookup keeps one entry per global item ID");
        check(ReferenceEquals(lookup, index.Get(local)), "Stable catalog reuses lookup");
        var added = new TreasureItem(999999, "新補錄", "Observed", "素材", true, true, []);
        local.Vaults[0] = LootDiscoveryCatalog.Merge(local.Vaults[0], added);
        check(index.Get(local).ContainsKey(added.Id), "Learned item invalidates lookup even when outer catalog and vault array are unchanged");
        var newVault = local.Vaults[0] with { Items = [added] };
        check(index.Get(local with { Vaults = [newVault] }).Count == 1, "Replacing catalog releases old index entries");
        check(ReferenceEquals(LootDiscoveryCatalog.Merge(local.Vaults[0], added), local.Vaults[0]),
            "Repeated learned ID does not rebuild or reorder its catalog");
        var ordered = new ObservationOrderCache();
        var stamp = DateTimeOffset.UnixEpoch;
        var observations = new Dictionary<uint, DiscoveredLoot>
        { [1] = new(1, stamp, stamp), [2] = new(2, stamp, stamp.AddSeconds(1)) };
        var initialOrder = ordered.Get(observations);
        check(initialOrder.Select(r => r.ItemId).SequenceEqual(new uint[] { 2, 1 }), "Observation cache sorts by latest acquisition");
        check(ReferenceEquals(initialOrder, ordered.Get(observations)), "Unchanged observations do not allocate or sort per frame");
        observations[1] = LootDiscoveryCatalog.Observe(observations[1], 1, stamp.AddSeconds(2));
        check(ordered.Get(observations)[0] == observations[1], "Timestamp update invalidates cached order even when count is unchanged");
        check(ordered.Get(null).Length == 0, "Switching to an unobserved vault clears old observation references");
        check(ordered.Get(new Dictionary<uint, DiscoveredLoot> { [3] = new(3, stamp, stamp) }).Single().ItemId == 3,
            "Switching vaults cannot show previous observations");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PopulateThenReplace(InventoryCountCache cache)
    {
        BagSlot[] old = [new(0, 0, 200, 1, false)];
        var reference = new WeakReference(old);
        cache.Get(old); cache.Get([]);
        return reference;
    }
}
