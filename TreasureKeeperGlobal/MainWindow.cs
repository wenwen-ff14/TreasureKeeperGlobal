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
            plugin.OpenMaps, plugin.Discovery.SetEnabled, plugin.SetDiscoveryDefault);
    }

    public override void Draw() => view.Draw(new(plugin.Catalog, plugin.SelectedItems, plugin.Slots,
        Plugin.Client.TerritoryType, plugin.Armed, plugin.Status, plugin.Config.IncludeHighQuality, plugin.HistorySnapshot,
        new(plugin.Config.AutoDiscoverLoot, plugin.Discovery.Status, plugin.Config.DiscoveredByVault,
            plugin.Config.DiscardNewByVault)), actions);
}
