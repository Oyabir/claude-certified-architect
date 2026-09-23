using System.Globalization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;

namespace PcSante.Core.Health;

/// <summary>Règle de diagnostic : ne signale un problème que sur un fait mesuré (aucun faux problème).</summary>
public interface IDiagnosticRule
{
    IEnumerable<HealthIssue> Evaluate(SystemSnapshot snapshot);
}

/// <summary>Règles du MVP. Seuils volontairement prudents pour éviter les alertes injustifiées.</summary>
public static class DiagnosticRules
{
    public const int SignaturesWarningDays = 3;
    public const int SignaturesCriticalDays = 7;
    public const int ScanWarningDays = 14;
    public const int UpdateSearchWarningDays = 14;
    public const int RestorePointInfoDays = 30;
    public const int StartupWarningCount = 12;
    public const double LowSpaceWarningPercent = 15;
    public const double LowSpaceCriticalPercent = 5;
    public const long CleanableInfoBytes = 1L << 30;
    public const double MemoryWarningPercent = 90;

    public static IReadOnlyList<IDiagnosticRule> All { get; } =
    [
        new Rule(Defender),
        new Rule(Firewall),
        new Rule(Updates),
        new Rule(Restore),
        new Rule(Crashes),
        new Rule(Startup),
        new Rule(Memory),
        new Rule(Storage),
    ];

    public static IReadOnlyList<HealthIssue> EvaluateAll(SystemSnapshot snapshot) =>
        All.SelectMany(r => r.Evaluate(snapshot)).ToList();

    private sealed class Rule(Func<SystemSnapshot, IEnumerable<HealthIssue>> evaluate) : IDiagnosticRule
    {
        public IEnumerable<HealthIssue> Evaluate(SystemSnapshot snapshot) => evaluate(snapshot);
    }

    private static HealthIssue Issue(string code, HealthCategory category, IssueSeverity severity, IssueFix? fix, params string[] args) =>
        new(code, category, severity, fix, args);

    private static string Days(double days) => ((int)Math.Floor(days)).ToString(CultureInfo.InvariantCulture);

    internal static IEnumerable<HealthIssue> Defender(SystemSnapshot s)
    {
        var d = s.Defender;

        // Defender absent ou passif (autre antivirus actif) : on ne conclut rien, pas de faux problème.
        if (d is null || !d.IsAvailable || !d.IsActiveAntivirus)
        {
            yield break;
        }

        if (!d.RealTimeProtectionEnabled)
        {
            yield return Issue("RealtimeOff", HealthCategory.Security, IssueSeverity.Critical, IssueFix.Run(CommandId.EnableRealtimeProtection));
        }

        if (d.ActiveThreats > 0)
        {
            yield return Issue("ActiveThreats", HealthCategory.Security, IssueSeverity.Critical, IssueFix.Open(ScreenId.Protection),
                d.ActiveThreats.ToString(CultureInfo.InvariantCulture));
        }

        if (d.SignaturesUpdatedAt is { } sig)
        {
            var age = (s.CollectedAt - sig).TotalDays;
            if (age > SignaturesCriticalDays)
            {
                yield return Issue("SignaturesOutdated", HealthCategory.Security, IssueSeverity.Critical, IssueFix.Run(CommandId.UpdateSignatures), Days(age));
            }
            else if (age > SignaturesWarningDays)
            {
                yield return Issue("SignaturesOutdated", HealthCategory.Security, IssueSeverity.Warning, IssueFix.Run(CommandId.UpdateSignatures), Days(age));
            }
        }

        var lastScan = Max(d.LastQuickScanAt, d.LastFullScanAt);
        if (lastScan is null || (s.CollectedAt - lastScan.Value).TotalDays > ScanWarningDays)
        {
            yield return Issue("NoRecentScan", HealthCategory.Security, IssueSeverity.Warning, IssueFix.Run(CommandId.StartQuickScan));
        }

        if (!d.CloudProtectionEnabled)
        {
            yield return Issue("CloudProtectionOff", HealthCategory.Security, IssueSeverity.Warning, IssueFix.Run(CommandId.EnableCloudProtection));
        }
    }

