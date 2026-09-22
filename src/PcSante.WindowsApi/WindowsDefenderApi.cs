using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Microsoft Defender via WMI (MSFT_Mp*) et MpCmdRun (quarantaine). Aucun PowerShell.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsDefenderApi : IDefenderApi
{
    public Task<DefenderStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            var status = WmiHelper.Query(WmiHelper.DefenderNamespace, "SELECT * FROM MSFT_MpComputerStatus").FirstOrDefault();
            var prefs = WmiHelper.Query(WmiHelper.DefenderNamespace, "SELECT * FROM MSFT_MpPreference").FirstOrDefault();
            if (status is null)
            {
                return DefenderStatus.Unavailable;
            }

            var threats = WmiHelper.Query(WmiHelper.DefenderNamespace, "SELECT * FROM MSFT_MpThreat WHERE IsActive = TRUE").Count;
            var runningMode = WmiHelper.Get<string>(status, "AMRunningMode") ?? "Normal";
            return new DefenderStatus
            {
                IsAvailable = true,
                IsActiveAntivirus = WmiHelper.Get<bool>(status, "AntivirusEnabled") && runningMode.Equals("Normal", StringComparison.OrdinalIgnoreCase),
                RealTimeProtectionEnabled = WmiHelper.Get<bool>(status, "RealTimeProtectionEnabled"),
                CloudProtectionEnabled = prefs is not null && WmiHelper.Get<byte>(prefs, "MAPSReporting") > 0,
                ControlledFolderAccessEnabled = prefs is not null && WmiHelper.Get<byte>(prefs, "EnableControlledFolderAccess") == 1,
                TamperProtectionEnabled = WmiHelper.Get<bool>(status, "IsTamperProtected"),
                SignaturesUpdatedAt = WmiHelper.Date(status, "AntivirusSignatureLastUpdated"),
                SignatureVersion = WmiHelper.Get<string>(status, "AntivirusSignatureVersion"),
                LastQuickScanAt = WmiHelper.Date(status, "QuickScanEndTime"),
                LastFullScanAt = WmiHelper.Date(status, "FullScanEndTime"),
                ActiveThreats = threats,
            };
        }
        catch (ManagementException)
        {
            return DefenderStatus.Unavailable;
        }
    }, cancellationToken);

    public Task<DefenderPreferences> GetPreferencesAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var prefs = WmiHelper.Query(WmiHelper.DefenderNamespace, "SELECT * FROM MSFT_MpPreference").First();
        return new DefenderPreferences(
            !WmiHelper.Get<bool>(prefs, "DisableRealtimeMonitoring"),
            WmiHelper.Get<byte>(prefs, "MAPSReporting"),
            WmiHelper.Get<byte>(prefs, "EnableControlledFolderAccess"));
    }, cancellationToken);

    public Task<bool> SetRealTimeProtectionAsync(bool enabled, CancellationToken cancellationToken) =>
        SetPreferenceAsync("DisableRealtimeMonitoring", !enabled, cancellationToken);

    public Task<bool> SetCloudReportingLevelAsync(int level, CancellationToken cancellationToken) =>
        SetPreferenceAsync("MAPSReporting", (byte)Math.Clamp(level, 0, 2), cancellationToken);

    public Task<bool> SetControlledFolderAccessAsync(int mode, CancellationToken cancellationToken) =>
        SetPreferenceAsync("EnableControlledFolderAccess", (byte)Math.Clamp(mode, 0, 2), cancellationToken);

    public Task<bool> RunScanAsync(DefenderScanType type, string? path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var parameters = new Dictionary<string, object> { ["ScanType"] = (byte)(type switch { DefenderScanType.Quick => 1, DefenderScanType.Full => 2, _ => 3 }) };
        if (type == DefenderScanType.Custom && path is not null)
        {
            parameters["ScanPath"] = path;
        }

        return WmiHelper.InvokeStatic(WmiHelper.DefenderNamespace, "MSFT_MpScan", "Start", parameters) == 0;
    }, cancellationToken);

    public async Task<bool> UpdateSignaturesAsync(CancellationToken cancellationToken)
    {
        var result = await SystemTools.RunAsync(SystemTools.MpCmdRun, ["-SignatureUpdate"], TimeSpan.FromMinutes(15), cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public Task<IReadOnlyList<ThreatInfo>> GetThreatHistoryAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<ThreatInfo>>(() =>
    {
        var threats = WmiHelper.Query(WmiHelper.DefenderNamespace, "SELECT * FROM MSFT_MpThreat")
            .ToDictionary(t => WmiHelper.Get<long>(t, "ThreatID"), t => t);
        return WmiHelper.Query(WmiHelper.DefenderNamespace, "SELECT * FROM MSFT_MpThreatDetection")
            .Select(d =>
            {
                var id = WmiHelper.Get<long>(d, "ThreatID");
                threats.TryGetValue(id, out var t);
                var resources = WmiHelper.Get<string[]>(d, "Resources");
                return new ThreatInfo(
                    id.ToString(CultureInfo.InvariantCulture),
                    t is null ? "?" : WmiHelper.Get<string>(t, "ThreatName") ?? "?",
                    t is null ? 0 : WmiHelper.Get<byte>(t, "SeverityID"),
                    WmiHelper.Date(d, "InitialDetectionTime") ?? DateTimeOffset.MinValue,
                    WmiHelper.Get<bool>(d, "ActionSuccess") ? "Handled" : "ActionRequired",
                    resources?.FirstOrDefault());
            })
            .OrderByDescending(t => t.DetectedAt)
            .ToList();
    }, cancellationToken);

    public async Task<IReadOnlyList<QuarantineItem>> GetQuarantineAsync(CancellationToken cancellationToken)
    {
        var result = await SystemTools.RunAsync(SystemTools.MpCmdRun, ["-Restore", "-ListAll"], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        return ParseQuarantine(result.Output);
    }

    public async Task<bool> RestoreFromQuarantineAsync(string id, CancellationToken cancellationToken)
    {
        var result = await SystemTools.RunAsync(SystemTools.MpCmdRun, ["-Restore", "-Name", id], TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    /// <summary>Analyse la sortie de « MpCmdRun -Restore -ListAll » (format stable, en anglais).</summary>
    internal static IReadOnlyList<QuarantineItem> ParseQuarantine(string output)
    {
        var items = new List<QuarantineItem>();
        string? current = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var name = ThreatNameRegex().Match(line);
            if (name.Success)
            {
                current = name.Groups[1].Value.Trim();
                continue;
            }

            var file = FileRegex().Match(line);
            if (file.Success && current is not null)
            {
                var when = DateTimeOffset.TryParse(file.Groups[2].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                    ? d
                    : DateTimeOffset.MinValue;
                items.Add(new QuarantineItem(current, current, when, file.Groups[1].Value.Trim()));
            }
        }

        return items.DistinctBy(i => i.Id).ToList();
    }

    private static Task<bool> SetPreferenceAsync(string name, object value, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            return WmiHelper.InvokeStatic(WmiHelper.DefenderNamespace, "MSFT_MpPreference", "Set", new Dictionary<string, object> { [name] = value }) == 0;
        }
        catch (ManagementException)
        {
            // Protection contre les falsifications ou stratégie de groupe : Windows refuse.
            return false;
        }
    }, cancellationToken);

    [GeneratedRegex(@"^ThreatName\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThreatNameRegex();

    [GeneratedRegex(@"^file:(.+?)\s+quarantined at\s+(.+?)(\s*\(UTC\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileRegex();
}
