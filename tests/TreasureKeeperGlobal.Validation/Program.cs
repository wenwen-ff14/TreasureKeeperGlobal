using System.Text.Json;
using TreasureKeeperGlobal.Core;

var checks = 0;
void Check(bool passed, string name)
{
    if (!passed) throw new Exception("FAIL: " + name);
    checks++;
}

using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "catalog.json"));
var catalog = TreasureCatalog.Load(file);
Check(catalog.Vaults.Length == 11, "Ten vaults plus a separate G10 outdoor catalog");
Check(catalog.Vaults.Count(v => v.Available && !v.IsOutdoor) == 9, "Nine verified local duty territories");
Check(catalog.Vaults.Single(v => v.Id == 55013).TerritoryId == 0, "Unreleased source territory remains unmapped");
foreach (var vault in catalog.Vaults)
{
    Check(vault.Items.All(i => i.Sources.Length > 0), $"Provenance: {vault.Name}");
    Check(vault.Items.Select(i => i.Id).Distinct().Count() == vault.Items.Length, $"No duplicates: {vault.Name}");
    Check(vault.Items.All(i => Categories.All.Contains(i.Category)), $"Known categories: {vault.Name}");
    var selected = new HashSet<uint>();
    CleanupPolicy.SetSelection(selected, vault.Items, true);
    Check(!selected.Contains(CleanupPolicy.DeepMapId), $"Bulk select preserves deep map: {vault.Name}");
    foreach (var item in vault.Items)
        Check(selected.Contains(item.Id) == CleanupPolicy.CanSelect(item), $"Bulk availability: {vault.Id}/{item.Id}");
    CleanupPolicy.SetSelection(selected, vault.Items.Where(i => i.Category == "魔晶石"), false);
    Check(!vault.Items.Any(i => i.Category == "魔晶石" && selected.Contains(i.Id)), "Category deselect only category");
}
var canals = catalog.Vaults.Single(v => v.Id == 55002);
var altars = catalog.Vaults.Single(v => v.Id == 55004);
var outdoor = catalog.Vaults.Single(v => v.Id == 17836);
Check(outdoor.Available && outdoor.IsOutdoor && outdoor.OutdoorTerritoryIds!.Length == 6, "Six verified G10 outdoor zones");
using var sourceFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "g10-supplement.json")));
var referenceCounts = new Dictionary<uint, int> { [17836] = 70, [55002] = 36, [55003] = 45, [55004] = 42 };
foreach (var source in sourceFile.RootElement.GetProperty("Sources").EnumerateArray())
{
    var vault = catalog.Vaults.Single(v => v.Id == source.GetProperty("VaultId").GetUInt32());
    var key = source.GetProperty("Key").GetString()!;
    var referenceIds = source.GetProperty("Groups").EnumerateObject().SelectMany(g => g.Value.EnumerateArray()).Select(v => v.GetUInt32()).ToArray();
    Check(referenceIds.Length == referenceCounts[vault.Id] && referenceIds.Distinct().Count() == referenceIds.Length,
        $"Reference snapshot has every distinct entry: {key}");
    foreach (var group in source.GetProperty("Groups").EnumerateObject())
    foreach (var value in group.Value.EnumerateArray())
        Check(vault.Items.Any(i => i.Id == value.GetUInt32() && i.Sources.Contains(key)), $"Every reference item imported: {key}/{value}");
}
Check(outdoor.Items.Any(i => i.Id == 19945) && outdoor.Items.Any(i => i.Id == 19889) && outdoor.Items.Any(i => i.Id == 20537),
    "Outdoor ordinary ingots, consumables, and minion all present");
Check(!outdoor.Items.Any(i => i.Id == 19770), "Deep map is not misrepresented as an outdoor drop");
Check(!outdoor.Items.Any(i => i.Id == 17836), "Map itself is not its own reward");
Check(!outdoor.Items.Any(i => i.Id == 21283), "No G9/vault reward leakage into G10 outdoors");
Check(canals.Items.Any(i => i.Id == 24260) && canals.Items.Any(i => i.Id == 19994), "Missing later materials and ordinary leather restored");
Check(!altars.Items.Any(i => i.Sources.Any(s => s.StartsWith("FF14house-"))), "Canal field logs do not leak into altars");
Check(!canals.Items.Any(i => i.Id == 21060 || i.Id == 21922), "Deep-only minions do not leak into normal canals");
using var wikiFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "g10-wiki-check.json")));
var wikiScopes = wikiFile.RootElement.GetProperty("Scopes").EnumerateArray().ToArray();
Check(wikiScopes.Select(s => s.GetProperty("VaultId").GetUInt32()).ToHashSet().SetEquals(new uint[] { 17836, 55002, 55003, 55004 }),
    "Wiki audit covers all four distinct G10 sources");
