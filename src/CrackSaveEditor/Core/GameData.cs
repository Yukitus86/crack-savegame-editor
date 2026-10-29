using System.Text.Json.Nodes;

namespace CrackSaveEditor.Core;

// Display names are resolved on access so they follow the UI language.
public sealed record ShopItem(string Id, string NameKey, int Price, int MaxPurchases, bool ResetDaily, uint? AssetId, string Catalog)
{
    public string Name => GameData.T(NameKey, Id);
    public override string ToString() => Name;
}

public sealed record PrefabInfo(uint AssetId, string Prefab, string NameKey, int MaxDurability, int ScrapValue)
{
    public string Name => NameKey.Length > 0 ? GameData.T(NameKey, GameData.Pretty(Prefab)) : GameData.Pretty(Prefab);
}

public sealed record CatalogInfo(string Id, string NameKey, string[] Items)
{
    public string Name => GameData.T(NameKey, Id);
}

public sealed record ScannerTrack(string Key, string LabelEn, string Unit, double[] Values, int[] Prices)
{
    public string Label => Loc.T(LabelEn);
    public int MaxLevel => Prices.Length;
}

/// <summary>Static data extracted from the game install by tools/extract_gamedata.py.</summary>
public static class GameData
{
    public static IReadOnlyDictionary<string, ShopItem> Shop { get; }
    public static IReadOnlyDictionary<uint, PrefabInfo> Prefabs { get; }
    public static IReadOnlyList<CatalogInfo> Catalogs { get; }
    public static IReadOnlyList<ScannerTrack> Scanner { get; }
    public static int BotMaxLevel { get; }
    public static int[] CartTierPrices { get; }
    public static int StartingBalance { get; }
    public static double MaxAgitation { get; }
    public static IReadOnlyList<string> Languages { get; }

    // Game string key -> (English, German) as shipped with the game.
    static readonly Dictionary<string, (string En, string De)> Texts = new();

    static GameData()
    {
        using var stream = typeof(GameData).Assembly.GetManifestResourceStream("GameData.json")!;
        var root = JsonNode.Parse(stream)!.AsObject();

        foreach (var src in new[] { "loc", "locAll" })
            foreach (var (k, v) in root.Obj(src))
                Texts[k] = (v.Str("en"), v.Str("de"));

        var catalogOf = new Dictionary<string, string>();
        Catalogs = root.Arr("catalogs").Objects().Select(c =>
        {
            var info = new CatalogInfo(c.Str("id"), c.Str("nameKey"), c.Arr("items").Select(i => i!.GetValue<string>()).ToArray());
            foreach (var i in info.Items) catalogOf[i] = info.Id;
            return info;
        }).ToList();

        Shop = root.Arr("shop").Objects().Select(s => new ShopItem(
                s.Str("id"), s.Str("nameKey"), s.Int("price"), s.Int("maxPurchases"),
                s.Bool("resetDaily"), s["assetId"] is JsonValue a && a.TryGetValue(out uint id) ? id : null,
                catalogOf.GetValueOrDefault(s.Str("id"), "")))
            .ToDictionary(s => s.Id);

        var prefabs = new Dictionary<uint, PrefabInfo>();
        foreach (var (k, v) in root.Obj("prefabs"))
            if (uint.TryParse(k, out uint id))
                prefabs[id] = new PrefabInfo(id, v.Str("prefab"), v.Str("nameKey"), v.Int("maxDurability"), v.Int("scrapValue"));
        Prefabs = prefabs;

        var sc = root.Obj("scanner");
        Scanner =
        [
            Track(sc, "depth", "Depth", "×"),
            Track(sc, "spread", "Spread", "×"),
            Track(sc, "duration", "Reveal duration", "s"),
            Track(sc, "cooldown", "Cooldown", "s"),
            Track(sc, "sensitivity", "Sensitivity", ""),
        ];
        BotMaxLevel = root.Int("botMaxLevel", 5);
        CartTierPrices = root.Arr("cartTierPrices").Select(p => p!.GetValue<int>()).ToArray();
        StartingBalance = root.Int("startingBalance", 2000);
        MaxAgitation = root.Num("maxAgitation", 100);
        Languages = root.Arr("languages").Select(l => l!.GetValue<string>()).ToArray();
    }

    /// <summary>Work order text with its target filled in ("…Value" targets are cents).</summary>
    public static string WorkOrderText(string stepKey, int? target)
    {
        if (string.IsNullOrEmpty(stepKey)) return "–";
        string arg = target switch
        {
            null => "…",
            int t when stepKey.EndsWith("Value") => "$" + (t / 100m).ToString("N0", Loc.I.Culture),
            int t => t.ToString(),
        };
        return T(stepKey, stepKey.Replace("WorkOrder_Step_", "")).Replace("{0}", arg);
    }

    static ScannerTrack Track(JsonObject sc, string key, string label, string unit)
    {
        var t = sc.Obj(key);
        double[] values = t["tierValues"] is JsonArray tv ? tv.Select(x => x!.GetValue<double>()).ToArray() : [];
        int[] prices = t.Arr("stepPrices").Select(x => x!.GetValue<int>()).ToArray();
        return new ScannerTrack(key + "Level", label, unit, values, prices);
    }

    /// <summary>The game's own translation of a string key in the current UI language.</summary>
    public static string T(string key, string? fallback = null)
    {
        if (!Texts.TryGetValue(key, out var t)) return fallback ?? key;
        string s = Loc.I.IsGerman && t.De.Length > 0 ? t.De : t.En;
        return s.Length > 0 ? s : fallback ?? key;
    }

    public static string ItemName(uint assetId) =>
        Prefabs.TryGetValue(assetId, out var p) ? p.Name : Loc.F("Unknown ({0})", assetId);

    public static string ShopName(string itemId) =>
        Shop.TryGetValue(itemId, out var s) ? s.Name : itemId;

    public static string TerminalName(string terminalId)
    {
        var parts = terminalId.Split('_');
        string num = parts[^1];
        return parts[0] switch
        {
            "vehicle" => Loc.F("Vehicle garage {0}", num),
            "bot" when parts.Length > 2 && parts[1] == "collector" => $"{Title(T("Bot_Collector_Name", "Collector"))} {num}",
            "bot" when parts.Length > 2 && parts[1] == "drone" => $"{Title(T("Bot_Drone_Name", "Drone"))} {num}",
            _ => terminalId,
        };
    }

    static string Title(string s) => Loc.I.Culture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    internal static string Pretty(string prefab) =>
        prefab.Replace("PF_Item_", "").Replace("PF_", "").Replace('_', ' ').Trim();
}
