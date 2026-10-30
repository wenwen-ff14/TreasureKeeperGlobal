namespace TreasureKeeperGlobal.Core;

public sealed class DiscardProfile
{
    public string Name { get; set; } = "預設設定集";
    public HashSet<uint> DiscardItems { get; set; } = [];
    public HashSet<uint> ConfiguredItems { get; set; } = [];
    public Dictionary<uint, bool> DiscardNewByVault { get; set; } = [];
    public bool IncludeHighQuality { get; set; }

    public DiscardProfile Copy(string name) => new()
    {
        Name = name.Trim(), DiscardItems = [.. DiscardItems], ConfiguredItems = [.. ConfiguredItems],
        DiscardNewByVault = new(DiscardNewByVault), IncludeHighQuality = IncludeHighQuality,
    };
}

public sealed class DiscardProfiles
{
    public Dictionary<string, DiscardProfile> Profiles { get; set; } = [];
    public string DefaultProfileId { get; set; } = "";
    public Dictionary<ulong, string> CharacterProfiles { get; set; } = [];

    public void Initialize(DiscardProfile legacy, IEnumerable<TreasureItem> items)
    {
        Profiles ??= [];
        CharacterProfiles ??= [];
        foreach (var id in Profiles.Where(p => p.Value is null).Select(p => p.Key).ToArray()) Profiles.Remove(id);
        if (Profiles.Count == 0) Profiles[Guid.NewGuid().ToString("N")] = legacy.Copy("預設設定集");
        if (!Profiles.ContainsKey(DefaultProfileId)) DefaultProfileId = Profiles.Keys.First();
        var known = items.ToArray();
        foreach (var profile in Profiles.Values)
        {
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "未命名設定集" : profile.Name.Trim();
            profile.DiscardItems = CleanupPolicy.RestoreSelection(known, profile.DiscardItems ?? [], []);
            profile.ConfiguredItems ??= [];
            profile.ConfiguredItems.UnionWith(profile.DiscardItems);
            profile.DiscardNewByVault ??= [];
        }
        foreach (var id in CharacterProfiles.Where(p => p.Key == 0 || !Profiles.ContainsKey(p.Value)).Select(p => p.Key).ToArray())
            CharacterProfiles.Remove(id);
    }

    public string Resolve(ulong character) => character != 0 && CharacterProfiles.TryGetValue(character, out var id) && Profiles.ContainsKey(id)
        ? id : DefaultProfileId;

    public bool Select(string id, ulong character)
    {
        if (!Profiles.ContainsKey(id)) return false;
        if (character != 0 && CharacterProfiles.ContainsKey(character)) CharacterProfiles[character] = id;
        else DefaultProfileId = id;
        return true;
    }

    public string Duplicate(string id, string name)
    {
        var key = Guid.NewGuid().ToString("N");
        Profiles.Add(key, Profiles[id].Copy(name));
        return key;
    }

    public bool Delete(string id)
    {
        if (Profiles.Count <= 1 || !Profiles.Remove(id)) return false;
        if (DefaultProfileId == id) DefaultProfileId = Profiles.Keys.First();
        foreach (var character in CharacterProfiles.Where(p => p.Value == id).Select(p => p.Key).ToArray())
            CharacterProfiles.Remove(character);
        return true;
    }
}
