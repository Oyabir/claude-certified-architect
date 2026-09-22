namespace PcSante.Core.Windows;

public enum DefenderScanType
{
    Quick,
    Full,
    Custom,
}

public sealed record DefenderStatus
{
    /// <summary>Microsoft Defender est présent et interrogeable.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Defender est l'antivirus actif (faux s'il est en mode passif à cause d'un autre antivirus).</summary>
    public bool IsActiveAntivirus { get; init; }

    public bool RealTimeProtectionEnabled { get; init; }

    public bool CloudProtectionEnabled { get; init; }

    public bool ControlledFolderAccessEnabled { get; init; }

    /// <summary>Protection contre les falsifications : bloque la désactivation de Defender.</summary>
    public bool TamperProtectionEnabled { get; init; }

    public DateTimeOffset? SignaturesUpdatedAt { get; init; }

    public string? SignatureVersion { get; init; }

    public DateTimeOffset? LastQuickScanAt { get; init; }

    public DateTimeOffset? LastFullScanAt { get; init; }

    public int ActiveThreats { get; init; }

    public static DefenderStatus Unavailable { get; } = new() { IsAvailable = false };
}

public sealed record DefenderPreferences(bool RealTimeProtection, int CloudReportingLevel, int ControlledFolderAccess);

public sealed record ThreatInfo(string Id, string Name, int SeverityLevel, DateTimeOffset DetectedAt, string Status, string? Resource);

public sealed record QuarantineItem(string Id, string ThreatName, DateTimeOffset QuarantinedAt, string? Path);

/// <summary>Pilotage de Microsoft Defender (WMI root\Microsoft\Windows\Defender, MpCmdRun).</summary>
public interface IDefenderApi
{
    Task<DefenderStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<DefenderPreferences> GetPreferencesAsync(CancellationToken cancellationToken);

    Task<bool> SetRealTimeProtectionAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Niveau MAPS : 0 = désactivé, 2 = avancé.</summary>
    Task<bool> SetCloudReportingLevelAsync(int level, CancellationToken cancellationToken);

    /// <summary>0 = désactivé, 1 = activé, 2 = audit.</summary>
    Task<bool> SetControlledFolderAccessAsync(int mode, CancellationToken cancellationToken);

    /// <summary>Lance un scan ; attend sa fin (à appeler en arrière-plan).</summary>
    Task<bool> RunScanAsync(DefenderScanType type, string? path, CancellationToken cancellationToken);

    Task<bool> UpdateSignaturesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ThreatInfo>> GetThreatHistoryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<QuarantineItem>> GetQuarantineAsync(CancellationToken cancellationToken);

    Task<bool> RestoreFromQuarantineAsync(string id, CancellationToken cancellationToken);
}
