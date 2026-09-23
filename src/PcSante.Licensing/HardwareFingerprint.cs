using System.Security.Cryptography;
using System.Text;

namespace PcSante.Licensing;

/// <summary>Valeurs matérielles brutes. Elles ne quittent JAMAIS le PC : seuls leurs hachages sont transmis.</summary>
public sealed record HardwareIdentity(string? MotherboardId, string? SystemDiskSerial, string? ProcessorId, string? MachineGuid);

/// <summary>Lecture des identifiants matériels (implémentation WMI/registre dans PcSante.WindowsApi).</summary>
public interface IHardwareInfoProvider
{
    HardwareIdentity Read();
}

/// <summary>
/// Empreinte matérielle (section 12) : 4 hachages SHA-256 salés (carte mère, disque système, processeur, MachineGuid).
/// L'empreinte reste valide si au moins 3 éléments sur 4 correspondent.
/// </summary>
public sealed record HardwareFingerprint
{
    public const int ComponentCount = 4;
    public const int RequiredMatches = 3;
    private const string Salt = "PcSante.Fingerprint.v1";
    private static readonly string[] ComponentNames = ["board", "disk", "cpu", "machine"];

    public HardwareFingerprint(IReadOnlyList<string> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count != ComponentCount)
        {
            throw new ArgumentException("L'empreinte doit contenir 4 éléments.", nameof(components));
        }

        Components = components.ToArray();
    }

    /// <summary>Hachages hexadécimaux (64 caractères) ; chaîne vide si l'élément est illisible.</summary>
    public IReadOnlyList<string> Components { get; }

    public static HardwareFingerprint Compute(HardwareIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string[] raw = [identity.MotherboardId ?? string.Empty, identity.SystemDiskSerial ?? string.Empty,
            identity.ProcessorId ?? string.Empty, identity.MachineGuid ?? string.Empty];
        var hashes = new string[ComponentCount];
        for (var i = 0; i < ComponentCount; i++)
        {
            hashes[i] = HashComponent(ComponentNames[i], raw[i]);
        }

        return new HardwareFingerprint(hashes);
    }

    /// <summary>Nombre d'éléments identiques (un élément vide ne correspond jamais).</summary>
    public int CountMatches(HardwareFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var matches = 0;
        for (var i = 0; i < ComponentCount; i++)
        {
            if (Components[i].Length > 0 && string.Equals(Components[i], other.Components[i], StringComparison.OrdinalIgnoreCase))
            {
                matches++;
            }
        }

        return matches;
    }

    public bool Matches(HardwareFingerprint other) => CountMatches(other) >= RequiredMatches;

    public static bool IsWellFormed(IReadOnlyList<string>? components) =>
        components is { Count: ComponentCount }
        && components.All(c => c is not null && (c.Length == 0 || (c.Length == 64 && c.All(Uri.IsHexDigit))));

    public bool Equals(HardwareFingerprint? other) =>
        other is not null && Components.SequenceEqual(other.Components, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => string.Join('|', Components).ToUpperInvariant().GetHashCode(StringComparison.Ordinal);

    internal static string HashComponent(string name, string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0 || IsPlaceholder(normalized))
        {
            return string.Empty;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Salt}|{name}|{normalized}")));
    }

    private static string Normalize(string value) =>
        new(value.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Valeurs génériques renvoyées par certains fabricants : inutilisables pour identifier un PC.</summary>
    private static bool IsPlaceholder(string normalized) => normalized is "TOBEFILLEDBYO.E.M." or "DEFAULTSTRING"
        or "NONE" or "0" or "00000000" or "0000000000000000" or "NOTAPPLICABLE" or "SYSTEMSERIALNUMBER" or "123456789";
}
