using PcSante.Core.Windows;

namespace PcSante.Core.Processes;

/// <summary>Classification par réputation (M3).</summary>
public enum Reputation
{
    Useful,
    Unnecessary,
    Unknown,
    Suspicious,
}

public sealed record ProcessView(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    double CpuPercent,
    long MemoryBytes,
    double DiskBytesPerSecond,
    IReadOnlyList<string> ServiceNames,
    bool IsSigned,
    string? Publisher,
    Reputation Reputation,
    bool IsProtected);

public sealed record ProcessHistoryEntry(string Name, double AverageCpuPercent, long AverageMemoryBytes, int Samples, double HighUsageRatio);

/// <summary>
/// Base de réputation embarquée (MVP) + règles de prudence.
/// Suspect = non signé ET lancé depuis un dossier temporaire ou de téléchargement, ou nom système usurpé.
/// </summary>
public static class ProcessReputation
{
    /// <summary>Processus indispensables : jamais arrêtés par PC Santé.</summary>
    public static IReadOnlySet<string> ProtectedNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "fontdrvhost", "dwm", "memory compression", "secure system", "spoolsv", "msmpeng",
        "nissrv", "securityhealthservice", "sgrmbroker", "wudfhost", "audiodg", "explorer", "sihost",
        "ctfmon", "taskhostw", "runtimebroker", "searchhost", "startmenuexperiencehost", "textinputhost",
        "conhost", "lsm", "wlanext", "mssense", "mpdefendercoreservice",
        "pcsante", "pcsante.service", "pcsante.overlay",
    };

    /// <summary>Programmes courants dont le lancement permanent est rarement utile.</summary>
    public static IReadOnlySet<string> UsuallyUnnecessary { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "adobearm", "adobeupdateservice", "acrotray", "jusched", "jucheck", "itunhelper", "ituneshelper",
        "googleupdate", "ccleaner", "ccleaner64", "skypeapp", "spotifywebhelper", "steamwebhelper",
        "epicgameslauncher", "cortana", "yourphone", "phoneexperiencehost", "teams_updater",
    };

    /// <summary>Noms système souvent usurpés par des logiciels malveillants s'ils tournent hors de System32.</summary>
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "lsass", "csrss", "winlogon", "services", "smss", "wininit", "explorer", "spoolsv", "dwm",
    };

    public static bool IsProtected(string processName) => ProtectedNames.Contains(Normalize(processName));

    public static Reputation Classify(string processName, string? executablePath, SignatureInfo signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var name = Normalize(processName);
        var path = executablePath ?? string.Empty;

        if (SystemNames.Contains(name) && path.Length > 0
            && !path.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase))
        {
            return Reputation.Suspicious;
        }

        if (!signature.IsSigned && IsRiskyLocation(path))
        {
            return Reputation.Suspicious;
        }

        if (UsuallyUnnecessary.Contains(name))
        {
            return Reputation.Unnecessary;
        }

        if (ProtectedNames.Contains(name) || (signature.IsTrusted && IsMicrosoft(signature.Publisher)))
        {
            return Reputation.Useful;
        }

        return signature.IsTrusted ? Reputation.Useful : Reputation.Unknown;
    }

    internal static bool IsRiskyLocation(string path) =>
        path.Contains(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Windows\Temp\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Users\Public\", StringComparison.OrdinalIgnoreCase);

    private static bool IsMicrosoft(string? publisher) =>
        publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

    private static string Normalize(string processName)
    {
        var n = processName.Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }
}
