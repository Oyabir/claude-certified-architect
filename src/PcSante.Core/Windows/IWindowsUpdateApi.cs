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
    /// vers <paramref name="backupPath"/> (sauvegarde), redémarre les services.
    /// </summary>
    Task<bool> ResetComponentsAsync(string backupPath, CancellationToken cancellationToken);

    /// <summary>Chemin proposé pour la sauvegarde du cache (ex. C:\Windows\SoftwareDistribution.pcsante-20260922).</summary>
    string ProposeBackupPath(DateTimeOffset now);

    /// <summary>Remet en place le cache sauvegardé par <see cref="ResetComponentsAsync"/>.</summary>
    Task<bool> RestoreComponentsAsync(string backupPath, CancellationToken cancellationToken);

    Task<bool> AreServicesHealthyAsync(CancellationToken cancellationToken);
}
