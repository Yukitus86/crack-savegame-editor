using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CrackSaveEditor.Core;
using Microsoft.Win32;

namespace CrackSaveEditor.ViewModels;

public sealed partial class FrameVm(string path)
{
    public string Path { get; } = path;

    public string Label
    {
        get
        {
            var m = FrameName().Match(System.IO.Path.GetFileName(Path));
            return m.Success ? Loc.F("Day {0} · {1:00}:00", int.Parse(m.Groups[1].Value) + 1, int.Parse(m.Groups[2].Value)) : System.IO.Path.GetFileNameWithoutExtension(Path);
        }
    }

    BitmapImage? _thumb;
    public BitmapImage? Thumb => _thumb ??= Fmt.Thumbnail(Path, 200);

    [GeneratedRegex(@"^d(\d+)_h(\d+)_")]
    private static partial Regex FrameName();
}

public sealed class TimelapseVm : ObservableObject
{
    readonly DispatcherTimer _timer = new();
    readonly int _slot;
    static Session S => Session.Current;

    public TimelapseVm(SaveSlot slot)
    {
        _slot = slot.Index;
        Frames = new(slot.TimelapseFrames().Select(f => new FrameVm(f)));
        Selected = Frames.LastOrDefault();
        _timer.Tick += (_, _) =>
        {
            if (Frames.Count == 0) return;
            int i = Selected == null ? 0 : (Frames.IndexOf(Selected) + 1) % Frames.Count;
            Selected = Frames[i];
        };
        Speed = 6;

        PlayCommand = new RelayCommand(_ => IsPlaying = !IsPlaying, _ => Frames.Count > 1);
        OpenFolderCommand = new RelayCommand(() =>
        {
            string dir = GamePaths.TimelapseDir(_slot);
            if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
        });
        GifCommand = new RelayCommand(_ => ExportGif(), _ => Frames.Count > 0);
    }

    public ObservableCollection<FrameVm> Frames { get; }
    public string Summary => Frames.Count == 0 ? Loc.T("This slot has no timelapse pictures yet.") : Loc.F("The game photographs the egg every hour. {0} pictures.", Frames.Count);

    FrameVm? _selected;
    public FrameVm? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) { Raise(nameof(Preview)); } }
    }

    public BitmapImage? Preview => Fmt.Thumbnail(Selected?.Path, 1280);

    bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (!Set(ref _isPlaying, value)) return;
            if (value) _timer.Start(); else _timer.Stop();
            Raise(nameof(PlayGlyph));
        }
    }

    public string PlayGlyph => IsPlaying ? "" : "";

    double _speed;
    /// <summary>Frames per second.</summary>
    public double Speed
    {
        get => _speed;
        set { if (Set(ref _speed, Math.Clamp(value, 1, 30))) { _timer.Interval = TimeSpan.FromSeconds(1.0 / _speed); Raise(nameof(SpeedText)); } }
    }

    public string SpeedText => Loc.F("{0:0} fps", Speed);

    public ICommand PlayCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand GifCommand { get; }

    public void Stop() => IsPlaying = false;

    void ExportGif()
    {
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("Animated GIF") + "|*.gif",
            FileName = $"CRACK_Slot{_slot + 1}_Timelapse.gif",
        };
        if (dlg.ShowDialog() != true) return;
        var frames = Frames.Select(f => GifWriter.LoadScaled(f.Path, 640)).ToList();
        GifWriter.Write(dlg.FileName, frames, (int)(1000 / Speed));
        S.Status = Loc.F("GIF saved: {0}", dlg.FileName);
    }
}
