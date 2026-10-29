using Dalamud.Configuration;
using TreasureKeeperGlobal.Core;

namespace TreasureKeeperGlobal;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public HashSet<uint>? DiscardItems { get; set; }
    // Read old configurations once; current selections are global item IDs.
    public Dictionary<uint, HashSet<uint>> DiscardByVault { get; set; } = [];
    public Dictionary<uint, HashSet<uint>> CustomItemsByVault { get; set; } = [];
    public bool IncludeHighQuality { get; set; }
    public bool MapHotbarEnabled { get; set; } = true;
    public bool AutoConfirmDecipher { get; set; } = true;
    public bool AutoDiscoverLoot { get; set; } = true;
    public Dictionary<uint, Dictionary<uint, DiscoveredLoot>> DiscoveredByVault { get; set; } = [];
    public Dictionary<uint, bool> DiscardNewByVault { get; set; } = [];
    public HashSet<uint> ConfiguredItems { get; set; } = [];
}
