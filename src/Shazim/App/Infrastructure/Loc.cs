using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Shazim.App.Infrastructure;

/// <summary>
/// UI translations. French is the reference language and the fallback for any missing key.
/// Strings live in Strings/{code}.json (embedded). Switching language updates every
/// {infra:T key} binding at once.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static readonly IReadOnlyList<(string Code, string Name)> Languages = new[]
    {
        ("fr", "Français"),
        ("en", "English"),
        ("es", "Español"),
        ("de", "Deutsch"),
    };

    private readonly Dictionary<string, string> _fallback;
    private Dictionary<string, string> _current;

    private Loc()
    {
        _fallback = Load("fr");
        _current = _fallback;
        Culture = CultureInfo.GetCultureInfo("fr-FR");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (code-built texts should be rebuilt).</summary>
    public event EventHandler? Changed;

    public string Code { get; private set; } = "fr";
    public CultureInfo Culture { get; private set; }

    public string this[string key] => TryGet(key) ?? key;

    public string? TryGet(string key) =>
        _current.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : null;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    /// <summary>Best match for Windows' display language, among the supported ones.</summary>
    public static string SystemDefault()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    public void SetLanguage(string? code)
    {
        code = Languages.Any(l => l.Code == code) ? code! : SystemDefault();
        Code = code;
        _current = code == "fr" ? _fallback : Load(code);
        Culture = CultureInfo.GetCultureInfo(code switch { "en" => "en-GB", "es" => "es-ES", "de" => "de-DE", _ => "fr-FR" });
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, string> Load(string code)
    {
        var name = $"Shazim.App.Strings.{code}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return new();
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? new();
    }
}

/// <summary>XAML: Text="{infra:T home.tap}" — follows the selected language live.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
