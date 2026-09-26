using System.Globalization;
using System.Text;
using PcSante.Core.Reporting;

namespace PcSante.Reporting;

/// <summary>
/// Export CSV du rapport (M8, pour les PME) : une ligne par score, action et menace de la période.
/// Séparateur « ; » et UTF-8 avec BOM (ouverture directe dans Excel en français) ; cellules protégées contre
/// l'injection de formules (=, +, -, @ en tête).
/// </summary>
public static class ReportCsvBuilder
{
    public static string Build(ReportData data, Func<string, string> translate, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(translate);
        ArgumentNullException.ThrowIfNull(culture);
        var csv = new StringBuilder();
        Line(csv, translate("Csv_Type"), translate("Csv_Date"), translate("Csv_Item"), translate("Csv_Value"), translate("Csv_Detail"));

        foreach (var point in data.ScoreHistory.OrderBy(p => p.At))
        {
            Line(csv, translate("Csv_Score"), Date(point.At, culture), data.MachineName, point.Score.ToString(culture),
                string.Format(culture, translate("Csv_SubScores"), point.SubScores.Security, point.SubScores.Performance,
                    point.SubScores.Stability, point.SubScores.Storage));
        }

        foreach (var action in data.Actions.OrderBy(a => a.Timestamp))
        {
            Line(csv, translate("Csv_Action"), Date(action.Timestamp, culture), ReportPdfBuilder.CommandLabel(action.Command, translate),
                translate($"Outcome_{action.Outcome}"), action.Who);
        }

        foreach (var threat in data.Threats.OrderBy(t => t.DetectedAt))
        {
            Line(csv, translate("Csv_Threat"), Date(threat.DetectedAt, culture), threat.Name, threat.Status, threat.Resource ?? string.Empty);
        }

        return csv.ToString();
    }

    /// <summary>Contenu du fichier : UTF-8 avec BOM, pour qu'Excel reconnaisse les accents et l'arabe.</summary>
    public static byte[] ToFileBytes(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    /// <summary>Cellule CSV : guillemets doublés, et apostrophe devant une formule potentielle.</summary>
    internal static string Cell(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string Date(DateTimeOffset at, CultureInfo culture) => at.ToLocalTime().ToString("g", culture);

    private static void Line(StringBuilder csv, params string[] cells) => csv.Append(string.Join(';', cells.Select(Cell))).Append("\r\n");
}
