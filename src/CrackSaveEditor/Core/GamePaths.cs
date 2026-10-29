using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CrackSaveEditor.Core;

public static partial class GamePaths
{
    public const int SlotCount = 3;
    const long SteamId64Base = 76561197960265728;

    public static string SaveDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Rafoli", "CRACK");

    public static string SettingsPath => Path.Combine(SaveDir, "settings.json");
    public static string PlayerLogPath => Path.Combine(SaveDir, "Player.log");

    public static string WorldPath(int slot) => Path.Combine(SaveDir, $"world_slot{slot}.json");
    public static string BaselinePath(int slot) => Path.Combine(SaveDir, $"world_slot{slot}_baseline.json");
    public static string MetaPath(int slot) => Path.Combine(SaveDir, $"world_slot{slot}.meta.json");
    public static string RunPath(int slot) => Path.Combine(SaveDir, $"world_slot{slot}.run");
    public static string TimelapseDir(int slot) => Path.Combine(SaveDir, $"world_slot{slot}_timelapse");

    public static IEnumerable<string> SlotFiles(int slot) =>
        new[] { WorldPath(slot), BaselinePath(slot), MetaPath(slot), RunPath(slot) };

    public static string BackupDir
    {
        get
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static bool IsGameRunning() => Process.GetProcessesByName("CRACK").Length > 0;

    public static string? SteamDir()
    {
        string? dir = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                      ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (dir == null)
        {
            string fallback = @"C:\Program Files (x86)\Steam";
            return Directory.Exists(fallback) ? fallback : null;
        }
        return dir.Replace('/', '\\');
    }

    /// <summary>SteamIDs that may have signed a save: offline (0) plus every account known to the local Steam install.</summary>
    public static List<long> CandidateSteamIds()
    {
        var ids = new List<long> { 0 };
        string? steam = SteamDir();
        if (steam == null) return ids;

        try
        {
            string vdf = Path.Combine(steam, "config", "loginusers.vdf");
            if (File.Exists(vdf))
                foreach (Match m in SteamId64Regex().Matches(File.ReadAllText(vdf)))
                    ids.Add(long.Parse(m.Groups[1].Value));

            string userdata = Path.Combine(steam, "userdata");
            if (Directory.Exists(userdata))
                foreach (string d in Directory.GetDirectories(userdata))
                    if (long.TryParse(Path.GetFileName(d), out long accountId) && accountId > 0)
                        ids.Add(SteamId64Base + accountId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return ids.Distinct().ToList();
    }

    [GeneratedRegex("\"(7656\\d{13})\"")]
    private static partial Regex SteamId64Regex();
}
