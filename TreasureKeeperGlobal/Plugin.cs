using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using TreasureKeeperGlobal.Core;

namespace TreasureKeeperGlobal;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static IPlayerState Player { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IGameGui Gui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ISeStringEvaluator StringEvaluator { get; private set; } = null!;
    [PluginService] internal static IGameInventory Inventory { get; private set; } = null!;

    internal Configuration Config { get; }
    internal TreasureCatalog Catalog { get; private set; }
    internal IReadOnlyList<BagSlot> Slots { get; private set; } = [];
    internal bool Armed { get; private set; }
    internal HashSet<uint> SelectedItems => Config.DiscardItems!;
    internal string Status { get; private set; } = "尚未啟動。先選擇寶物庫，再勾選要丟棄的物品。";
    internal Queue<string> History { get; } = new();
    internal string[] HistorySnapshot { get; private set; } = [];
    internal MapDecipherService Maps { get; }
    internal LootDiscoveryService Discovery { get; }
    internal bool CanStartMap => tracker.Pending is null;
    private readonly WindowSystem windows = new("TreasureKeeperGlobal");
    private readonly MainWindow window;
    private readonly MapWindow mapWindow;
    private readonly DiscardTracker tracker = new();
    private readonly DeferredSave deferredSave = new();
    private readonly System.Action saveConfig;
    private readonly IClientState.LogoutDelegate logout;
    private readonly Dictionary<uint, TreasureItem> itemIndex = [];
    private long lastScan;
    private ulong armedCharacter;
    private bool disposed;
    private bool keepCommandRegistered;
    private bool mapCommandRegistered;
    private string? lastBlockedSummary;

