using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace PcSante.App.Localization;

/// <summary>
/// Accès aux textes de l'interface (Resources/Strings*.resx). Aucun texte affiché n'est écrit en dur dans le code.
/// Français par défaut, anglais et arabe (droite à gauche).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("PcSante.App.Resources.Strings", typeof(Loc).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("fr-FR");

    public static bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;

    public static void SetLanguage(string language)
    {
        Culture = language switch
        {
            "en" => CultureInfo.GetCultureInfo("en-US"),
            "ar" => CultureInfo.GetCultureInfo("ar-MA"),
            _ => CultureInfo.GetCultureInfo("fr-FR"),
        };
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    public static string T(string key) => Resources.GetString(key, Culture) ?? key;

    public static string F(string key, params object?[] args)
    {
        try
        {
            return args.Length == 0 ? T(key) : string.Format(Culture, T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }

    public static string Bytes(long bytes)
    {
        string[] units = [T("Unit_B"), T("Unit_KB"), T("Unit_MB"), T("Unit_GB"), T("Unit_TB")];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(Culture, "{0:0.#} {1}", value, units[unit]);
    }

    /// <summary>Heure sans secondes (« 15:20 »).</summary>
    public static string Time(DateTimeOffset date) => date.ToLocalTime().ToString("t", Culture);

    /// <summary>Date relative : « aujourd'hui à 15:20 », « hier à 22:20 », sinon « 24/09/2026 à 15:20 ».</summary>
    public static string When(DateTimeOffset? date)
    {
        if (date is not { } d || d.Year <= 1990)
        {
            return T("Common_Never");
        }

        var local = d.ToLocalTime();
        var today = DateTime.Today;
        return local.Date == today ? F("Date_TodayAt", Time(d))
            : local.Date == today.AddDays(-1) ? F("Date_YesterdayAt", Time(d))
            : F("Date_DayAt", local.ToString("d", Culture), Time(d));
    }

    /// <summary>Première lettre en majuscule (début de ligne : « Aujourd'hui à 12:06 »).</summary>
    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Culture) + text[1..];

    /// <summary>Nombre décimal transmis en format invariant (« 28.7 ») → format de la langue (« 28,7 »).</summary>
    public static object Number(string value) =>
        value.Contains('.', StringComparison.Ordinal)
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n.ToString("0.#", Culture)
            : value;

    public static string Date(DateTimeOffset? date) =>
        date is { } d && d.Year > 1990 ? d.ToLocalTime().ToString("g", Culture) : T("Common_Never");
}

/// <summary>Extension XAML : <c>Text="{l:T Home_Title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
