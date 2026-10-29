namespace TreasureKeeperGlobal.Core;

public sealed record BagSlot(int Bag, int Slot, uint ItemId, int Quantity, bool Protected,
    DiscardBlockReason ProtectionReason = DiscardBlockReason.None, byte RawFlags = 0);

public enum DiscardBlockReason
{
    None, InvalidSlot, UnknownItem, FixedKeep, Unavailable, Indisposable, NotSelected,
    Symbolic, HighQuality, Collectable, InventoryFlags, Spiritbond, Glamour, Dyed, Melded, Protected
}

public static class SlotProtection
{
    // Verified API 13 InventoryItem.ItemFlags: HQ=1, company crest=2, relic=4, collectable=8.
    // Keep all unexpected/non-HQ flags protected, including for loose materia.
    public static DiscardBlockReason Evaluate(bool symbolic, byte flags, bool includeHighQuality, bool looseMateria,
        ushort spiritbondOrCollectability, uint glamour, bool dyed, bool melded)
    {
        if (symbolic) return DiscardBlockReason.Symbolic;
        if ((flags & 8) != 0) return DiscardBlockReason.Collectable;
        if ((flags & 1) != 0 && !includeHighQuality) return DiscardBlockReason.HighQuality;
        if ((flags & ~1) != 0) return DiscardBlockReason.InventoryFlags;
        // ItemSearchCategory=57 AND EquipSlotCategory=0 is a standalone materia item,
        // not equipment with materia melded into it. Gear-only fields do not describe its state.
        if (looseMateria) return DiscardBlockReason.None;
        if (spiritbondOrCollectability != 0) return DiscardBlockReason.Spiritbond;
        if (glamour != 0) return DiscardBlockReason.Glamour;
        if (dyed) return DiscardBlockReason.Dyed;
        return melded ? DiscardBlockReason.Melded : DiscardBlockReason.None;
    }
}

public static class CleanupPolicy
{
    public const uint DeepMapId = 19770;

    public static bool CanSelect(TreasureItem item) => item.Id != DeepMapId && item.Available && item.Discardable;

    public static bool Eligible(BagSlot slot, TreasureItem? item, IReadOnlySet<uint> selected) =>
        BlockReason(slot, item, selected) == DiscardBlockReason.None;

    public static DiscardBlockReason BlockReason(BagSlot slot, TreasureItem? item, IReadOnlySet<uint> selected)
    {
        if (slot.Bag is < 0 or >= 4 || slot.Slot is < 0 or >= 35 || slot.Quantity <= 0) return DiscardBlockReason.InvalidSlot;
        if (item is null || item.Id != slot.ItemId) return DiscardBlockReason.UnknownItem;
        if (item.Id == DeepMapId) return DiscardBlockReason.FixedKeep;
        if (!item.Available) return DiscardBlockReason.Unavailable;
        if (!item.Discardable) return DiscardBlockReason.Indisposable;
        if (!selected.Contains(slot.ItemId)) return DiscardBlockReason.NotSelected;
        if (slot.ProtectionReason != DiscardBlockReason.None) return slot.ProtectionReason;
        return slot.Protected ? DiscardBlockReason.Protected : DiscardBlockReason.None;
    }

    public static string Describe(DiscardBlockReason reason) => reason switch
    {
        DiscardBlockReason.None => "可丟棄",
        DiscardBlockReason.InvalidSlot => "不在一般背包或數量無效",
        DiscardBlockReason.UnknownItem => "無法核對物品資料",
        DiscardBlockReason.FixedKeep => "深層地圖固定保留",
        DiscardBlockReason.Unavailable => "本機尚無此物品",
        DiscardBlockReason.Indisposable => "遊戲不允許丟棄",
        DiscardBlockReason.NotSelected => "尚未勾選丟棄",
        DiscardBlockReason.Symbolic => "連結物品欄位保護",
        DiscardBlockReason.HighQuality => "HQ 保留開關",
        DiscardBlockReason.Collectable => "收藏品保護",
        DiscardBlockReason.InventoryFlags => "物品特殊旗標保護",
        DiscardBlockReason.Spiritbond => "精煉度／收藏價值保護",
        DiscardBlockReason.Glamour => "投影裝備保護",
        DiscardBlockReason.Dyed => "染色裝備保護",
        DiscardBlockReason.Melded => "已鑲嵌魔晶石的裝備保護",
        _ => "物品狀態保護"
    };

    public static HashSet<uint> RestoreSelection(IEnumerable<TreasureItem> items, IEnumerable<uint>? global,
        IEnumerable<IEnumerable<uint>> legacy)
    {
        var valid = items.Where(CanSelect).Select(i => i.Id).ToHashSet();
        // An explicitly empty global set is authoritative; never resurrect cleared legacy selections.
        return (global ?? legacy.SelectMany(ids => ids)).Where(valid.Contains).ToHashSet();
    }

    public static void SetSelection(HashSet<uint> selected, IEnumerable<TreasureItem> visible, bool discard,
        HashSet<uint>? configured = null)
    {
        foreach (var item in visible)
        {
            if (CanSelect(item)) configured?.Add(item.Id);
            if (discard && CanSelect(item)) selected.Add(item.Id);
            else selected.Remove(item.Id);
        }
        selected.Remove(DeepMapId);
    }
}

public enum RequestResult { None, Waiting, InventoryChanged, TimedOut, RescanAfterTransition }

public sealed class DiscardTracker
{
    public BagSlot? Pending { get; private set; }
    public bool Faulted { get; private set; }
    private long started;
    private long totalBefore;
    private uint territory;
    private bool transitioned;

    public void Start(BagSlot slot, IReadOnlyList<BagSlot> inventory, uint zone, long now)
    {
        if (Pending is not null || Faulted) throw new InvalidOperationException("A discard is already pending or faulted");
        Pending = slot;
        totalBefore = inventory.Where(s => s.ItemId == slot.ItemId).Sum(s => (long)s.Quantity);
        territory = zone;
        transitioned = false;
        started = now;
    }

    public void OnTerritoryChanged() { if (Pending is not null) transitioned = true; }

    public RequestResult Observe(IReadOnlyList<BagSlot> inventory, uint zone, long now)
    {
        if (Pending is not { } pending) return RequestResult.None;
        transitioned |= zone != territory;
        var total = inventory.Where(s => s.ItemId == pending.ItemId).Sum(s => (long)s.Quantity);
        if (total <= totalBefore - pending.Quantity)
        {
            Pending = null;
            return RequestResult.InventoryChanged;
        }
        if (now - started >= 8000)
        {
            if (!transitioned) return Fail(RequestResult.TimedOut);
            // The caller only observes fully loaded normal bags. Retire the old slot request,
            // then choose a fresh candidate on the next update; never reuse its old location.
            Reset();
            return RequestResult.RescanAfterTransition;
        }
        return RequestResult.Waiting;
    }

    private RequestResult Fail(RequestResult result) { Pending = null; Faulted = true; return result; }
    public void Reset() { Pending = null; Faulted = false; transitioned = false; }
}
