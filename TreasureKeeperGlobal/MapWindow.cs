using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TreasureKeeperGlobal.Ui;

namespace TreasureKeeperGlobal;

internal sealed class MapWindow : Window
{
    private readonly Plugin plugin;
    private readonly MapView view = new();
    private readonly MapActions actions;

    public MapWindow(Plugin plugin) : base("地圖快捷解讀###TreasureKeeperGlobalMaps")
    {
        this.plugin = plugin;
        Size = new Vector2(960, 740);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(820, 620), MaximumSize = new Vector2(2200, 1800) };
        actions = new(plugin.Maps.SetEnabled, plugin.Maps.SetAutoConfirm, plugin.Maps.PlaceOnHotbar,
            id => plugin.Maps.Request(id), () => plugin.Maps.Cancel("已取消本次解讀。已送出的解讀請求仍可能完成。"));
    }

    public override void Draw() => view.Draw(new(plugin.Maps.Maps, plugin.Slots, plugin.Config.MapHotbarEnabled,
        plugin.Config.AutoConfirmDecipher, plugin.Maps.Available, plugin.Maps.IsBusy, plugin.Maps.Status), actions);
}
