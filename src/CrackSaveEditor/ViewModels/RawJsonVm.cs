using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CrackSaveEditor.Core;
using Microsoft.Win32;

namespace CrackSaveEditor.ViewModels;

/// <summary>Lazily expanded tree node over the save JSON.</summary>
public sealed class JsonTreeNode : ObservableObject
{
    const int PageSize = 200;
    static readonly JsonTreeNode Placeholder = new("…", null, null, "", null);

    readonly int _skip;
    readonly Action<JsonTreeNode>? _onSelect;
    bool _loaded;

    public JsonTreeNode(string key, JsonNode? node, JsonTreeNode? parent, string path, Action<JsonTreeNode>? onSelect, int skip = 0)
    {
        Key = key;
        Node = node;
        Parent = parent;
        Path = path;
        _onSelect = onSelect;
        _skip = skip;
        if (HasChildren) Children.Add(Placeholder);
    }

    public string Key { get; }
    public JsonNode? Node { get; private set; }
    public JsonTreeNode? Parent { get; }
    public string Path { get; }
    public ObservableCollection<JsonTreeNode> Children { get; } = [];

    bool HasChildren => Node is JsonObject { Count: > 0 } or JsonArray { Count: > 0 };
    public bool IsContainer => Node is JsonObject or JsonArray;

    public string Preview => Node switch
    {
        JsonObject o => $"{{ {o.Count} }}",
        JsonArray a => $"[ {a.Count} ]",
        null => Key == "…" ? "" : "null",
        _ => Node.ToJsonString(JsonExt.Compact) is { Length: > 80 } s ? s[..80] + "…" : Node.ToJsonString(JsonExt.Compact),
    };

    public string KindGlyph => Node switch
    {
        JsonObject => "",
        JsonArray => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => "",
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => "",
        _ => "",
    };

    bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value) || !value || _loaded) return;
            _loaded = true;
            Children.Clear();
            foreach (var child in CreateChildren()) Children.Add(child);
        }
    }

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value) && value) _onSelect?.Invoke(this); }
    }

    IEnumerable<JsonTreeNode> CreateChildren()
    {
        switch (Node)
        {
            case JsonObject o:
                foreach (var (k, v) in o) yield return new JsonTreeNode(k, v, this, $"{Path}.{k}", _onSelect);
                break;
            case JsonArray a:
                int end = Math.Min(a.Count, _skip + PageSize);
                for (int i = _skip; i < end; i++) yield return new JsonTreeNode($"[{i}]", a[i], this, $"{Path}[{i}]", _onSelect);
                if (end < a.Count)
                    yield return new JsonTreeNode($"[{end}…{a.Count - 1}]", a, this, Path, _onSelect, end);
                break;
        }
    }

    public void Replace(JsonNode node)
    {
        Node = node;
        Raise(nameof(Preview));
        Raise(nameof(KindGlyph));
    }
}

public sealed record SearchHit(string Path, string Value, JsonNode Node);

public sealed class RawJsonVm : ObservableObject
{
    readonly SaveSlot _slot;
    static Session S => Session.Current;

    public RawJsonVm(SaveSlot slot)
    {
        _slot = slot;
        Roots = [new JsonTreeNode(Loc.T("Save game"), slot.World, null, "$", n => Selected = n) { IsExpanded = true }];

        ApplyCommand = new RelayCommand(_ => Apply(), _ => Selected?.Node is JsonValue);
        SearchCommand = new RelayCommand(_ => Search(), _ => SearchText.Length >= 2);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
    }

    public ObservableCollection<JsonTreeNode> Roots { get; }
    public ObservableCollection<SearchHit> Hits { get; } = [];

