using System.ComponentModel;
using System.Globalization;
using System.Windows.Markup;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CrackSaveEditor.Core;
using CrackSaveEditor.ViewModels;
using CrackSaveEditor.Views;

namespace CrackSaveEditor;

public partial class MainWindow : Window
{
    sealed class Page(Func<object?> create, bool needsSlot)
    {
        public Func<object?> Create { get; } = create;
        public bool NeedsSlot { get; } = needsSlot;
        public object? Vm { get; set; }
        public bool Stale { get; set; } = true;
    }

    static Session S => Session.Current;
    readonly Dictionary<RadioButton, Page> _pages;
    readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    RadioButton _current;

    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = S;
        VersionRun.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);

        SaveCommand = new RelayCommand(_ => Save(), _ => S.HasSlot);
        ReloadCommand = new RelayCommand(_ => Reload(), _ => S.HasSlot);
        InputBindings.Add(new KeyBinding(SaveCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ReloadCommand, Key.F5, ModifierKeys.None));

        _pages = new()
        {
            [NavDashboard] = new(() => new DashboardVm((t, b) => Dialog.Confirm(this, t, b)), false),
            [NavFinance] = new(() => new FinanceVm(S.Slot!), true),
            [NavUpgrades] = new(() => new UpgradesVm(S.Slot!), true),
            [NavShop] = new(() => new ShopVm(S.Slot!), true),
            [NavItems] = new(() => new ItemsVm(S.Slot!), true),
            [NavWorld] = new(() => new WorldVm(S.Slot!), true),
            [NavPlayers] = new(() => new PlayersVm(S.Slot!), true),
            [NavRun] = new(() => new RunVm(S.Slot!), true),
            [NavTimelapse] = new(() => new TimelapseVm(S.Slot!), true),
            [NavRaw] = new(() => new RawJsonVm(S.Slot!), true),
            [NavSettings] = new(() => new GameSettingsVm(), false),
            [NavBackups] = new(() => new BackupsVm((t, b) => Dialog.Confirm(this, t, b)), false),
        };
        _current = NavDashboard;
        foreach (var nav in _pages.Keys) nav.Checked += (_, _) => Navigate(nav);

        S.SlotLoaded += () =>
        {
            foreach (var p in _pages.Values) p.Stale = true;
            if (!S.HasSlot && _pages[_current].NeedsSlot) NavDashboard.IsChecked = true;
            else Navigate(_current);
        };
        S.DataChanged += source =>
        {
            foreach (var p in _pages.Values) if (!ReferenceEquals(p.Vm, source)) p.Stale = true;
            Navigate(_current);
        };

        ApplyCulture();
        (Loc.I.IsGerman ? LangDe : LangEn).IsChecked = true;
        Loc.I.Changed += () =>
        {
            ApplyCulture();
            S.RefreshTexts();
            UpdateGameStatus();
            foreach (var p in _pages.Values) p.Stale = true;
            Navigate(_current);
        };

        _gameTimer.Tick += (_, _) => UpdateGameStatus();
        _gameTimer.Start();
        UpdateGameStatus();
        StateChanged += (_, _) => UpdateMaximizeState();

        Loaded += (_, _) => AutoLoad();
    }

    /// <summary>Opens the most recently played slot on start.</summary>
    void AutoLoad()
    {
        if (!Directory.Exists(GamePaths.SaveDir))
        {
            S.Status = Loc.F("Save folder not found: {0}", GamePaths.SaveDir);
            Navigate(NavDashboard);
            return;
        }
        int? latest = Enumerable.Range(0, GamePaths.SlotCount)
            .Where(SaveSlot.Exists)
            .OrderByDescending(i => File.GetLastWriteTime(GamePaths.WorldPath(i)))
            .Cast<int?>()
            .FirstOrDefault();
        if (latest is int slot) TryLoad(slot);
        else Navigate(NavDashboard);
    }

    void TryLoad(int slot)
    {
        try
        {
            S.LoadSlot(slot);
        }
        catch (Exception ex)
        {
            Dialog.Show(this, Loc.F("Slot {0} could not be loaded", slot + 1), ex.Message, "OK");
            Navigate(NavDashboard);
        }
    }

    void Navigate(RadioButton nav)
    {
        if (_pages[_current].Vm is TimelapseVm tl && nav != _current) tl.Stop();
        _current = nav;
        var page = _pages[nav];
        if (page.NeedsSlot && !S.HasSlot)
        {
            NavDashboard.IsChecked = true;
            return;
        }
        if (page.Stale)
        {
            if (page.Vm is TimelapseVm old) old.Stop();
            page.Vm = page.Create();
            page.Stale = false;
        }
        PageHost.Content = page.Vm;
    }

    void Save()
    {
        if (!S.HasSlot) return;
        if (GamePaths.IsGameRunning() && !Dialog.Confirm(this, Loc.T("CRACK is still running"),
                Loc.T("While the game runs it overwrites the save the next time it saves or quits. Close CRACK first (or reload from the main menu afterwards)."),
                Loc.T("Save anyway")))
            return;

        string body = S.IsDirty
            ? Loc.F("{0} change(s) will be written to slot {1}. A backup is created first.", S.Changes.Count, S.Slot!.Index + 1)
            : Loc.T("There are no changes. Save again anyway?");
        if (S.Slot!.Ranked == true)
            body += "\n\n" + Loc.T("The run will be marked unranked and no longer counts for the Steam leaderboards.");
        if (Dialog.ShowChanges(this, Loc.T("Save game?"), body, S.Changes.ToList(), Loc.T("Cancel"), Loc.T("Save")) != Loc.T("Save"))
            return;

        try
        {
            S.Save();
            foreach (var p in _pages.Values) p.Stale = true;
            Navigate(_current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialog.Show(this, Loc.T("Saving failed"), ex.Message, "OK");
        }
    }

    void Reload()
    {
        if (S.IsDirty && !Dialog.Confirm(this, Loc.T("Discard changes?"), Loc.F("{0} unsaved change(s) will be lost.", S.Changes.Count), Loc.T("Discard")))
            return;
        TryLoad(S.Slot!.Index);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (S.IsDirty)
        {
            string save = Loc.T("Save"), cancel = Loc.T("Cancel");
            string? r = Dialog.Show(this, Loc.T("Unsaved changes"), Loc.F("There are {0} unsaved change(s).", S.Changes.Count),
                cancel, Loc.T("Discard"), save);
            if (r == save) Save();
            if (r == null || r == cancel || (r == save && S.IsDirty)) e.Cancel = true;
        }
        base.OnClosing(e);
    }

    void UpdateGameStatus()
    {
        bool running = GamePaths.IsGameRunning();
        GameDot.Fill = (System.Windows.Media.Brush)FindResource(running ? "Danger" : "Success");
        GameText.Text = running ? Loc.T("CRACK is running") : Loc.T("Game closed");
        GameHint.Text = running ? Loc.T("Close the game first, then save.") : Loc.T("Safe to save.");
    }

    void UpdateMaximizeState()
    {
        // With WindowChrome a maximized window overhangs the screen by the resize border.
        Frame.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(1);
        Frame.BorderBrush = WindowState == WindowState.Maximized ? Background : (System.Windows.Media.Brush)FindResource("Line");
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    /// <summary>Renders every page to PNG files (used with "--shot dir" for visual checks), then closes.</summary>
    public async Task RenderAllPagesAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var nav in _pages.Keys.ToList())
        {
            if (!nav.IsEnabled) continue;
            nav.IsChecked = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(400);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(this);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(dir, $"{++n:00}_{nav.Name}.png"));
            enc.Save(fs);
        }
    }

    void Language_Checked(object sender, RoutedEventArgs e) =>
        Loc.I.SetLanguage(ReferenceEquals(sender, LangDe) ? "de" : "en");

    /// <summary>Number/date formatting follows the UI language ("$1,234.56" vs "$1.234,56").</summary>
    void ApplyCulture()
    {
        var culture = Loc.I.Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
