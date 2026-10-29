using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using TreasureKeeperGlobal.Core;

namespace TreasureKeeperGlobal;

internal sealed class LootDiscoveryService : IDisposable
{
    private readonly Plugin plugin;
    private readonly LootDiscoveryTracker tracker = new();
    private readonly IGameInventory.InventoryChangelogDelegate changed;
    private readonly List<LootSlot> snapshot = new(158);
    private readonly List<LootSlotChange> changes = new(32);
    private static readonly int[] Containers = [0, 1, 2, 3, LootDiscoveryTracker.Crystals];
    private ulong character;
    private uint territory;
    private uint vaultId;
    private long lastUpdate;
    private bool disposed;
    internal string Status { get; private set; } = "等待進入寶物庫。";

    internal LootDiscoveryService(Plugin plugin)
    {
        this.plugin = plugin;
        changed = OnChanges;
        Plugin.Inventory.InventoryChangedRaw += changed;
    }

    internal void SetEnabled(bool enabled)
    {
        plugin.Config.AutoDiscoverLoot = enabled;
        Reset();
        Status = enabled ? "已啟用；進入寶物庫後自動補錄新取得物品。" : "已停止自動補錄，既有紀錄保留。";
        plugin.SaveConfig();
    }

    internal void Update()
    {
        if (disposed || Environment.TickCount64 - lastUpdate < 500) return;
        lastUpdate = Environment.TickCount64;
        try { EnsureSession(); }
        catch (Exception e) { Fault(e); }
    }

    private bool EnsureSession()
    {
        if (!plugin.Config.AutoDiscoverLoot) { Reset(); Status = "自動補錄已關閉。"; return false; }
        if (!Plugin.Client.IsLoggedIn || !Plugin.Player.IsLoaded || Plugin.Loading)
        { Reset(); Status = "等待角色與區域載入。"; return false; }
        var vault = LootDiscoveryCatalog.ResolveVault(plugin.Catalog.Vaults, Plugin.Client.TerritoryType);
        if (vault is null) { Reset(); Status = "等待進入寶物庫；一般野外不會自動歸入 G10 外場。"; return false; }
        if (character != Plugin.Player.ContentId || territory != Plugin.Client.TerritoryType || vaultId != vault.Id)
        {
            Reset(); character = Plugin.Player.ContentId; territory = Plugin.Client.TerritoryType; vaultId = vault.Id;
        }
        // Once seeded, validate only the five containers; events advance the saved slots.
        // Do not allocate/read 158 slots again every heartbeat and every inventory event.
        snapshot.Clear();
        if (!ReadContainers(tracker.Ready ? null : snapshot)) { tracker.Reset(); Status = "等待一般背包與水晶完整載入。"; return false; }
        if (!tracker.Ready)
        {
            tracker.Seed(snapshot);
            snapshot.Clear();
            if (!tracker.Ready) return false;
            Status = $"正在記錄：{vault.Name}。進場前持有的物品不補入。";
            Plugin.Log.Information($"[LootDiscovery] Started; vault={vault.Id}; territory={territory}");
        }
        return true;
    }

    private void OnChanges(IEnumerable<InventoryEventArgs> events)
    {
        if (disposed) return;
        try
        {
            if (!EnsureSession()) return;
            changes.Clear();
            foreach (var entry in events)
            {
                var current = Snapshot(entry.Item);
                switch (entry)
                {
                    case InventoryItemAddedArgs: changes.Add(new(current with { ItemId = 0, Quantity = 0 }, current)); break;
                    case InventoryItemRemovedArgs: changes.Add(new(current, current with { ItemId = 0, Quantity = 0 })); break;
                    case InventoryItemChangedArgs changedItem: changes.Add(new(Snapshot(changedItem.OldItemState), current)); break;
                }
            }
            var record = !Plugin.Conditions[ConditionFlag.TradeOpen] && !Plugin.Conditions[ConditionFlag.Crafting] &&
                !Plugin.Conditions[ConditionFlag.Gathering] && !Plugin.Conditions[ConditionFlag.OccupiedSummoningBell];
            var gains = tracker.Apply(changes, record);
            if (gains.Length == 0) return;
            var count = plugin.RecordDiscovered(vaultId, gains, DateTimeOffset.UtcNow);
            if (count > 0) Status = $"已補錄 {count} 種物品；新物品預設保留，可到清單勾選丟棄。";
        }
        catch (Exception e) { Fault(e); }
        finally { changes.Clear(); }
    }

    private static LootSlot Snapshot(GameInventoryItem item) =>
        new((int)item.ContainerType, (int)item.InventorySlot, item.BaseItemId, item.IsEmpty ? 0 : item.Quantity);

    private static unsafe bool ReadContainers(List<LootSlot>? result)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return false;
        foreach (var kind in Containers)
        {
            var container = manager->GetInventoryContainer((InventoryType)kind);
            var size = kind == LootDiscoveryTracker.Crystals ? 18 : 35;
            if (container == null || !container->IsLoaded || container->Size != size) return false;
            if (result is null) continue;
            for (var slot = 0; slot < size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null) return false;
                result.Add(new(kind, slot, item->ItemId, item->ItemId == 0 ? 0 : item->Quantity));
            }
        }
        return true;
    }

    private void Fault(Exception error)
    {
        Reset();
        Status = "補錄處理發生錯誤，等待重新同步；詳見 Dalamud 記錄。";
        Plugin.Log.Error(error, "TreasureKeeperGlobal loot discovery failed");
    }

    internal void Reset() { tracker.Reset(); snapshot.Clear(); character = 0; territory = 0; vaultId = 0; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Plugin.Inventory.InventoryChangedRaw -= changed; }
        finally { Reset(); changes.Clear(); }
    }
}
