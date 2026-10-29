using System.Reflection;
using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Reflection.PortableExecutable;
using System.Text.Json;

System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => name.Name == "Lumina"
    ? context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Lumina.dll")) : null;
Run(args);
static void Run(string[] args)
{
if (args.FirstOrDefault() == "--items")
{
    using var data = new GameData(args[1], new LuminaOptions { DefaultExcelLanguage = Language.English });
    foreach (var id in args.Skip(2).Select(uint.Parse))
    {
        var row = data.GetExcelSheet<Item>()!.GetRow(id);
        Console.WriteLine($"ITEM {id} {row.Name}: search={row.ItemSearchCategory.RowId}; ui={row.ItemUICategory.RowId}/{row.ItemUICategory.Value.Name}; equip={row.EquipSlotCategory.RowId}; indisposable={row.IsIndisposable}; canBeHq={row.CanBeHq}");
    }
    foreach (var value in Enum.GetValues<InventoryItem.ItemFlags>()) Console.WriteLine($"FLAG {value}={(byte)value}");
    foreach (var field in new[] { "ItemId", "Quantity", "IsSymbolic", "Flags", "SpiritbondOrCollectability", "GlamourId" })
        Console.WriteLine($"OFFSET {field}=0x{System.Runtime.InteropServices.Marshal.OffsetOf<InventoryItem>(field):X}");
    return;
}
if (args.FirstOrDefault() == "--native")
{
    using var stream = File.OpenRead(args[1]);
    using var pe = new PEReader(stream);
    var section = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    var code = pe.GetSectionData(section.VirtualAddress).GetContent().ToArray();
    foreach (var (type, name) in new[] { (typeof(ActionManager), "UseAction"), (typeof(AgentInventoryContext), "OpenForItemSlot"),
        (typeof(EventItemManager), "GetTreasureHuntRank"), (typeof(RaptureHotbarModule), "SetAndSaveSlot"),
        (typeof(AtkUnitBase), "FireCallback"), (typeof(AtkUnitBase), "FireCallbackInt") })
    {
        var method = type.GetMethods().Single(m => m.Name == name);
        var signature = method.CustomAttributes.Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value!.ToString()!;
        var pattern = signature.Split(' ').Select(t => t == "??" || t == "?" ? (byte?)null : Convert.ToByte(t, 16)).ToArray();
        var matches = new List<int>();
        for (var offset = 0; offset <= code.Length - pattern.Length; offset++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
                if (pattern[j] is { } expected && code[offset + j] != expected) { match = false; break; }
            if (match) matches.Add(section.VirtualAddress + offset);
        }
        Console.WriteLine($"{type.Name}.{name}: matches={matches.Count}; RVA={string.Join(',', matches.Select(m => m.ToString("X")))}");
        var targets = matches.Select(match =>
        {
            var hit = match - section.VirtualAddress;
            return code[hit] is 0xE8 or 0xE9 ? match + 5 + BitConverter.ToInt32(code, hit + 1) : match;
        }).Distinct().ToArray();
        Console.WriteLine($"  resolved RVA={string.Join(',', targets.Select(t => t.ToString("X")))}; signature={signature}");
        if (targets.Length != 1) throw new Exception($"Resolved function is not unique: {type.Name}.{name}");
    }
    return;
}
if (args.FirstOrDefault() == "--maps-json")
{
    using var data = new GameData(args[1], new LuminaOptions { DefaultExcelLanguage = Language.English });
    var maps = data.GetExcelSheet<Item>()!.Where(i => i.FilterGroup == 18 && !string.IsNullOrWhiteSpace(i.Name.ToString()))
        .Select(i => new { Id = i.RowId, Name = i.Name.ToString(), Rank = i.AdditionalData.RowId }).ToArray();
    File.WriteAllText(args[2], JsonSerializer.Serialize(maps, new JsonSerializerOptions { WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"Verified {maps.Length} map rows and saved {args[2]}");
    return;
}
if (args.FirstOrDefault() == "--sdk")
{
    foreach (var type in new[] { typeof(ActionManager), typeof(AgentInventoryContext), typeof(AddonSelectYesno),
        typeof(RaptureHotbarModule), typeof(RaptureHotbarModule.HotbarSlot), typeof(EventItemManager), typeof(RaptureTextModule) })
    {
        Console.WriteLine(type.FullName);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Console.WriteLine($"FIELD {field.Name}: {field.FieldType}");
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && (m.Name.Contains("Action") || m.Name.Contains("Slot") || m.Name.Contains("Use") || m.Name.Contains("Decipher") || m.Name.Contains("Map") || m.Name.Contains("Treasure") || m.Name.Contains("FormatAddon"))))
            Console.WriteLine($"METHOD {method}");
    }
    foreach (var name in Enum.GetNames<RaptureHotbarModule.HotbarSlotType>()) Console.WriteLine($"SLOT_TYPE {name}");
    foreach (var property in typeof(AgentInventoryContext).GetProperties()) Console.WriteLine($"CONTEXT_PROPERTY {property.Name} {property.PropertyType}");
    return;
}
using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.English });
foreach (var row in game.GetExcelSheet<Item>()!.Where(i => i.FilterGroup == 18))
    Console.WriteLine($"MAP {row.RowId} {row.Name} action={row.ItemAction.RowId}/{row.ItemAction.Value.Action.RowId} param={row.AdditionalData.RowId}");
foreach (var row in game.GetExcelSheet<Addon>()!.Where(r => r.RowId is >= 8090 and <= 8130 || r.Text.ToString().Contains("解讀")))
    Console.WriteLine($"ADDON {row.RowId}: {row.Text} | {Convert.ToHexString(row.Text.Data.Span)}");
foreach (var property in typeof(TreasureHuntRank).GetProperties()) Console.WriteLine($"RANK_PROPERTY {property.Name} {property.PropertyType}");
}
