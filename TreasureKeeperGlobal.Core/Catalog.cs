using System.Text.Json;

namespace TreasureKeeperGlobal.Core;

public sealed record TreasureItem(uint Id, string Name, string EnglishName, string Category,
    bool Available, bool Discardable, string[] Sources, bool IsLooseMateria = false);

public sealed record TreasureVault(uint Id, uint TerritoryId, uint DutyId, string Name, string EnglishName,
    string Map, bool Available, TreasureItem[] Items, uint[]? OutdoorTerritoryIds = null)
{
    public bool IsOutdoor => OutdoorTerritoryIds is { Length: > 0 };
    public bool ContainsTerritory(uint territory) => Available && territory != 0 &&
        (IsOutdoor ? OutdoorTerritoryIds!.Contains(territory) : territory == TerritoryId);
}

public sealed record TreasureCatalog(string Updated, string ClientVersion, string TeamcraftCommit, TreasureVault[] Vaults)
{
    public static TreasureCatalog Load(Stream stream)
    {
        var catalog = JsonSerializer.Deserialize<TreasureCatalog>(stream) ?? throw new InvalidDataException("Missing catalog");
        if (catalog.Vaults.Length == 0 || catalog.Vaults.Select(v => v.Id).Distinct().Count() != catalog.Vaults.Length)
            throw new InvalidDataException("Empty or duplicate vaults");
        var active = catalog.Vaults.Where(v => v.Available && !v.IsOutdoor).ToArray();
        if (active.Any(v => v.TerritoryId == 0) || active.Select(v => v.TerritoryId).Distinct().Count() != active.Length)
            throw new InvalidDataException("Invalid territory mapping");
        foreach (var outdoor in catalog.Vaults.Where(v => v.IsOutdoor))
        {
            var zones = outdoor.OutdoorTerritoryIds!;
            if (outdoor.TerritoryId != 0 || outdoor.DutyId != 0 || zones.Any(id => id == 0) ||
                zones.Distinct().Count() != zones.Length || active.Any(v => zones.Contains(v.TerritoryId)))
                throw new InvalidDataException("Invalid outdoor mapping");
        }
        foreach (var vault in catalog.Vaults)
            if (vault.Items.Length == 0 || vault.Items.Any(i => i.Id == 0 || string.IsNullOrWhiteSpace(i.Name)) ||
                vault.Items.Select(i => i.Id).Distinct().Count() != vault.Items.Length)
                throw new InvalidDataException($"Invalid items in {vault.Name}");
        return catalog;
    }
}

public static class Categories
{
    public static readonly string[] All = ["全部", "素材", "魔晶石", "寵物", "坐騎", "樂譜", "家具", "外觀／動作", "裝備", "消耗品", "地圖", "水晶", "其他"];

    public static string Classify(uint id, string englishName, uint searchCategory, string uiCategory, uint actionType = 0)
    {
        if (id == CleanupPolicy.DeepMapId || englishName.EndsWith(" Map", StringComparison.OrdinalIgnoreCase) || uiCategory.Contains("地圖")) return "地圖";
        if (englishName.EndsWith(" Orchestrion Roll", StringComparison.OrdinalIgnoreCase)) return "樂譜";
        if (actionType == 1322 || uiCategory.Contains("坐騎")) return "坐騎";
        if (searchCategory == 75 || uiCategory.Contains("寵物")) return "寵物";
        if (searchCategory == 80) return "樂譜";
        if (searchCategory == 57) return "魔晶石";
        if (searchCategory is 56 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 82) return "家具";
        if (searchCategory == 90 || englishName.StartsWith("Modern Aesthetics") || englishName.StartsWith("Ballroom Etiquette")) return "外觀／動作";
        if (uiCategory.Contains("防具") || uiCategory == "套裝" || searchCategory is >= 9 and <= 29 or >= 31 and <= 42 or 73 or 76 or 77 or 78 or >= 83 and <= 89 or 91 or 92) return "裝備";
        if (searchCategory is 43 or 45 or 54) return "消耗品";
        if (searchCategory == 58 || id is >= 2 and <= 19) return "水晶";
        if (searchCategory is 44 or 46 or >= 47 and <= 53 or 55 or 59 or 81) return "素材";
        return "其他";
    }
}