var wikiCounts = new Dictionary<uint, int> { [17836] = 53, [55002] = 95, [55003] = 64, [55004] = 79 };
foreach (var source in wikiScopes)
{
    var vault = catalog.Vaults.Single(v => v.Id == source.GetProperty("VaultId").GetUInt32());
    var items = source.GetProperty("Items").EnumerateArray().ToArray();
    Check(items.Length == wikiCounts[vault.Id] && items.Select(i => i.GetProperty("Id").GetUInt32()).Distinct().Count() == items.Length,
        $"Pinned Wiki reward section complete: {vault.Id}");
    foreach (var item in items)
        Check(vault.Items.Any(i => i.Id == item.GetProperty("Id").GetUInt32() &&
            i.EnglishName == item.GetProperty("EnglishName").GetString() && i.Sources.Contains("Consolegameswiki")),
            $"Wiki reward is in its own source catalog: {vault.Id}/{item.GetProperty("Id")}");
}
var deepMap = canals.Items.Single(i => i.Id == CleanupPolicy.DeepMapId);
var normal = canals.Items.First(i => CleanupPolicy.CanSelect(i) && i.Category == "素材");
var slot = new BagSlot(0, 3, normal.Id, 5, false);
var selection = new HashSet<uint> { normal.Id, CleanupPolicy.DeepMapId };
var outsideItem = outdoor.Items.Single(i => i.Id == 19994);
var outsideSlot = slot with { Slot = 4, ItemId = outsideItem.Id };
var allItems = catalog.Vaults.SelectMany(v => v.Items).DistinctBy(i => i.Id).ToDictionary(i => i.Id);
var legacy = new[] { new uint[] { normal.Id, 19770, 999999 }, new uint[] { outsideItem.Id, normal.Id } };
var global = CleanupPolicy.RestoreSelection(allItems.Values, null, legacy);
Check(global.SetEquals(new[] { normal.Id, outsideItem.Id }), "Upgrade unions all legacy catalogs and filters protected/unknown IDs");
Check(CleanupPolicy.RestoreSelection(allItems.Values, Array.Empty<uint>(), legacy).Count == 0, "Empty current selection never revives old rules");
Check(CleanupPolicy.RestoreSelection(allItems.Values, new[] { outsideItem.Id }, legacy).SetEquals(new[] { outsideItem.Id }), "Current settings override legacy after migration");
var restored = JsonSerializer.Deserialize<HashSet<uint>>(JsonSerializer.Serialize(global))!;
Check(restored.SetEquals(global), "Global selection persistence roundtrip");
Check(CleanupPolicy.Eligible(slot, normal, global) && CleanupPolicy.Eligible(outsideSlot, outsideItem, global), "All selected catalogs share one eligibility policy without territory inputs");
BagSlot[] multipleStacks = [slot, outsideSlot, slot with { Slot = 6 }, slot with { Slot = 7, ItemId = 19770 }];
var candidates = multipleStacks.Where(s => CleanupPolicy.Eligible(s, allItems.GetValueOrDefault(s.ItemId), global)).ToArray();
Check(candidates.Length == 3 && candidates.Sum(s => s.Quantity) == 15, "All selected stacks processed once, including multiple sources and duplicate item stacks");
CleanupPolicy.SetSelection(global, [outsideItem], false);
Check(!CleanupPolicy.Eligible(outsideSlot, outsideItem, global) && CleanupPolicy.Eligible(slot, normal, global), "Deselecting an item anywhere keeps it globally without changing other items");
var custom = new TreasureItem(999998, "測試補充物品", "Custom", "素材", true, true, ["手動新增"]);
var customSelection = CleanupPolicy.RestoreSelection([custom], null, new[] { new uint[] { custom.Id } });
Check(CleanupPolicy.Eligible(slot with { ItemId = custom.Id }, custom, customSelection), "Legacy manually added selections survive migration");
var future = catalog.Vaults.Single(v => v.Id == 55013).Items.First(CleanupPolicy.CanSelect);
Check(CleanupPolicy.Eligible(slot with { ItemId = future.Id }, future, new HashSet<uint> { future.Id }), "Local item from an unavailable source territory can still be discarded globally");
Check(CleanupPolicy.Eligible(slot, normal, selection), "Ordinary selected stack eligible");
Check(!CleanupPolicy.Eligible(slot, normal, new HashSet<uint>()), "Default empty selection keeps items");
Check(!CleanupPolicy.Eligible(slot with { ItemId = deepMap.Id }, deepMap, selection), "Deep map protected even in corrupted config");
Check(!CleanupPolicy.Eligible(slot with { Protected = true }, normal, selection), "Modified gear protected");
Check(!CleanupPolicy.Eligible(slot, normal with { Available = false }, selection), "Unknown local item protected");
Check(!CleanupPolicy.Eligible(slot, normal with { Discardable = false }, selection), "Indisposable item protected");
Check(!CleanupPolicy.Eligible(slot, null, selection), "Uncatalogued item protected");
Check(!CleanupPolicy.Eligible(slot, normal with { Id = 999999 }, selection), "Stale row identity rejected");
foreach (var bag in new[] { -1, 4, 1000, 2000 })
    Check(!CleanupPolicy.Eligible(slot with { Bag = bag }, normal, selection), "Only normal bags");
