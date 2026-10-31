using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using CrackSaveEditor.Core;

namespace CrackSaveEditor;

/// <summary>
/// Headless checks: "--selftest" verifies decrypt/encrypt round trips on the real saves (read-only);
/// "--selftest --write" additionally edits and saves a copy of the save folder in %TEMP%.
/// </summary>
static class SelfTest
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    static int _failures;

    public static int Run(string[] args)
    {
        AttachConsole(-1);
        Console.WriteLine();
        Console.WriteLine($"Save dir: {GamePaths.SaveDir}");
        Console.WriteLine($"GameData: {GameData.Shop.Count} shop items, {GameData.Prefabs.Count} prefabs, scanner tracks {GameData.Scanner.Count}");

        for (int slot = 0; slot < GamePaths.SlotCount; slot++)
        {
            if (!SaveSlot.Exists(slot)) { Console.WriteLine($"Slot {slot}: empty"); continue; }
            byte[] raw = File.ReadAllBytes(GamePaths.WorldPath(slot));
            string json = SaveCrypto.Decrypt(raw);
            var loaded = SaveSlot.Load(slot);
            Check($"slot {slot} signature host found ({loaded.HostId})", loaded.HostId != null);

            long host = loaded.HostId ?? 0;
            byte[] again = SaveCrypto.Encrypt(json, host);
            Check($"slot {slot} re-encrypt verifies", SaveCrypto.VerifyTag(again, host));
            Check($"slot {slot} re-decrypt identical", SaveCrypto.Decrypt(again) == json);

            string roundTrip = JsonNode.Parse(json)!.ToCompact();
            Check($"slot {slot} JsonNode round trip identical", roundTrip == json);
            if (roundTrip != json) ReportDiff(json, roundTrip);
            Check($"slot {slot} run record readable", loaded.Run != null || !File.Exists(GamePaths.RunPath(slot)));
        }

        if (args.Contains("--write")) WriteTest();

        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    static void WriteTest()
    {
        string original = GamePaths.SaveDir;
        string temp = Path.Combine(Path.GetTempPath(), "CrackSaveEditor_selftest");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        foreach (var f in Directory.GetFiles(original)) File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        GamePaths.SaveDir = temp;
        Console.WriteLine($"Write test in {temp}");

        var s = Session.Current;
        s.LoadSlot(0);
        long host = s.Slot!.HostId!.Value;
        int before = s.Slot.World.Obj("finance").Arr("accounts").Objects().First().Int("balance");
        int metaDayBefore = s.Slot.Meta.Int("day");
        Cheats.AddMoney(100_000);
        Cheats.MaxScanner();
        var account = s.Slot.World.Obj("finance").Arr("accounts").Objects().First(a => a.Long("steamId") != 0);
        new ViewModels.PlayerVm(s.Slot.World, account).HotbarSlots = 3;
        s.Save();

        var reloaded = SaveSlot.Load(0);
        int after = reloaded.World.Obj("finance").Arr("accounts").Objects().First().Int("balance");
        Check("saved file signed for same host", reloaded.HostId == host);
        Check($"balance {before} -> {after}", after == Math.Min(before + 100_000, Cheats.MaxBalanceCents));
        Check("scanner depth max", reloaded.World.Obj("scannerUpgrades").Int("depthLevel") == GameData.Scanner[0].MaxLevel);
        Check($"meta balance synced ({reloaded.Meta.Int("balanceCents")})", reloaded.Meta.Int("balanceCents") == after);
        Check($"meta day kept ({reloaded.Meta.Int("day")})", reloaded.Meta.Int("day") == metaDayBefore);
        Check("run marked unranked", reloaded.Run == null || reloaded.Run.Bool("ranked") == false);
        var hotbar = reloaded.World.Obj("hotbarSlots");
        var hotbarEntry = hotbar.Arr("players").Objects().FirstOrDefault(e => e.Long("steamId") == account.Long("steamId"));
        Check($"hotbar slots saved (version {hotbar.Int("version")}, slots {hotbarEntry.Int("slots")})",
            hotbar.Int("version") == 1 && hotbarEntry.Int("slots") == 3);
        Check("backup created", BackupService.List().Count > 0);

        // File-level tools (slot copy etc.) must unrank too: copy the original ranked files into slot 2.
        foreach (var (src, dst) in GamePaths.SlotFiles(0).Zip(GamePaths.SlotFiles(2)))
            File.Copy(Path.Combine(original, Path.GetFileName(src)), dst, overwrite: true);
        bool rankedBefore = SaveSlot.Load(2).Ranked == true;
        SaveSlot.ForceUnranked(2);
        var copied = SaveSlot.Load(2);
        Check($"copied slot unranked (was ranked: {rankedBefore})", copied.Ranked == false);
        Check("copied run still signed for host", copied.RunHostId == host);

        GamePaths.SaveDir = original;
    }

    static void ReportDiff(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int from = Math.Max(0, i - 60);
        Console.WriteLine($"  first difference at {i}:");
        Console.WriteLine($"  game  : {a.Substring(from, Math.Min(140, a.Length - from))}");
        Console.WriteLine($"  editor: {b.Substring(from, Math.Min(140, b.Length - from))}");
    }

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {what}");
        if (!ok) _failures++;
    }
}
