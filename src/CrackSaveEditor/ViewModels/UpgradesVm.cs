using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class ScannerTrackVm(JsonObject node, ScannerTrack track) : NodeVm(node, "Upgrades")
{
    public string Label => track.Label;
    public int Max => track.MaxLevel;

    public int Level
    {
        get => Node.Int(track.Key);
        set { SetInt(track.Key, Math.Clamp(value, 0, Max), Loc.F("Scanner {0}", Label)); Raise(nameof(ValueText)); Raise(nameof(NextText)); }
    }

    public string ValueText => Level < track.Values.Length
        ? track.Values[Level].ToString("0.##", Loc.I.Culture) + track.Unit
        : Loc.F("Tier {0}", Level);

    public string NextText => Level < Max ? Loc.F("Next tier: {0}", Fmt.Money(track.Prices[Level])) : Loc.T("Maxed out");
}

public sealed class BotVm(JsonObject node) : NodeVm(node, "Upgrades")
{
    public string Name => GameData.TerminalName(Node.Str("terminalId"));
    public bool IsDrone => Node.Str("terminalId").Contains("drone");
    public string Glyph => IsDrone ? "" : "";
    public int Max => GameData.BotMaxLevel;

    public int Level
    {
        get => Node.Int("level");
        set => SetInt("level", Math.Clamp(value, 1, Max), Loc.F("{0} level", Name));
    }
}

public sealed class TerminalVm(JsonObject node) : NodeVm(node, "Upgrades")
{
    public string Name => GameData.TerminalName(Node.Str("terminalId"));
    public string Id => Node.Str("terminalId");

    public bool Unlocked
    {
        get => Node.Bool("unlocked");
        set => SetBool("unlocked", value, Loc.F("{0} unlocked", Name));
    }
}

public sealed class CartVm(JsonObject node) : NodeVm(node, "Upgrades")
{
    public string Name => Loc.F("Golf cart {0}", Node.Str("cartId").Replace("cart_", ""));
    public int Max => Math.Max(1, GameData.CartTierPrices.Length);

    public int Tier
    {
        get => Node.Int("tier");
        set { SetInt("tier", Math.Clamp(value, 1, Max), Loc.F("{0} tier", Name)); Raise(nameof(TierText)); }
    }

    public string TierText => Loc.F("Tier {0} / {1}", Tier, Max);
}

public sealed class KeypadVm(JsonObject node) : NodeVm(node, "Upgrades")
{
    public string Name => Loc.F("Keypad {0}", Node.Str("keypadId").Replace("keypad_", ""));

    public bool Unlocked
    {
        get => Node.Bool("unlocked");
        set => SetBool("unlocked", value, Loc.F("{0} unlocked", Name));
    }
}

public sealed class UpgradesVm
{
    public UpgradesVm(SaveSlot slot)
    {
        var w = slot.World;
        var scanner = w.Obj("scannerUpgrades");
        Scanner = new(GameData.Scanner.Select(t => new ScannerTrackVm(scanner, t)));
        Bots = new(w.Obj("botLevels").Arr("bots").Objects().Select(b => new BotVm(b)).OrderBy(b => b.Name));
        Terminals = new(w.Obj("garageTerminals").Arr("terminals").Objects().Select(t => new TerminalVm(t)).OrderBy(t => t.Name));
        Carts = new(w.Obj("vehicles").Arr("vehicles").Objects().Select(c => new CartVm(c)));
        Keypads = new(w.Obj("keypads").Arr("keypads").Objects().Select(k => new KeypadVm(k)));

        MaxScannerCommand = new RelayCommand(() => { foreach (var t in Scanner) t.Level = t.Max; });
        MaxBotsCommand = new RelayCommand(() => { foreach (var b in Bots) b.Level = b.Max; });
        UnlockAllCommand = new RelayCommand(() =>
        {
            foreach (var t in Terminals) t.Unlocked = true;
            foreach (var k in Keypads) k.Unlocked = true;
        });
        MaxCartsCommand = new RelayCommand(() => { foreach (var c in Carts) c.Tier = c.Max; });
    }

    public ObservableCollection<ScannerTrackVm> Scanner { get; }
    public ObservableCollection<BotVm> Bots { get; }
    public ObservableCollection<TerminalVm> Terminals { get; }
    public ObservableCollection<CartVm> Carts { get; }
    public ObservableCollection<KeypadVm> Keypads { get; }

    public ICommand MaxScannerCommand { get; }
    public ICommand MaxBotsCommand { get; }
    public ICommand UnlockAllCommand { get; }
    public ICommand MaxCartsCommand { get; }
}
