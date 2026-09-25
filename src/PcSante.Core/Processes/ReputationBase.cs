using System.Text.Json;

namespace PcSante.Core.Processes;

/// <summary>
/// Base de réputation enrichie (M3, V2) : fichier de données embarqué (ReputationBase.json), modifiable sans toucher
/// au code. Associe les programmes courants à une catégorie et à une courte description en langage simple.
/// </summary>
public static class ReputationBase
{
    private sealed record Data(Dictionary<string, string> Unnecessary, Dictionary<string, string> Useful);

    private static readonly Data Base = Load();

    /// <summary>Programmes dont le lancement permanent est rarement utile (mises à jour, assistants).</summary>
    public static IReadOnlySet<string> Unnecessary { get; } = new HashSet<string>(Base.Unnecessary.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Programmes courants et légitimes.</summary>
    public static IReadOnlySet<string> Useful { get; } = new HashSet<string>(Base.Useful.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Toutes les clés de description (pour vérifier les traductions).</summary>
    public static IReadOnlySet<string> DescriptionKeys { get; } = Base.Unnecessary.Values.Concat(Base.Useful.Values).ToHashSet(StringComparer.Ordinal);

    /// <summary>Clé de description (Proc_&lt;clé&gt;) d'un processus connu, ou null.</summary>
    public static string? DescriptionKeyOf(string processName)
    {
        ArgumentNullException.ThrowIfNull(processName);
        var name = processName.Trim();
        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return Base.Unnecessary.GetValueOrDefault(name) ?? Base.Useful.GetValueOrDefault(name);
    }

    private static Data Load()
    {
        using var stream = typeof(ReputationBase).Assembly.GetManifestResourceStream("PcSante.Core.Processes.ReputationBase.json")
            ?? throw new InvalidOperationException("Base de réputation absente.");
        var data = JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Base de réputation illisible.");
        return new Data(
            new Dictionary<string, string>(data.Unnecessary, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(data.Useful, StringComparer.OrdinalIgnoreCase));
    }
}
