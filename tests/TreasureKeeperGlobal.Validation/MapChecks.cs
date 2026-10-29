using TreasureKeeperGlobal.Core;

internal static class MapChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var map = new TreasureMapItem(17836, "陳舊的瞪羚革地圖", 13);
        var slot = new BagSlot(0, 3, map.Id, 1, false);
        const string prompt = "確定要對陳舊的瞪羚革地圖進行解讀嗎？";
        var flow = new MapDecipherFlow();
        bool Confirm(long generation = 5, string text = prompt, bool visible = true, bool enabled = true,
            BagSlot? current = null, ulong character = 123, uint territory = 129, long now = 1200) =>
            flow.CanConfirm(generation, text, visible, enabled, current ?? slot, character, territory, now);
        void Start(TreasureMapItem? item = null)
        {
            flow.Reset();
            check(flow.Start(item ?? map, slot, 123, 129, 1000, prompt), "Map request starts with matching normal bag item");
        }
        Start();
        check(!flow.Start(map, slot, 123, 129, 1001, prompt), "Repeated hotbar press cannot enqueue a second map");
        check(!Confirm(), "Cannot confirm before opening own context menu");
        check(flow.Observe(123, 129, 13, 1100) == DecipherObservation.Waiting, "Existing decoded map cannot acknowledge a queued request");
        flow.OpenedContext();
        check(!Confirm(), "Cannot confirm before requesting Decipher");
        flow.RequestedDecipher(4);
        check(flow.ExpectedDialogGeneration == 5 && Confirm(), "Only the next newly created matching dialog can be confirmed");
        foreach (var generation in new long[] { 0, 3, 4, 6, 100 }) check(!Confirm(generation), "Stale or replaced dialog is rejected");
        foreach (var text in new[] { "", "確定要丟棄嗎？", "確定要進入傳送魔紋嗎？", prompt.Replace("瞪羚", "山羊"), prompt + "其他提示" })
            check(!Confirm(text: text), "Unrelated or different map prompt is never confirmed");
        check(!Confirm(visible: false) && !Confirm(enabled: false), "Hidden dialog and disabled Yes button are rejected");
        check(!Confirm(character: 0) && !Confirm(character: 124) && !Confirm(territory: 712), "Login and territory changes block confirmation");
        check(!Confirm(now: 999) && Confirm(now: 8999) && !Confirm(now: 9000), "Confirmation time bounds are enforced");
        foreach (var changed in new[] { slot with { Bag = 1 }, slot with { Slot = 4 }, slot with { ItemId = 19770 },
            slot with { Quantity = 0 }, slot with { Quantity = 2 }, slot with { Protected = true } })
            check(!Confirm(current: changed), "Moved, replaced, consumed or protected map is rejected");
        check(!flow.CanConfirm(5, prompt, true, true, null, 123, 129, 1200), "Unloaded bag is rejected");
        flow.Confirmed();
        check(!Confirm(), "Callback can only be sent once");
        check(flow.Observe(123, 129, 0, 1300) == DecipherObservation.Waiting, "Sending a callback alone does not prove success");
        check(flow.Observe(123, 129, 14, 1400) == DecipherObservation.Waiting, "Different decoded rank cannot acknowledge this map");
        check(flow.Observe(123, 129, 13, 1500) == DecipherObservation.Completed && flow.Pending is null, "Expected decoded map completes request");
        Start(); flow.OpenedContext(); flow.RequestedDecipher(4);
        check(flow.Observe(123, 129, 13, 1300) == DecipherObservation.Completed, "Manual Yes works with automatic confirmation off");
        Start(map with { Rank = 0 }); flow.OpenedContext(); flow.RequestedDecipher(4); flow.Confirmed();
        check(flow.Observe(123, 129, 20, 1300) == DecipherObservation.Completed, "Special map without rank metadata accepts newly decoded rank");
        Start();
        check(flow.Observe(123, 129, 0, 8999) == DecipherObservation.Waiting &&
            flow.Observe(123, 129, 0, 9000) == DecipherObservation.TimedOut && flow.Pending is null, "Timeout clears request without automatic retries");
        Start();
        check(flow.Observe(0, 129, 13, 1200) == DecipherObservation.Interrupted, "Logout wins over an apparent completion");
        Start();
        check(flow.Observe(123, 712, 13, 1200) == DecipherObservation.Interrupted, "Territory change interrupts operation");
        Start(); flow.OpenedContext(); flow.RequestedDecipher(4); flow.Reset();
        check(!Confirm() && flow.Pending is null, "Cancel/settings change prevents any later confirmation");
        Start(); flow.OpenedContext(); flow.RequestedDecipher(4); flow.Confirmed();
        foreach (var changed in new[] { slot with { Quantity = 0 }, slot with { Slot = 4 }, slot with { ItemId = 19770 } })
            check(!flow.Restart(map.Id, changed, 123, 129, 0, false, false, 1300), "Immediate retry rejects consumed or moved map");
        check(!flow.Restart(map.Id, null, 123, 129, 0, false, false, 1300), "Immediate retry rejects unloaded inventory");
        check(!flow.Restart(19770, slot, 123, 129, 0, false, false, 1300), "Retry cannot silently switch maps");
        check(!flow.Restart(map.Id, slot, 124, 129, 0, false, false, 1300) &&
            !flow.Restart(map.Id, slot, 123, 712, 0, false, false, 1300), "Retry verifies character and territory");
        check(!flow.Restart(map.Id, slot, 123, 129, 13, false, false, 1300) &&
            !flow.Restart(map.Id, slot, 123, 129, uint.MaxValue, false, false, 1300), "Successful or unknown decoded state cannot be retried");
        check(!flow.Restart(map.Id, slot, 123, 129, 0, true, false, 1300) &&
            !flow.Restart(map.Id, slot, 123, 129, 0, false, true, 1300), "Open dialogs and active game operations block reissuing");
        check(flow.Restart(map.Id, slot, 123, 129, 0, false, false, 1300) && flow.Stage == DecipherStage.Queued &&
            flow.Pending?.StartedAt == 1300 && flow.ExpectedDialogGeneration == 0,
            "Explicit retry after 300 ms replaces stalled result without an eight-second or one-second lockout");
        check(!Confirm(now: 1400), "Retired confirmation cannot fire during a retry");
        flow.OpenedContext(); flow.RequestedDecipher(5);
        check(!Confirm(5, now: 1400) && Confirm(6, now: 1400), "Retry confirms only its own new dialog generation");
        flow.Confirmed();
        check(flow.Observe(123, 129, 13, 1600) == DecipherObservation.Completed, "Immediate retry can complete normally");
        check(!flow.Restart(map.Id, slot, 123, 129, 0, false, false, 1700), "No queued retry remains after completion");
        foreach (var invalid in new[] { slot with { Bag = -1 }, slot with { Bag = 4 }, slot with { Slot = -1 }, slot with { Slot = 35 },
            slot with { ItemId = 19770 }, slot with { Quantity = 0 }, slot with { Protected = true } })
            check(!flow.Start(map, invalid, 123, 129, 1000, prompt), "Invalid initial map slot rejected");
        check(!flow.Start(map, slot, 0, 129, 1000, prompt) && !flow.Start(map, slot, 123, 129, 1000, ""), "No character or missing localized prompt rejected");
        for (var bar = -1; bar <= 18; bar++)
        for (var index = -1; index <= 16; index++)
        {
            var expected = bar >= 0 && bar <= 17 && index >= 0 && index <= (bar < 10 ? 11 : 15);
            check(MapDecipherFlow.ValidHotbarSlot(bar, index) == expected, "Native hotbar bounds exclude scratch and hidden slots");
        }
        check(MapDecipherFlow.IsTreasureMap(19770, 18, "深層傳送魔紋的地圖"), "Deep map supports explicit deciphering independent of discard protection");
        check(MapDecipherFlow.IsTreasureMap(999999, 18, "未來地圖"), "New local map rows do not require a static ID update");
        check(!MapDecipherFlow.IsTreasureMap(0, 18, "地圖") && !MapDecipherFlow.IsTreasureMap(1, 18, " ") &&
            !MapDecipherFlow.IsTreasureMap(1, 0, "地圖"), "Empty and unrelated items do not enter the hook");
    }
}
