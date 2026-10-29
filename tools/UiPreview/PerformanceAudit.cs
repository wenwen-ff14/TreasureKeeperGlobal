using System.Diagnostics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using TreasureKeeperGlobal.Core;
using TreasureKeeperGlobal.Ui;

// Offline CPU/managed-allocation audit. No game process, native inventory or disk-save timings.
internal static class PerformanceAudit
{
    internal static void Run(TreasureCatalog catalog, string output)
    {
        var results = new List<object>();
        var seed = Enumerable.Range(0, 4).SelectMany(b => Enumerable.Range(0, 35).Select(s => new LootSlot(b, s, 0, 0)))
            .Concat(Enumerable.Range(0, 18).Select(s => new LootSlot(2001, s, 0, 0))).ToArray();
        seed[0] = new(0, 0, 100, 1);
        var tracker = new LootDiscoveryTracker(); tracker.Seed(seed);
        LootSlotChange[] receive = [new(seed[0], seed[0] with { Quantity = 2 })];
        LootSlotChange[] consume = [new(receive[0].After, seed[0])];
        Measure("discovery_receive_consume", 100_000, () => { tracker.Apply(receive, true); tracker.Apply(consume, true); });
        var vault = catalog.Vaults.Single(v => v.Id == 55002);
        var known = vault.Items.First(i => i.Category == "素材");
        vault = LootDiscoveryCatalog.Merge(vault, known);
        Measure("repeat_catalog_merge", 20_000, () => vault = LootDiscoveryCatalog.Merge(vault, known));

        var originals = vault.Items.ToArray();
        foreach (var item in originals) vault = LootDiscoveryCatalog.Merge(vault, item);
        catalog.Vaults[Array.FindIndex(catalog.Vaults, v => v.Id == vault.Id)] = vault;
        var all = catalog.Vaults.SelectMany(v => v.Items).DistinctBy(i => i.Id).ToArray();
        var selection = all.Where(CleanupPolicy.CanSelect).Select(i => i.Id).ToHashSet();
        var slots = all.Take(140).Select((i, n) => new BagSlot(n / 35, n % 35, i.Id, 5, false)).ToArray();
        var stamp = DateTimeOffset.UnixEpoch;
        var records = new Dictionary<uint, Dictionary<uint, DiscoveredLoot>>
        { [vault.Id] = vault.Items.ToDictionary(i => i.Id, i => new DiscoveredLoot(i.Id, stamp, stamp)) };
        var view = new TreasureView();
        var actions = new TreasureActions(_ => { }, (_, _, _) => { }, _ => { }, _ => [], (_, _) => { }, () => { }, _ => { });
        var state = new TreasureViewState(catalog, selection, slots, 129, false, "離線效能測試。", false, [],
            new(true, "離線測試。", records));
        var io = ImGui.GetIO(); io.DisplaySize = new(1080, 820); io.FontGlobalScale = 1;
        Measure("ui_items_142_rows_140_slots", 2_000, Frame);
        Click(385, 260);
        Measure("ui_global_preview_140_slots", 2_000, Frame);
        Click(530, 260);
        Measure("ui_discovery_142_records", 2_000, Frame);
        File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(new
        {
            Runtime = Environment.Version.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Note = "Offline ImGui CPU and managed allocations only; includes NewFrame/Render, excludes software/GPU rendering, game hooks and saves. Retained bytes are after full GC, not total process RAM.",
            Results = results
        }, new JsonSerializerOptions { WriteIndented = true }));

        void Measure(string name, int iterations, Action action)
        {
            // Let tiered JIT/PGO settle even when a tiny operation finishes 1,000 iterations in < 1 ms.
            var warmup = Stopwatch.GetTimestamp();
            for (var i = 0; i < 1_000 || Stopwatch.GetElapsedTime(warmup).TotalSeconds < 1; i++) action();
            var timings = new double[3]; var allocations = new double[3]; var retained = new long[3]; var collections = new int[3];
            for (var sample = 0; sample < 3; sample++)
            {
                var retainedBefore = GC.GetTotalMemory(true);
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var gen0 = GC.CollectionCount(0);
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < iterations; i++) action();
                timings[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000 / iterations;
                allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / iterations;
                collections[sample] = GC.CollectionCount(0) - gen0;
                retained[sample] = GC.GetTotalMemory(true) - retainedBefore;
            }
            var median = timings.Order().ElementAt(1);
            results.Add(new { Name = name, IterationsPerSample = iterations, Samples = 3, MicrosecondsPerOperation = median,
                TimingSamples = timings, AllocatedBytesPerOperation = allocations.Average(), Gen0Collections = collections, RetainedBytesDeltas = retained });
            Console.WriteLine($"{name}: median {median:F2} us/op; {allocations.Average():F0} B/op; retained=[{string.Join(',', retained)}] B; Gen0=[{string.Join(',', collections)}]");
        }
        void Frame()
        {
            ImGui.NewFrame(); ImGui.SetNextWindowPos(new(8, 8), ImGuiCond.Always); ImGui.SetNextWindowSize(new(1064, 804), ImGuiCond.Always);
            if (ImGui.Begin("Performance audit", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)) view.Draw(state, actions);
            ImGui.End(); ImGui.Render();
        }
        void Click(float x, float y)
        {
            io.AddMousePosEvent(x, y); Frame();
            io.AddMouseButtonEvent(0, true); Frame();
            io.AddMouseButtonEvent(0, false); Frame();
        }
    }
}
