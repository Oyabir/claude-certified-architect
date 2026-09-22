using PcSante.Core.Windows;

namespace PcSante.Ipc.Security;

/// <summary>Faits observés sur le processus qui se connecte au named pipe.</summary>
public sealed record ClientProcessFacts(int ProcessId, string? ExecutablePath, string? UserName, string? UserSid);

public enum TrustDecision
{
    Trusted,
    UnknownProcess,
    OutsideInstallDirectory,
    NotAnApplicationBinary,
    Unsigned,
    SignerMismatch,
}

/// <summary>
/// Politique de confiance du named pipe (section 5) : seuls les exécutables de l'application,
/// installés dans le dossier protégé et signés par le même certificat que le service, sont acceptés.
/// </summary>
public sealed class ClientTrustPolicy
{
    private readonly string _installDirectory;
    private readonly IReadOnlySet<string> _allowedExecutables;
    private readonly bool _requireSignature;

    /// <param name="installDirectory">Dossier d'installation (Program Files, modifiable seulement par un administrateur).</param>
    /// <param name="allowedExecutables">Noms de fichiers autorisés (ex. PcSante.exe).</param>
    /// <param name="requireSignature">Toujours vrai en Release.</param>
    public ClientTrustPolicy(string installDirectory, IEnumerable<string> allowedExecutables, bool requireSignature)
    {
        ArgumentException.ThrowIfNullOrEmpty(installDirectory);
        _installDirectory = Path.TrimEndingDirectorySeparator(installDirectory);
        _allowedExecutables = new HashSet<string>(allowedExecutables, StringComparer.OrdinalIgnoreCase);
        _requireSignature = requireSignature;
    }

    public static IReadOnlyList<string> ApplicationExecutables { get; } =
        ["PcSante.exe", "PcSante.Overlay.exe", "PcSante.Service.exe"];

    public TrustDecision Evaluate(ClientProcessFacts client, SignatureInfo clientSignature, SignatureInfo serviceSignature)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clientSignature);
        ArgumentNullException.ThrowIfNull(serviceSignature);

        if (string.IsNullOrEmpty(client.ExecutablePath))
        {
            return TrustDecision.UnknownProcess;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(client.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return TrustDecision.UnknownProcess;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !string.Equals(Path.TrimEndingDirectorySeparator(directory), _installDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return TrustDecision.OutsideInstallDirectory;
        }

        if (!_allowedExecutables.Contains(Path.GetFileName(fullPath)))
        {
            return TrustDecision.NotAnApplicationBinary;
        }

        if (!_requireSignature)
        {
            return TrustDecision.Trusted;
        }

        if (!clientSignature.IsSigned || !clientSignature.IsTrusted || string.IsNullOrEmpty(clientSignature.Thumbprint))
        {
            return TrustDecision.Unsigned;
        }

        if (!serviceSignature.IsSigned || string.IsNullOrEmpty(serviceSignature.Thumbprint)
            || !string.Equals(clientSignature.Thumbprint, serviceSignature.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return TrustDecision.SignerMismatch;
        }

        return TrustDecision.Trusted;
    }
}
