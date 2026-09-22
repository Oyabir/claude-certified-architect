using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>Points de restauration via WMI root\default:SystemRestore.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRestorePointApi : IRestorePointApi
{
    private const string Scope = @"root\default";
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
        if (key is null)
        {
            return false;
        }

        var disabled = key.GetValue("DisableSR") is int d && d == 1;
        var enabled = key.GetValue("RPSessionInterval") is int i && i >= 1;
        return enabled && !disabled;
    }, cancellationToken);

    public Task<bool> EnableAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
        return WmiHelper.InvokeStatic(Scope, "SystemRestore", "Enable", new Dictionary<string, object> { ["Drive"] = drive }) == 0;
    }, cancellationToken);

    public Task<bool> CreateAsync(string description, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            return WmiHelper.InvokeStatic(Scope, "SystemRestore", "CreateRestorePoint", new Dictionary<string, object>
            {
                ["Description"] = description.Length > 60 ? description[..60] : description,
                ["RestorePointType"] = ModifySettings,
                ["EventType"] = BeginSystemChange,
            }) == 0;
        }
        catch (ManagementException)
        {
            return false;
        }
    }, cancellationToken);

    public Task<IReadOnlyList<RestorePointInfo>> ListAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<RestorePointInfo>>(() =>
    {
        try
        {
            return WmiHelper.Query(Scope, "SELECT * FROM SystemRestore")
                .Select(p => new RestorePointInfo(
                    Convert.ToInt32(p["SequenceNumber"], CultureInfo.InvariantCulture),
                    WmiHelper.Get<string>(p, "Description") ?? string.Empty,
                    WmiHelper.Date(p, "CreationTime") ?? DateTimeOffset.MinValue))
                .OrderByDescending(p => p.CreatedAt)
                .ToList();
        }
        catch (ManagementException)
        {
            return [];
        }
    }, cancellationToken);
}
