using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class StockVm(JsonObject node) : NodeVm(node, "Shop")
{
    ShopItem? Item => GameData.Shop.GetValueOrDefault(Node.Str("itemId"));

    public string Name => GameData.ShopName(Node.Str("itemId"));
    public string Catalog => Item is { Catalog.Length: > 0 } i ? GameData.Catalogs.FirstOrDefault(c => c.Id == i.Catalog)?.Name ?? i.Catalog : "–";
    public string PriceText => Item is { } i ? Fmt.Money(i.Price) : "–";
    public int MaxPurchases => Item?.MaxPurchases ?? 0;

    public int Remaining
    {
        get => Node.Int("remaining");
        set => SetInt("remaining", Math.Clamp(value, 0, 999), Loc.F("{0} stock", Name));
    }
}

public sealed class SaleVm(JsonObject node) : NodeVm(node, "Shop")
{
    public JsonObject Json => Node;
    public string ItemId => Node.Str("itemId");
    public string Name => GameData.ShopName(ItemId);
    public string PriceText => GameData.Shop.TryGetValue(ItemId, out var i)
        ? $"{Fmt.Money(i.Price)} → {Fmt.Money(i.Price * (100 - Discount) / 100)}"
        : "";

    public int Discount
    {
        get => Node.Int("discountPercent");
        set { SetInt("discountPercent", Math.Clamp(value, 0, 100), Loc.F("Deal {0} discount %", Name)); Raise(nameof(PriceText)); }
    }
}

public sealed class ShopVm : NodeVm
{
    readonly JsonArray _sales;

    public ShopVm(SaveSlot slot) : base(slot.World.Obj("shopState"), "Shop")
    {
        _sales = Node.Arr("sales");
        Stocks = new(Node.Arr("stocks").Objects().Select(s => new StockVm(s)));
        Sales = new(_sales.Objects().Select(s => new SaleVm(s)));
        AllItems = GameData.Shop.Values.OrderBy(i => i.Name).ToList();
        NewSaleItem = AllItems.FirstOrDefault();

        RestockCommand = new RelayCommand(() => { foreach (var s in Stocks) if (s.MaxPurchases > 0) s.Remaining = s.MaxPurchases; });
        AddSaleCommand = new RelayCommand(() => { if (NewSaleItem != null) AddSale(NewSaleItem.Id, NewSaleDiscount); });
        SaleEverythingCommand = new RelayCommand(() => { foreach (var i in AllItems) AddSale(i.Id, NewSaleDiscount); });
        RemoveSaleCommand = new RelayCommand(p =>
        {
            if (p is not SaleVm sale) return;
            _sales.Remove(sale.Json);
            Sales.Remove(sale);
            S.Track(Area, Loc.F("Deal {0}", sale.Name), $"{sale.Discount}%", Loc.T("removed"));
        });
    }

    void AddSale(string itemId, int discount)
    {
        var existing = Sales.FirstOrDefault(s => s.ItemId == itemId);
        if (existing != null)
        {
            existing.Discount = discount;
            return;
        }
        var node = new JsonObject { ["itemId"] = itemId, ["discountPercent"] = discount };
        _sales.Add(node);
        Sales.Add(new SaleVm(node));
        S.Track(Area, Loc.F("Deal {0}", GameData.ShopName(itemId)), "–", $"{discount}%");
    }

    public ObservableCollection<StockVm> Stocks { get; }
    public ObservableCollection<SaleVm> Sales { get; }
    public List<ShopItem> AllItems { get; }

    ShopItem? _newSaleItem;
    public ShopItem? NewSaleItem { get => _newSaleItem; set => Set(ref _newSaleItem, value); }

    int _newSaleDiscount = 50;
    public int NewSaleDiscount { get => _newSaleDiscount; set => Set(ref _newSaleDiscount, Math.Clamp(value, 0, 100)); }

    public bool ToolboxNotYetBought
    {
        get => Node.Bool("toolboxNotYetBought");
        set => SetBool("toolboxNotYetBought", value, Loc.T("Toolbox counts as not yet bought"));
    }

    public string Subtitle => Loc.F("Stock and daily deals of the terminal shop. Catalogs: {0}", string.Join(" · ", GameData.Catalogs.Select(c => c.Name)));

    public ICommand RestockCommand { get; }
    public ICommand AddSaleCommand { get; }
    public ICommand SaleEverythingCommand { get; }
    public ICommand RemoveSaleCommand { get; }
}
