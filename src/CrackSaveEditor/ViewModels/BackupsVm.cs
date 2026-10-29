using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

/// <summary>Backup list and slot-level file tools. Destructive actions ask via <see cref="Confirm"/>.</summary>
public sealed class BackupsVm : ObservableObject
{
    static Session S => Session.Current;

    public BackupsVm(Func<string, string, bool> confirm)
    {
        Confirm = confirm;
        Backups = new(BackupService.List());
        SlotNames = Enumerable.Range(0, GamePaths.SlotCount)
            .Select(i => new Choice(i, $"Slot {i + 1}{(SaveSlot.Exists(i) ? "" : Loc.T(" (empty)"))}")).ToList();
        CopyFrom = SlotNames[0];
        CopyTo = SlotNames.Count > 1 ? SlotNames[1] : SlotNames[0];
        ToolSlot = SlotNames[S.Slot?.Index ?? 0];

        CreateCommand = new RelayCommand(() => Run(() => BackupService.Create(-1, "manual"), Loc.T("Backup of all slots created")));
        RestoreCommand = new RelayCommand(p =>
        {
            if (p is not BackupInfo b || !Confirm(Loc.T("Restore backup?"),
                    Loc.F("The files from \"{0}\" overwrite the current saves. A safety backup is created first. The run is marked unranked.", b.FileName))) return;
            Run(() =>
            {
                BackupService.Create(-1, "before-restore");
                foreach (int slot in BackupService.Restore(b.Path)) SaveSlot.ForceUnranked(slot);
            }, Loc.T("Backup restored"), reload: true);
        });
        DeleteCommand = new RelayCommand(p =>
        {
            if (p is not BackupInfo b || !Confirm(Loc.T("Delete backup?"), Loc.F("\"{0}\" will be deleted permanently.", b.FileName))) return;
            Run(() => File.Delete(b.Path), Loc.T("Backup deleted"));
        });
        OpenFolderCommand = new RelayCommand(() => Process.Start("explorer.exe", GamePaths.BackupDir));

        CopySlotCommand = new RelayCommand(_ => CopySlot(), _ => CopyFrom != null && CopyTo != null && CopyFrom.Value != CopyTo.Value);
        ResetToBaselineCommand = new RelayCommand(_ => ResetToBaseline(), _ => ToolSlot != null && File.Exists(GamePaths.BaselinePath(ToolSlot.Value)));
        DeleteSlotCommand = new RelayCommand(_ => DeleteSlot(), _ => ToolSlot != null && SaveSlot.Exists(ToolSlot.Value));
    }

    Func<string, string, bool> Confirm { get; }

    public ObservableCollection<BackupInfo> Backups { get; }
    public List<Choice> SlotNames { get; }
    public string BackupDir => Loc.F("Stored in {0}", GamePaths.BackupDir);

    Choice? _copyFrom, _copyTo, _toolSlot;
    public Choice? CopyFrom { get => _copyFrom; set => Set(ref _copyFrom, value); }
    public Choice? CopyTo { get => _copyTo; set => Set(ref _copyTo, value); }
    public Choice? ToolSlot { get => _toolSlot; set => Set(ref _toolSlot, value); }

    public ICommand CreateCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopySlotCommand { get; }
    public ICommand ResetToBaselineCommand { get; }
    public ICommand DeleteSlotCommand { get; }

    void CopySlot()
    {
        int from = CopyFrom!.Value, to = CopyTo!.Value;
        if (!SaveSlot.Exists(from)) { S.Status = Loc.F("Slot {0} is empty.", from + 1); return; }
        if (!Confirm(Loc.T("Copy slot?"), Loc.F("Slot {0} is copied to slot {1}. Everything in slot {1} is replaced (a backup is created). The copy is unranked.", from + 1, to + 1))) return;
        Run(() =>
        {
            BackupService.Create(to, "before-copy");
            foreach (var (src, dst) in GamePaths.SlotFiles(from).Zip(GamePaths.SlotFiles(to)))
            {
                if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
                else if (File.Exists(dst)) File.Delete(dst);
            }
            string tlFrom = GamePaths.TimelapseDir(from), tlTo = GamePaths.TimelapseDir(to);
            if (Directory.Exists(tlFrom))
            {
                Directory.CreateDirectory(tlTo);
                foreach (var f in Directory.GetFiles(tlFrom, "*.jpg"))
                    File.Copy(f, Path.Combine(tlTo, Path.GetFileName(f)), overwrite: true);
            }
            SaveSlot.ForceUnranked(to);
        }, Loc.F("Slot {0} → slot {1} copied", from + 1, to + 1), reload: true);
    }

    void ResetToBaseline()
    {
        int slot = ToolSlot!.Value;
        if (!Confirm(Loc.T("Reset to day 0?"), Loc.F("The checkpoint of slot {0} is replaced by the starting state (baseline). Progress is lost – a backup is created. The run becomes unranked.", slot + 1))) return;
        Run(() =>
        {
            BackupService.Create(slot, "before-reset");
            File.Copy(GamePaths.BaselinePath(slot), GamePaths.WorldPath(slot), overwrite: true);
            SaveSlot.ForceUnranked(slot);
        }, Loc.F("Slot {0} reset to day 0", slot + 1), reload: true);
    }

    void DeleteSlot()
    {
        int slot = ToolSlot!.Value;
        if (!Confirm(Loc.T("Delete slot?"), Loc.F("Slot {0} is deleted (including timelapse). A backup of the save files is created.", slot + 1))) return;
        Run(() =>
        {
            BackupService.Create(slot, "before-delete");
            foreach (var f in GamePaths.SlotFiles(slot).Where(File.Exists)) File.Delete(f);
            if (Directory.Exists(GamePaths.TimelapseDir(slot)))
                foreach (var f in Directory.GetFiles(GamePaths.TimelapseDir(slot), "*.jpg")) File.Delete(f);
            if (S.Slot?.Index == slot) S.Unload();
        }, Loc.F("Slot {0} deleted", slot + 1), reload: true);
    }

    void Run(Action action, string done, bool reload = false)
    {
        if (GamePaths.IsGameRunning() && !Confirm(Loc.T("CRACK is still running"), Loc.T("The game overwrites files when it saves or quits. Continue anyway?")))
            return;
        try
        {
            action();
            S.Status = done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            S.Status = Loc.T("Error: ") + ex.Message;
        }
        if (reload && S.Slot != null && SaveSlot.Exists(S.Slot.Index)) S.Reload();
        else S.NotifyDataChanged();
    }
}
