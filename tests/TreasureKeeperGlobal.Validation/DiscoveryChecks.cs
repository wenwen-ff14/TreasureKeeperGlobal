using System.Text.Json;
using TreasureKeeperGlobal.Core;

internal static class DiscoveryChecks
{
    internal static void Run(Action<bool, string> check, TreasureCatalog catalog)
    {
        var tracker = new LootDiscoveryTracker();
        var seed = Enumerable.Range(0, 4).SelectMany(b => Enumerable.Range(0, 35).Select(s => new LootSlot(b, s, 0, 0)))
            .Concat(Enumerable.Range(0, 18).Select(s => new LootSlot(2001, s, 0, 0))).ToArray();
        seed[0] = new(0, 0, 100, 10);
        seed[1] = new(0, 1, 101, 5);
        var empty = new LootSlot(0, 2, 0, 0);
        var received = empty with { ItemId = 102, Quantity = 3 };
        tracker.Seed(seed);
        check(tracker.Ready, "Discovery starts only after all bags and crystals are loaded");
        check(tracker.Apply(seed.Select(s => new LootSlotChange(s with { ItemId = 0, Quantity = 0 }, s)).ToArray(), true).Length == 0,
            "Initial inventory notifications do not import pre-existing items");
        var gains = tracker.Apply([new(empty, received)], true);
        check(gains.SequenceEqual(new[] { new LootGain(102, 3) }), "Newly received item is discovered");
        check(tracker.Apply([new(empty, received)], true).Length == 0, "Repeated notification does not double count");
        gains = tracker.Apply([new(received, received with { Quantity = 7 })], true);
        check(gains.SequenceEqual(new[] { new LootGain(102, 4) }), "Quantity increase in existing stack is discovered");
        check(tracker.Apply([new(received with { Quantity = 7 }, empty)], true).Length == 0, "Discard or consumption never becomes a gain");
        tracker.Seed(seed);
        var split = new LootSlot(1, 6, 100, 4);
        check(tracker.Apply([new(seed[0], seed[0] with { Quantity = 6 }), new(split with { ItemId = 0, Quantity = 0 }, split)], true).Length == 0,
            "Splitting across bags is not a new acquisition");
        check(tracker.Apply([new(seed[0] with { Quantity = 6 }, seed[0]), new(split, split with { ItemId = 0, Quantity = 0 })], true).Length == 0,
            "Merging across bags is not a new acquisition");
        tracker.Seed(seed);
        var moved = new LootSlot(2, 5, 100, 10);
        check(tracker.Apply([new(seed[0], seed[0] with { ItemId = 0, Quantity = 0 }), new(moved with { ItemId = 0, Quantity = 0 }, moved)], true).Length == 0,
            "Sorting and moving between normal bags are ignored");
        tracker.Seed(seed);
        var equipment = new LootSlot(1000, 0, 102, 3);
        check(tracker.Apply([new(equipment, equipment with { ItemId = 0, Quantity = 0 }), new(empty, received)], true).Length == 0,
            "Unequipping into the backpack is excluded using all-container net change");
        tracker.Seed(seed);
        var saddle = new LootSlot(4000, 4, 102, 2);
        gains = tracker.Apply([new(saddle, saddle with { ItemId = 0, Quantity = 0 }), new(empty, received)], true);
        check(gains.SequenceEqual(new[] { new LootGain(102, 1) }), "Simultaneous transfer and real reward only count the newly acquired portion");
        tracker.Seed(seed);
        check(tracker.Apply([new(empty, received)], false).Length == 0, "Suppressed crafting or trade does not enter discovery");
        gains = tracker.Apply([new(received, received with { Quantity = 4 })], true);
        check(gains.SequenceEqual(new[] { new LootGain(102, 1) }), "Suppressed activity still advances the baseline");
        tracker.Seed(seed);
        var crystalEmpty = new LootSlot(2001, 0, 0, 0);
        gains = tracker.Apply([new(crystalEmpty, crystalEmpty with { ItemId = 2, Quantity = 30 })], true);
        check(gains.SequenceEqual(new[] { new LootGain(2, 30) }), "Crystals can be learned without being made discardable");
        tracker.Seed(seed);
        check(tracker.Apply([new(empty, received with { ItemId = 1 })], true).Length == 0, "Gil does not become a catalog item");
        tracker.Seed(seed);
        check(tracker.Apply([new(received with { Quantity = 99 }, received)], true).Length == 0, "Unknown old slot state resynchronizes without inventing a reward");
        check(tracker.Apply([new(received, received with { Quantity = 4 })], true).Single().Quantity == 1, "Resynchronized slot can observe subsequent gains");
        tracker.Reset();
        check(!tracker.Ready && tracker.Apply([new(empty, received)], true).Length == 0, "Logout, transition or disabling clears in-flight state");
        tracker.Seed(seed[..^1]); check(!tracker.Ready, "Incomplete load cannot seed an empty inventory baseline");
        tracker.Seed([.. seed, seed[0]]); check(!tracker.Ready, "Duplicate snapshot slot is rejected");
        tracker.Seed(seed.Select(s => s.Slot == 34 ? s with { Quantity = -1 } : s)); check(!tracker.Ready, "Invalid snapshot quantity is rejected");
        tracker.Seed(seed.Select(s => s.Slot == 0 && s.Inventory == 0 ? s with { ItemId = 999 } : s));
        check(tracker.Apply([new(empty, received)], true).Single().ItemId == 102, "Fresh instance uses a new baseline");

        var canals = catalog.Vaults.Single(v => v.Id == 55002);
        var altars = catalog.Vaults.Single(v => v.TerritoryId == 794);
        check(LootDiscoveryCatalog.ResolveVault(catalog.Vaults, 712)?.Id == canals.Id &&
            LootDiscoveryCatalog.ResolveVault(catalog.Vaults, 794)?.Id == altars.Id, "Territories resolve to separate vaults");
        check(LootDiscoveryCatalog.ResolveVault(catalog.Vaults, 129) is null && LootDiscoveryCatalog.ResolveVault(catalog.Vaults, 612) is null,
            "City and G10 outdoor area alone do not imply treasure-vault loot");
        check(LootDiscoveryCatalog.ResolveVault([canals with { Available = false }], 712) is null, "Unavailable local territory is not a valid source");
        var item = new TreasureItem(999999, "補錄測試素材", "", "素材", true, true, ["手動新增"]);
        var merged = LootDiscoveryCatalog.Merge(canals, item);
        check(merged.Items.Length == canals.Items.Length + 1 && merged.Items.Single(i => i.Id == item.Id).Sources.SequenceEqual(new[] { LootDiscoveryCatalog.Source }),
            "Unlisted local item is appended with observed provenance, not manual provenance");
        check(!altars.Items.Any(i => i.Id == item.Id), "Discovery never adds an item to another vault");
        check(LootDiscoveryCatalog.Merge(merged, item).Items.Length == merged.Items.Length, "Repeated discoveries never create duplicate catalog rows");
        var known = canals.Items[0];
        var tagged = LootDiscoveryCatalog.Merge(canals, known).Items.Single(i => i.Id == known.Id);
        check(tagged.Sources.Contains(LootDiscoveryCatalog.Source) && known.Sources.All(tagged.Sources.Contains), "Existing public evidence is preserved when an item is observed");
        var selections = new HashSet<uint>();
        check(!CleanupPolicy.Eligible(new(0, 0, item.Id, 1, false), item, selections), "Discovery alone does not select new items for discard");
        selections.Add(item.Id);
        check(CleanupPolicy.Eligible(new(0, 0, item.Id, 1, false), item, selections), "Existing explicit global selection remains effective");
        check(!CleanupPolicy.CanSelect(item with { Id = 19770 }), "Discovering deep maps never removes fixed keep protection");
        var time = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        var first = LootDiscoveryCatalog.Observe(null, item.Id, time);
        var again = LootDiscoveryCatalog.Observe(first, item.Id, time.AddHours(1));
        check(first.FirstSeenUtc == again.FirstSeenUtc && again.LastSeenUtc == time.AddHours(1), "First and last observations are retained separately");
        var stored = new Dictionary<uint, Dictionary<uint, DiscoveredLoot>> { [canals.Id] = new() { [item.Id] = again } };
        var restored = JsonSerializer.Deserialize<Dictionary<uint, Dictionary<uint, DiscoveredLoot>>>(JsonSerializer.Serialize(stored))!;
        check(restored[canals.Id][item.Id] == again, "Per-vault discovery metadata survives persistence");
        var reloaded = LootDiscoveryCatalog.Merge(canals, item);
        check(CleanupPolicy.RestoreSelection(reloaded.Items, selections, []).Contains(item.Id), "Rebuilding discovered catalog before restoring rules preserves selected learned items");
        CheckDefaults(catalog, check);
    }

