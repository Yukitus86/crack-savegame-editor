using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class SettingSliderVm(GameSettingsVm owner, string key, string label, double min, double max, string format, double scale = 1)
    : ObservableObject
{
    public string Label => label;
    public double Min => min;
    public double Max => max;

    public double Value
    {
        get => owner.Json.Num(key) * scale;
        set
        {
            // Quality settings are int indices in the file; everything else is a float.
            owner.Json[key] = format == "0" ? JsonValue.Create((int)Math.Round(value)) : JsonValue.Create(Math.Round(value / scale, 4));
            owner.MarkDirty();
            Raise();
            Raise(nameof(Text));
        }
    }

    public string Text => Value.ToString(format, Fmt.Culture);
}

public sealed class SettingToggleVm(GameSettingsVm owner, string key, string label, string hint) : ObservableObject
{
    public string Label => label;
    public string Hint => hint;

    public bool Value
    {
        get => owner.Json.Int(key) != 0;
        set
        {
            owner.Json[key] = value ? 1 : 0;
            owner.MarkDirty();
            Raise();
        }
    }
}

public sealed record Choice(int Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Editor for the plain-text settings.json (independent of the save slots).</summary>
public sealed class GameSettingsVm : ObservableObject
{
    static Session S => Session.Current;

    public GameSettingsVm()
    {
        Exists = File.Exists(GamePaths.SettingsPath);
        Json = Exists ? JsonNode.Parse(File.ReadAllText(GamePaths.SettingsPath)) as JsonObject ?? new() : new();

        Audio =
        [
            new(this, "masterVolume", Loc.T("Master volume"), 0, 100, "0'%'", 100),
            new(this, "musicVolume", Loc.T("Music"), 0, 100, "0'%'", 100),
            new(this, "ambianceVolume", Loc.T("Ambience"), 0, 100, "0'%'", 100),
            new(this, "sfxVolume", Loc.T("Effects"), 0, 100, "0'%'", 100),
            new(this, "voiceVolume", Loc.T("Voice chat"), 0, 100, "0'%'", 100),
            new(this, "micVolume", Loc.T("Microphone"), 0, 200, "0'%'", 100),
        ];
        Camera =
        [
            new(this, "fieldOfView", Loc.T("Field of view – beyond the menu limit too"), 50, 160, "0'°'"),
            new(this, "mouseSensitivity", Loc.T("Mouse sensitivity"), 0.01, 1, "0.000"),
            new(this, "terminalCursorSpeed", Loc.T("Terminal cursor"), 0.1, 3, "0.00"),
        ];
        Quality =
        [
            new(this, "antiAliasingIndex", Loc.T("Anti-aliasing (level)"), 0, 3, "0"),
            new(this, "ambientOcclusionIndex", Loc.T("Ambient occlusion (level)"), 0, 3, "0"),
            new(this, "shadowsIndex", Loc.T("Shadows (level)"), 0, 3, "0"),
            new(this, "volumetricsQuality", Loc.T("Volumetric fog (level)"), 0, 3, "0"),
            new(this, "vSyncIndex", Loc.T("VSync (level)"), 0, 2, "0"),
        ];
        Toggles =
        [
            new(this, "chillMode", Loc.T("Chill mode"), Loc.T("A more relaxed game without time pressure.")),
            new(this, "offlineMode", Loc.T("Offline mode"), Loc.T("Play without a Steam lobby.")),
            new(this, "holidayTheme", Loc.T("Holiday theme"), Loc.T("Seasonal decorations.")),
            new(this, "showTips", Loc.T("Show tips"), Loc.T("Show hints in-game.")),
            new(this, "showSpeedrunTimer", Loc.T("Speedrun timer"), Loc.T("Show a timer at the top of the screen.")),
            new(this, "invertY", Loc.T("Invert Y axis"), ""),
            new(this, "toggleSprint", Loc.T("Toggle sprint"), Loc.T("Instead of holding the key.")),
            new(this, "toggleCrouch", Loc.T("Toggle crouch"), Loc.T("Instead of holding the key.")),
            new(this, "screenshake", Loc.T("Screen shake"), ""),
            new(this, "smoothCamera", Loc.T("Smooth camera"), ""),
            new(this, "reduceCameraMotion", Loc.T("Reduce camera motion"), Loc.T("Helps against motion sickness.")),
            new(this, "screenDither", Loc.T("Screen dithering"), ""),
            new(this, "pushToTalk", "Push-to-Talk", ""),
            new(this, "publicLobby", Loc.T("Public lobby"), ""),
            new(this, "inviteOnlyLobby", Loc.T("Invite only"), ""),
            new(this, "regionalMatchmaking", Loc.T("Regional matchmaking"), ""),
        ];
        Languages = GameData.Languages.ToList();
        DisplayModes = [new(0, Loc.T("Exclusive fullscreen")), new(1, Loc.T("Borderless fullscreen")), new(2, Loc.T("Maximized window")), new(3, Loc.T("Windowed"))];

        SaveCommand = new RelayCommand(_ => Save(), _ => IsDirty);
        ReloadCommand = new RelayCommand(() => S.NotifyDataChanged());
    }

    public JsonObject Json { get; }
    public bool Exists { get; }
    public SettingSliderVm[] Audio { get; }
    public SettingSliderVm[] Camera { get; }
    public SettingSliderVm[] Quality { get; }
    public SettingToggleVm[] Toggles { get; }
    public List<string> Languages { get; }
    public List<Choice> DisplayModes { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }

    bool _dirty;
    public bool IsDirty { get => _dirty; private set => Set(ref _dirty, value); }
    public void MarkDirty() => IsDirty = true;

    public string Language
    {
        get => Json.Str("language");
        set { Json["language"] = value; MarkDirty(); Raise(); }
    }

    public Choice? DisplayMode
    {
        get => DisplayModes.FirstOrDefault(d => d.Value == Json.Int("fullscreenMode"));
        set { if (value == null) return; Json["fullscreenMode"] = value.Value; MarkDirty(); Raise(); }
    }

    void Save()
    {
        if (GamePaths.IsGameRunning())
            S.Status = Loc.T("Warning: CRACK is running – the game overwrites its settings when it quits.");
        BackupService.Create(-1, "settings");
        File.WriteAllText(GamePaths.SettingsPath, Json.ToJsonString(JsonExt.Pretty));
        IsDirty = false;
        S.Status = Loc.T("Game settings saved");
    }
}
