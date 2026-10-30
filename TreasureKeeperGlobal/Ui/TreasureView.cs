using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureKeeperGlobal.Core;

namespace TreasureKeeperGlobal.Ui;

public sealed record TreasureViewState(TreasureCatalog Catalog, IReadOnlySet<uint> Selections,
    IReadOnlyList<BagSlot> Slots, uint Territory, bool Armed, string Status, bool IncludeHighQuality, string[] History,
    DiscoveryViewState? Discovery = null);
public sealed record DiscoveryViewState(bool Enabled, string Status, IReadOnlyDictionary<uint, Dictionary<uint, DiscoveredLoot>> Records,
    IReadOnlyDictionary<uint, bool>? DiscardNewByVault = null);
public sealed record TreasureActions(Action<bool> Arm, Action<uint, IEnumerable<TreasureItem>, bool> Select,
    Action<bool> SetHighQuality, Func<string, TreasureItem[]> Search, Action<uint, uint> AddCustom, Action? OpenMaps = null,
    Action<bool>? SetDiscovery = null, Action<uint, bool>? SetDiscoveryDefault = null, Func<uint, Vector2, bool>? DrawIcon = null);

public sealed class TreasureView
{
    public uint VaultId { get; set; } = 55002;
    public string Search = "";
    public int Category;
    public bool DiscoveredOnly;
    private bool selectedOnly;
    private bool inBagOnly;
    private string customSearch = "";
    private TreasureItem[] customResults = [];
    private readonly InventoryCountCache inventoryCounts = new();
    private readonly CatalogIndexCache catalogIndex = new();
    private TreasureItem[]? sortedSnapshot;
    private TreasureItem[] sortedItems = [];
    private readonly ObservationOrderCache observationOrder = new();
    private DiscoveredLoot[]? datedSnapshot;
    private string[] datedLabels = [];
    private static readonly string CategoryLabels = string.Join('\0', Categories.All) + '\0';
    private static readonly Vector4 Accent = new(0.43f, 0.82f, 0.73f, 1);
    private static readonly Vector4 Muted = new(0.64f, 0.69f, 0.75f, 1);
    private static readonly Vector4 Warning = new(1, 0.75f, 0.40f, 1);
    private static float U(float size) => size * ImGui.GetFontSize() / 17;

