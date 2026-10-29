using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Data;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class ItemVm(JsonObject node, int number) : NodeVm(node, "Items")
{
    public JsonObject Json => Node;
    public int Number { get; set; } = number;
    public uint AssetId => (uint)Node.Long("assetId");
    PrefabInfo? Prefab => GameData.Prefabs.GetValueOrDefault(AssetId);

    public string Name => GameData.ItemName(AssetId);
    public string PrefabName => Prefab?.Prefab ?? "?";
    public bool IsSceneObject => Node.Long("sceneId") != 0;
    public bool InPocket => Node.Long("pocketedBySteamId") != 0;
    public string Origin => IsSceneObject ? Loc.T("Level") : Loc.T("Spawned");
    public int MaxDurability => Prefab?.MaxDurability ?? 0;

    public string Location
    {
        get
        {
            long pocket = Node.Long("pocketedBySteamId");
            long worn = Node.Long("wornBySteamId");
            if (pocket != 0) return Loc.T("In a pocket");
            if (worn != 0) return Loc.T("Worn");
            var p = Node.Obj("position");
            return string.Create(CultureInfo.InvariantCulture, $"{p.Num("x"):0.0}, {p.Num("y"):0.0}, {p.Num("z"):0.0}");
        }
    }

    public string Properties
    {
        get
        {
            var keys = Node.Arr("keys");
            var values = Node.Arr("values");
            return string.Join("  ·  ", keys.Select((k, i) =>
                $"{k?.GetValue<string>()}={(i < values.Count ? values[i]?.GetValue<string>() : "")}"));
        }
    }

    int DurabilityIndex => Node.Arr("keys").Select(k => k?.GetValue<string>()).ToList().IndexOf("durability");
    public bool HasDurability => DurabilityIndex >= 0;

    public int? Durability
    {
        get
        {
            int i = DurabilityIndex;
            var values = Node.Arr("values");
            return i >= 0 && i < values.Count && int.TryParse(values[i]?.GetValue<string>(), out int d) ? d : null;
        }
        set
        {
            int i = DurabilityIndex;
            var values = Node.Arr("values");
            if (value == null || i < 0 || i >= values.Count) return;
            int v = Math.Max(0, value.Value);
            int? old = Durability;
            if (old == v) return;
            values[i] = v.ToString(CultureInfo.InvariantCulture);
            S.Track(Area, Loc.F("{0} #{1} durability", Name, Number), old, v);
            Raise();
            Raise(nameof(Properties));
            Raise(nameof(DurabilityPercent));
        }
    }

    public double DurabilityPercent => MaxDurability > 0 && Durability is int d ? Math.Clamp(d * 100.0 / MaxDurability, 0, 100) : 0;
}

public sealed class SpawnOption(PrefabInfo prefab)
{
    public PrefabInfo Prefab { get; } = prefab;
    public override string ToString() => Prefab.Name;
}

public sealed class ItemsVm : ObservableObject
{
    readonly JsonArray _items;
    static Session S => Session.Current;

    public ItemsVm(SaveSlot slot)
    {
        _items = slot.World.Obj("groundItems").Arr("items");
        Items = new(_items.Objects().Select((o, i) => new ItemVm(o, i + 1)));
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = o => o is ItemVm vm && (string.IsNullOrWhiteSpace(Filter)
            || vm.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || vm.PrefabName.Contains(Filter, StringComparison.OrdinalIgnoreCase));

        // Anything with a readable item name key can be spawned.
        SpawnOptions = GameData.Prefabs.Values
            .Where(p => p.Prefab.StartsWith("PF_Item_"))
            .OrderBy(p => p.Name)
            .Select(p => new SpawnOption(p))
            .ToList();
        SpawnChoice = SpawnOptions.FirstOrDefault();

        RepairAllCommand = new RelayCommand(() =>
        {
            foreach (var i in Items.Where(i => i.HasDurability && i.MaxDurability > 0)) i.Durability = i.MaxDurability;
            S.Status = Loc.F("{0} tools repaired", Items.Count(i => i.HasDurability && i.MaxDurability > 0));
        });
        DuplicateCommand = new RelayCommand(_ => Duplicate(Selected!), _ => Selected != null);
        DeleteCommand = new RelayCommand(_ => Delete(Selected!), _ => Selected != null);
        SpawnCommand = new RelayCommand(_ => Spawn(SpawnChoice!.Prefab), _ => SpawnChoice != null && Items.Count > 0);
    }

