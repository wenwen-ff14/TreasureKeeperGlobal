using System.Text.Json;
using TreasureKeeperGlobal.Core;

internal static class SharedCatalogChecks
{
    internal static void Run(Action<bool, string> check, TreasureCatalog catalog)
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "g10-observed.json")));
        var source = file.RootElement.GetProperty("SourceKey").GetString()!;
        var scopes = file.RootElement.GetProperty("Scopes").EnumerateArray().ToArray();
        check(scopes.Select(s => s.GetProperty("VaultId").GetUInt32()).ToHashSet().SetEquals(new uint[] { 17836, 55002, 55003, 55004 }),
            "Shared snapshot keeps the four G10 scopes separate");
        foreach (var scope in scopes)
        {
            var vault = catalog.Vaults.Single(v => v.Id == scope.GetProperty("VaultId").GetUInt32());
            var ids = scope.GetProperty("ItemIds").EnumerateArray().Select(i => i.GetUInt32()).ToArray();
            var added = scope.GetProperty("NewItemIds").EnumerateArray().Select(i => i.GetUInt32()).ToArray();
            check(vault.TerritoryId == scope.GetProperty("TerritoryId").GetUInt32(), "Shared source territory matches the catalog");
            check(ids.Distinct().Count() == ids.Length && added.All(ids.Contains), "Shared observations are unique and contain all additions");
            check(vault.Items.Where(i => i.Sources.Contains(source)).Select(i => i.Id).ToHashSet().SetEquals(ids),
                "Shared provenance appears only in its actual vault and never leaks to other G10 lists");
            foreach (var id in ids)
            {
                var item = vault.Items.Single(i => i.Id == id);
                check(item.Available && Categories.All.Contains(item.Category) && !string.IsNullOrWhiteSpace(item.EnglishName),
                    $"Observed item has verified local name, category and English name: {id}");
                check(!item.Sources.Contains(LootDiscoveryCatalog.Source), "Shared evidence is not mistaken for this player's personal observation");
            }
            if (vault.Id != 55004) { check(ids.Length == 0 && added.Length == 0, "No unobserved source scope receives additions"); continue; }
            check(ids.Length == 53 && added.Length == 19 && vault.Items.Length == 117, "Pinned 2026-10-15 altars snapshot: 53 observations and 19 additions");
            var newItems = vault.Items.Where(i => added.Contains(i.Id)).ToArray();
            check(newItems.Count(i => i.Category == "素材") == 14 && newItems.Count(i => i.Category == "魔晶石") == 4 &&
                newItems.Count(i => i.Category == "消耗品") == 1, "New altars rows are classified as 14 materials, 4 materia and 1 consumable");
            check(!CleanupPolicy.CanSelect(vault.Items.Single(i => i.Id == 19770)) &&
                vault.Items.Where(i => i.Category == "水晶").All(i => !CleanupPolicy.CanSelect(i)), "Shared observations preserve deep-map and crystal protection");
        }
        check(catalog.Vaults.Where(v => v.Id is not (17836 or 55002 or 55003 or 55004)).All(v => v.Items.All(i => !i.Sources.Contains(source))),
            "G10 observations do not change other map families");
    }
}
