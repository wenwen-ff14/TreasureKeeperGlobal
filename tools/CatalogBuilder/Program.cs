using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using TreasureKeeperGlobal.Core;

System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => name.Name == "Lumina"
    ? context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Lumina.dll")) : null;
Run(args);

static void Run(string[] args)
{
    if (args.Length != 1) throw new ArgumentException("CatalogBuilder <game/sqpack>; run from TreasureKeeperGlobal root after Fetch-CatalogSources.ps1");
    using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.English });
    var duties = game.GetExcelSheet<ContentFinderCondition>()!.Where(d => d.ContentType.RowId == 9).ToArray();
    var sheet = game.GetExcelSheet<Item>()!;
    using var instances = JsonDocument.Parse(File.ReadAllText(".cache/teamcraft/instances.json"));
    using var extracts = JsonDocument.Parse(File.ReadAllText(".cache/teamcraft/extracts.json"));
    using var names = JsonDocument.Parse(File.ReadAllText(".cache/teamcraft/items.json"));
    var byName = names.RootElement.EnumerateObject().Where(p => p.Value.TryGetProperty("en", out _))
        .GroupBy(p => p.Value.GetProperty("en").GetString()!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Select(p => uint.Parse(p.Name)).ToArray(), StringComparer.OrdinalIgnoreCase);
    var pages = new Dictionary<uint, (string Page, string Map)>
    {
        [55001] = ("The_Aquapolis", "G8"),
        [17836] = ("Timeworn_Gazelleskin_Map", "G10 外場"),
        [55002] = ("The_Lost_Canals_of_Uznair", "G10"),
        [55003] = ("The_Hidden_Canals_of_Uznair", "深層地圖"),
        [55004] = ("The_Shifting_Altars_of_Uznair", "G10"),
        [55006] = ("The_Dungeons_of_Lyhe_Ghiah", "G12"),
        [55008] = ("The_Shifting_Oubliettes_of_Lyhe_Ghiah", "G12"),
        [55009] = ("The_Excitatron_6000", "G14"),
        [55011] = ("The_Shifting_Gymnasion_Agonon", "G15"),
        [55012] = ("Cenote_Ja_Ja_Gural", "G17"),
        [55013] = ("Vault_Oneiron", "G18"),
    };
    var sources = pages.Keys.ToDictionary(id => id, _ => new Dictionary<uint, HashSet<string>>());
    using var mapLoot = JsonDocument.Parse(File.ReadAllText(".cache/teamcraft/loot-sources.json"));
    foreach (var entry in mapLoot.RootElement.EnumerateObject())
        if (entry.Value.EnumerateArray().Any(v => v.GetUInt32() == 17836))
            AddSource(sources[17836], uint.Parse(entry.Name), "Teamcraft-G10-outdoor");
    using var supplement = JsonDocument.Parse(File.ReadAllText("docs/g10-supplement.json"));
    foreach (var source in supplement.RootElement.GetProperty("Sources").EnumerateArray())
    foreach (var group in source.GetProperty("Groups").EnumerateObject())
    foreach (var item in group.Value.EnumerateArray())
    {
        var id = item.GetUInt32();
        if (!names.RootElement.TryGetProperty(id.ToString(), out _) || !sheet.TryGetRow(id, out _))
            throw new InvalidDataException($"Unresolved supplement ID {id}");
        AddSource(sources[source.GetProperty("VaultId").GetUInt32()], id, source.GetProperty("Key").GetString()!);
    }
    using var observed = JsonDocument.Parse(File.ReadAllText("docs/g10-observed.json"));
    var observedKey = observed.RootElement.GetProperty("SourceKey").GetString()!;
    foreach (var scope in observed.RootElement.GetProperty("Scopes").EnumerateArray())
    {
        var vaultId = scope.GetProperty("VaultId").GetUInt32();
        if (vaultId is not (17836 or 55002 or 55003 or 55004)) throw new InvalidDataException("Unexpected G10 observation scope");
        foreach (var entry in scope.GetProperty("ItemIds").EnumerateArray())
        {
            var itemId = entry.GetUInt32();
            if (vaultId == 17836 || !sheet.TryGetRow(itemId, out _) || !names.RootElement.TryGetProperty(itemId.ToString(), out _))
                throw new InvalidDataException($"Unverified observed item {vaultId}/{itemId}");
            AddSource(sources[vaultId], itemId, observedKey);
        }
    }
    var excludedHistorical = new HashSet<uint>();
    var japanese = names.RootElement.EnumerateObject().Where(p => p.Value.TryGetProperty("ja", out _))
        .GroupBy(p => p.Value.GetProperty("ja").GetString()!.Normalize(NormalizationForm.FormKC))
        .ToDictionary(g => g.Key, g => g.Select(p => uint.Parse(p.Name)).ToArray());
    foreach (var article in new[] { 4721, 4744, 4759 })
    {
        var html = File.ReadAllText($".cache/g10-audit/house-{article}.html");
        var start = html.IndexOf("class=\"entry-content", StringComparison.Ordinal);
        var end = html.IndexOf("</article>", start, StringComparison.Ordinal);
        if (start < 0 || end <= start) throw new InvalidDataException("Missing historical article boundary");
        var text = Regex.Replace(html[start..end], "(?is)<script.*?</script>|<style.*?</style>", "");
        text = Regex.Replace(text, "(?i)<br\\s*/?>|</p>|</h[1-6]>", "\n");
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", "")).Normalize(NormalizationForm.FormKC);
        var matches = Regex.Matches(text, "(?m)^\\s*(.+?)\\s*×\\s*\\d+\\s*$");
        if (matches.Count < 20) throw new InvalidDataException("Historical drop lines unexpectedly missing");
        foreach (Match match in matches)
        {
            var ja = Regex.Replace(match.Groups[1].Value.Trim(), "HQ$", "").Trim();
            if (!japanese.TryGetValue(ja, out var ids) || ids.Length != 1)
                throw new InvalidDataException($"Unresolved historical drop: {ja}");
            var id = ids[0];
            var en = names.RootElement.GetProperty(id.ToString()).GetProperty("en").GetString()!;
            // Main-stat materia were retired. Keep the evidence in the audit, not the current selection list.
            if (new[] { "Strength Materia", "Dexterity Materia", "Vitality Materia", "Intelligence Materia", "Mind Materia" }.Any(en.StartsWith))
            { excludedHistorical.Add(id); continue; }
            AddSource(sources[55002], id, $"FF14house-4.05-{article}");
        }
    }
    foreach (var entry in extracts.RootElement.EnumerateObject())
    foreach (var source in entry.Value.GetProperty("sources").EnumerateArray())
    {
        if (source.GetProperty("type").GetInt32() != 6) continue;
        foreach (var instance in source.GetProperty("data").EnumerateArray())
            if (instance.TryGetUInt32(out var id) && sources.TryGetValue(id, out var loot))
                AddSource(loot, uint.Parse(entry.Name), "Teamcraft");
    }
    var unmatched = new List<string>();
    var vaults = new List<TreasureVault>();
    foreach (var (id, (page, map)) in pages)
    {
        var outdoor = id == 17836;
        var instance = outdoor ? default : instances.RootElement.GetProperty(id.ToString());
        var englishName = outdoor ? "G10 Outdoor / Gazelleskin Treasure Map" : instance.GetProperty("en").GetString()!;
        var mapId = outdoor ? uint.MaxValue : instance.GetProperty("map").GetUInt32();
        var matching = duties.Where(d => d.TerritoryType.Value.Map.RowId == mapId).ToArray();
        if (matching.Length > 1) throw new InvalidDataException($"Ambiguous territory for {id}");
        var available = outdoor || matching.Length == 1;
        var duty = matching.FirstOrDefault();
        uint[]? outdoorZones = outdoor ? [612, 613, 614, 620, 621, 622] : null;
        if (outdoorZones is not null)
            foreach (var zone in outdoorZones)
            {
                if (!game.GetExcelSheet<TerritoryType>()!.TryGetRow(zone, out var territory) ||
                    territory.ContentFinderCondition.RowId != 0 || string.IsNullOrWhiteSpace(territory.PlaceName.Value.Name.ToString()))
                    throw new InvalidDataException($"Unverified outdoor territory {zone}");
                Console.WriteLine($"G10 outdoor territory {zone}: {territory.PlaceName.Value.Name}");
            }
        var html = File.ReadAllText($".cache/wiki/{page}.html");
        var start = Regex.Match(html, outdoor ? "<h3[^>]*id=\"Loot\"" : "<h2[^>]*id=\"(?:Possible|Potential)_Rewards\"");
        if (!start.Success) throw new InvalidDataException($"Missing rewards section: {page}");
        var rest = html[(start.Index + start.Length)..];
        var end = Regex.Match(rest, "<h2[ >]");
        if (!end.Success) throw new InvalidDataException($"Missing rewards section boundary: {page}");
        var section = rest[..end.Index];
        foreach (var title in Regex.Matches(section, "<a[^>]+title=\"([^\"]+)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct())
        {
            if (byName.TryGetValue(title, out var ids) && ids.Length == 1) AddSource(sources[id], ids[0], "Consolegameswiki");
            else unmatched.Add($"{page}: {title}");
        }
        var items = sources[id].OrderBy(p => p.Key).Select(pair =>
        {
            var en = names.RootElement.GetProperty(pair.Key.ToString()).GetProperty("en").GetString()!;
            var found = sheet.TryGetRow(pair.Key, out var row) && !string.IsNullOrWhiteSpace(row.Name.ToString());
            var category = Categories.Classify(pair.Key, en, found ? row.ItemSearchCategory.RowId : 0,
                found ? row.ItemUICategory.Value.Name.ToString() : "", found ? row.ItemAction.Value.Action.RowId : 0u);
            return new TreasureItem(pair.Key, found ? row.Name.ToString() : en, en, category, found,
                found && !row.IsIndisposable && pair.Key > 19, pair.Value.Order().ToArray(),
                found && row.ItemSearchCategory.RowId == 57 && row.EquipSlotCategory.RowId == 0);
        }).ToArray();
        vaults.Add(new(id, available && !outdoor ? duty.TerritoryType.RowId : 0, available && !outdoor ? duty.RowId : 0,
            outdoor ? "G10 外場寶箱" : available ? duty.Name.ToString() : englishName, englishName, map, available, items, outdoorZones));
        Console.WriteLine($"{id} {vaults[^1].Name} zone={vaults[^1].TerritoryId}: {items.Length} items ({items.Count(i => i.Available)} local)");
    }
    var version = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(args[0]))!, "ffxivgame.ver")).Trim();
    var result = new TreasureCatalog(observed.RootElement.GetProperty("SnapshotDate").GetString()!, version,
        File.ReadAllText(".cache/teamcraft/commit.txt").Trim(), vaults.ToArray());
    Directory.CreateDirectory("TreasureKeeperGlobal/Data");
    Directory.CreateDirectory("docs");
    var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    File.WriteAllLines(".cache/unmatched-wiki-links.txt", unmatched);
    var unexpected = unmatched.Where(line => !line.EndsWith(": Final Fantasy XIV Wiki:Style manual") &&
        !line.EndsWith(": Fashion Accessories") && !line.EndsWith(": Treasure Hunt") &&
        !line.EndsWith(": The Lost Canals of Uznair") && !line.EndsWith(": The Shifting Altars of Uznair")).ToArray();
    if (unexpected.Length > 0) throw new InvalidDataException("Unmatched reward links: " + string.Join("; ", unexpected));
    // Validate every source before replacing the embedded catalog; unresolved names must never silently disappear.
    File.WriteAllText("TreasureKeeperGlobal/Data/catalog.json", JsonSerializer.Serialize(result, options));
    var audit = new
    {
        result.Updated,
        Note = "Known source relationships; not a server-complete loot table. Historical-only entries are explicitly identified.",
        ExcludedRetiredMateria = excludedHistorical.Order().ToArray(),
        Scopes = vaults.Where(v => v.Id is 17836 or 55002 or 55003 or 55004).Select(v => new
        {
            v.Id, v.Name, Count = v.Items.Length,
            HistoricalOnly = v.Items.Where(i => i.Sources.All(s => s.StartsWith("FF14house-"))).Select(i => i.Id).ToArray(),
            Items = v.Items.Select(i => new { i.Id, i.Name, i.EnglishName, i.Category, i.Sources })
        })
    };
    File.WriteAllText("docs/g10-audit.json", JsonSerializer.Serialize(audit, options));
    var markdown = new StringBuilder($"# G10 分庫分類物品清單\n\n更新：{result.Updated}。名稱、分類與可丟棄性使用繁中客戶端 `{version}` 核對。\n\n" +
        "這是公開來源與庫內取得補錄的聯集，並非伺服器完整掉落表。同一物品依來源分庫列出；補錄不會套用到其他寶物庫。\n\n" +
        "「社群補錄」表示本次分享的庫內取得紀錄；「僅舊版實測」仍待現版確認。個人是否丟棄由插件設定決定，表中只列可操作性。\n\n" +
        "| 清單 | 物品數 | 社群補錄 | 本次新增 |\n|---|---:|---:|---:|\n");
    var orderedScopes = new uint[] { 17836, 55002, 55004, 55003 };
    foreach (var id in orderedScopes)
    {
        var vault = vaults.Single(v => v.Id == id);
        var scope = observed.RootElement.GetProperty("Scopes").EnumerateArray().Single(s => s.GetProperty("VaultId").GetUInt32() == id);
        markdown.AppendLine($"| {vault.Name} | {vault.Items.Length} | {scope.GetProperty("ItemIds").GetArrayLength()} | {scope.GetProperty("NewItemIds").GetArrayLength()} |");
    }
    foreach (var id in orderedScopes)
    {
        var vault = vaults.Single(v => v.Id == id);
        markdown.AppendLine($"\n## {vault.Name}（{vault.Items.Length} 項）\n");
        foreach (var category in Categories.All.Skip(1))
        {
            var items = vault.Items.Where(i => i.Category == category).ToArray();
            if (items.Length == 0) continue;
            markdown.AppendLine($"### {category}（{items.Length}）\n\n| ID | 繁中物品名 | 英文名 | 可操作性 | 取得證據 |\n|---:|---|---|---|---|");
            foreach (var item in items)
            {
                var evidence = item.Sources.Contains(observedKey) ? "社群補錄" : item.Sources.All(s => s.StartsWith("FF14house-")) ? "僅舊版實測" : "公開清單";
                var disposition = item.Id == CleanupPolicy.DeepMapId ? "固定保留" : CleanupPolicy.CanSelect(item) ? "可選擇丟棄" : !item.Available ? "本機無此物品" : "不可丟棄";
                markdown.AppendLine($"| {item.Id} | {item.Name.Replace("|", "\\|")} | {item.EnglishName.Replace("|", "\\|")} | {disposition} | {evidence} |");
            }
        }
    }
    markdown.AppendLine("\n完整逐項來源鍵見 [g10-audit.json](g10-audit.json)；本次補錄 ID 見 [g10-observed.json](g10-observed.json)。\n");
    File.WriteAllText("docs/G10_ITEMS.md", markdown.ToString());
    foreach (var group in vaults.SelectMany(v => v.Items).DistinctBy(i => i.Id).GroupBy(i => i.Category))
        Console.WriteLine($"CATEGORY {group.Key}: {group.Count()} [{string.Join(", ", group.Take(4).Select(i => i.Name))}]");
    Console.WriteLine($"Unmatched links: {unmatched.Count}; inspect .cache/unmatched-wiki-links.txt");
}

static void AddSource(Dictionary<uint, HashSet<string>> loot, uint id, string source)
{
    if (!loot.TryGetValue(id, out var set)) loot[id] = set = [];
    set.Add(source);
}