    JsonTreeNode? _selected;
    public JsonTreeNode? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            EditText = value?.Node is JsonValue v && v.TryGetValue(out string? s) ? s : value?.Node?.ToJsonString(JsonExt.Compact) ?? "";
            Raise(nameof(SelectedPath));
            Raise(nameof(CanEdit));
        }
    }

    public string SelectedPath => Selected?.Path ?? "";
    public bool CanEdit => Selected?.Node is JsonValue;

    string _editText = "";
    public string EditText { get => _editText; set => Set(ref _editText, value); }

    string _searchText = "";
    public string SearchText { get => _searchText; set => Set(ref _searchText, value); }

    string _searchInfo = "";
    public string SearchInfo { get => _searchInfo; set => Set(ref _searchInfo, value); }

    SearchHit? _hit;
    public SearchHit? Hit
    {
        get => _hit;
        set
        {
            if (!Set(ref _hit, value) || value == null) return;
            Selected = new JsonTreeNode(value.Path, value.Node, null, value.Path, null);
        }
    }

    public ICommand ApplyCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }

    void Apply()
    {
        if (Selected?.Node is not JsonValue old || old.Parent is not { } parent) return;
        JsonNode replacement;
        switch (old.GetValueKind())
        {
            case JsonValueKind.String:
                replacement = JsonValue.Create(EditText);
                break;
            case JsonValueKind.True or JsonValueKind.False:
                if (!bool.TryParse(EditText.Trim(), out bool b)) { S.Status = Loc.T("Please enter true or false."); return; }
                replacement = JsonValue.Create(b);
                break;
            default:
                string t = EditText.Trim().Replace(',', '.');
                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) replacement = JsonValue.Create(l);
                else if (ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u)) replacement = JsonValue.Create(u);
                else if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) replacement = JsonValue.Create(d);
                else { S.Status = Loc.T("Not a valid number."); return; }
                break;
        }

        string before = old.ToJsonString(JsonExt.Compact);
        switch (parent)
        {
            case JsonObject o: o[old.GetPropertyName()] = replacement; break;
            case JsonArray a: a[old.GetElementIndex()] = replacement; break;
        }
        S.Track("Raw JSON", Selected.Path, before, replacement.ToJsonString(JsonExt.Compact));
        S.Status = Loc.F("{0} changed", Selected.Path);
        Selected.Replace(replacement);
        S.NotifyDataChanged(this);
    }

    void Search()
    {
        Hits.Clear();
        string q = SearchText;
        int total = 0;
        Walk(_slot.World, "$");
        SearchInfo = total > Hits.Count ? Loc.F("{0} hits (showing the first {1})", total, Hits.Count) : Loc.F("{0} hits", total);

        void Walk(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (k, v) in o)
                    {
                        string p = $"{path}.{k}";
                        if (k.Contains(q, StringComparison.OrdinalIgnoreCase) && v != null) AddHit(p, v);
                        else Walk(v, p);
                    }
                    break;
                case JsonArray a:
                    // Huge numeric arrays (whiteboard pixels) are not worth searching.
                    if (a.Count > 5000) return;
                    for (int i = 0; i < a.Count; i++) Walk(a[i], $"{path}[{i}]");
                    break;
                case JsonValue v:
                    string text = v.TryGetValue(out string? s) ? s : v.ToJsonString();
                    if (text.Length < 200 && text.Contains(q, StringComparison.OrdinalIgnoreCase)) AddHit(path, v);
                    break;
            }
        }

        void AddHit(string path, JsonNode node)
        {
            total++;
            if (Hits.Count < 500) Hits.Add(new SearchHit(path, new JsonTreeNode("", node, null, "", null).Preview, node));
        }
    }

    void Export()
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = $"world_slot{_slot.Index}_decrypted.json" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, _slot.World.ToJsonString(JsonExt.Pretty));
        S.Status = Loc.F("Exported decrypted: {0}", dlg.FileName);
    }

    void Import()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(dlg.FileName)) is not JsonObject world || world["finance"] == null)
            {
                S.Status = Loc.T("That is not a CRACK save (finance is missing).");
                return;
            }
            _slot.ReplaceWorld(world);
            S.Track("Raw JSON", Loc.T("Whole save"), "Original", Path.GetFileName(dlg.FileName));
            S.Status = Loc.T("JSON imported – not saved yet");
            S.NotifyDataChanged();
        }
        catch (JsonException ex)
        {
            S.Status = Loc.T("Invalid JSON: ") + ex.Message;
        }
    }
}