foreach (var index in new[] { -1, 35, 999 })
    Check(!CleanupPolicy.Eligible(slot with { Slot = index }, normal, selection), "Slot bounds");
foreach (var quantity in new[] { 0, -1 })
    Check(!CleanupPolicy.Eligible(slot with { Quantity = quantity }, normal, selection), "Positive quantity");
var tracker = new DiscardTracker();
tracker.Start(slot, [slot], 712, 0);
Check(tracker.Observe([slot], 712, 1000) == RequestResult.Waiting, "Wait for server inventory change");
Check(tracker.Observe([slot with { Slot = 8 }], 712, 1500) == RequestResult.Waiting, "Moving stack does not count as discard");
Check(tracker.Observe([slot with { Quantity = 4 }], 712, 2000) == RequestResult.Waiting, "Partial consumption does not acknowledge full-stack request");
Check(tracker.Observe([], 712, 2200) == RequestResult.InventoryChanged, "Full stack decrement acknowledged");
Check(tracker.Pending is null && !tracker.Faulted, "Success clears pending");
tracker.Start(slot, [slot], 712, 3000);
var rejected = false;
try { tracker.Start(slot, [slot], 712, 3200); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Cannot send concurrent requests");
Check(tracker.Observe([slot], 712, 10999) == RequestResult.Waiting, "Timeout boundary before eight seconds");
Check(tracker.Observe([slot], 712, 11000) == RequestResult.TimedOut && tracker.Faulted, "Timeout faults instead of retrying");
rejected = false;
try { tracker.Start(slot, [slot], 712, 12000); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Fault prevents further requests");
tracker.Reset();
tracker.Start(slot, [slot], 712, 13000);
tracker.OnTerritoryChanged();
Check(tracker.Observe([slot], 794, 13500) == RequestResult.Waiting && !tracker.Faulted,
    "Zone change retains the pending request without stopping or resending");
Check(tracker.Observe([], 794, 14000) == RequestResult.InventoryChanged && !tracker.Faulted,
    "Loaded inventory after transition can acknowledge the old request");
tracker.Start(slot, [slot], 712, 15000);
tracker.OnTerritoryChanged();
Check(tracker.Observe([slot with { Slot = 12 }], 794, 22999) == RequestResult.Waiting,
    "Moved stacks across zones do not acknowledge the original discard");
Check(tracker.Observe([slot with { Slot = 12 }], 794, 23000) == RequestResult.RescanAfterTransition && tracker.Pending is null && !tracker.Faulted,
    "Unconfirmed pre-transition request retires for a fresh scan without faulting");
tracker.Start(slot with { Slot = 12 }, [slot with { Slot = 12 }], 794, 23750);
Check(tracker.Pending?.Slot == 12, "Resume uses current location, never the old slot");
tracker.Reset();
tracker.Start(slot, [slot], 712, 0);
tracker.OnTerritoryChanged();
Check(tracker.Observe([slot], 712, 8000) == RequestResult.RescanAfterTransition,
    "Same-territory reload still resumes safely");
tracker.Reset();
tracker.Start(slot, [slot, slot with { Slot = 10, Quantity = 10 }], 712, 15000);
Check(tracker.Observe([slot with { Slot = 10, Quantity = 15 }], 712, 15500) == RequestResult.Waiting, "Stack merge does not acknowledge discard");
Check(tracker.Observe([slot with { Slot = 10, Quantity = 10 }], 712, 16000) == RequestResult.InventoryChanged, "Remaining matching stacks accounted for");
Check(Categories.Classify(18047, "Autumnal Maple Tree", 67, "庭具") == "家具", "Maple is not a map");
Check(Categories.Classify(19770, "Timeworn Thief's Map", 0, "雜貨") == "地圖", "Deep map classification");
Check(Categories.Classify(36000, "Calydontis Horn", 0, "其他", 1322) == "坐騎", "Mount classified using verified client action type");
Check(Categories.Classify(39592, "Burning Horn", 60, "雜貨", 0) == "其他", "Exchange token is not a mount");
Check(Categories.Classify(14838, "Expeditioner's Flyers", 0, "頭部防具") == "裝備", "Unavailable market category still recognizes equipment");
MapChecks.Run(Check);
DiscoveryChecks.Run(Check, catalog);
PerformanceChecks.Run(Check, catalog);
MateriaChecks.Run(Check, catalog);
SharedCatalogChecks.Run(Check, catalog);
Console.WriteLine($"PASS {checks} checks; {catalog.Vaults.Length} vaults; {catalog.Vaults.Sum(v => v.Items.Length)} per-vault records.");