    public void Draw(TreasureViewState state, TreasureActions actions)
    {
        ImGui.TextColored(Accent, "TREASURE KEEPER  /  寶物庫整理助手");
        ImGui.TextWrapped("勾選物品全域丟棄，不限地點；各清單的同一物品同步勾選。深層地圖固定保留。");
        ImGui.Spacing();
        if (ImGui.Button(state.Armed ? "停止自動丟棄" : "啟動全域自動丟棄", new Vector2(U(170), U(30)))) actions.Arm(!state.Armed);
        ImGui.SameLine();
        var hq = state.IncludeHighQuality;
        if (ImGui.Checkbox("包含已勾選物品的 HQ", ref hq)) actions.SetHighQuality(hq);
        if (actions.OpenMaps is not null) { ImGui.SameLine(); if (ImGui.Button("地圖快捷解讀")) actions.OpenMaps(); }
        ImGui.TextColored(state.Armed ? Accent : Muted, state.Armed ? "狀態：已啟動" : "狀態：已停止");
        ImGui.TextWrapped(state.Status);
        ImGui.PushStyleColor(ImGuiCol.Text, Warning);
        ImGui.TextWrapped("丟棄無法復原，會處理一般背包的整疊數量（包含進場前持有的物品）。");
        ImGui.PopStyleColor();
        ImGui.TextWrapped("啟動後勾選立即生效。戰鬥／轉場暫停；重新登入或載入插件後需手動啟動。");
        ImGui.Separator();

        ImGui.BeginChild("vaults", new Vector2(U(245), 0), true);
        ImGui.TextColored(Muted, "掉落來源 / 共用全域勾選");
        foreach (var vault in state.Catalog.Vaults)
        {
            ImGui.PushID((int)vault.Id);
            var count = 0;
            foreach (var item in vault.Items) if (state.Selections.Contains(item.Id)) count++;
            if (ImGui.Selectable(vault.Name, VaultId == vault.Id)) { VaultId = vault.Id; Search = ""; Category = 0; }
            ImGui.TextColored(Muted, $"{vault.Map} · {vault.Items.Length} 項 · 勾選 {count}");
            if (!vault.Available) ImGui.TextColored(Warning, "本機尚無此區域");
            if (vault.ContainsTerritory(state.Territory)) ImGui.TextColored(Accent, vault.IsOutdoor ? "目前在 G10 外場區域" : "目前所在寶物庫");
            ImGui.Spacing();
            ImGui.PopID();
        }
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("content", Vector2.Zero, false);
        var selectedVault = state.Catalog.Vaults.First(v => v.Id == VaultId);
        var selected = state.Selections;
        ImGui.TextColored(Accent, selectedVault.Name);
        ImGui.TextColored(Muted, selectedVault.EnglishName);
        if (!selectedVault.Available) ImGui.TextWrapped("本機尚無此寶物庫區域；本機已存在的物品仍可勾選全域丟棄。");
        if (ImGui.BeginTabBar("tabs"))
        {
            if (ImGui.BeginTabItem("物品清單")) { DrawItems(state, actions, selectedVault, selected); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("全域待丟棄預覽")) { DrawPreview(state); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("補充物品")) { DrawCustom(actions, selectedVault); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("自動補錄")) { DrawDiscovery(state, actions, selectedVault); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("記錄與資料")) { DrawDetails(state); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.EndChild();
    }

    private void DrawItems(TreasureViewState state, TreasureActions actions, TreasureVault vault, IReadOnlySet<uint> selected)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "搜尋名稱 / 英文 / 物品 ID", ref Search, 128);
        ImGui.SetNextItemWidth(U(140));
        ImGui.Combo("分類", ref Category, CategoryLabels);
        ImGui.Checkbox("只看已勾選", ref selectedOnly);
        ImGui.SameLine(); ImGui.Checkbox("背包持有", ref inBagOnly);
        ImGui.SameLine(); ImGui.Checkbox("只看自動補錄", ref DiscoveredOnly);
        var counts = inventoryCounts.Get(state.Slots);
        if (!ReferenceEquals(sortedSnapshot, vault.Items))
        {
            sortedSnapshot = vault.Items;
            sortedItems = vault.Items.OrderBy(i => i.Id == CleanupPolicy.DeepMapId ? 0 : CleanupPolicy.CanSelect(i) ? 1 : 2)
                .ThenBy(i => i.Category).ThenBy(i => i.Name).ToArray();
        }
        var query = Search.Trim();
        var rows = sortedItems.Where(i => (Category == 0 || i.Category == Categories.All[Category]) &&
            (!selectedOnly || selected.Contains(i.Id)) && (!inBagOnly || counts.GetValueOrDefault(i.Id) > 0) &&
            (!DiscoveredOnly || i.Sources.Contains(LootDiscoveryCatalog.Source)) &&
            (query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase) || i.ChineseName.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Id.ToString().Contains(query))).ToArray();
        if (ImGui.Button("勾選篩選結果")) actions.Select(vault.Id, rows, true);
        ImGui.SameLine();
        if (ImGui.Button("取消篩選勾選")) actions.Select(vault.Id, rows, false);
        ImGui.TextColored(Muted, $"顯示 {rows.Length} / {vault.Items.Length} 項 · 全域共勾選 {selected.Count} 項");
        ImGui.TextWrapped("一鍵操作只影響上方篩選結果；深層地圖、水晶及無法丟棄的物品不會被勾選。");
        if (!ImGui.BeginTable("items", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH, new Vector2(0, Math.Max(U(80), ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupColumn("丟棄", ImGuiTableColumnFlags.WidthFixed, U(42));
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("分類", ImGuiTableColumnFlags.WidthFixed, U(82));
        ImGui.TableSetupColumn("背包", ImGuiTableColumnFlags.WidthFixed, U(44));
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        foreach (var item in rows)
        {
            ImGui.PushID((int)item.Id);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            var discard = selected.Contains(item.Id);
            ImGui.BeginDisabled(!CleanupPolicy.CanSelect(item));
            if (ImGui.Checkbox("##discard", ref discard)) actions.Select(vault.Id, [item], discard);
            ImGui.EndDisabled();
            ImGui.TableNextColumn(); DrawItem(item, actions);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"ID {item.Id}  {item.EnglishName}");
                ImGui.TextWrapped("來源：" + string.Join(" / ", item.Sources));
                if (item.Sources.Any(s => s.StartsWith("Community-observed-"))) ImGui.TextWrapped("社群補錄：經分享的庫內取得紀錄，按實際寶物庫歸類。");
                if (item.Sources.All(s => s.StartsWith("FF14house-"))) ImGui.TextWrapped("僅有 4.05 實測紀錄；現版掉落尚待實測核對。");
                ImGui.EndTooltip();
            }
            if (item.Id == CleanupPolicy.DeepMapId) ImGui.TextColored(Accent, "固定保留");
            else if (!item.Available) ImGui.TextColored(Warning, "本機尚無此物品");
            else if (!item.Discardable) ImGui.TextColored(Muted, "不可丟棄 / 獨立物品欄");
            else if (item.Sources.All(s => s.StartsWith("FF14house-"))) ImGui.TextColored(Muted, "舊版實測；現版待核對");
            else if (item.Sources.Contains("手動新增")) ImGui.TextColored(Muted, "手動新增");
            if (item.Sources.Contains(LootDiscoveryCatalog.Source)) ImGui.TextColored(Accent, "庫內取得 · 已補錄");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Category);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(counts.GetValueOrDefault(item.Id).ToString());
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawPreview(TreasureViewState state)
    {
        ImGui.TextWrapped("列出全部清單已勾選、目前背包可處理的整疊，不限所在地或正在瀏覽的寶物庫。");
        var byId = catalogIndex.Get(state.Catalog);
        var rows = state.Slots.Where(s => CleanupPolicy.Eligible(s, byId.GetValueOrDefault(s.ItemId), state.Selections)).ToArray();
        ImGui.TextColored(Accent, $"可丟棄 {rows.Length} 疊，共 {rows.Sum(s => s.Quantity)} 個");
        ImGui.TextWrapped("HQ 依上方開關處理；收藏品、染色、投影、鑲嵌及已有精煉度的裝備保留。只處理一般背包 1～4。");
        ImGui.Separator();
        if (rows.Length == 0) ImGui.TextWrapped("目前沒有符合條件的物品。");
        foreach (var slot in rows)
            ImGui.TextWrapped($"背包 {slot.Bag + 1} / 格 {slot.Slot + 1}　{byId[slot.ItemId].Name} × {slot.Quantity}");
        var blocked = state.Slots.Where(s => state.Selections.Contains(s.ItemId) &&
            !CleanupPolicy.Eligible(s, byId.GetValueOrDefault(s.ItemId), state.Selections)).ToArray();
        if (blocked.Length == 0) return;
        ImGui.Separator();
        ImGui.TextColored(Warning, $"已勾選但暫不丟棄：{blocked.Length} 疊");
        foreach (var slot in blocked)
        {
            var item = byId.GetValueOrDefault(slot.ItemId);
            var reason = CleanupPolicy.BlockReason(slot, item, state.Selections);
            ImGui.TextWrapped($"{item?.Name ?? slot.ItemId.ToString()} × {slot.Quantity}：{CleanupPolicy.Describe(reason)}");
        }
    }

    private void DrawCustom(TreasureActions actions, TreasureVault vault)
    {
        ImGui.TextWrapped("輸入至少兩個字的遊戲物品名稱，或完整物品 ID，補入目前清單。全新物品預設保留；已在其他清單勾選的同一物品會共用勾選。");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##custom", "輸入遊戲名稱或 ID", ref customSearch, 128);
        if (ImGui.Button("搜尋遊戲物品")) customResults = actions.Search(customSearch);
        ImGui.TextColored(Muted, $"{customResults.Length} 筆結果（最多 80 筆）");
        foreach (var item in customResults)
        {
            ImGui.PushID((int)item.Id);
            var exists = vault.Items.Any(i => i.Id == item.Id);
            ImGui.BeginDisabled(exists);
            if (ImGui.SmallButton(exists ? "已加入" : "加入清單")) actions.AddCustom(vault.Id, item.Id);
            ImGui.EndDisabled(); ImGui.SameLine();
            DrawItem(item, actions);
            ImGui.TextUnformatted($"{item.Category} · ID {item.Id}");
            ImGui.PopID();
        }
    }

    private void DrawDiscovery(TreasureViewState state, TreasureActions actions, TreasureVault vault)
    {
        if (state.Discovery is not { } discovery) { ImGui.TextWrapped("自動補錄狀態尚未載入。"); return; }
        var enabled = discovery.Enabled;
        if (ImGui.Checkbox("自動偵測庫內取得物品並補入清單", ref enabled)) actions.SetDiscovery?.Invoke(enabled);
        ImGui.TextWrapped(discovery.Status);
        if (!vault.IsOutdoor)
        {
            ImGui.PushID((int)vault.Id);
            var discardNew = discovery.DiscardNewByVault?.GetValueOrDefault(vault.Id) == true ? 1 : 0;
            ImGui.SetNextItemWidth(U(135));
            if (ImGui.Combo("本庫新補錄預設", ref discardNew, "保留\0自動丟棄\0"))
                actions.SetDiscoveryDefault?.Invoke(vault.Id, discardNew == 1);
            ImGui.PopID();
            ImGui.TextWrapped("各庫獨立設定，只影響首次補錄且未設定過的物品；既有物品可在下方逐項調整。");
        }
        ImGui.TextWrapped("記錄自己一般背包與水晶的新增物品，並依當時所在寶物庫歸類。開啟功能不需啟動自動丟棄。");
        ImGui.TextWrapped("同一物品共用全域設定，跨庫再次補錄沿用原選擇。需啟動全域自動丟棄才會執行，包含原有整疊；深層地圖固定保留。");
        ImGui.TextWrapped("搬移、分堆、合併及進場前持有的物品不補入。一般野外不自動歸入 G10；目前記錄的是庫內取得，並非完整伺服器掉落表。");
        var records = discovery.Records.GetValueOrDefault(vault.Id);
        var observed = observationOrder.Get(records);
        if (!ReferenceEquals(datedSnapshot, observed))
        {
            datedSnapshot = observed;
            datedLabels = observed.Select(r => r.LastSeenUtc.ToLocalTime().ToString("MM-dd HH:mm:ss")).ToArray();
        }
        ImGui.TextColored(Accent, $"{vault.Name} · 已觀測 {records?.Count ?? 0} 種");
        ImGui.Separator();
        if (records is null || records.Count == 0) { ImGui.TextWrapped("這份清單尚無補錄紀錄；進入對應寶物庫後開始取得物品即可。"); return; }
        if (!ImGui.BeginTable("discovery", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new Vector2(0, Math.Max(U(80), ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("目前設定", ImGuiTableColumnFlags.WidthFixed, U(120));
        ImGui.TableSetupColumn("最近取得", ImGuiTableColumnFlags.WidthFixed, U(150));
        ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
        var byId = catalogIndex.Get(state.Catalog);
        for (var row = 0; row < observed.Length; row++)
        {
            var record = observed[row];
            var item = byId.GetValueOrDefault(record.ItemId);
            if (item is null) continue;
            ImGui.PushID((int)item.Id);
            ImGui.TableNextRow(); ImGui.TableNextColumn(); DrawItem(item, actions);
            ImGui.TableNextColumn();
            if (CleanupPolicy.CanSelect(item))
            {
                var discard = state.Selections.Contains(item.Id) ? 1 : 0;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##disposition", ref discard, "保留\0自動丟棄\0")) actions.Select(vault.Id, [item], discard == 1);
            }
            else ImGui.TextUnformatted(item.Id == CleanupPolicy.DeepMapId ? "固定保留" : !item.Available ? "本機無此物品" : "不可丟棄");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(datedLabels[row]);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static void DrawItem(TreasureItem item, TreasureActions actions)
    {
        if (actions.DrawIcon is not null)
        {
            var size = new Vector2(U(28));
            if (!actions.DrawIcon(item.IconId, size)) ImGui.Dummy(size);
            ImGui.SameLine();
        }
        ImGui.BeginGroup();
        ImGui.TextWrapped(item.Name);
        if (!string.IsNullOrWhiteSpace(item.ChineseName) && item.ChineseName != item.Name)
            ImGui.TextColored(Muted, item.ChineseName);
        ImGui.EndGroup();
    }

    private static void DrawDetails(TreasureViewState state)
    {
        ImGui.TextWrapped($"清單更新：{state.Catalog.Updated}。本機核對版本：{state.Catalog.ClientVersion}。");
        ImGui.TextWrapped("來源：FFXIV Teamcraft、FFXIV Wiki、灰機維基與掉落實測紀錄。名稱與 ID 使用本機遊戲資料核對。滑鼠停在物品上可看來源；只有 4.05 實測資料的項目會註明。清單無法保證涵蓋伺服器所有獎勵。");
        ImGui.TextWrapped("分庫清單只用於查找掉落；同一物品共用全域勾選，不限地點。每次只送出一筆丟棄，8 秒未確認背包數量減少便停止，不會自動重試。");
        ImGui.Separator();
        ImGui.TextColored(Accent, "本次執行記錄（最新在上，最多 60 筆）");
        if (state.History.Length == 0) ImGui.TextColored(Muted, "尚無執行記錄。");
        foreach (var line in state.History) ImGui.TextWrapped(line);
    }
}
