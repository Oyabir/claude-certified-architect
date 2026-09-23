namespace PcSante.Core.Windows;

public enum CleanupTarget
{
    TemporaryFiles,
    WindowsUpdateCache,
    RecycleBin,
    BrowserCaches,
}

public sealed record CleanupEstimate(long TemporaryFilesBytes, long WindowsUpdateCacheBytes, long RecycleBinBytes, long BrowserCachesBytes)
{
    public long TotalBytes => TemporaryFilesBytes + WindowsUpdateCacheBytes + RecycleBinBytes + BrowserCachesBytes;
}

public sealed record CleanupResult(long BytesFreed, int FilesDeleted, int FilesSkipped);

/// <summary>
/// Nettoyage. Ne supprime que dans des dossiers connus, fichiers de plus de 24 h pour les fichiers
/// temporaires, en ignorant les fichiers verrouillés. Les navigateurs ouverts sont ignorés.
/// </summary>
public interface ICleanupApi
{
    Task<CleanupEstimate> EstimateAsync(CancellationToken cancellationToken);

    Task<CleanupResult> CleanAsync(CleanupTarget target, CancellationToken cancellationToken);
}

public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>Plans d'alimentation (powrprof.dll).</summary>
public interface IPowerApi
{
    Task<IReadOnlyList<PowerPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<Guid?> GetActivePlanAsync(CancellationToken cancellationToken);

    Task<bool> SetActivePlanAsync(Guid planId, CancellationToken cancellationToken);
}
