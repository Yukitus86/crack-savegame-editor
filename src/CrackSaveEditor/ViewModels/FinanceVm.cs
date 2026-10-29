using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class AccountVm : NodeVm
{
    public AccountVm(JsonObject node) : base(node, Loc.T("Finances"))
    {
        AddCommand = new RelayCommand(p => BalanceCents = (int)Math.Clamp(BalanceCents + long.Parse((string)p!) * 100L, 0, Cheats.MaxBalanceCents));
        MaxCommand = new RelayCommand(() => BalanceCents = Cheats.MaxBalanceCents);
        StartCommand = new RelayCommand(() => BalanceCents = GameData.StartingBalance);
    }

    public ICommand AddCommand { get; }
    public ICommand MaxCommand { get; }
    public ICommand StartCommand { get; }

    public long SteamId => Node.Long("steamId");
    public string Initial => DisplayName.Trim('*', ' ') is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "?";

    public string DisplayName
    {
        get => Node.Str("displayName");
        set { SetString("displayName", value, Loc.F("Display name {0}", SteamId)); Raise(nameof(Initial)); }
    }

    public int BalanceCents
    {
        get => Node.Int("balance");
        set => SetMoney("balance", value, Loc.F("Balance {0}", DisplayName), nameof(Balance), nameof(BalanceText));
    }

    public decimal Balance { get => BalanceCents / 100m; set => BalanceCents = ToCents(value); }
    public string BalanceText => Fmt.Money(BalanceCents);

    public decimal Income
    {
        get => Node.Int("totalIncome") / 100m;
        set => SetMoney("totalIncome", ToCents(value), Loc.F("Total income {0}", DisplayName), nameof(Income));
    }

    public decimal Expenses
    {
        get => Node.Int("totalExpenses") / 100m;
        set => SetMoney("totalExpenses", ToCents(value), Loc.F("Total expenses {0}", DisplayName), nameof(Expenses));
    }

    void SetMoney(string key, int cents, string label, params string[] props)
    {
        int old = Node.Int(key);
        if (old == cents) return;
        Node[key] = cents;
        S.Track(Area, label, Fmt.Money(old), Fmt.Money(cents));
        if (key == "balance") Raise(nameof(BalanceCents));
        foreach (var p in props) Raise(p);
    }

    static int ToCents(decimal dollars) => (int)Math.Clamp(Math.Round(dollars * 100m), 0, int.MaxValue);
}

public sealed class FinanceVm : NodeVm
{
    public FinanceVm(SaveSlot slot) : base(slot.World.Obj("finance"), Loc.T("Finances"))
    {
        Accounts = new(Node.Arr("accounts").Objects().Select(a => new AccountVm(a)));
    }

    public ObservableCollection<AccountVm> Accounts { get; }

    public decimal GlobalShopSpending
    {
        get => Node.Int("globalShopSpending") / 100m;
        set
        {
            int cents = (int)Math.Clamp(Math.Round(value * 100m), 0, int.MaxValue);
            int old = Node.Int("globalShopSpending");
            if (old == cents) return;
            Node["globalShopSpending"] = cents;
            S.Track(Area, Loc.T("Total shop spending"), Fmt.Money(old), Fmt.Money(cents));
            Raise();
        }
    }

    public string MaxText => Loc.F("Maximum ({0})", Fmt.Money(Cheats.MaxBalanceCents));
    public string StartText => Loc.F("Starting money ({0})", Fmt.Money(GameData.StartingBalance));
}
