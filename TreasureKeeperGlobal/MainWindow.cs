using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TreasureKeeperGlobal.Ui;

namespace TreasureKeeperGlobal;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly TreasureView view = new();
    private readonly TreasureActions actions;

    public MainWindow(Plugin plugin) : base("寶物庫整理助手###TreasureKeeperGlobal")
    {
        this.plugin = plugin;
        Size = new Vector2(1050, 780);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(820, 640), MaximumSize = new Vector2(2000, 1800) };
        actions = new(plugin.SetArmed, plugin.SetSelected, plugin.SetHighQuality, plugin.SearchItems, plugin.AddCustom,
            plugin.OpenMaps, plugin.Discovery.SetEnabled, plugin.SetDiscoveryDefault, Plugin.DrawItemIcon);
    }

    private string profileName = "";
    public override void Draw()
    {
        ImGui.BeginDisabled(plugin.ProfileChangePending);
        if (ImGui.BeginCombo("設定集", plugin.ActiveProfile.Name))
        {
            foreach (var entry in plugin.Config.Profiles.Profiles.ToArray())
                if (ImGui.Selectable(entry.Value.Name + "###" + entry.Key, entry.Key == plugin.ActiveProfileId))
                    plugin.SelectProfile(entry.Key);
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##profileName", "設定集名稱", ref profileName, 80);
        ImGui.SameLine();
        if (ImGui.Button("複製目前設定集") && !string.IsNullOrWhiteSpace(profileName)) plugin.CopyProfile(profileName);
        ImGui.SameLine();
        if (ImGui.Button("重新命名") && !string.IsNullOrWhiteSpace(profileName)) plugin.RenameProfile(profileName);
        var bound = plugin.Config.Profiles.CharacterProfiles.ContainsKey(plugin.CurrentCharacter);
        ImGui.BeginDisabled(plugin.CurrentCharacter == 0);
        if (ImGui.Checkbox("此角色登入時套用此設定集", ref bound)) plugin.BindProfile(bound);
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.TextWrapped("各設定集獨立保存丟棄／保留、HQ 與新補錄預設。修改自動儲存；切換後需重新啟動。不同角色要獨立設定時，請先複製設定集再綁定。");
        ImGui.Separator();
        view.Draw(new(plugin.Catalog, plugin.SelectedItems, plugin.Slots,
        Plugin.Client.TerritoryType, plugin.Armed, plugin.Status, plugin.ActiveProfile.IncludeHighQuality, plugin.HistorySnapshot,
        new(plugin.Config.AutoDiscoverLoot, plugin.Discovery.Status, plugin.Config.DiscoveredByVault,
            plugin.ActiveProfile.DiscardNewByVault)), actions);
    }
}
