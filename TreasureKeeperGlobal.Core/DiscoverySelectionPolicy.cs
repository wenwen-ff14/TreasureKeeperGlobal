namespace TreasureKeeperGlobal.Core;

public static class DiscoverySelectionPolicy
{
    // Keep/discard decisions are global. A vault default initializes each item only once.
    public static bool ApplyDefault(uint vaultId, TreasureItem item, IReadOnlyDictionary<uint, bool> defaults,
        HashSet<uint> configured, HashSet<uint> selected)
    {
        if (!CleanupPolicy.CanSelect(item)) { selected.Remove(item.Id); return false; }
        if (!configured.Add(item.Id) || selected.Contains(item.Id)) return false;
        if (defaults.GetValueOrDefault(vaultId)) selected.Add(item.Id);
        return true;
    }

    public static HashSet<uint> RestoreConfigured(IEnumerable<uint>? configured, IEnumerable<uint> selected,
        IEnumerable<uint> observed, IEnumerable<uint> custom)
    {
        var result = configured?.ToHashSet() ?? [];
        result.UnionWith(selected);
        result.UnionWith(observed);
        result.UnionWith(custom);
        return result;
    }

    public static bool CanAwaitDiscovery(IEnumerable<TreasureVault> vaults, bool enabled,
        IReadOnlyDictionary<uint, bool> defaults) => enabled &&
        vaults.Any(v => v.Available && !v.IsOutdoor && defaults.GetValueOrDefault(v.Id));
}
