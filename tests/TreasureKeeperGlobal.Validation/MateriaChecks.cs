using TreasureKeeperGlobal.Core;

internal static class MateriaChecks
{
    internal static void Run(Action<bool, string> check, TreasureCatalog catalog)
    {
        foreach (var id in new uint[] { 5697, 5682 })
        {
            var item = catalog.Vaults.SelectMany(v => v.Items).First(i => i.Id == id) with { IsLooseMateria = true };
            var selected = new HashSet<uint> { id };
            var cases = new (ushort Spiritbond, uint Glamour, bool Dyed, bool Melded, DiscardBlockReason GearReason)[]
            {
                (1, 0, false, false, DiscardBlockReason.Spiritbond),
                (0, 100, false, false, DiscardBlockReason.Glamour),
                (0, 0, true, false, DiscardBlockReason.Dyed),
                (0, 0, false, true, DiscardBlockReason.Melded),
                (10000, 100, true, true, DiscardBlockReason.Spiritbond),
            };
            foreach (var c in cases)
            {
                var reason = SlotProtection.Evaluate(false, 0, false, item.IsLooseMateria, c.Spiritbond, c.Glamour, c.Dyed, c.Melded);
                var slot = new BagSlot(0, 2, id, 3, reason != DiscardBlockReason.None, reason);
                check(CleanupPolicy.Eligible(slot, item, selected), $"Standalone materia {id} ignores equipment-only state");
                check(!CleanupPolicy.Eligible(slot, item, new HashSet<uint>()), "Detected but unselected materia remains kept");
                check(SlotProtection.Evaluate(false, 0, false, false, c.Spiritbond, c.Glamour, c.Dyed, c.Melded) == c.GearReason,
                    "Non-materia retains equipment protection");
            }
            foreach (var flags in new byte[] { 2, 4, 8, 16, 32, 64, 128, 255 })
                check(SlotProtection.Evaluate(false, flags, true, true, 0, 0, false, false) != DiscardBlockReason.None,
                    "Loose materia never bypasses collectible or special flags");
            check(SlotProtection.Evaluate(true, 0, true, true, 0, 0, false, false) == DiscardBlockReason.Symbolic,
                "Symbolic loose materia is rejected");
            check(SlotProtection.Evaluate(false, 1, false, true, 0, 0, false, false) == DiscardBlockReason.HighQuality &&
                SlotProtection.Evaluate(false, 1, true, true, 0, 0, false, false) == DiscardBlockReason.None,
                "HQ option remains authoritative");
            var blocked = new BagSlot(0, 2, id, 3, true, DiscardBlockReason.Melded);
            check(!CleanupPolicy.Eligible(blocked, item, selected) && CleanupPolicy.BlockReason(blocked, item, selected) == DiscardBlockReason.Melded,
                "Preview reason matches execution policy");
            check(!CleanupPolicy.Eligible(blocked with { Protected = false }, item, selected), "Explicit reason cannot be bypassed by legacy bool");
        }
        foreach (var reason in Enum.GetValues<DiscardBlockReason>())
            check(!string.IsNullOrWhiteSpace(CleanupPolicy.Describe(reason)), "Every blocked reason has visible diagnostic text");
    }
}
