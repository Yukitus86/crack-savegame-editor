using System.IO;
using System.Text.Json.Nodes;

namespace CrackSaveEditor.Core;

/// <summary>One save slot: checkpoint world json, slot meta, run record and timelapse frames.</summary>
public sealed class SaveSlot
{
    public int Index { get; }
    public JsonObject World { get; private set; } = new();
    public JsonObject? Run { get; private set; }
    public JsonObject Meta { get; private set; } = new();

    /// <summary>SteamID the checkpoint was signed with; null if no candidate matched.</summary>
    public long? HostId { get; private set; }
    public long? RunHostId { get; private set; }
    public DateTime LastWrite { get; private set; }

    SaveSlot(int index) => Index = index;

    public static bool Exists(int slot) => File.Exists(GamePaths.WorldPath(slot));

    public static SaveSlot Load(int slot, IReadOnlyCollection<long>? extraIds = null)
    {
        var s = new SaveSlot(slot);
        string path = GamePaths.WorldPath(slot);
        byte[] raw = File.ReadAllBytes(path);
        s.LastWrite = File.GetLastWriteTime(path);
        s.World = JsonNode.Parse(SaveCrypto.Decrypt(raw)) as JsonObject
                  ?? throw new InvalidDataException(Loc.T("The save is empty."));
        s._loadedDayNumber = s.World.Int("dayNumber");

        var candidates = CandidatesFor(s.World, extraIds);
        s.HostId = SaveCrypto.FindHostId(raw, candidates);

        string runPath = GamePaths.RunPath(slot);
        if (File.Exists(runPath))
        {
            byte[] runRaw = File.ReadAllBytes(runPath);
            try
            {
                s.Run = JsonNode.Parse(SaveCrypto.Decrypt(runRaw)) as JsonObject;
                s.RunHostId = SaveCrypto.FindHostId(runRaw, candidates);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            {
                s.Run = null;
            }
        }

        string metaPath = GamePaths.MetaPath(slot);
        if (File.Exists(metaPath))
            s.Meta = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject ?? new JsonObject();
        return s;
    }

    static List<long> CandidatesFor(JsonObject world, IReadOnlyCollection<long>? extra)
    {
        var ids = GamePaths.CandidateSteamIds();
        foreach (var acc in world.Obj("finance").Arr("accounts").Objects())
            ids.Add(acc.Long("steamId"));
        if (extra != null) ids.AddRange(extra);
        return ids.Distinct().ToList();
    }

    public long BalanceCentsTotal => World.Obj("finance").Arr("accounts").Objects().Sum(a => (long)a.Int("balance"));

    public bool? Ranked => Run?.Bool("ranked");

    public const string UnrankedReason = "save edited";

    /// <summary>Writes checkpoint and meta. The run always becomes unranked: edited saves never count for leaderboards.</summary>
    public void Save(long hostId)
    {
        File.WriteAllBytes(GamePaths.WorldPath(Index), SaveCrypto.Encrypt(World.ToCompact(), hostId));
        WriteMeta();

        if (Run != null && Run.Bool("ranked"))
        {
            Run["ranked"] = false;
            Run["unrankedReason"] = UnrankedReason;
            File.WriteAllBytes(GamePaths.RunPath(Index), SaveCrypto.Encrypt(Run.ToCompact(), RunHostId ?? hostId));
        }
        LastWrite = File.GetLastWriteTime(GamePaths.WorldPath(Index));
        HostId = hostId;
    }

    /// <summary>
    /// Marks the run record of a slot unranked after the editor touched its files on disk
    /// (slot copy, baseline reset, backup restore), so no editor path can keep a run ranked.
    /// </summary>
    public static void ForceUnranked(int slot)
    {
        string runPath = GamePaths.RunPath(slot);
        if (!File.Exists(runPath)) return;
        byte[] raw = File.ReadAllBytes(runPath);
        if (JsonNode.Parse(SaveCrypto.Decrypt(raw)) is not JsonObject run || !run.Bool("ranked")) return;

        var candidates = GamePaths.CandidateSteamIds();
        if (File.Exists(GamePaths.WorldPath(slot)))
        {
            try
            {
                var world = JsonNode.Parse(SaveCrypto.Decrypt(File.ReadAllBytes(GamePaths.WorldPath(slot)))) as JsonObject;
                candidates = CandidatesFor(world ?? new JsonObject(), null);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            {
            }
        }
        long host = SaveCrypto.FindHostId(raw, candidates) ?? candidates.FirstOrDefault(id => id != 0);

        run["ranked"] = false;
        run["unrankedReason"] = UnrankedReason;
        File.WriteAllBytes(runPath, SaveCrypto.Encrypt(run.ToCompact(), host));
    }

    /// <summary>Keeps the slot menu summary in sync (the game shows day and balance from it).</summary>
    void WriteMeta()
    {
        int dayNumber = World.Int("dayNumber");
        int oldDay = Meta.Int("day", dayNumber + 1);
        Meta["day"] = Math.Max(0, dayNumber + (oldDay - (_loadedDayNumber ?? dayNumber)));
        Meta["balanceCents"] = (int)Math.Clamp(BalanceCentsTotal, int.MinValue, int.MaxValue);
        if (Meta["saveVersion"] == null) Meta["saveVersion"] = 2;
        File.WriteAllText(GamePaths.MetaPath(Index), Meta.ToCompact());
        _loadedDayNumber = dayNumber;
    }

    // Day number at load time, so the meta "day" offset (meta day = dayNumber + 1 in practice) is preserved.
    int? _loadedDayNumber;

    public void ReplaceWorld(JsonObject world) => World = world;

    public string[] TimelapseFrames()
    {
        string dir = GamePaths.TimelapseDir(Index);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.jpg").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
    }
}
