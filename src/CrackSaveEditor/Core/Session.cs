using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CrackSaveEditor.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed record Change(string Area, string Label, string Original, string Current);

/// <summary>Application-wide state: the loaded slot and the list of pending edits.</summary>
public sealed class Session : ObservableObject
{
    public static Session Current { get; } = new();

    readonly Dictionary<string, Change> _changes = new();
    SaveSlot? _slot;
    string _status = Loc.T("Ready");

    public SaveSlot? Slot { get => _slot; private set { _slot = value; Raise(); Raise(nameof(HasSlot)); Raise(nameof(SlotTitle)); Raise(nameof(SignatureText)); } }
    public bool HasSlot => _slot != null;
    public string SlotTitle => _slot == null ? Loc.T("No save loaded") : $"Slot {_slot.Index + 1}";

    public string SignatureText => _slot switch
    {
        null => "",
        { HostId: 0 } => Loc.T("Signed: offline (no Steam)"),
        { HostId: long id } => Loc.F("Signed for SteamID {0}", id),
        _ => Loc.T("Unknown signature – the game marks the run unranked"),
    };

    public string Status { get => _status; set => Set(ref _status, value); }

    public IReadOnlyCollection<Change> Changes => _changes.Values;
    public bool IsDirty => _changes.Count > 0;

    /// <summary>Raised after a slot was (re)loaded so pages rebuild their view models.</summary>
    public event Action? SlotLoaded;

    /// <summary>Raised when another page changed data a page might display (e.g. raw JSON edits).</summary>
    /// <remarks>The argument is the view model that made the change (it does not need rebuilding), or null.</remarks>
    public event Action<object?>? DataChanged;

    public void Track(string area, string label, object? oldValue, object? newValue)
    {
        string key = area + "|" + label;
        string newText = Format(newValue);
        if (_changes.TryGetValue(key, out var existing))
        {
            if (existing.Original == newText) _changes.Remove(key);
            else _changes[key] = existing with { Current = newText };
        }
        else
        {
            string oldText = Format(oldValue);
            if (oldText == newText) return;
            _changes[key] = new Change(area, label, oldText, newText);
        }
        Raise(nameof(IsDirty));
        Raise(nameof(Changes));
    }

    /// <summary>Re-evaluates computed texts after a UI language switch.</summary>
    public void RefreshTexts()
    {
        Raise(nameof(SlotTitle));
        Raise(nameof(SignatureText));
        Status = Loc.T("Ready");
    }

    public void NotifyDataChanged(object? source = null) => DataChanged?.Invoke(source);

    static string Format(object? v) => v switch
    {
        null => "–",
        double d when d == Math.Floor(d) => d.ToString("0", Loc.I.Culture),
        bool b => b ? Loc.T("Yes") : Loc.T("No"),
        double d => d.ToString("0.##", Loc.I.Culture),
        float f => f.ToString("0.##", Loc.I.Culture),
        _ => v.ToString() ?? "",
    };

    public void LoadSlot(int index)
    {
        Slot = SaveSlot.Load(index);
        _changes.Clear();
        Raise(nameof(IsDirty));
        Raise(nameof(Changes));
        Status = Loc.F("Slot {0} loaded ({1})", index + 1, Path.GetFileName(GamePaths.WorldPath(index)));
        SlotLoaded?.Invoke();
    }

    public void Reload()
    {
        if (_slot != null) LoadSlot(_slot.Index);
    }

    public void Unload()
    {
        Slot = null;
        _changes.Clear();
        Raise(nameof(IsDirty));
        SlotLoaded?.Invoke();
    }

    /// <summary>Backs up the slot, then writes it. Returns the backup path.</summary>
    public string Save()
    {
        if (_slot == null) throw new InvalidOperationException(Loc.T("No save loaded"));
        string backup = BackupService.Create(_slot.Index, "vor-speichern");
        long host = _slot.HostId ?? _slot.World.Obj("finance").Arr("accounts").Objects().Select(a => a.Long("steamId")).FirstOrDefault();
        _slot.Save(host);
        _changes.Clear();
        Raise(nameof(IsDirty));
        Raise(nameof(Changes));
        Raise(nameof(SignatureText));
        Status = Loc.F("Saved – backup: {0}", Path.GetFileName(backup));
        return backup;
    }
}
