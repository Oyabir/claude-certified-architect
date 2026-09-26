using System.Reflection;

namespace PcSante.Core;

/// <summary>
/// Identité du produit, lue depuis les métadonnées injectées par <c>branding.props</c>.
/// Aucun nom de produit n'est écrit en dur ailleurs dans le code.
/// </summary>
public static class ProductInfo
{
    private static readonly Assembly Source = typeof(ProductInfo).Assembly;

    public static string Name { get; } =
        Source.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PC Santé";

    public static string TechnicalName { get; } = Metadata("PcSante.TechnicalName") ?? "PcSante";

    public static string Version { get; } =
        Source.GetName().Version?.ToString(3) ?? "1.0.0";

    public static string LicenseServerUrl { get; } = Metadata("PcSante.LicenseServerUrl") ?? string.Empty;

    /// <summary>Adresse de la console PME ; vide si l'offre PME n'est pas configurée (fonction masquée).</summary>
    public static string ConsoleUrl { get; } = Metadata("PcSante.ConsoleUrl") ?? string.Empty;

    /// <summary>Clé publique Ed25519 (base64). Vide si la licence n'est pas configurée.</summary>
    public static string LicensePublicKey { get; } = Metadata("PcSante.LicensePublicKey") ?? string.Empty;

    /// <summary>Nom du named pipe du service (versionné pour permettre l'évolution du contrat).</summary>
    public static string PipeName => $"{TechnicalName}.Service.v1";

    /// <summary>Nom du service Windows.</summary>
    public static string ServiceName => $"{TechnicalName}Service";

    private static string? Metadata(string key) =>
        Source.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value ? value : null;
}
