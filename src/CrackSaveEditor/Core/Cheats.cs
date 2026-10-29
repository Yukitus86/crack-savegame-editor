using System.Globalization;
using System.Text.Json.Nodes;

namespace CrackSaveEditor.Core;

/// <summary>One-click edits shared by the dashboard and the individual pages. All edits are tracked.</summary>
public static class Cheats
{
    public const int MaxBalanceCents = 100_000_000; // $1.000.000,00

    static Session S => Session.Current;
    static JsonObject W => S.Slot!.World;

    public static void AddMoney(long cents)
    {
        foreach (var acc in W.Obj("finance").Arr("accounts").Objects())
        {
            int old = acc.Int("balance");
            int now = (int)Math.Clamp(old + cents, 0, MaxBalanceCents);
            acc["balance"] = now;
            S.Track(Loc.T("Finances"), Loc.F("Balance {0}", acc.Str("displayName")), Money(old), Money(now));
        }
    }

    public static void MaxScanner()
    {
        var sc = W.Obj("scannerUpgrades");
        foreach (var t in GameData.Scanner)
        {
            int old = sc.Int(t.Key);
            sc[t.Key] = t.MaxLevel;
            S.Track("Upgrades", Loc.F("Scanner {0}", t.Label), old, t.MaxLevel);
        }
    }

    public static void MaxBots()
    {
        foreach (var bot in W.Obj("botLevels").Arr("bots").Objects())
        {
            int old = bot.Int("level");
            bot["level"] = GameData.BotMaxLevel;
            S.Track("Upgrades", Loc.F("{0} level", GameData.TerminalName(bot.Str("terminalId"))), old, GameData.BotMaxLevel);
        }
    }

    public static void UnlockTerminals()
    {
        foreach (var t in W.Obj("garageTerminals").Arr("terminals").Objects())
        {
            bool old = t.Bool("unlocked");
            t["unlocked"] = true;
            S.Track("Upgrades", Loc.F("{0} unlocked", GameData.TerminalName(t.Str("terminalId"))), old, true);
        }
        foreach (var k in W.Obj("keypads").Arr("keypads").Objects())
        {
            bool old = k.Bool("unlocked");
            k["unlocked"] = true;
            S.Track("Upgrades", Loc.F("Keypad {0} unlocked", k.Str("keypadId").Replace("keypad_", "")), old, true);
        }
    }

    public static void MaxCarts()
    {
        int max = Math.Max(1, GameData.CartTierPrices.Length);
        foreach (var cart in W.Obj("vehicles").Arr("vehicles").Objects())
        {
            int old = cart.Int("tier");
            cart["tier"] = max;
            S.Track("Upgrades", Loc.F("Golf cart {0} tier", cart.Str("cartId").Replace("cart_", "")), old, max);
        }
    }

    /// <summary>Sets durability of every tool (in the world and in pockets) to its maximum.</summary>
    public static int RepairTools()
    {
        int repaired = 0;
        foreach (var item in W.Obj("groundItems").Arr("items").Objects())
        {
            uint id = (uint)item.Long("assetId");
            if (!GameData.Prefabs.TryGetValue(id, out var p) || p.MaxDurability <= 0) continue;
            var keys = item.Arr("keys");
            var values = item.Arr("values");
            int idx = keys.Select(k => k?.GetValue<string>()).ToList().IndexOf("durability");
            if (idx < 0 || idx >= values.Count) continue;
            string old = values[idx]?.GetValue<string>() ?? "";
            string max = p.MaxDurability.ToString(CultureInfo.InvariantCulture);
            if (old == max) continue;
            values[idx] = max;
            int number = item.Parent is JsonArray a ? a.IndexOf(item) + 1 : 0;
            S.Track("Items", Loc.F("{0} #{1} durability", p.Name, number), old, max);
            repaired++;
        }
        return repaired;
    }

    public static void CalmEgg()
    {
        var ag = W.Obj("agitation");
        double old = ag.Num("agitation");
        ag["agitation"] = 0.0;
        S.Track(Loc.T("World"), Loc.T("Egg agitation"), old, 0.0);
    }

    public static void RepairPipes()
    {
        var bp = W.Obj("burstPipes");
        foreach (var pipe in bp.Arr("pipes").Objects())
        {
            if (pipe.Bool("leaking"))
            {
                pipe["leaking"] = false;
                S.Track(Loc.T("World"), Loc.F("Pipe {0} leaking", pipe.Str("pipeId")), true, false);
            }
        }
        double flood = bp.Num("floodDepth");
        if (flood > 0)
        {
            bp["floodDepth"] = 0.0;
            S.Track(Loc.T("World"), Loc.T("Flooding"), flood, 0.0);
        }
    }

    public static void Restock()
    {
        foreach (var stock in W.Obj("shopState").Arr("stocks").Objects())
        {
            if (!GameData.Shop.TryGetValue(stock.Str("itemId"), out var item) || item.MaxPurchases <= 0) continue;
            int old = stock.Int("remaining");
            if (old == item.MaxPurchases) continue;
            stock["remaining"] = item.MaxPurchases;
            S.Track("Shop", Loc.F("{0} stock", item.Name), old, item.MaxPurchases);
        }
    }

    public static string Money(long cents) => (cents < 0 ? "-$" : "$") + (Math.Abs(cents) / 100m).ToString("N2", Loc.I.Culture);
}