    public Plugin()
    {
        saveConfig = SaveConfig;
        Config = Interface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.DiscardByVault ??= [];
        Config.CustomItemsByVault ??= [];
        Config.DiscoveredByVault ??= [];
        Config.DiscardNewByVault ??= [];
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TreasureKeeperGlobal.Data.catalog.json")!;
        Catalog = TreasureCatalog.Load(stream);
        var duties = Data.GetExcelSheet<ContentFinderCondition>();
        var englishDuties = Data.GetExcelSheet<ContentFinderCondition>(Dalamud.Game.ClientLanguage.English);
        Catalog = Catalog with { Vaults = Catalog.Vaults.Select(v =>
        {
            // Resolve against the international client, including vaults absent from the TW snapshot.
            if (!v.IsOutdoor)
            {
                var matches = englishDuties.Where(d => d.ContentType.RowId == 9 &&
                    string.Equals(d.Name.ToString(), v.EnglishName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 1 && duties.TryGetRow(matches[0].RowId, out var localDuty))
                    v = v with { DutyId = localDuty.RowId, TerritoryId = localDuty.TerritoryType.RowId,
                        Name = localDuty.Name.ToString() };
                else
                    v = v with { DutyId = 0, TerritoryId = 0, Available = false };
            }
            var available = v.IsOutdoor ? v.OutdoorTerritoryIds!.All(id =>
                Data.GetExcelSheet<TerritoryType>().TryGetRow(id, out var zone) && zone.ContentFinderCondition.RowId == 0) :
                v.TerritoryId != 0 && duties.TryGetRow(v.DutyId, out var duty) &&
                duty.ContentType.RowId == 9 && duty.TerritoryType.RowId == v.TerritoryId;
            var items = v.Items.Select(RefreshItem).ToList();
            if (Config.CustomItemsByVault.TryGetValue(v.Id, out var added) && added is not null)
                foreach (var id in added.Where(id => items.All(i => i.Id != id)))
                    if (FindItem(id) is { } item) items.Add(item);
            var result = v with { Available = available, Items = items.OrderBy(i => i.Id).ToArray() };
            if (!v.IsOutdoor && Config.DiscoveredByVault.TryGetValue(v.Id, out var found) && found is not null)
                foreach (var (id, observation) in found)
                    if (observation is not null && observation.ItemId == id && id > 1 && FindItem(id) is { } discovered)
                        result = LootDiscoveryCatalog.Merge(result, discovered);
            return result;
        }).ToArray() };
        foreach (var item in Catalog.Vaults.SelectMany(v => v.Items)) itemIndex.TryAdd(item.Id, item);
        Config.DiscardItems = CleanupPolicy.RestoreSelection(itemIndex.Values, Config.DiscardItems,
            Config.DiscardByVault.Values.Where(ids => ids is not null));
        Config.DiscardByVault.Clear();
        Config.ConfiguredItems = DiscoverySelectionPolicy.RestoreConfigured(Config.ConfiguredItems, SelectedItems,
            Config.DiscoveredByVault.Values.Where(r => r is not null).SelectMany(r => r.Keys),
            Config.CustomItemsByVault.Values.Where(ids => ids is not null).SelectMany(ids => ids));
        Config.Version = 4;
        SaveConfig();
        logout = (_, _) => { Armed = false; armedCharacter = 0; Slots = []; tracker.Reset(); Discovery?.Reset(); Maps?.Cancel("已登出，停止本次解讀。"); FlushConfig(true); Status = "已登出，需重新啟動。"; };
        try
        {
            Maps = new MapDecipherService(this);
            Discovery = new LootDiscoveryService(this);
            mapWindow = new MapWindow(this);
            window = new MainWindow(this);
            windows.AddWindow(window);
            windows.AddWindow(mapWindow);
            keepCommandRegistered = Commands.AddHandler("/tkeep", new CommandInfo(OnCommand)
            { HelpMessage = "寶物庫整理助手。/tkeep 開啟；/tkeep stop 立即停止自動丟棄。" });
            mapCommandRegistered = Commands.AddHandler("/tmap", new CommandInfo(OnMapCommand)
            { HelpMessage = "地圖快捷解讀。/tmap 開啟；/tmap <物品 ID> 解讀；/tmap stop 取消本次操作。" });
            if (!keepCommandRegistered || !mapCommandRegistered) throw new InvalidOperationException("TreasureKeeperGlobal command is already registered");
            Framework.Update += Update;
            Client.TerritoryChanged += OnTerritoryChanged;
            Client.Logout += logout;
            Interface.UiBuilder.Draw += windows.Draw;
            Interface.UiBuilder.OpenMainUi += Open;
            Interface.UiBuilder.OpenConfigUi += Open;
        }
        catch { Dispose(); throw; }
    }

    private static TreasureItem RefreshItem(TreasureItem item)
    {
        var available = Data.GetExcelSheet<Item>().TryGetRow(item.Id, out var row) && !string.IsNullOrWhiteSpace(row.Name.ToString());
        return item with { Available = available, Discardable = available && !row.IsIndisposable && item.Id > 19,
            Name = available ? row.Name.ToString() : item.Name,
            IsLooseMateria = available && row.ItemSearchCategory.RowId == 57 && row.EquipSlotCategory.RowId == 0,
            Category = available ? Categories.Classify(item.Id, item.EnglishName, row.ItemSearchCategory.RowId, row.ItemUICategory.Value.Name.ToString(), row.ItemAction.Value.Action.RowId) : item.Category };
    }

    private static TreasureItem? FindItem(uint id)
    {
        if (id == 0 || !Data.GetExcelSheet<Item>().TryGetRow(id, out var row) || string.IsNullOrWhiteSpace(row.Name.ToString())) return null;
        return RefreshItem(new(id, row.Name.ToString(), "", "其他", true, !row.IsIndisposable && id > 19, ["手動新增"]));
    }

    internal TreasureItem[] SearchItems(string query)
    {
        query = query.Trim();
        if (uint.TryParse(query, out var id)) return FindItem(id) is { } item ? [item] : [];
        if (query.Length < 2) return [];
        return Data.GetExcelSheet<Item>().Where(i => i.Name.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(80).Select(i => FindItem(i.RowId)!).ToArray();
    }

    internal void AddCustom(uint vaultId, uint itemId)
    {
        var index = Array.FindIndex(Catalog.Vaults, v => v.Id == vaultId);
        if (index < 0 || Catalog.Vaults[index].Items.Any(i => i.Id == itemId) || FindItem(itemId) is not { } item) return;
        if (!Config.CustomItemsByVault.TryGetValue(vaultId, out var custom) || custom is null)
            Config.CustomItemsByVault[vaultId] = custom = [];
        custom.Add(itemId);
        var vault = Catalog.Vaults[index] with { Items = [.. Catalog.Vaults[index].Items, item] };
        Catalog.Vaults[index] = vault;
        itemIndex.TryAdd(itemId, item);
        Config.ConfiguredItems.Add(itemId);
        SaveConfig();
        Status = $"已加入 {vault.Name}：{item.Name}。" + (SelectedItems.Contains(itemId) ? "此物品已在全域丟棄清單。" : "預設保留，可在清單勾選丟棄。");
    }

    internal void SetSelected(uint vaultId, IEnumerable<TreasureItem> items, bool discard)
    {
        var vault = Catalog.Vaults.FirstOrDefault(v => v.Id == vaultId);
        if (vault is null) return;
        var known = vault.Items.Select(i => i.Id).ToHashSet();
        CleanupPolicy.SetSelection(SelectedItems, items.Where(i => known.Contains(i.Id)).Select(i => itemIndex[i.Id]), discard,
            Config.ConfiguredItems);
        SaveConfig();
        Status = Armed ? "全域丟棄清單已更新，所有地點立即套用。已送出的請求仍可能完成。" : "全域丟棄清單已儲存；啟動後不分地點處理所有已勾選物品。";
    }

    internal void SetDiscoveryDefault(uint vaultId, bool discard)
    {
        var vault = Catalog.Vaults.FirstOrDefault(v => v.Id == vaultId && !v.IsOutdoor);
        if (vault is null) return;
        Config.DiscardNewByVault[vaultId] = discard;
        SaveConfig();
        Status = $"{vault.Name}：新補錄物品預設{(discard ? "自動丟棄" : "保留")}；既有物品設定不變。";
    }

    internal int RecordDiscovered(uint vaultId, IEnumerable<LootGain> gains, DateTimeOffset when)
    {
        var index = Array.FindIndex(Catalog.Vaults, v => v.Id == vaultId && !v.IsOutdoor && v.Available);
        if (index < 0) return 0;
        if (!Config.DiscoveredByVault.TryGetValue(vaultId, out var records) || records is null)
            Config.DiscoveredByVault[vaultId] = records = [];
        var added = 0;
        var changed = false;
        foreach (var gain in gains)
        {
            if (gain.ItemId <= 1 || gain.Quantity <= 0) continue;
            var first = !records.TryGetValue(gain.ItemId, out var previous) || previous is null;
            // Known observations need only a timestamp update; no sheet lookup, catalog copy or sort.
            if (!first && itemIndex.ContainsKey(gain.ItemId))
            {
                records[gain.ItemId] = LootDiscoveryCatalog.Observe(previous, gain.ItemId, when);
                changed = true;
                continue;
            }
            var item = itemIndex.GetValueOrDefault(gain.ItemId) ?? FindItem(gain.ItemId);
            if (item is null) continue;
            records[item.Id] = LootDiscoveryCatalog.Observe(previous, item.Id, when);
            Catalog.Vaults[index] = LootDiscoveryCatalog.Merge(Catalog.Vaults[index], item);
            itemIndex.TryAdd(item.Id, item);
            changed = true;
            if (!first) continue;
            added++;
            var applied = DiscoverySelectionPolicy.ApplyDefault(vaultId, item, Config.DiscardNewByVault,
                Config.ConfiguredItems, SelectedItems);
            var disposition = item.Id == CleanupPolicy.DeepMapId ? "深層地圖固定保留" :
                !CleanupPolicy.CanSelect(item) ? "不可丟棄，保留" :
                applied ? (SelectedItems.Contains(item.Id) ? "依本庫預設加入全域丟棄" : "依本庫預設保留") :
                SelectedItems.Contains(item.Id) ? "沿用全域丟棄勾選" : "沿用既有保留設定";
            Record($"自動補錄 / {Catalog.Vaults[index].Name}：{item.Name}（ID {item.Id}，本次增加 {gain.Quantity}）；{disposition}。");
        }
        if (changed)
        {
            deferredSave.MarkDirty(Environment.TickCount64);
            if (added > 0) FlushConfig(true); // Persist new discoveries immediately; coalesce repeat timestamps.
        }
        return added;
    }

    internal void SaveConfig()
    {
        Interface.SavePluginConfig(Config);
        deferredSave.Saved();
    }

    private void FlushConfig(bool force = false)
    {
        try { deferredSave.Flush(Environment.TickCount64, saveConfig, force); }
        catch (Exception e) { Log.Error(e, "TreasureKeeperGlobal config save failed; will retry"); }
    }

    internal void SetHighQuality(bool value)
    {
        SetArmed(false);
        Config.IncludeHighQuality = value;
        SaveConfig();
        Slots = [];
        Status = "HQ 設定已儲存，自動丟棄已停止。";
    }

    internal void SetArmed(bool value)
    {
        if (!value) { Armed = false; Status = "已停止。已送出的請求仍可能完成。"; return; }
        if (tracker.Pending is not null) { Status = "上一筆丟棄請求尚未完成，請稍候。"; return; }
        if (!Client.IsLoggedIn || !Player.IsLoaded || Objects.LocalPlayer is null) { Status = "登入角色後才能啟動。"; return; }
        if (SelectedItems.Count == 0 && !DiscoverySelectionPolicy.CanAwaitDiscovery(Catalog.Vaults,
            Config.AutoDiscoverLoot, Config.DiscardNewByVault)) { Status = "尚未勾選可丟棄物品，也未設定新補錄自動丟棄。"; return; }
        tracker.Reset();
        armedCharacter = Player.ContentId;
        Armed = true;
        Status = "已啟動全域自動丟棄：所有地點依勾選清單處理整疊。";
        Record(Status);
    }

    private void OnTerritoryChanged(uint zone)
    {
        Slots = [];
        FlushConfig(true);
        Discovery.Reset();
        Maps.Cancel("已轉場，停止本次解讀。");
        tracker.OnTerritoryChanged();
        if (Armed) Status = "轉場中，保持全域丟棄啟用；背包載入後自動繼續。";
    }

    internal static bool Loading => Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.BetweenAreas51];
    internal static bool ActivityBusy => Conditions[ConditionFlag.InCombat] || Conditions[ConditionFlag.Mounted] ||
        Conditions[ConditionFlag.WatchingCutscene] || Conditions[ConditionFlag.WatchingCutscene78] ||
        Conditions[ConditionFlag.OccupiedInCutSceneEvent] || Conditions[ConditionFlag.TradeOpen] ||
        Conditions[ConditionFlag.Crafting] || Conditions[ConditionFlag.Gathering] || Conditions[ConditionFlag.Casting] ||
        Client.IsGPosing || Objects.LocalPlayer is null || Objects.LocalPlayer.IsDead;
    internal static bool Busy => ActivityBusy || Conditions[ConditionFlag.Occupied] || Conditions[ConditionFlag.Occupied30] || Conditions[ConditionFlag.Occupied33] ||
        Conditions[ConditionFlag.Occupied38] || Conditions[ConditionFlag.Occupied39] ||
        Conditions[ConditionFlag.OccupiedInQuestEvent] || Conditions[ConditionFlag.OccupiedSummoningBell];

    private unsafe bool TryReadInventory(out List<BagSlot> slots)
    {
        slots = new(140);
        var inventory = InventoryManager.Instance();
        if (inventory == null) return false;
        for (var bag = 0; bag < 4; bag++)
        {
            var container = inventory->GetInventoryContainer((InventoryType)bag);
            if (container == null || !container->IsLoaded || container->Size != 35) return false;
            for (var index = 0; index < container->Size; index++)
            {
                var item = container->GetInventorySlot(index);
                if (item == null) return false;
                if (item->ItemId == 0) continue;
                var looseMateria = itemIndex.GetValueOrDefault(item->ItemId)?.IsLooseMateria == true;
                var melded = false;
                if (!looseMateria)
                    for (var m = 0; m < 5; m++) melded |= item->Materia[m] != 0;
                var protection = SlotProtection.Evaluate(item->IsSymbolic, (byte)item->Flags, Config.IncludeHighQuality, looseMateria,
                    looseMateria ? (ushort)0 : item->SpiritbondOrCollectability, looseMateria ? 0u : item->GlamourId,
                    !looseMateria && (item->Stains[0] != 0 || item->Stains[1] != 0), melded);
                slots.Add(new(bag, index, item->ItemId, item->Quantity, protection != DiscardBlockReason.None, protection, (byte)item->Flags));
            }
        }
        return true;
    }

    internal static unsafe bool DialogOpen()
    {
        foreach (var name in BlockingDialogs)
        {
            var addon = (AtkUnitBase*)Gui.GetAddonByName(name).Address;
            if (addon != null && addon->IsVisible) return true;
        }
        return false;
    }

    private static readonly string[] BlockingDialogs = ["SelectYesno", "InputNumeric", "ContextMenu", "Shop", "RetainerSell", "RetainerList"];

    private unsafe void Update(IFramework _)
    {
        if (disposed) return;
        Maps.Update();
        Discovery.Update();
        FlushConfig();
        if (!Armed && tracker.Pending is null && !window.IsOpen && !mapWindow.IsOpen) return;
        var now = Environment.TickCount64;
        if (now - lastScan < 750) return;
        lastScan = now;
        try
        {
            // Player objects disappear during zone loading; this is not a request to stop.
            // Actual logout is handled by the Logout event, with a separate character check below.
            if (Loading || !Client.IsLoggedIn || !Player.IsLoaded || Player.ContentId == 0 || Objects.LocalPlayer is null)
            { Slots = []; if (Armed) Status = "等待角色與背包載入，全域丟棄保持啟用。"; return; }
            if (Armed && armedCharacter != Player.ContentId)
            { Armed = false; Slots = []; tracker.Reset(); Status = "角色已變更，需重新啟動。"; return; }
            if (!TryReadInventory(out var inventory)) { Slots = []; if (Armed) Status = "等待一般背包完整載入。"; return; }
            if (!Slots.SequenceEqual(inventory)) Slots = inventory;
            var pending = tracker.Pending;
            var outcome = tracker.Observe(inventory, Client.TerritoryType, now);
            if (outcome == RequestResult.RescanAfterTransition)
            {
                Status = Armed ? "轉場後背包已載入，下一次掃描繼續全域丟棄。" : "已停止，轉場前的請求追蹤已結束。";
                Record(Status);
                return;
            }
            if (outcome == RequestResult.TimedOut)
            {
                Armed = false;
                Status = "8 秒內未確認數量減少，已停止；請檢查遊戲提示再手動啟動。";
                Record(Status);
                return;
            }
            if (outcome == RequestResult.InventoryChanged && pending is not null)
                Record($"背包數量已減少：物品 ID {pending.ItemId} × {pending.Quantity}。");
            if (!Armed) return;
            if (tracker.Pending is not null) { Status = "等待上一筆丟棄請求的背包更新。"; return; }
            if (Maps.IsBusy) { Status = "正在解讀地圖，暫停丟棄。"; return; }
            if (Busy || DialogOpen()) { Status = "戰鬥、角色忙碌或其他操作視窗開啟，暫停丟棄。"; return; }
            var candidate = inventory.FirstOrDefault(s => CleanupPolicy.Eligible(s, itemIndex.GetValueOrDefault(s.ItemId), SelectedItems));
            if (candidate is null)
            {
                var blocked = inventory.FirstOrDefault(s => SelectedItems.Contains(s.ItemId));
                if (blocked is null)
                {
                    lastBlockedSummary = null;
                    Status = "全域自動丟棄：目前沒有符合勾選清單的物品，持續等待。";
                }
                else
                {
                    var reason = CleanupPolicy.BlockReason(blocked, itemIndex.GetValueOrDefault(blocked.ItemId), SelectedItems);
                    var name = itemIndex.GetValueOrDefault(blocked.ItemId)?.Name ?? blocked.ItemId.ToString();
                    Status = $"已勾選但暫不丟棄：{name}（{CleanupPolicy.Describe(reason)}）。詳見全域預覽。";
                    var summary = $"[DiscardBlocked] item={blocked.ItemId}; bag={blocked.Bag}; slot={blocked.Slot}; flags=0x{blocked.RawFlags:X2}; reason={reason}";
                    if (summary != lastBlockedSummary) { lastBlockedSummary = summary; Log.Information(summary); }
                }
                return;
            }
            lastBlockedSummary = null;
            // Native discard consumes a whole slot; validate its identity again immediately before sending.
            if (Loading || Busy || DialogOpen() || !TryReadInventory(out var fresh) || !fresh.Contains(candidate) ||
                !CleanupPolicy.Eligible(candidate, itemIndex.GetValueOrDefault(candidate.ItemId), SelectedItems)) return;
            tracker.Start(candidate, fresh, Client.TerritoryType, now);
            var result = InventoryManager.Instance()->DiscardItem((InventoryType)candidate.Bag, (ushort)candidate.Slot);
            Status = $"已送出：{itemIndex[candidate.ItemId].Name} × {candidate.Quantity}；等待背包更新。";
            Record($"全域 / {Status} (native={result})");
        }
        catch (Exception e)
        {
            Armed = false;
            Status = "背包處理發生錯誤，已停止；請查看 Dalamud 記錄。";
            Log.Error(e, "TreasureKeeperGlobal inventory update failed");
        }
    }

    private void Record(string text)
    {
        History.Enqueue($"{DateTime.Now:HH:mm:ss}  {text}");
        while (History.Count > 60) History.Dequeue();
        HistorySnapshot = History.Reverse().ToArray();
        Log.Information(text);
    }

    private void Open() => window.IsOpen = true;
    internal void OpenMaps() => mapWindow.IsOpen = true;
    private void OnMapCommand(string command, string arguments)
    {
        var arg = arguments.Trim();
        if (arg.Equals("stop", StringComparison.OrdinalIgnoreCase)) Maps.Cancel("已取消本次解讀。已送出的解讀請求仍可能完成。");
        else if (uint.TryParse(arg, out var id)) Maps.Request(id);
        else OpenMaps();
    }
    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
        { SetArmed(false); Chat.Print("[TreasureKeeperGlobal] 已停止自動丟棄。"); }
        else Open();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Armed = false;
        Release(() => Framework.Update -= Update);
        Release(() => Client.TerritoryChanged -= OnTerritoryChanged);
        Release(() => Client.Logout -= logout);
        Release(() => Interface.UiBuilder.Draw -= windows.Draw);
        Release(() => Interface.UiBuilder.OpenMainUi -= Open);
        Release(() => Interface.UiBuilder.OpenConfigUi -= Open);
        if (keepCommandRegistered) Release(() => Commands.RemoveHandler("/tkeep"));
        if (mapCommandRegistered) Release(() => Commands.RemoveHandler("/tmap"));
        Release(() => Maps?.Dispose());
        Release(() => Discovery?.Dispose());
        Release(windows.RemoveAllWindows);
        FlushConfig(true);
    }

    private static void Release(System.Action release)
    {
        try { release(); }
        catch (Exception e) { Log.Error(e, "TreasureKeeperGlobal cleanup failed; continuing remaining cleanup"); }
    }
}
