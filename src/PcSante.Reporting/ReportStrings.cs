using System.Globalization;
using System.Resources;

namespace PcSante.Reporting;

/// <summary>
/// Textes des rapports hors de l'interface (rapport mensuel produit par le service), lus dans les mêmes
/// fichiers de traduction que l'interface (fr par défaut, en, ar).
/// </summary>
public static class ReportStrings
{
    private static readonly ResourceManager Resources = new("PcSante.Reporting.Resources.Strings", typeof(ReportStrings).Assembly);

    /// <summary>Culture d'un code de langue de l'application ; français si le code est inconnu.</summary>
    public static CultureInfo CultureOf(string? language) => language switch
    {
        "en" => CultureInfo.GetCultureInfo("en"),
        "ar" => CultureInfo.GetCultureInfo("ar"),
        _ => CultureInfo.GetCultureInfo("fr"),
    };

    /// <summary>Traduction d'une clé ; la clé elle-même si elle est absente (jamais d'exception dans un rapport).</summary>
    public static Func<string, string> For(CultureInfo culture) =>
        key => Resources.GetString(key, culture) ?? key;
}
