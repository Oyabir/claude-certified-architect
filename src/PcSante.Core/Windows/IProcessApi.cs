namespace PcSante.Core.Windows;

public sealed record ProcessSample(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    double CpuPercent,
    long MemoryBytes,
    double DiskBytesPerSecond,
    IReadOnlyList<string> ServiceNames);

/// <summary>Processus en cours (mesure différentielle CPU/disque entre deux appels).</summary>
public interface IProcessApi
{
    Task<IReadOnlyList<ProcessSample>> SampleAsync(CancellationToken cancellationToken);

    Task<ProcessSample?> GetAsync(int processId, CancellationToken cancellationToken);

    /// <summary>Arrête le processus (arbre compris). Renvoie vrai s'il ne tourne plus.</summary>
    Task<bool> StopAsync(int processId, CancellationToken cancellationToken);

    Task<bool> IsRunningAsync(int processId, CancellationToken cancellationToken);
}

public enum ServiceStartMode
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown,
}

public sealed record ServiceInfo(string Name, string DisplayName, ServiceStartMode StartMode, bool IsRunning, string? ExecutablePath);

public interface IServiceControlApi
{
    Task<ServiceInfo?> GetAsync(string serviceName, CancellationToken cancellationToken);

    Task<bool> SetStartModeAsync(string serviceName, ServiceStartMode mode, CancellationToken cancellationToken);

    Task<bool> StopAsync(string serviceName, CancellationToken cancellationToken);

    Task<bool> StartAsync(string serviceName, CancellationToken cancellationToken);
}

public sealed record SignatureInfo(bool IsSigned, bool IsTrusted, string? Publisher, string? Thumbprint)
{
    public static SignatureInfo Unsigned { get; } = new(false, false, null, null);
}

/// <summary>Vérification de la signature numérique Authenticode d'un exécutable.</summary>
public interface ISignatureVerifier
{
    SignatureInfo Verify(string filePath);
}
