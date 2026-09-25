using System.Globalization;
using System.Resources;

namespace PcSante.PmeConsole.Services;

/// <summary>Textes des problèmes des postes, lus dans les fichiers de traduction de l'application (fr, en, ar).</summary>
public static class ConsoleStrings
{
    private static readonly ResourceManager Resources = new("PcSante.PmeConsole.Resources.Strings", typeof(ConsoleStrings).Assembly);

    public static CultureInfo CultureOf(string? language) => language switch
    {
        "en" => CultureInfo.GetCultureInfo("en"),
        "ar" => CultureInfo.GetCultureInfo("ar"),
        _ => CultureInfo.GetCultureInfo("fr"),
    };

    public static string Text(string key, CultureInfo culture, params object[] args)
    {
        var format = Resources.GetString(key, culture) ?? key;
        try
        {
            return args.Length == 0 ? format : string.Format(culture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
