namespace PcSante.Core.Windows;

/// <summary>Mesures instantanées (mini-affichage, écran Performance).</summary>
public sealed record LiveMetrics
{
    public DateTimeOffset At { get; init; }

    public double CpuPercent { get; init; }

    public double MemoryPercent { get; init; }

    public long MemoryUsedBytes { get; init; }

    public long MemoryTotalBytes { get; init; }

    public double DiskActivityPercent { get; init; }

    public long NetworkReceivedBytesPerSecond { get; init; }

    public long NetworkSentBytesPerSecond { get; init; }

    /// <summary>Null si Windows ne fournit pas la température (fréquent) : affiché « non disponible ».</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Null s'il n'y a pas de batterie.</summary>
    public int? BatteryPercent { get; init; }

    public bool? OnAcPower { get; init; }
}

public interface IMetricsProvider
{
    LiveMetrics Sample();
}

public sealed record DriveSpace(string Name, long TotalBytes, long FreeBytes)
{
    public double FreePercent => TotalBytes <= 0 ? 0 : 100.0 * FreeBytes / TotalBytes;
}

public sealed record SystemInfo(string ProductName, string DisplayVersion, int Build, bool IsHomeEdition, string MachineName, bool Is64Bit);

public interface ISystemInfoApi
{
    SystemInfo GetSystemInfo();

    DriveSpace GetSystemDrive();

    /// <summary>Nombre d'arrêts brutaux (écrans bleus) depuis une date (dossier Minidump, journal Système).</summary>
    int CountCrashesSince(DateTimeOffset since);

    /// <summary>Utilisateur de la session interactive et SID, pour les réglages par utilisateur.</summary>
    (string? UserName, string? Sid) GetProcessOwner(int processId);
}
