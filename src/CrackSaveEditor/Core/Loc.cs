using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Data;
using System.Windows.Markup;

namespace CrackSaveEditor.Core;

/// <summary>
/// UI language. English text is the key; <see cref="Strings.German"/> maps it to German.
/// XAML uses <c>{l:T 'English text'}</c>, code uses <see cref="T"/> / <see cref="F"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrackSaveEditor", "editor.json");

    Loc()
    {
        try
        {
            if (File.Exists(ConfigPath) && JsonNode.Parse(File.ReadAllText(ConfigPath)) is JsonObject cfg)
                Lang = cfg.Str("language", "en") == "de" ? "de" : "en";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (pages rebuild their view models).</summary>
    public event Action? Changed;

    public string Lang { get; private set; } = "en";
    public bool IsGerman => Lang == "de";
    public CultureInfo Culture => CultureInfo.GetCultureInfo(IsGerman ? "de-DE" : "en-US");

    public string this[string key] => T(key);

    public static string T(string english) =>
        I.IsGerman && Strings.German.TryGetValue(english, out var de) ? de : english;

    public static string F(string english, params object?[] args) => string.Format(I.Culture, T(english), args);

    public void SetLanguage(string lang, bool persist = true)
    {
        lang = lang == "de" ? "de" : "en";
        if (lang == Lang) return;
        Lang = lang;
        if (persist)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, new JsonObject { ["language"] = lang }.ToJsonString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGerman)));
        Changed?.Invoke();
    }
}

/// <summary>XAML: <c>Text="{l:T 'Save'}"</c> – a live binding that follows language switches.</summary>
/// <remarks>Binds to <see cref="Loc.Lang"/> and translates in a converter; an indexer path would split keys at commas.</remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Loc.Lang)) { Source = Loc.I, Mode = BindingMode.OneWay, Converter = Translate.Instance, ConverterParameter = Key }
            .ProvideValue(serviceProvider);

    sealed class Translate : IValueConverter
    {
        public static readonly Translate Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.T((string)parameter);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
