using System.Numerics;
using Dalamud.Bindings.ImGui;
using TreasureKeeperGlobal.Core;

namespace TreasureKeeperGlobal.Ui;

public sealed record MapViewState(IReadOnlyList<TreasureMapItem> Maps, IReadOnlyList<BagSlot> Slots,
    bool Enabled, bool AutoConfirm, bool Available, bool Busy, string Status);
public sealed record MapActions(Action<bool> Enable, Action<bool> AutoConfirm, Action<uint, int, int> Place,
    Action<uint> Decipher, Action Cancel);

public sealed class MapView
{
    public string Search = "";
    public int Bar;
    public int Slot;
    public bool InBagOnly;
    private readonly InventoryCountCache inventoryCounts = new();
    private static readonly Vector4 Accent = new(0.43f, 0.82f, 0.73f, 1);
    private static readonly Vector4 Muted = new(0.64f, 0.69f, 0.75f, 1);
    private static readonly string Bars = string.Join('\0', Enumerable.Range(1, 10).Select(i => $"快捷欄 {i}")
        .Concat(Enumerable.Range(1, 8).Select(i => $"十字快捷欄 {i}"))) + '\0';
    private static readonly string StandardSlots = string.Join('\0', Enumerable.Range(1, 12).Select(i => $"第 {i} 格")) + '\0';
    private static readonly string CrossSlots = string.Join('\0', Enumerable.Range(1, 16).Select(i => $"第 {i} 格")) + '\0';
    private static float U(float value) => value * ImGui.GetFontSize() / 17;

    public void Draw(MapViewState state, MapActions actions)
    {
        ImGui.TextColored(Accent, "MAP TOOLS  /  地圖快捷解讀");
        ImGui.TextWrapped("選擇快捷欄空格，再將地圖放入。按下該快捷鍵，即可解讀並自動確認。");
        var enabled = state.Enabled;
        ImGui.BeginDisabled(!state.Available);
        if (ImGui.Checkbox("啟用快捷解讀", ref enabled)) actions.Enable(enabled);
        ImGui.SameLine();
        var confirm = state.AutoConfirm;
        if (ImGui.Checkbox("自動確認本次解讀", ref confirm)) actions.AutoConfirm(confirm);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!state.Busy);
        if (ImGui.Button("取消本次操作")) actions.Cancel();
        ImGui.EndDisabled();
        ImGui.TextWrapped(state.Status);
        ImGui.TextColored(Muted, $"本機可辨識 {state.Maps.Count} 種寶圖 · /tmap 開啟此視窗");
        ImGui.TextWrapped("已有解讀中的寶圖時會停止；每次按鍵只解讀一張。地圖需在一般背包，且角色停止移動、非戰鬥。");
        ImGui.TextWrapped("卡住可再按同一快捷鍵立即重試；深層地圖仍固定保留，但可按鍵解讀。");
        ImGui.Separator();

        ImGui.SetNextItemWidth(U(170));
        if (ImGui.Combo("目標快捷欄", ref Bar, Bars)) Slot = Math.Min(Slot, Bar < 10 ? 11 : 15);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(U(100));
        ImGui.Combo("格子", ref Slot, Bar < 10 ? StandardSlots : CrossSlots);
        ImGui.TextColored(Muted, "只寫入所選空格；已有內容的格子會提示另選位置。");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##mapSearch", "搜尋地圖名稱 / 物品 ID", ref Search, 128);
        ImGui.Checkbox("只看背包持有", ref InBagOnly);

        var counts = inventoryCounts.Get(state.Slots);
        var query = Search.Trim();
        var maps = state.Maps.Where(i => (!InBagOnly || counts.GetValueOrDefault(i.Id) > 0) &&
            (query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Id.ToString().Contains(query))).ToArray();
        ImGui.TextColored(Muted, $"顯示 {maps.Length} 種 · 未持有的地圖也可先放入快捷欄");
        if (!ImGui.BeginTable("maps", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new Vector2(0, Math.Max(U(80), ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupColumn("地圖", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("背包", ImGuiTableColumnFlags.WidthFixed, U(42));
        ImGui.TableSetupColumn("快捷欄", ImGuiTableColumnFlags.WidthFixed, U(100));
        ImGui.TableSetupColumn("解讀", ImGuiTableColumnFlags.WidthFixed, U(65));
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        foreach (var map in maps)
        {
            ImGui.PushID((int)map.Id);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.TextWrapped(map.Name);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"物品 ID {map.Id}\n也可使用指令 /tmap {map.Id}");
            ImGui.TableNextColumn(); ImGui.Text(counts.GetValueOrDefault(map.Id).ToString());
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(state.Busy);
            if (ImGui.Button("放入空格")) actions.Place(map.Id, Bar, Slot);
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!state.Enabled || !state.Available || counts.GetValueOrDefault(map.Id) == 0);
            if (ImGui.Button(state.Busy ? "重試" : "解讀")) actions.Decipher(map.Id);
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        ImGui.EndTable();
    }
}
