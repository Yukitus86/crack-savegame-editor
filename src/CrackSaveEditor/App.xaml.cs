using System.Windows;
using System.Windows.Threading;

namespace CrackSaveEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            Shutdown(SelfTest.Run(e.Args));
            return;
        }

        // "--savedir <path>" works on another save folder (e.g. a copy or a demo save).
        int dir = Array.IndexOf(e.Args, "--savedir");
        if (dir >= 0 && dir + 1 < e.Args.Length) Core.GamePaths.SaveDir = e.Args[dir + 1];

        // "--lang en|de" switches the UI language for this run only (used for README screenshots).
        int lang = Array.IndexOf(e.Args, "--lang");
        if (lang >= 0 && lang + 1 < e.Args.Length) Core.Loc.I.SetLanguage(e.Args[lang + 1], persist: false);

        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();

        int shot = Array.IndexOf(e.Args, "--shot");
        if (shot >= 0 && shot + 1 < e.Args.Length)
            window.ContentRendered += async (_, _) =>
            {
                // "--god" previews the quick actions in memory (nothing is written).
                if (e.Args.Contains("--god") && Core.Session.Current.HasSlot)
                {
                    Core.Cheats.AddMoney(Core.Cheats.MaxBalanceCents);
                    Core.Cheats.RepairTools();
                    Core.Cheats.MaxScanner();
                    Core.Cheats.MaxBots();
                    Core.Cheats.UnlockTerminals();
                    Core.Cheats.CalmEgg();
                    Core.Session.Current.NotifyDataChanged();
                }
                await window.RenderAllPagesAsync(e.Args[shot + 1]);
                Shutdown();
            };
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Views.Dialog.Show(MainWindow, Core.Loc.T("Unexpected error"), e.Exception.Message, "OK");
    }
}
