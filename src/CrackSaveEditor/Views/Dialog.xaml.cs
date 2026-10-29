using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.Views;

/// <summary>Dark replacement for MessageBox. The last button is the primary (accent) one.</summary>
public partial class Dialog : Window
{
    string? _result;

    Dialog(string title, string body, IEnumerable? changes, string[] buttons)
    {
        InitializeComponent();
        Language = XmlLanguage.GetLanguage(Loc.I.Culture.IetfLanguageTag);
        TitleText.Text = title;
        BodyText.Text = body;
        if (changes != null)
        {
            ChangeList.ItemsSource = changes;
            ChangeList.Visibility = Visibility.Visible;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            string label = buttons[i];
            bool primary = i == buttons.Length - 1;
            var b = new Button
            {
                Content = label,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 90,
                IsDefault = primary,
                IsCancel = i == 0 && buttons.Length > 1,
            };
            if (primary) b.Style = (Style)FindResource("AccentButton");
            b.Click += (_, _) => { _result = label; Close(); };
            Buttons.Children.Add(b);
        }
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    public static string? Show(Window? owner, string title, string body, params string[] buttons) =>
        ShowCore(owner, title, body, null, buttons);

    public static bool Confirm(Window? owner, string title, string body, string? yes = null) =>
        ShowCore(owner, title, body, null, [Loc.T("Cancel"), yes ??= Loc.T("Continue")]) == yes;

    public static string? ShowChanges(Window? owner, string title, string body, IEnumerable changes, params string[] buttons) =>
        ShowCore(owner, title, body, changes, buttons);

    static string? ShowCore(Window? owner, string title, string body, IEnumerable? changes, string[] buttons)
    {
        var d = new Dialog(title, body, changes, buttons.Length == 0 ? ["OK"] : buttons);
        if (owner is { IsLoaded: true }) d.Owner = owner;
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d._result;
    }
}
