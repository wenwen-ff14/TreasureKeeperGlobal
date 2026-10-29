using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using TreasureKeeperGlobal.Core;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace TreasureKeeperGlobal;

// The item-hotbar -> inventory context -> Decipher callback sequence was informed by
// Pandora's Box HotbarMapDecipher (BSD-3-Clause); see third-party/NOTICE.md.
internal sealed unsafe class MapDecipherService : IDisposable
{
    private readonly Plugin plugin;
    private readonly MapDecipherFlow flow = new();
    private readonly Hook<ActionManager.Delegates.UseAction>? useAction;
    private readonly string decipherLabel;
    private readonly Dictionary<uint, TreasureMapItem> byId;
    private long dialogGeneration;
    private nint dialogAddress;
    private long lastUpdate;
    private long lastMoved;
    private long lastError;
    private string lastWait = "";
    private Vector3? lastPosition;
    private bool disposed;
    internal TreasureMapItem[] Maps { get; }
    internal bool Available => useAction is not null;
    internal bool IsBusy => flow.Pending is not null;
    internal string Status { get; private set; } = "將寶圖放入快捷欄，按一次即可解讀。";

    public MapDecipherService(Plugin plugin)
    {
        this.plugin = plugin;
        Maps = Plugin.Data.GetExcelSheet<Item>()
            .Where(i => i.FilterGroup == 18 && MapDecipherFlow.IsTreasureMap(i.RowId, i.FilterGroup, i.Name.ToString()))
            .Select(i => new TreasureMapItem(i.RowId, i.Name.ToString(), i.AdditionalData.RowId)).OrderBy(i => i.Id).ToArray();
        byId = Maps.ToDictionary(i => i.Id);
        decipherLabel = Plugin.Data.GetExcelSheet<Addon>().GetRow(8100).Text.ToString();
        try
        {
            useAction = Plugin.Interop.HookFromAddress<ActionManager.Delegates.UseAction>(
                (nint)ActionManager.MemberFunctionPointers.UseAction, UseAction);
            if (plugin.Config.MapHotbarEnabled) useAction.Enable();
        }
        catch (Exception e)
        {
            useAction?.Dispose(); useAction = null;
            Status = "快捷解讀初始化失敗；請查看 Dalamud 記錄。";
            Plugin.Log.Error(e, "Map hotbar hook failed");
        }
        try
        {
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnDialogSetup);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectYesno", OnDialogClosed);
            Plugin.Log.Information($"[MapDecipher] v{typeof(Plugin).Assembly.GetName().Version}; maps={Maps.Length}; hook={Available}; enabled={plugin.Config.MapHotbarEnabled}; autoConfirm={plugin.Config.AutoConfirmDecipher}");
        }
        catch { Dispose(); throw; }
    }

    private void OnDialogSetup(AddonEvent _, AddonArgs args)
    {
        dialogGeneration++;
        dialogAddress = (nint)args.Addon.Address;
        if (IsBusy) Plugin.Log.Information($"[MapDecipher] SelectYesno setup; generation={dialogGeneration}; stage={flow.Stage}");
    }

    private void OnDialogClosed(AddonEvent _, AddonArgs args)
    {
        if (dialogAddress == (nint)args.Addon.Address) dialogAddress = 0;
    }

    private bool UseAction(ActionManager* manager, ActionType type, uint id, ulong target, uint extra,
        ActionManager.UseActionMode mode, uint route, bool* area)
    {
        if (!disposed && plugin.Config.MapHotbarEnabled && type == ActionType.Item && byId.ContainsKey(id))
        {
            if (area != null) *area = false;
            // Consume only this known map action; do not also issue the original item use.
            return Request(id, "快捷欄");
        }
        return useAction!.Original(manager, type, id, target, extra, mode, route, area);
    }

    internal void SetEnabled(bool value)
    {
        Cancel("快捷解讀設定已更新。");
        plugin.Config.MapHotbarEnabled = value;
        if (value) useAction?.Enable(); else useAction?.Disable();
        plugin.SaveConfig();
    }

    internal void SetAutoConfirm(bool value)
    {
        Cancel("自動確認設定已更新；下次解讀生效。");
        plugin.Config.AutoConfirmDecipher = value;
        plugin.SaveConfig();
    }

    internal bool Request(uint id, string source = "指令／地圖工具")
    {
        try
        {
            if (!Available || !plugin.Config.MapHotbarEnabled) return Fail("請先啟用地圖快捷解讀。");
            if (!byId.TryGetValue(id, out var map)) return Fail("此 ID 不是本機可辨識的寶圖。");
            var now = Environment.TickCount64;
            if (IsBusy) return Retry(id, now);
            if (!Ready() || Plugin.Busy || now - lastMoved < 250) return Fail("請在角色停止移動、非戰鬥且可操作時解讀。");
            if (Plugin.DialogOpen() || !plugin.CanStartMap) return Fail("請先完成目前的視窗或丟棄操作。");
            if (DecodedRank() != 0) return Fail("已有解讀中的寶圖，請先完成目前尋寶。");
            if (!TryFind(id, out var slot)) return Fail("一般背包沒有這張地圖，或背包尚未完整載入。");
            var prompt = Plugin.StringEvaluator.EvaluateFromAddon(8101, [id]).ExtractText();
            if (!prompt.Contains(map.Name, StringComparison.Ordinal)) return Fail("無法核對本機解讀提示，已停止。");
            if (!flow.Start(map, slot!, Plugin.Player.ContentId, Plugin.Client.TerritoryType, now, prompt)) return false;
            lastWait = "等待開始解讀";
            Plugin.Log.Information($"[MapDecipher] Request from {source}; item={id}; bag={slot!.Bag}; slot={slot.Slot}; rank={map.Rank}");
            Status = $"準備解讀：{map.Name}。";
            return true;
        }
        catch (Exception e) { Cancel("解讀操作發生錯誤，已停止。"); Plugin.Log.Error(e, "Map decipher request failed"); return false; }
    }

    private bool Retry(uint id, long now)
    {
        var request = flow.Pending!;
        if (!Ready() || Plugin.Player.ContentId != request.Character || Plugin.Client.TerritoryType != request.Territory)
        { Cancel("角色或區域已變更，請在載入完成後再次解讀。"); return false; }
        if (DecodedRank() != 0)
        { Cancel("已有解讀中的寶圖，請先完成目前尋寶。"); return false; }
        if (id != request.Map.Id) return Fail($"正在處理 {request.Map.Name}；可再按同一張重試，或取消本次操作。");
        var current = ReadSlot(request.Slot.Bag, request.Slot.Slot);
        if (current != request.Slot)
        { Cancel("地圖所在格或數量已變更，請確認背包後再次按鍵。"); return false; }
        if (Plugin.ActivityBusy || now - lastMoved < 250)
            return Fail("角色仍在移動、施法或其他活動；可操作時即可再次按鍵重試。");
        // Reuse the current matching menu/dialog. Never close or accept an unrelated popup.
        if (flow.Stage == DecipherStage.Confirmation && dialogGeneration == flow.ExpectedDialogGeneration)
        {
            var addon = (AtkUnitBase*)Plugin.Gui.GetAddonByName("SelectYesno").Address;
            if (addon != null && addon->IsVisible && (nint)addon == dialogAddress)
            { Confirm(request, current, now); return true; }
        }
        if (flow.Stage == DecipherStage.ContextMenu)
        {
            var menu = (AtkUnitBase*)Plugin.Gui.GetAddonByName("ContextMenu").Address;
            if (menu != null && menu->IsVisible) { ChooseDecipher(request); return true; }
        }
        if (!plugin.CanStartMap || !flow.Restart(id, current, Plugin.Player.ContentId, Plugin.Client.TerritoryType,
                DecodedRank(), Plugin.DialogOpen(), Plugin.Busy, now))
            return Fail("請先完成目前的視窗或操作，再按快捷鍵即可重試。");
        lastWait = "重新開始解讀";
        Status = $"立即重試：{request.Map.Name}。";
        Plugin.Log.Information($"[MapDecipher] Manual retry; item={id}; bag={current!.Bag}; slot={current.Slot}");
        return true;
    }

    internal void PlaceOnHotbar(uint id, int bar, int slot)
    {
        try { PlaceOnHotbarCore(id, bar, slot); }
        catch (Exception e) { Fail("快捷欄設定失敗，請查看 Dalamud 記錄。"); Plugin.Log.Error(e, "Map hotbar placement failed"); }
    }

    private void PlaceOnHotbarCore(uint id, int bar, int slot)
    {
        if (!byId.TryGetValue(id, out var map) || !MapDecipherFlow.ValidHotbarSlot(bar, slot)) { Fail("快捷欄位置無效。"); return; }
        if (!Ready() || Plugin.Busy || IsBusy) { Fail("請在角色可操作時設定快捷欄。"); return; }
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars == null || !hotbars->ModuleReady || !hotbars->DatFileLoadedSuccessfully || hotbars->PvPHotbarsActive ||
            hotbars->CharacterContentId != Plugin.Player.ContentId)
        { Fail("快捷欄尚未就緒，或目前使用 PvP 快捷欄。"); return; }
        var target = hotbars->GetSlotById((uint)bar, (uint)slot);
        if (target == null || target->CommandType != RaptureHotbarModule.HotbarSlotType.Empty)
        { Fail("這個格子已有內容，請選擇空格。"); return; }
        hotbars->SetAndSaveSlot((uint)bar, (uint)slot, RaptureHotbarModule.HotbarSlotType.Item, id);
        Status = $"已放入{(bar < 10 ? $"快捷欄 {bar + 1}" : $"十字快捷欄 {bar - 9}")} 第 {slot + 1} 格：{map.Name}。";
    }

    internal void Update()
    {
        if (disposed) return;
        var now = Environment.TickCount64;
        if (now - lastUpdate < 100) return;
        lastUpdate = now;
        try
        {
            if (Plugin.Objects.LocalPlayer is { } player)
            {
                if (lastPosition is null || Vector3.DistanceSquared(lastPosition.Value, player.Position) > 0.0001f) lastMoved = now;
                lastPosition = player.Position;
            }
            else lastPosition = null;
            if (flow.Pending is not { } request) return;
            if (!Ready()) { Cancel("角色登出或轉場，已停止解讀。"); return; }
            var previousStage = flow.Stage;
            var observation = flow.Observe(Plugin.Player.ContentId, Plugin.Client.TerritoryType, DecodedRank(), now);
            if (observation != DecipherObservation.Waiting)
            {
                var message = observation == DecipherObservation.Completed ? $"已解讀：{request.Map.Name}。" :
                    observation == DecipherObservation.TimedOut ? $"本次解讀未完成（{lastWait}）；可立即再按快捷鍵重試。" : "角色或區域已變更，已停止解讀。";
                Cancel(message);
                Plugin.Log.Information($"[MapDecipher] {observation}; item={request.Map.Id}; stage={previousStage}; {message}");
                if (observation != DecipherObservation.Completed) Fail(message);
                return;
            }
            if (flow.Stage == DecipherStage.Result) return;
            if (Plugin.ActivityBusy || now - lastMoved < 250)
            { Cancel("角色移動、戰鬥或開始其他活動，已停止本次解讀。"); return; }
            var current = ReadSlot(request.Slot.Bag, request.Slot.Slot);
            if (current != request.Slot) { Cancel("地圖所在格或數量已改變，已停止解讀。"); return; }
            switch (flow.Stage)
            {
                case DecipherStage.Queued:
                    if (Plugin.Busy || Plugin.DialogOpen() || !plugin.CanStartMap || DecodedRank() != 0)
                    { Cancel("目前無法開始解讀，已取消本次操作。"); return; }
                    var context = AgentInventoryContext.Instance();
                    var agents = AgentModule.Instance();
                    var inventory = agents == null ? null : agents->GetAgentByInternalId(AgentId.Inventory);
                    if (context == null || inventory == null) { Cancel("背包選單尚未就緒。"); return; }
                    flow.OpenedContext();
                    lastWait = "等待背包右鍵選單";
                    context->OpenForItemSlot((InventoryType)request.Slot.Bag, request.Slot.Slot, 0, inventory->GetAddonId());
                    Plugin.Log.Information($"[MapDecipher] OpenForItemSlot; item={request.Map.Id}; parentAddon={inventory->GetAddonId()}; contextItems={context->ContextItemCount}");
                    // Inventory popups can disappear on the next frame when their parent inventory is closed.
                    // Consume the Decipher entry while this request's newly opened menu is still available.
                    ChooseDecipher(request);
                    break;
                case DecipherStage.ContextMenu:
                    ChooseDecipher(request);
                    break;
                case DecipherStage.Confirmation:
                    Confirm(request, current, now);
                    break;
            }
        }
        catch (Exception e) { Cancel("地圖處理發生錯誤，已停止。"); Plugin.Log.Error(e, "Map decipher update failed"); }
    }

    private void ChooseDecipher(DecipherRequest request)
    {
        var menu = (AtkUnitBase*)Plugin.Gui.GetAddonByName("ContextMenu").Address;
        var agent = AgentInventoryContext.Instance();
        if (menu == null || !menu->IsVisible) return;
        if (agent == null || agent->TargetInventoryId != (InventoryType)request.Slot.Bag ||
            agent->TargetInventorySlotId != request.Slot.Slot || agent->TargetInventorySlot == null ||
            agent->TargetInventorySlot->ItemId != request.Map.Id || agent->ContextItemCount is < 1 or > 32)
        { Cancel("選單已切換至其他物品，已停止解讀。"); return; }
        var index = 0;
        var decipherIndex = -1;
        foreach (var value in agent->EventParams)
        {
            if (value.Type != AtkValueType.String) continue;
            if (index >= agent->ContextItemCount) break;
            if ((byte*)value.String != null && SeString.Parse((byte*)value.String).TextValue == decipherLabel) decipherIndex = index;
            index++;
        }
        if (decipherIndex is < 0 or >= 32 || (agent->ContextItemDisabledMask & (1u << decipherIndex)) != 0)
        { Cancel("這張地圖目前無法解讀，請確認解鎖條件及現有寶圖。"); return; }
        var existingDialog = (AtkUnitBase*)Plugin.Gui.GetAddonByName("SelectYesno").Address;
        if (existingDialog != null && existingDialog->IsVisible)
        { Cancel("其他確認視窗已開啟，已停止解讀。"); return; }
        flow.RequestedDecipher(dialogGeneration);
        lastWait = "等待解讀確認框";
        var values = stackalloc AtkValue[5];
        for (var i = 0; i < 5; i++) { values[i] = default; values[i].Type = AtkValueType.Int; }
        values[1].Int = decipherIndex;
        menu->FireCallback(5, values, true);
        Plugin.Log.Information($"[MapDecipher] Decipher selected; index={decipherIndex}; expectedDialog={flow.ExpectedDialogGeneration}");
        Status = plugin.Config.AutoConfirmDecipher ? "等待本次地圖的解讀確認。" : "請在遊戲視窗確認解讀。";
    }

    private void Confirm(DecipherRequest request, BagSlot? current, long now)
    {
        if (dialogGeneration > flow.ExpectedDialogGeneration) { Cancel("確認視窗已更換，已停止自動確認。"); return; }
        var addon = (AddonSelectYesno*)Plugin.Gui.GetAddonByName("SelectYesno").Address;
        if (addon == null || !addon->IsVisible) { lastWait = "等待解讀確認框出現"; return; }
        if ((nint)addon != dialogAddress || dialogGeneration != flow.ExpectedDialogGeneration)
        { lastWait = "確認框已出現，等待核對本次視窗"; return; }
        if (addon->PromptText == null || !addon->PromptText->NodeText.StringPtr.HasValue) { lastWait = "等待確認文字載入"; return; }
        var prompt = SeString.Parse((byte*)addon->PromptText->NodeText.StringPtr).TextValue;
        if (dialogGeneration == flow.ExpectedDialogGeneration && prompt != request.Prompt)
        { Cancel("出現非本次解讀的提示，已停止自動確認。"); return; }
        if (!plugin.Config.AutoConfirmDecipher) { lastWait = "等待手動確認解讀"; return; }
        lastWait = "等待確認按鈕可用";
        if (!flow.CanConfirm(dialogGeneration, prompt, addon->IsVisible, addon->YesButton != null && addon->YesButton->IsEnabled,
            current, Plugin.Player.ContentId, Plugin.Client.TerritoryType, now)) return;
        if (DecodedRank() != 0) { Cancel("已有解讀中的寶圖，已停止自動確認。"); return; }
        flow.Confirmed(); // Mark before sending: a synchronous callback cannot confirm twice.
        lastWait = "已按確認，等待遊戲更新寶圖";
        addon->FireCallbackInt(0);
        Plugin.Log.Information($"[MapDecipher] Confirmed; item={request.Map.Id}");
        Status = $"已確認解讀 {request.Map.Name}；若未完成，可再按同一快捷鍵重試。";
    }

    private static bool Ready() => Plugin.Client.IsLoggedIn && Plugin.Player.IsLoaded &&
        Plugin.Objects.LocalPlayer is { IsDead: false } && !Plugin.Loading && !Plugin.Client.IsGPosing && EventItemManager.Instance() != null;
    private static uint DecodedRank()
    {
        var manager = EventItemManager.Instance();
        return manager == null ? uint.MaxValue : manager->GetTreasureHuntRank();
    }
    private static BagSlot? ReadSlot(int bag, int index)
    {
        if (bag is < 0 or > 3 || index is < 0 or >= 35) return null;
        var manager = InventoryManager.Instance();
        if (manager == null) return null;
        var container = manager->GetInventoryContainer((InventoryType)bag);
        if (container == null || !container->IsLoaded || container->Size != 35) return null;
        var item = container->GetInventorySlot(index);
        return item == null ? null : new(bag, index, item->ItemId, item->Quantity, item->IsSymbolic || item->Flags != 0);
    }

    private static bool TryFind(uint id, out BagSlot? found)
    {
        found = null;
        for (var bag = 0; bag < 4; bag++)
        for (var slot = 0; slot < 35; slot++)
        {
            var item = ReadSlot(bag, slot);
            if (item is null) { found = null; return false; }
            if (item.ItemId == id && item.Quantity > 0 && !item.Protected) found ??= item;
        }
        return found is not null;
    }

    private bool Fail(string message)
    {
        Status = message;
        var now = Environment.TickCount64;
        if (now - lastError >= 1500)
        { lastError = now; Plugin.Log.Warning($"[MapDecipher] {message}"); Plugin.Chat.PrintError($"[TreasureKeeperGlobal] {message}"); }
        return false;
    }
    internal void Cancel(string message)
    {
        if (flow.Pending is { } pending)
        {
            Plugin.Log.Information($"[MapDecipher] Stopped; item={pending.Map.Id}; stage={flow.Stage}; {message}");
        }
        flow.Reset(); Status = message;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        flow.Reset();
        try { Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", OnDialogSetup); }
        finally
        {
            try { Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "SelectYesno", OnDialogClosed); }
            finally { useAction?.Dispose(); }
        }
    }
}
