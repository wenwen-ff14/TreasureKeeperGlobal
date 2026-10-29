namespace TreasureKeeperGlobal.Core;

public sealed record TreasureMapItem(uint Id, string Name, uint Rank);
public enum DecipherStage { Queued, ContextMenu, Confirmation, Result }
public enum DecipherObservation { Waiting, Completed, Interrupted, TimedOut }
public sealed record DecipherRequest(TreasureMapItem Map, BagSlot Slot, ulong Character, uint Territory,
    long StartedAt, string Prompt);

public sealed class MapDecipherFlow
{
    public DecipherRequest? Pending { get; private set; }
    public DecipherStage Stage { get; private set; }
    public long ExpectedDialogGeneration { get; private set; }
    public const long TimeoutMs = 8000;

    public bool Start(TreasureMapItem map, BagSlot slot, ulong character, uint territory, long now, string prompt)
    {
        if (Pending is not null || map.Id == 0 || character == 0 || string.IsNullOrWhiteSpace(prompt) ||
            slot.ItemId != map.Id || slot.Bag is < 0 or > 3 || slot.Slot is < 0 or > 34 || slot.Quantity < 1 || slot.Protected)
            return false;
        Pending = new(map, slot, character, territory, now, prompt);
        Stage = DecipherStage.Queued;
        return true;
    }

    public void OpenedContext()
    {
        if (Pending is null || Stage != DecipherStage.Queued) throw new InvalidOperationException();
        Stage = DecipherStage.ContextMenu;
    }

    // A new explicit press can replace a stalled attempt immediately. No timer/cooldown gates
    // this path; the same still-present map and an idle game are required before reopening it.
    public bool Restart(uint mapId, BagSlot? current, ulong character, uint territory, uint decodedRank,
        bool dialogOpen, bool busy, long now)
    {
        if (Pending is not { } request || mapId != request.Map.Id || current != request.Slot ||
            character != request.Character || territory != request.Territory || decodedRank != 0 || dialogOpen || busy)
            return false;
        Pending = request with { StartedAt = now };
        Stage = DecipherStage.Queued;
        ExpectedDialogGeneration = 0;
        return true;
    }

    public void RequestedDecipher(long currentDialogGeneration)
    {
        if (Pending is null || Stage != DecipherStage.ContextMenu) throw new InvalidOperationException();
        ExpectedDialogGeneration = currentDialogGeneration + 1;
        Stage = DecipherStage.Confirmation;
    }

    public bool CanConfirm(long generation, string prompt, bool visible, bool enabled, BagSlot? currentSlot,
        ulong character, uint territory, long now) => Pending is { } request &&
        Stage == DecipherStage.Confirmation && generation == ExpectedDialogGeneration && visible && enabled &&
        now >= request.StartedAt && now - request.StartedAt < TimeoutMs &&
        character == request.Character && territory == request.Territory && currentSlot == request.Slot &&
        string.Equals(prompt, request.Prompt, StringComparison.Ordinal);

    public void Confirmed()
    {
        if (Pending is null || Stage != DecipherStage.Confirmation) throw new InvalidOperationException();
        Stage = DecipherStage.Result;
    }

    public DecipherObservation Observe(ulong character, uint territory, uint decodedRank, long now)
    {
        if (Pending is not { } request) return DecipherObservation.Waiting;
        var outcome = character != request.Character || territory != request.Territory ? DecipherObservation.Interrupted :
            decodedRank != 0 && (request.Map.Rank == 0 || decodedRank == request.Map.Rank) &&
            Stage is DecipherStage.Confirmation or DecipherStage.Result ? DecipherObservation.Completed :
            now - request.StartedAt >= TimeoutMs ? DecipherObservation.TimedOut : DecipherObservation.Waiting;
        if (outcome != DecipherObservation.Waiting) Reset();
        return outcome;
    }

    public void Reset() { Pending = null; ExpectedDialogGeneration = 0; Stage = DecipherStage.Queued; }

    // Normal bars have 12 visible slots; cross bars have 16. Both use zero-based native IDs.
    public static bool ValidHotbarSlot(int bar, int slot) => bar is >= 0 and < 18 && slot >= 0 && slot < (bar < 10 ? 12 : 16);
    public static bool IsTreasureMap(uint id, uint filterGroup, string name) => id != 0 && filterGroup == 18 && !string.IsNullOrWhiteSpace(name);
}