    public ObservableCollection<ItemVm> Items { get; }
    public ICollectionView View { get; }
    public List<SpawnOption> SpawnOptions { get; }

    string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) View.Refresh(); } }

    ItemVm? _selected;
    public ItemVm? Selected { get => _selected; set => Set(ref _selected, value); }

    SpawnOption? _spawnChoice;
    public SpawnOption? SpawnChoice { get => _spawnChoice; set => Set(ref _spawnChoice, value); }

    int _spawnCount = 1;
    public int SpawnCount { get => _spawnCount; set => Set(ref _spawnCount, Math.Clamp(value, 1, 20)); }

    public string Summary => Loc.F("{0} objects · {1} tools · {2} in pockets", Items.Count, Items.Count(i => i.HasDurability), Items.Count(i => i.InPocket));

    public ICommand RepairAllCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SpawnCommand { get; }

    void Duplicate(ItemVm source)
    {
        var copy = (JsonObject)source.Json.DeepClone();
        PrepareSpawned(copy, (JsonObject)source.Json["position"]!, 0);
        Add(copy, Loc.F("{0} duplicated", source.Name));
    }

    /// <summary>Spawns new items next to the selected (or first) item. The game respawns sceneId 0 entries by assetId.</summary>
    void Spawn(PrefabInfo prefab)
    {
        var anchor = (Selected ?? Items[0]).Json;
        for (int n = 0; n < SpawnCount; n++)
        {
            var item = new JsonObject
            {
                ["assetId"] = prefab.AssetId,
                ["sceneId"] = 0,
                ["position"] = anchor["position"]!.DeepClone(),
                ["rotation"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0, ["w"] = 1.0 },
                ["wornBySteamId"] = 0,
                ["pocketedBySteamId"] = 0,
                ["keys"] = prefab.MaxDurability > 0 ? new JsonArray("durability") : new JsonArray(),
                ["values"] = prefab.MaxDurability > 0 ? new JsonArray(prefab.MaxDurability.ToString(CultureInfo.InvariantCulture)) : new JsonArray(),
            };
            PrepareSpawned(item, (JsonObject)anchor["position"]!, n);
            Add(item, Loc.F("{0} spawned", prefab.Name));
        }
    }

    static void PrepareSpawned(JsonObject item, JsonObject anchorPos, int n)
    {
        item["sceneId"] = 0;
        item["pocketedBySteamId"] = 0;
        item["wornBySteamId"] = 0;
        // Drop a little above and beside the anchor so physics settles it on the floor.
        item["position"] = new JsonObject
        {
            ["x"] = anchorPos.Num("x") + 0.4 + 0.3 * (n % 4),
            ["y"] = anchorPos.Num("y") + 0.6 + 0.25 * (n / 4),
            ["z"] = anchorPos.Num("z") + 0.3,
        };
    }

    void Add(JsonObject node, string label)
    {
        _items.Add(node);
        var vm = new ItemVm(node, Items.Count + 1);
        Items.Add(vm);
        Selected = vm;
        S.Track("Items", $"#{vm.Number} {label}", "–", Loc.T("new"));
        Raise(nameof(Summary));
    }

    void Delete(ItemVm item)
    {
        _items.Remove(item.Json);
        Items.Remove(item);
        S.Track("Items", $"#{item.Number} {item.Name}", Loc.T("present"), Loc.T("deleted"));
        Raise(nameof(Summary));
    }
}
