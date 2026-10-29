using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace CrackSaveEditor.Core;

public sealed record BackupInfo(string Path, DateTime Created, int Slot, string Label, long Size)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SlotText => Slot < 0 ? Loc.T("All") : $"Slot {Slot + 1}";
    public string SizeText => (Size / 1024.0).ToString("N0", Loc.I.Culture) + " KB";
}

/// <summary>Zips slot files (plus settings.json) next to the exe before anything is overwritten.</summary>
public static class BackupService
{
    const string Stamp = "yyyy-MM-dd_HH-mm-ss";

    public static string Create(int slot, string label = "auto")
    {
        string name = $"{DateTime.Now.ToString(Stamp, CultureInfo.InvariantCulture)}_slot{slot}_{Sanitize(label)}.zip";
        string zipPath = Path.Combine(GamePaths.BackupDir, name);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var files = slot < 0
            ? Enumerable.Range(0, GamePaths.SlotCount).SelectMany(GamePaths.SlotFiles)
            : GamePaths.SlotFiles(slot);
        foreach (string file in files.Append(GamePaths.SettingsPath).Where(File.Exists))
            zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
        return zipPath;
    }

    public static List<BackupInfo> List()
    {
        var result = new List<BackupInfo>();
        foreach (string f in Directory.GetFiles(GamePaths.BackupDir, "*.zip"))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            var parts = n.Split('_', 4);
            DateTime created = File.GetCreationTime(f);
            int slot = -1;
            string label = "";
            if (parts.Length >= 3 && DateTime.TryParseExact($"{parts[0]}_{parts[1]}", Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                created = dt;
                if (parts[2].StartsWith("slot") && int.TryParse(parts[2][4..], out int s)) slot = s;
                label = parts.Length > 3 ? parts[3] : "";
            }
            result.Add(new BackupInfo(f, created, slot, label, new FileInfo(f).Length));
        }
        return result.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>Restores every file contained in the zip into the save folder; returns the slots that were touched.</summary>
    public static List<int> Restore(string zipPath)
    {
        var slots = new List<int>();
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            // Only flat file names are ever written; refuse anything that would escape the save dir.
            if (entry.FullName != Path.GetFileName(entry.FullName)) continue;
            entry.ExtractToFile(Path.Combine(GamePaths.SaveDir, entry.Name), overwrite: true);
            for (int s = 0; s < GamePaths.SlotCount; s++)
                if (GamePaths.SlotFiles(s).Any(f => Path.GetFileName(f) == entry.Name) && !slots.Contains(s)) slots.Add(s);
        }
        return slots;
    }

    static string Sanitize(string s) =>
        new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray()).Trim('-');
}
