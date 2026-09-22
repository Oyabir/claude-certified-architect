using System.Globalization;
using System.Text.RegularExpressions;

namespace PcSante.Core.Commands;

public sealed record ValidationOutcome(bool IsValid, CommandDescriptor? Descriptor, CommandParameters? Parameters, FailureReason Reason, string? Details)
{
    public static ValidationOutcome Invalid(FailureReason reason, string details, CommandDescriptor? d = null) =>
        new(false, d, null, reason, details);
}

/// <summary>
/// Première barrière de sécurité du service : nom de commande, paramètres et confirmation.
/// Tout ce qui n'est pas explicitement autorisé est refusé.
/// </summary>
public static partial class CommandValidator
{
    public const int MaxParameterLength = 256;
    public const int MaxParameters = 8;

    [GeneratedRegex(@"^[\p{L}\p{N} ._\-{}:\\/|()]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"^[1-9]\d{0,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex PositiveIntRegex();

    /// <param name="commandName">Nom textuel reçu du client (jamais un nombre).</param>
    /// <param name="rawParameters">Paramètres bruts reçus.</param>
    /// <param name="confirmed">Le client affirme que l'utilisateur a confirmé.</param>
    /// <param name="pathExists">Vérification d'existence pour les chemins (injectée pour les tests).</param>
    public static ValidationOutcome Validate(
        string? commandName,
        IReadOnlyDictionary<string, string>? rawParameters,
        bool confirmed,
        Func<string, bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(pathExists);

        if (string.IsNullOrWhiteSpace(commandName)
            || commandName.Length > 64
            || !commandName.All(char.IsAsciiLetter)
            || !Enum.TryParse<CommandId>(commandName, ignoreCase: false, out var id)
            || !Enum.IsDefined(id)
            || !CommandDefinitions.TryGet(id, out var descriptor))
        {
            return ValidationOutcome.Invalid(FailureReason.NotInCatalog, $"Commande inconnue : « {Truncate(commandName)} »");
        }

        var raw = rawParameters ?? new Dictionary<string, string>();
        if (raw.Count > MaxParameters)
        {
            return ValidationOutcome.Invalid(FailureReason.InvalidParameters, "Trop de paramètres", descriptor);
        }

        foreach (var name in raw.Keys)
        {
            if (!descriptor.Parameters.Any(p => p.Name == name))
            {
                return ValidationOutcome.Invalid(FailureReason.InvalidParameters, $"Paramètre non autorisé : {Truncate(name)}", descriptor);
            }
        }

        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in descriptor.Parameters)
        {
            if (!raw.TryGetValue(spec.Name, out var value))
            {
                if (spec.Required)
                {
                    return ValidationOutcome.Invalid(FailureReason.InvalidParameters, $"Paramètre manquant : {spec.Name}", descriptor);
                }

                continue;
            }

            if (value is null || value.Length > MaxParameterLength || !IsValid(spec, value, pathExists))
            {
                return ValidationOutcome.Invalid(FailureReason.InvalidParameters, $"Valeur invalide pour {spec.Name}", descriptor);
            }

            accepted[spec.Name] = value;
        }

        if (descriptor.RequiresConfirmation && !confirmed)
        {
            return ValidationOutcome.Invalid(FailureReason.ConfirmationRequired, "Confirmation de l'utilisateur requise", descriptor);
        }

        return new ValidationOutcome(true, descriptor, new CommandParameters(accepted), FailureReason.None, null);
    }

    internal static bool IsValid(ParameterSpec spec, string value, Func<string, bool> pathExists) => spec.Type switch
    {
        ParameterType.PositiveInteger => PositiveIntRegex().IsMatch(value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _),
        ParameterType.Choice => spec.AllowedValues?.Contains(value, StringComparer.Ordinal) == true,
        ParameterType.Identifier => IdentifierRegex().IsMatch(value) && !value.Contains("..", StringComparison.Ordinal),
        ParameterType.LocalPath => IsSafeLocalPath(value) && pathExists(value),
        ParameterType.LicenseKey => Licensing.LicenseKeyFormat.IsWellFormed(value),
        ParameterType.TimeOfDay => TimeRegex().IsMatch(value),
        ParameterType.Boolean => value is "true" or "false",
        ParameterType.Guid => Guid.TryParseExact(value, "D", out _),
        _ => false,
    };

    /// <summary>Chemin absolu sur un lecteur local : pas d'UNC, pas de périphérique, pas de remontée.</summary>
    internal static bool IsSafeLocalPath(string path)
    {
        if (path.Length < 3 || path.Length > 240)
        {
            return false;
        }

        var drive = char.ToUpperInvariant(path[0]);
        if (drive is < 'A' or > 'Z' || path[1] != ':' || path[2] != '\\')
        {
            return false;
        }

        if (path.Contains("..", StringComparison.Ordinal)
            || path.IndexOfAny(['*', '?', '"', '<', '>', '|', '\0']) >= 0
            || path.IndexOf(':', 2) >= 0)
        {
            return false;
        }

        return true;
    }

    private static string Truncate(string? s) => s is null ? "(vide)" : s.Length <= 40 ? s : s[..40] + "…";
}
