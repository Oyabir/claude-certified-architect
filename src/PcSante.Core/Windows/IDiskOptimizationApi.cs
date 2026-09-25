namespace PcSante.Core.Windows;

public enum DiskMediaType
{
    Unknown,
    Hdd,
    Ssd,
}

/// <summary>Disque système (M4) : type de support et gestion du fichier d'échange.</summary>
public sealed record DiskOptimizationInfo(string Drive, DiskMediaType MediaType, bool PageFileAutomatic, long? PageFileSizeMb);

public interface IDiskOptimizationApi
{
    Task<DiskOptimizationInfo?> GetSystemDiskAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Optimisation adaptée au support par l'outil de Windows (defrag /O) : TRIM pour un SSD, défragmentation
    /// pour un disque dur. Peut durer longtemps sur un disque dur.
    /// </summary>
    Task<bool> OptimizeSystemDriveAsync(CancellationToken cancellationToken);

    /// <summary>Gestion automatique du fichier d'échange par Windows (effet au redémarrage).</summary>
    Task<bool> SetPageFileAutomaticAsync(bool automatic, CancellationToken cancellationToken);
}
