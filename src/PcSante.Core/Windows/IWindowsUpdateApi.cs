namespace PcSante.Core.Windows;

public sealed record PendingUpdate(string Id, string Title, bool IsImportant, long SizeBytes);

public sealed record UpdateStatus(DateTimeOffset? LastSearchAt, DateTimeOffset? LastInstallAt, bool RebootRequired, bool ServiceRunning);

public sealed record UpdateInstallResult(int Installed, int Failed, bool RebootRequired);

/// <summary>Windows Update Agent (WUApiLib) et services associés.</summary>
public interface IWindowsUpdateApi
{
    Task<UpdateStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingUpdate>> SearchAsync(CancellationToken cancellationToken);

    Task<UpdateInstallResult> InstallAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Réparation d'une mise à jour bloquée : arrête les services, renomme le cache de téléchargement
    /// (sauvegarde), redémarre les services. Renvoie le chemin de la sauvegarde, ou null en cas d'échec.
    /// </summary>
    Task<string?> ResetComponentsAsync(CancellationToken cancellationToken);

    /// <summary>Remet en place le cache sauvegardé par <see cref="ResetComponentsAsync"/>.</summary>
    Task<bool> RestoreComponentsAsync(string backupPath, CancellationToken cancellationToken);

    Task<bool> AreServicesHealthyAsync(CancellationToken cancellationToken);
}
