using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class PlayerVm : ObservableObject
{
    // Hotbar slots (added with the satchel game update): 2 by default, +1 per worn satchel, max 4.
    public const int DefaultSlots = 2, MaxSlots = 4;

    readonly JsonObject _world;
    readonly JsonObject? _bed;
    readonly JsonObject? _tips;
    static Session S => Session.Current;

    public PlayerVm(JsonObject world, JsonObject account)
    {
        _world = world;
        SteamId = account.Long("steamId");
        Name = account.Str("displayName");
        _bed = world.Obj("bedRecord").Arr("entries").Objects().FirstOrDefault(e => e.Long("steamId") == SteamId);
        _tips = world.Obj("tips").Arr("players").Objects().FirstOrDefault(e => e.Long("steamId") == SteamId);
        var life = world.Obj("shiftSummary").Arr("lifetime").Objects().FirstOrDefault(e => e.Long("steamId") == SteamId);
        Lifetime = life?.Arr("amounts").Select((v, i) => $"#{i + 1}: {v?.GetValue<double>():0.##}").ToList() ?? [];

        ResetTipsCommand = new RelayCommand(() =>
        {
            if (_tips == null) return;
            int old = TipsSeen;
            _tips["seen"] = new JsonArray();
            S.Track(Loc.T("Players"), Loc.F("{0} seen tips", Name), old, 0);
            Raise(nameof(TipsSeen));
            Raise(nameof(TipsSeenText));
            Raise(nameof(TipsText));
        });
    }

    public long SteamId { get; }
    public string Name { get; }
    public IReadOnlyList<string> Lifetime { get; }
    public ICommand ResetTipsCommand { get; }

    public int NightsInBed
    {
        get => _bed.Int("nightsInBed");
        set
        {
            if (_bed == null) return;
            int old = NightsInBed;
            if (old == value) return;
            _bed["nightsInBed"] = Math.Max(0, value);
            S.Track(Loc.T("Players"), Loc.F("{0} nights in bed", Name), old, value);
            Raise();
        }
    }

    /// <summary>
    /// Saves from before the satchel update have no hotbarSlots (version 0); the game then grants
    /// every account the maximum. Offline players (SteamID 0) always get the default.
    /// </summary>
    public int HotbarSlots
    {
        get
        {
            if (SteamId == 0) return DefaultSlots;
            if (_world["hotbarSlots"] is not JsonObject hb || hb.Int("version") == 0) return MaxSlots;
            var entry = hb.Arr("players").Objects().FirstOrDefault(e => e.Long("steamId") == SteamId);
            return entry?.Int("slots") ?? DefaultSlots;
        }
        set
        {
            int old = HotbarSlots;
            value = Math.Clamp(value, DefaultSlots, MaxSlots);
            if (SteamId == 0 || old == value) { Raise(); return; }

            var hb = _world.Obj("hotbarSlots");
            var players = hb.Arr("players");
            if (hb.Int("version") == 0)
            {
                // Materialise the implicit "everyone has the max" so other players keep it.
                players.Clear();
                foreach (var acc in _world.Obj("finance").Arr("accounts").Objects())
                    if (acc.Long("steamId") != 0)
                        players.Add(new JsonObject { ["steamId"] = unchecked((ulong)acc.Long("steamId")), ["slots"] = MaxSlots });
                hb["version"] = 1;
            }
            var entry = players.Objects().FirstOrDefault(e => e.Long("steamId") == SteamId);
            if (entry == null)
                players.Add(entry = new JsonObject { ["steamId"] = unchecked((ulong)SteamId) });
            entry["slots"] = value;
            S.Track(Loc.T("Players"), Loc.F("{0} hotbar slots", Name), old, value);
            Raise();
        }
    }

    public bool CanEditHotbar => SteamId != 0;

    public int TipsSeen => _tips?["seen"] is JsonArray a ? a.Count : 0;
    public string TipsText => _tips?["seen"] is JsonArray a && a.Count > 0
        ? string.Join(", ", a.Select(t => t!.GetValue<string>().Split('#')[0].Replace("Tip_", "").Replace("Hint_", "")).Distinct())
        : Loc.T("None");

    public string TipsSeenText => Loc.F("{0} tips seen", TipsSeen);
}

public sealed class PlayersVm : ObservableObject
{
    readonly JsonObject _world;
    readonly JsonArray _revealed;
    static Session S => Session.Current;

    public PlayersVm(SaveSlot slot)
    {
        _world = slot.World;
        Players = new(_world.Obj("finance").Arr("accounts").Objects().Select(a => new PlayerVm(_world, a)));
        _revealed = _world.Obj("handbookClearance").Arr("revealed");
        Revealed = new(_revealed.Select(r => r!.GetValue<string>()));

        AddRevealCommand = new RelayCommand(_ =>
        {
            string key = NewReveal.Trim();
            if (key.Length == 0 || Revealed.Contains(key)) return;
            _revealed.Add(key);
            Revealed.Add(key);
            S.Track(Loc.T("Handbook"), key, Loc.T("hidden"), Loc.T("revealed"));
            NewReveal = "";
        });
        RemoveRevealCommand = new RelayCommand(p =>
        {
            if (p is not string key) return;
            var node = _revealed.FirstOrDefault(r => r?.GetValue<string>() == key);
            if (node != null) _revealed.Remove(node);
            Revealed.Remove(key);
            S.Track(Loc.T("Handbook"), key, Loc.T("revealed"), Loc.T("hidden"));
        });
        ResetAnnouncementsCommand = new RelayCommand(() =>
        {
            var pa = _world.Obj("paHistory");
            int old = pa.Arr("entries").Count;
            pa["entries"] = new JsonArray();
            S.Track(Loc.T("Players"), Loc.T("Announcement history"), Loc.F("{0} entries", old), Loc.T("reset"));
            Raise(nameof(AnnouncementCount));
        });
    }

    public ObservableCollection<PlayerVm> Players { get; }
    public ObservableCollection<string> Revealed { get; }

    string _newReveal = "";
    public string NewReveal { get => _newReveal; set => Set(ref _newReveal, value); }

    public int AnnouncementCount => _world.Obj("paHistory").Arr("entries").Count;

    public int BedNights
    {
        get => _world.Obj("bedRecord").Int("nights");
        set
        {
            var bed = _world.Obj("bedRecord");
            int old = bed.Int("nights");
            if (old == value) return;
            bed["nights"] = Math.Max(0, value);
            S.Track(Loc.T("Players"), Loc.T("Total nights"), old, value);
            Raise();
        }
    }

    public string EventsText
    {
        get
        {
            var entries = _world.Obj("eventHistory").Arr("entries").Objects()
                .Select(e => Loc.F("{0} ×{1} (day {2})", e.Str("eventId").Replace("Event", ""), e.Int("occurrences"), e.Int("lastDay"))).ToList();
            return entries.Count > 0 ? string.Join("  ·  ", entries) : Loc.T("No events yet");
        }
    }

    public string SoldText => string.Join(", ", _world.Obj("eventHistory").Arr("soldResources").Select(r => r!.GetValue<string>().Replace("Egg", "")));

    public ICommand AddRevealCommand { get; }
    public ICommand RemoveRevealCommand { get; }
    public ICommand ResetAnnouncementsCommand { get; }
}
