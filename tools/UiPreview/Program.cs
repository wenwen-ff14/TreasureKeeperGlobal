using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using HexaGen.Runtime;
using TreasureKeeperGlobal.Core;
using TreasureKeeperGlobal.Ui;

unsafe
{
    var performance = args.FirstOrDefault() == "--performance";
    var output = Path.GetFullPath((performance ? args.ElementAtOrDefault(1) : args.FirstOrDefault()) ?? "artifacts/ui-preview");
    Directory.CreateDirectory(output);
    using var native = new NativeLibraryContext(Path.Combine(AppContext.BaseDirectory, "cimgui.dll"));
    ImGui.InitApi(native);
    var context = ImGui.CreateContext();
    try
    {
        var io = ImGui.GetIO();
        io.IniFilename = null; io.LogFilename = null; io.DeltaTime = 1 / 60f;
        var font = ImGui.ImFontConfig();
        ushort[] ranges = [0x20, 0xFF, 0x2000, 0x206F, 0x3000, 0x30FF, 0x4E00, 0x9FFF, 0xFF00, 0xFFEF, 0];
        fixed (ushort* glyphs = ranges)
        {
            io.Fonts.AddFontFromFileTTF("C:/Windows/Fonts/msjh.ttc", 17, font, glyphs);
            if (!io.Fonts.Build()) throw new Exception("Font atlas failed");
        }
        ImGui.Destroy(font);
        var textures = new Dictionary<ulong, Texture>();
        for (var i = 0; i < io.Fonts.Textures.Size; i++)
        {
            byte* pixels; int w, h;
            io.Fonts.GetTexDataAsRGBA32(i, &pixels, &w, &h);
            var data = new byte[w * h * 4];
            Marshal.Copy((nint)pixels, data, 0, data.Length);
            io.Fonts.SetTexID(i, new ImTextureID((ulong)i + 1));
            textures[(ulong)i + 1] = new(w, h, data);
        }
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "catalog.json"));
        var catalog = TreasureCatalog.Load(file);
        if (performance) { PerformanceAudit.Run(catalog, output); return; }
        var selections = new HashSet<uint>();
        var active = false;
        uint territory = 129; // City: all catalogs must work without duty or outdoor-session gating.
        var selectedCalls = 0;
        var discoverEnabled = true;
        var discovered = new Dictionary<uint, Dictionary<uint, DiscoveredLoot>>();
        var discoveryDefaults = new Dictionary<uint, bool>();
        var configured = new HashSet<uint>();
        var actions = new TreasureActions(v => active = v, (id, rows, discard) =>
        {
            selectedCalls++;
            CleanupPolicy.SetSelection(selections, rows, discard, configured);
        }, _ => active = false, _ => [], (_, _) => { }, () => { }, enabled => discoverEnabled = enabled,
            (id, discard) => discoveryDefaults[id] = discard);
        var view = new TreasureView();
        var canals = catalog.Vaults.Single(v => v.Id == 55002);
        var materia = canals.Items.First(i => i.Category == "魔晶石");
        BagSlot[] slots = [new(0, 0, materia.Id, 5, false), new(0, 1, 19770, 2, false)];
        foreach (var scenario in new[] { ("standard", 1080, 820, 1f), ("narrow", 840, 740, 1f), ("large-font", 1500, 1150, 1.5f) })
        {
            var (name, width, height, scale) = scenario;
            io.DisplaySize = new(width, height); io.FontGlobalScale = scale;
            for (var frame = 0; frame < 3; frame++) Frame(name, width, height);
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, Path.Combine(output, name + ".png"));
            Console.WriteLine($"Rendered {name}: {width}x{height}");
        }
        // Global selections are shared across source catalogs, including while the character is in a city.
        io.DisplaySize = new(1080, 820); io.FontGlobalScale = 1;
        for (var i = 0; i < 3; i++) Frame("standard", 1080, 820);
        Click(312, 368);
        if (selectedCalls != 1 || selections.Count == 0 || selections.Contains(19770))
            throw new Exception($"UI bulk select failed: calls={selectedCalls}, selected={selections.Count}");
        Console.WriteLine("PASS actual ImGui global bulk selection in a city and deep-map exclusion");
        Click(281, 461);
        if (selections.Contains(19770) || selectedCalls != 1) throw new Exception("Fixed-keep checkbox was editable");
        var beforeCategory = selections.Count;
        view.Category = Array.IndexOf(Categories.All, "魔晶石");
        Frame("standard", 1080, 820);
        Click(405, 368);
        if (selections.Count != beforeCategory - canals.Items.Count(i => i.Category == "魔晶石" && CleanupPolicy.CanSelect(i)))
            throw new Exception("Category deselection affected wrong items");
        Console.WriteLine("PASS actual disabled deep-map checkbox and category-scoped deselection");
        view.Category = 0;
        Frame("standard", 1080, 820);
        Click(312, 368);
        Frame("standard", 1080, 820);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "selected.png"));
        Click(90, 100);
        if (!active) throw new Exception("Global start failed in a city");
        var sharedItem = catalog.Vaults.Single(v => v.Id == 17836).Items.Single(i => i.Id == 19994);
        if (!selections.Contains(sharedItem.Id)) throw new Exception("Missing shared item from canals bulk selection");
        view.VaultId = 17836;
        view.Search = sharedItem.Id.ToString();
        Frame("standard", 1080, 820);
        Click(405, 368);
        if (selections.Contains(sharedItem.Id) || !active) throw new Exception("Shared deselection did not apply live");
        Click(312, 368);
        if (!selections.Contains(sharedItem.Id) || !active) throw new Exception("Shared selection did not apply live");
        view.Search = "";
        Frame("standard", 1080, 820);
        Click(312, 368);
        if (catalog.Vaults.Single(v => v.Id == 17836).Items.Where(CleanupPolicy.CanSelect).Any(i => !selections.Contains(i.Id)))
            throw new Exception("Outdoor source bulk selection failed in city");
        Console.WriteLine("PASS live shared selection/deselection, outdoor source in a city, and global start");
        Frame("standard", 1080, 820);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "outdoor.png"));
        Console.WriteLine("Rendered outdoor: 1080x820");
        // The currently displayed outdoor source has no materia: preview must still show the selected canal materia.
        if (catalog.Vaults.Single(v => v.Id == 17836).Items.Any(i => i.Id == materia.Id))
            throw new Exception("Preview fixture must span different source catalogs");
        Click(385, 260);
        Frame("standard", 1080, 820);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "global-preview.png"));
        Console.WriteLine("Rendered global preview in a city, with selected inventory from another source catalog");

        var previousSlots = slots;
        var hqMaterial = canals.Items.First(i => i.Category == "素材" && selections.Contains(i.Id));
        slots = [new(0, 0, materia.Id, 3, false), new(0, 1, hqMaterial.Id, 2, true, DiscardBlockReason.HighQuality, 1)];
        Frame("standard", 1080, 820);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "blocked-preview.png"));
        Console.WriteLine("Rendered eligible loose materia alongside selected-but-kept HQ with its reason");
        slots = previousSlots;

        view.VaultId = canals.Id;
        var observed = canals.Items.First(i => i.Category == "素材" && CleanupPolicy.CanSelect(i));
        catalog.Vaults[Array.FindIndex(catalog.Vaults, v => v.Id == canals.Id)] = LootDiscoveryCatalog.Merge(canals, observed);
        var stamp = new DateTimeOffset(2026, 10, 15, 3, 40, 0, TimeSpan.FromHours(8));
        discovered[canals.Id] = new() { [observed.Id] = new(observed.Id, stamp, stamp) };
        Frame("standard", 1080, 820);
        Click(305, 260);
        Click(483, 341);
        if (!view.DiscoveredOnly) throw new Exception("Observed-only inventory filter did not enable");
        Frame("standard", 1080, 820);
        var oldCount = selections.Count;
        var wasSelected = selections.Contains(observed.Id);
        Click(405, 368);
        if (selections.Contains(observed.Id) || selections.Count != oldCount - (wasSelected ? 1 : 0))
            throw new Exception("Observed-only bulk action affected unrelated inventory rules");
        Frame("standard", 1080, 820);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "discovered-items.png"));
        Click(530, 260);
        Frame("standard", 1080, 820);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "discovery.png"));
        Click(281, 289);
        if (discoverEnabled) throw new Exception("Automatic discovery toggle did not disable");
        Click(281, 289);
        if (!discoverEnabled || discovered[canals.Id].Count != 1) throw new Exception("Toggling discovery lost persisted observations");
        Console.WriteLine("PASS actual ImGui discovery toggle, learned-only filter and scoped bulk selection");
        var selectionsBeforeDefault = selections.ToHashSet();
        ChooseDisposition(392, 335, true);
        if (!discoveryDefaults.GetValueOrDefault(canals.Id) || !selections.SetEquals(selectionsBeforeDefault))
            throw new Exception("Vault default failed or retroactively changed existing selections");
        view.VaultId = 55004;
        Frame("standard", 1080, 820);
        if (discoveryDefaults.GetValueOrDefault(55004u)) throw new Exception("Canal default leaked into altars");
        ChooseDisposition(392, 335, true);
        if (!discoveryDefaults.GetValueOrDefault(55004u)) throw new Exception("Altars default targeted the wrong vault");
        ChooseDisposition(392, 335, false);
        if (discoveryDefaults.GetValueOrDefault(55004u) || !discoveryDefaults.GetValueOrDefault(canals.Id))
            throw new Exception("Changing altars default also changed canals");
        view.VaultId = canals.Id;
        Frame("standard", 1080, 820);
        var beforeDisposition = selectedCalls;
        ChooseDisposition(893, 494, true);
        if (selectedCalls != beforeDisposition + 1 || !selections.Contains(observed.Id) || !active)
            throw new Exception("Discovery per-item discard did not update the live global selection");
        ChooseDisposition(893, 494, false);
        if (selectedCalls != beforeDisposition + 2 || selections.Contains(observed.Id) || !configured.Contains(observed.Id))
            throw new Exception("Discovery per-item keep was not saved as an explicit decision");
        DiscoverySelectionPolicy.ApplyDefault(canals.Id, observed, discoveryDefaults, configured, selections);
        if (selections.Contains(observed.Id)) throw new Exception("Discovery default overwrote the UI keep decision");
        Click(305, 260); Click(312, 368); Click(530, 260);
        if (!selections.Contains(observed.Id)) throw new Exception("Item-list changes failed to share the discovery selection");
        var crystal = canals.Items.First(i => i.Category == "水晶");
        discovered[canals.Id][19770] = new(19770, stamp.AddMinutes(-1), stamp.AddMinutes(-1));
        discovered[canals.Id][crystal.Id] = new(crystal.Id, stamp.AddMinutes(-2), stamp.AddMinutes(-2));
        Frame("standard", 1080, 820);
        var beforeProtected = selectedCalls;
        Click(893, 520); Click(893, 542);
        if (selectedCalls != beforeProtected || selections.Contains(19770) || selections.Contains(crystal.Id))
            throw new Exception("Protected discovery rows were editable");
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 1080, 820, Path.Combine(output, "discovery.png"));
        Console.WriteLine("PASS actual ImGui independent vault defaults, live per-item discard/keep, global sync and protected discovery rows");
        foreach (var scenario in new[] { ("discovery-narrow", 840, 740, 1f), ("discovery-large-font", 1500, 1150, 1.5f) })
        {
            var (name, width, height, scale) = scenario;
            io.DisplaySize = new(width, height); io.FontGlobalScale = scale;
            for (var i = 0; i < 3; i++) Frame("standard", width, height);
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, Path.Combine(output, name + ".png"));
            Console.WriteLine($"Rendered {name}: {width}x{height}");
        }

        var maps = JsonSerializer.Deserialize<TreasureMapItem[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "map-items.json")))!;
        var mapView = new MapView();
        var mapEnabled = true;
        var autoConfirm = true;
        var mapBusy = false;
        var placed = new List<(uint Item, int Bar, int Slot)>();
        var decoded = new List<uint>();
        var mapActions = new MapActions(value => mapEnabled = value, value => autoConfirm = value,
            (id, bar, index) => placed.Add((id, bar, index)), id => decoded.Add(id), () => { });
        BagSlot[] mapSlots = [new(0, 1, 17836, 1, false), new(0, 2, 19770, 2, false)];
        foreach (var scenario in new[] { ("maps", 1080, 820, 1f), ("maps-narrow", 840, 740, 1f), ("maps-large-font", 1500, 1150, 1.5f) })
        {
            var (name, width, height, scale) = scenario;
            io.DisplaySize = new(width, height); io.FontGlobalScale = scale;
            for (var i = 0; i < 3; i++) MapFrame(name, width, height);
            SoftwareRenderer.Render(ImGui.GetDrawData(), textures, width, height, Path.Combine(output, name + ".png"));
            Console.WriteLine($"Rendered {name}: {width}x{height}");
        }

        io.DisplaySize = new(840, 740); io.FontGlobalScale = 1;
        for (var i = 0; i < 3; i++) MapFrame("maps-narrow", 840, 740);
        MapClick(667, 354);
        if (placed.Count != 1 || placed[0] != (6688u, 0, 0)) throw new Exception("Map placement did not target the selected empty-slot request");
        MapClick(762, 354);
        if (decoded.Count != 0) throw new Exception("Map without inventory was clickable for decipher");
        mapView.Search = "G10";
        MapFrame("maps-narrow", 840, 740);
        MapClick(762, 354);
        if (decoded.Count != 1 || decoded[0] != 17836) throw new Exception("Filtered G10 decipher action failed");
        mapView.Bar = 17; mapView.Slot = 15;
        MapFrame("maps-narrow", 840, 740);
        MapClick(667, 354);
        if (placed[^1] != (17836u, 17, 15)) throw new Exception("Cross-hotbar placement did not preserve requested map and slot");
        MapClick(140, 94);
        if (autoConfirm) throw new Exception("Map automatic confirmation setting did not change");
        MapClick(27, 94);
        if (mapEnabled) throw new Exception("Map hotbar enable setting did not change");
        MapClick(762, 354);
        if (decoded.Count != 1) throw new Exception("Disabled map feature still accepted decipher clicks");
        MapClick(27, 94); MapClick(140, 94);
        mapView.Search = "";
        MapFrame("maps-narrow", 840, 740);
        MapClick(27, 282);
        MapClick(762, 380);
        if (!mapView.InBagOnly || decoded.Count != 2 || decoded[^1] != 19770)
            throw new Exception("Inventory filter or explicit deep-map decipher action failed");
        MapFrame("maps-narrow", 840, 740);
        SoftwareRenderer.Render(ImGui.GetDrawData(), textures, 840, 740, Path.Combine(output, "maps-inventory.png"));
        Console.WriteLine("PASS actual ImGui map search, inventory filter, standard/cross-hotbar placement, decipher, deep map and settings");
        mapBusy = true;
        var beforeRetry = decoded.Count;
        var beforePlacement = placed.Count;
        MapClick(762, 354);
        MapClick(667, 354);
        if (decoded.Count != beforeRetry + 1 || placed.Count != beforePlacement)
            throw new Exception("Busy map must allow an explicit retry while still blocking hotbar edits");
        Console.WriteLine("PASS actual ImGui retry remains clickable during a pending map attempt");

        void MapClick(float x, float y)
        {
            io.AddMousePosEvent(x, y); MapFrame("maps-narrow", 840, 740);
            io.AddMouseButtonEvent(0, true); MapFrame("maps-narrow", 840, 740);
            io.AddMouseButtonEvent(0, false); MapFrame("maps-narrow", 840, 740);
        }

        void MapFrame(string name, int width, int height)
        {
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(new(8, 8), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new(width - 16, height - 16), ImGuiCond.Always);
            if (ImGui.Begin("地圖快捷解讀 · 離線示範##" + name, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                mapView.Draw(new(maps, mapSlots, mapEnabled, autoConfirm, true, mapBusy, "離線示範：按下快捷鍵即可解讀；此預覽不會操作遊戲。"), mapActions);
            ImGui.End(); ImGui.Render();
        }

        void Click(float x, float y)
        {
            io.AddMousePosEvent(x, y); Frame("standard", 1080, 820);
            io.AddMouseButtonEvent(0, true); Frame("standard", 1080, 820);
            io.AddMouseButtonEvent(0, false); Frame("standard", 1080, 820);
        }
        void ChooseDisposition(float x, float y, bool discard)
        {
            Click(x, y);
            // ImGui sizes a new popup over its first frames before displaying its choices.
            Frame("standard", 1080, 820); Frame("standard", 1080, 820);
            Click(x - 35, y + (discard ? 49 : 28));
        }
        void Frame(string name, int width, int height)
        {
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(new(8, 8), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new(width - 16, height - 16), ImGuiCond.Always);
            if (ImGui.Begin("寶物庫整理助手 · 離線示範##" + name, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                view.Draw(new(catalog, selections, slots, territory, active, "離線示範：目前在主城，勾選全域生效；此預覽不會操作遊戲。", false, [],
                    new(discoverEnabled, "離線示範：等待進入寶物庫；已補錄紀錄會保留。", discovered, discoveryDefaults)), actions);
            ImGui.End(); ImGui.Render();
        }
    }
    finally { ImGui.DestroyContext(context); }
}