    internal static IEnumerable<HealthIssue> Firewall(SystemSnapshot s)
    {
        if (s.Firewall is null)
        {
            yield break;
        }

        foreach (var profile in s.Firewall.Where(p => !p.Enabled))
        {
            // Le profil public (Wi-Fi des lieux publics) est le plus exposé : rouge. Les autres : rouge aussi,
            // un pare-feu désactivé laisse le PC ouvert aux intrusions.
            yield return Issue($"FirewallOff{profile.Profile}", HealthCategory.Security, IssueSeverity.Critical,
                IssueFix.Run(CommandId.EnableFirewallProfile, new Dictionary<string, string> { ["profile"] = profile.Profile.ToString() }));
        }
    }

    internal static IEnumerable<HealthIssue> Updates(SystemSnapshot s)
    {
        if (s.Updates is not { } u)
        {
            yield break;
        }

        if (!u.ServiceRunning && u.LastSearchAt is { } last && (s.CollectedAt - last).TotalDays > UpdateSearchWarningDays)
        {
            yield return Issue("UpdatesBroken", HealthCategory.Security, IssueSeverity.Warning, IssueFix.Run(CommandId.RepairWindowsUpdate));
        }
        else if (u.LastSearchAt is { } search && (s.CollectedAt - search).TotalDays > UpdateSearchWarningDays)
        {
            yield return Issue("UpdatesNotChecked", HealthCategory.Security, IssueSeverity.Warning, IssueFix.Run(CommandId.SearchUpdates), Days((s.CollectedAt - search).TotalDays));
        }

        if (u.RebootRequired)
        {
            yield return Issue("RebootPending", HealthCategory.Stability, IssueSeverity.Info, null);
        }
    }

    internal static IEnumerable<HealthIssue> Restore(SystemSnapshot s)
    {
        if (s.RestoreEnabled == false)
        {
            yield return Issue("RestoreDisabled", HealthCategory.Stability, IssueSeverity.Warning, IssueFix.Run(CommandId.EnableSystemRestore));
            yield break;
        }

        if (s.RestoreEnabled == true
            && (s.LastRestorePointAt is null || (s.CollectedAt - s.LastRestorePointAt.Value).TotalDays > RestorePointInfoDays))
        {
            yield return Issue("NoRecentRestorePoint", HealthCategory.Stability, IssueSeverity.Info, IssueFix.Run(CommandId.CreateRestorePoint));
        }
    }

    internal static IEnumerable<HealthIssue> Crashes(SystemSnapshot s)
    {
        if (s.CrashesLast30Days is > 0 and var n)
        {
            yield return Issue("RecentCrashes", HealthCategory.Stability, n >= 3 ? IssueSeverity.Critical : IssueSeverity.Warning,
                IssueFix.Run(CommandId.RunSystemFileCheck), n.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static IEnumerable<HealthIssue> Startup(SystemSnapshot s)
    {
        if (s.EnabledStartupItems is { } n && n > StartupWarningCount)
        {
            yield return Issue("ManyStartupPrograms", HealthCategory.Performance, IssueSeverity.Warning, IssueFix.Open(ScreenId.Optimization),
                n.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static IEnumerable<HealthIssue> Memory(SystemSnapshot s)
    {
        if (s.MemoryPercent is { } m && m >= MemoryWarningPercent)
        {
            yield return Issue("HighMemory", HealthCategory.Performance, IssueSeverity.Warning, IssueFix.Open(ScreenId.Processes),
                ((int)m).ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static IEnumerable<HealthIssue> Storage(SystemSnapshot s)
    {
        if (s.SystemDrive is { TotalBytes: > 0 } drive)
        {
            var free = drive.FreePercent;
            var freeGb = (drive.FreeBytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture);
            if (free < LowSpaceCriticalPercent)
            {
                yield return Issue("LowDiskSpace", HealthCategory.Storage, IssueSeverity.Critical, IssueFix.Open(ScreenId.Optimization), freeGb);
            }
            else if (free < LowSpaceWarningPercent)
            {
                yield return Issue("LowDiskSpace", HealthCategory.Storage, IssueSeverity.Warning, IssueFix.Open(ScreenId.Optimization), freeGb);
            }
        }

        if (s.CleanableBytes is { } bytes && bytes >= CleanableInfoBytes)
        {
            var gb = (bytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture);
            yield return Issue("CleanableFiles", HealthCategory.Storage, IssueSeverity.Info, IssueFix.Open(ScreenId.Optimization), gb);
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}
