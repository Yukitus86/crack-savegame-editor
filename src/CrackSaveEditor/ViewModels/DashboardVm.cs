using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class SlotCardVm
{
    public SlotCardVm(int index, bool isActive)
    {
        Index = index;
        Title = $"Slot {index + 1}";
        IsActive = isActive;
        Exists = SaveSlot.Exists(index);
        if (!Exists) return;

        try
        {
            var slot = SaveSlot.Load(index);
            Day = Loc.F("Day {0}", slot.World.Int("dayNumber") + 1);
            Balance = Fmt.Money(slot.BalanceCentsTotal);
            PlayTime = Fmt.Duration(slot.World.Obj("playtest").Num("playSeconds"));
            Players = string.Join(", ", slot.World.Obj("finance").Arr("accounts").Objects().Select(a => a.Str("displayName").Trim('*')));
            LastWrite = slot.LastWrite.ToString("g", Fmt.Culture);
            Ranked = slot.Ranked switch { true => "Ranked", false => "Unranked", null => "–" };
            IsRanked = slot.Ranked == true;
            Thumbnail = Fmt.Thumbnail(slot.TimelapseFrames().LastOrDefault());
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public int Index { get; }
    public string Title { get; }
    public bool Exists { get; }
    public bool IsActive { get; }
    public string Day { get; } = "";
    public string Balance { get; } = "";
    public string PlayTime { get; } = "";
    public string Players { get; } = "";
    public string LastWrite { get; } = "";
    public string Ranked { get; } = "";
    public bool IsRanked { get; }
    public string? Error { get; }
    public BitmapImage? Thumbnail { get; }
}

public sealed class DashboardVm : ObservableObject
{
    static Session S => Session.Current;

    public DashboardVm(Func<string, string, bool> confirm)
    {
        int? active = S.Slot?.Index;
        Slots = new(Enumerable.Range(0, GamePaths.SlotCount).Select(i => new SlotCardVm(i, i == active)));

        OpenSlotCommand = new RelayCommand(p =>
        {
            if (p is not SlotCardVm { Exists: true } card) return;
            if (S.IsDirty && !confirm(Loc.T("Discard changes?"), Loc.F("{0} unsaved change(s) will be lost when switching to {1}.", S.Changes.Count, card.Title))) return;
            try { S.LoadSlot(card.Index); }
            catch (Exception ex) { S.Status = Loc.F("{0} could not be loaded: {1}", card.Title, ex.Message); }
        });
        OpenFolderCommand = new RelayCommand(() => Process.Start("explorer.exe", GamePaths.SaveDir));
        MoneyCommand = new RelayCommand(p => Quick(() => Cheats.AddMoney(long.Parse((string)p!) * 100), Loc.T("Money added")), _ => S.HasSlot);
        RepairCommand = new RelayCommand(_ => Quick(() => Cheats.RepairTools(), Loc.T("Tools repaired")), _ => S.HasSlot);
        ScannerCommand = new RelayCommand(_ => Quick(Cheats.MaxScanner, Loc.T("Scanner maxed")), _ => S.HasSlot);
        BotsCommand = new RelayCommand(_ => Quick(Cheats.MaxBots, Loc.T("Bots maxed")), _ => S.HasSlot);
        UnlockCommand = new RelayCommand(_ => Quick(Cheats.UnlockTerminals, Loc.T("Garages unlocked")), _ => S.HasSlot);
        CalmCommand = new RelayCommand(_ => Quick(Cheats.CalmEgg, Loc.T("The egg is calm")), _ => S.HasSlot);
        PipesCommand = new RelayCommand(_ => Quick(Cheats.RepairPipes, Loc.T("Pipes fixed")), _ => S.HasSlot);
        RestockCommand = new RelayCommand(_ => Quick(Cheats.Restock, Loc.T("Shop restocked")), _ => S.HasSlot);
        GodCommand = new RelayCommand(_ => Quick(() =>
        {
            Cheats.AddMoney(Cheats.MaxBalanceCents);
            Cheats.RepairTools();
            Cheats.MaxScanner();
            Cheats.MaxBots();
            Cheats.UnlockTerminals();
            Cheats.MaxCarts();
            Cheats.CalmEgg();
            Cheats.RepairPipes();
            Cheats.Restock();
        }, Loc.T("Everything maxed")), _ => S.HasSlot);
    }

    static void Quick(Action action, string status)
    {
        action();
        S.Status = status + Loc.T(" – not saved yet");
        S.NotifyDataChanged();
    }

    public ObservableCollection<SlotCardVm> Slots { get; }
    public bool HasSlot => S.HasSlot;
    public string ActiveTitle => S.Slot is { } s ? Loc.F("Quick actions for slot {0}", s.Index + 1) : Loc.T("Quick actions");
    public string SaveDir => GamePaths.SaveDir;
    public bool GameRunning => GamePaths.IsGameRunning();

    public ICommand OpenSlotCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand MoneyCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand ScannerCommand { get; }
    public ICommand BotsCommand { get; }
    public ICommand UnlockCommand { get; }
    public ICommand CalmCommand { get; }
    public ICommand PipesCommand { get; }
    public ICommand RestockCommand { get; }
    public ICommand GodCommand { get; }
}

public sealed class RunVm
{
    public RunVm(SaveSlot slot)
    {
        var run = slot.Run;
        HasRun = run != null;
        if (run == null) return;
        RunId = run.Str("runId");
        MaxCrew = run.Int("maxCrew");
        Ranked = run.Bool("ranked");
        UnrankedReason = run.Str("unrankedReason") is { Length: > 0 } r ? r : "–";
        Finished = run.Bool("finished");
        Elapsed = Fmt.Duration(run.Num("elapsedSeconds"));
        HostText = slot.RunHostId switch { null => Loc.T("unknown"), 0 => "Offline", long id => id.ToString() };
        RunFile = Path.GetFileName(GamePaths.RunPath(slot.Index));
    }

    public bool HasRun { get; }
    public string RunId { get; } = "";
    public int MaxCrew { get; }
    public bool Ranked { get; }
    public string RankedText => Ranked ? "Ranked" : "Unranked";
    public string UnrankedReason { get; } = "";
    public bool Finished { get; }
    public string FinishedText => Finished ? Loc.T("Finished") : Loc.T("In progress");
    public string Elapsed { get; } = "";
    public string HostText { get; } = "";
    public string RunFile { get; } = "";
}