    private static void CheckDefaults(TreasureCatalog catalog, Action<bool, string> check)
    {
        var defaults = new Dictionary<uint, bool> { [55002] = true, [55004] = false };
        var configured = new HashSet<uint>();
        var selected = new HashSet<uint>();
        var material = new TreasureItem(999990, "新素材", "", "素材", true, true, []);
        var second = material with { Id = 999991 };
        check(DiscoverySelectionPolicy.ApplyDefault(55002, material, defaults, configured, selected) && selected.Contains(material.Id),
            "First discovery in a discard-default vault enters the global selection");
        check(CleanupPolicy.Eligible(new(0, 0, material.Id, 8, false), material, selected),
            "Discovery defaults feed the existing whole-stack cleanup policy");
        check(DiscoverySelectionPolicy.ApplyDefault(55004, second, defaults, configured, selected) && !selected.Contains(second.Id),
            "A separate vault can keep new items while another discards them");
        check(!DiscoverySelectionPolicy.ApplyDefault(55002, second, defaults, configured, selected) && !selected.Contains(second.Id),
            "Cross-vault discovery cannot overwrite a prior keep decision");
        check(!DiscoverySelectionPolicy.ApplyDefault(55004, material, defaults, configured, selected) && selected.Contains(material.Id),
            "Cross-vault keep defaults cannot remove an existing global discard decision");
        CleanupPolicy.SetSelection(selected, [material], false, configured);
        check(!DiscoverySelectionPolicy.ApplyDefault(55002, material, defaults, configured, selected) && !selected.Contains(material.Id),
            "Manual keep survives repeated discovery under a discard default");
        var manuallyKept = material with { Id = 999992 };
        CleanupPolicy.SetSelection(selected, [manuallyKept], false, configured);
        check(!DiscoverySelectionPolicy.ApplyDefault(55002, manuallyKept, defaults, configured, selected) && !selected.Contains(manuallyKept.Id),
            "Manual keep before first observation also wins over the vault default");
        var manuallyDiscarded = material with { Id = 999993 };
        CleanupPolicy.SetSelection(selected, [manuallyDiscarded], true, configured);
        check(!DiscoverySelectionPolicy.ApplyDefault(55004, manuallyDiscarded, defaults, configured, selected) && selected.Contains(manuallyDiscarded.Id),
            "Manual discard before observation wins over a keep default");
        var unconfiguredVaultItem = material with { Id = 999994 };
        DiscoverySelectionPolicy.ApplyDefault(55003, unconfiguredVaultItem, defaults, configured, selected);
        check(!selected.Contains(unconfiguredVaultItem.Id), "Vaults without a saved default keep new items");
        defaults[55003] = true;
        check(!DiscoverySelectionPolicy.ApplyDefault(55003, unconfiguredVaultItem, defaults, configured, selected) && !selected.Contains(unconfiguredVaultItem.Id),
            "Changing defaults never retroactively selects existing observations");
        foreach (var protectedItem in new[] { material with { Id = 19770 }, material with { Id = 2, Discardable = false },
            material with { Id = 999995, Available = false }, material with { Id = 999996, Discardable = false } })
        {
            selected.Add(protectedItem.Id);
            check(!DiscoverySelectionPolicy.ApplyDefault(55002, protectedItem, defaults, configured, selected) && !selected.Contains(protectedItem.Id),
                $"Auto-discard default respects fixed keep/availability/indisposable protection: {protectedItem.Id}");
        }
        var migrated = DiscoverySelectionPolicy.RestoreConfigured(null, [999997], [999998], [999999]);
        selected.Add(999997);
        foreach (var id in new uint[] { 999997, 999998, 999999 })
            check(!DiscoverySelectionPolicy.ApplyDefault(55002, material with { Id = id }, defaults, migrated, selected) && selected.Contains(id) == (id == 999997),
                $"Upgrade preserves existing selection/observation/custom item disposition: {id}");
        var stored = JsonSerializer.Serialize(new SavedDefaults(defaults, configured, selected));
        var restored = JsonSerializer.Deserialize<SavedDefaults>(stored)!;
        check(restored.Defaults[55002] && !restored.Defaults[55004] && restored.Configured.SetEquals(configured) && restored.Selected.SetEquals(selected),
            "Independent defaults and global decisions survive persistence together");
        check(!DiscoverySelectionPolicy.ApplyDefault(55002, manuallyKept, restored.Defaults, restored.Configured, restored.Selected) &&
            !restored.Selected.Contains(manuallyKept.Id), "Reloaded manual keep is not overwritten by a later first discovery");
        check(DiscoverySelectionPolicy.CanAwaitDiscovery(catalog.Vaults, true, defaults), "Empty selection may arm and await eligible auto-discard discoveries");
        check(!DiscoverySelectionPolicy.CanAwaitDiscovery(catalog.Vaults, false, defaults), "Disabled discovery cannot arm empty cleanup");
        check(!DiscoverySelectionPolicy.CanAwaitDiscovery(catalog.Vaults, true, new Dictionary<uint, bool> { [17836] = true, [55013] = true }),
            "Outdoor and unavailable vault defaults cannot arm empty cleanup");
        check(!DiscoverySelectionPolicy.CanAwaitDiscovery(catalog.Vaults, true, new Dictionary<uint, bool>()), "Default keep-only settings cannot arm empty cleanup");
    }

    private sealed record SavedDefaults(Dictionary<uint, bool> Defaults, HashSet<uint> Configured, HashSet<uint> Selected);
}
