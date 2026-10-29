using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute) : this(_ => execute()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}

public static class Fmt
{
    public static CultureInfo Culture => Loc.I.Culture;

    public static string Money(long cents) => Cheats.Money(cents);

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{t.Minutes} min {t.Seconds:00} s";
    }

    public static BitmapImage? Thumbnail(string? path, int width = 480)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; // do not keep the game's file locked
            img.DecodePixelWidth = width;
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}

/// <summary>View model bound to a JSON object of the save; every setter records the edit in the session.</summary>
public abstract class NodeVm(JsonObject node, string area) : ObservableObject
{
    protected JsonObject Node { get; } = node;
    protected string Area { get; } = area;
    protected static Session S => Session.Current;

    protected void SetInt(string key, int value, string label, [CallerMemberName] string? prop = null)
    {
        int old = Node.Int(key);
        if (old == value) return;
        Node[key] = value;
        S.Track(Area, label, old, value);
        Raise(prop);
    }

    protected void SetDouble(string key, double value, string label, [CallerMemberName] string? prop = null)
    {
        double old = Node.Num(key);
        if (Math.Abs(old - value) < 1e-9) return;
        Node[key] = value;
        S.Track(Area, label, old, value);
        Raise(prop);
    }

    protected void SetBool(string key, bool value, string label, [CallerMemberName] string? prop = null)
    {
        bool old = Node.Bool(key);
        if (old == value) return;
        Node[key] = value;
        S.Track(Area, label, old, value);
        Raise(prop);
    }

    protected void SetString(string key, string value, string label, [CallerMemberName] string? prop = null)
    {
        string old = Node.Str(key);
        if (old == value) return;
        Node[key] = value;
        S.Track(Area, label, old, value);
        Raise(prop);
    }
}
