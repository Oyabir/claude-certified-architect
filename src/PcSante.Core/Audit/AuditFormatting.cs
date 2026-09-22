using PcSante.Core.Commands;
using PcSante.Core.Licensing;

namespace PcSante.Core.Audit;

public static class AuditFormatting
{
    /// <summary>Paramètres lisibles pour l'audit ; une clé de licence n'y apparaît que sous forme d'indice.</summary>
    public static string? FormatParameters(CommandParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Values.Count == 0)
        {
            return null;
        }

        return string.Join(", ", parameters.Values
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={Mask(kv.Key, kv.Value)}"));
    }

    private static string Mask(string name, string value) =>
        name == "key" && LicenseKeyFormat.Normalize(value) is { } key ? LicenseKeyFormat.Hint(key) : value;
}
