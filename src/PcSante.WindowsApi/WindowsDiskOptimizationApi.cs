using System.Management;
using System.Runtime.Versioning;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>
/// Disque système : type de support (MSFT_PhysicalDisk), optimisation par defrag.exe /O (arguments fixes),
/// fichier d'échange (Win32_ComputerSystem.AutomaticManagedPagefile).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskOptimizationApi : IDiskOptimizationApi
{
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";
    private const string Cimv2 = @"root\cimv2";
    private static readonly TimeSpan OptimizationTimeout = TimeSpan.FromHours(4);

    private static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

    public Task<DiskOptimizationInfo?> GetSystemDiskAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            var computer = WmiHelper.Query(Cimv2, "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").FirstOrDefault();
            var usage = WmiHelper.Query(Cimv2, "SELECT AllocatedBaseSize FROM Win32_PageFileUsage").FirstOrDefault();
            return (DiskOptimizationInfo?)new DiskOptimizationInfo(
                SystemDrive,
                MediaTypeOfSystemDrive(),
                computer is null || WmiHelper.Get<bool>(computer, "AutomaticManagedPagefile"),
                usage is null ? null : WmiHelper.Get<uint>(usage, "AllocatedBaseSize"));
        }
        catch (ManagementException)
        {
            return null;
        }
    }, cancellationToken);

    public async Task<bool> OptimizeSystemDriveAsync(CancellationToken cancellationToken) =>
        (await SystemTools.RunAsync(SystemTools.Defrag, [SystemDrive, "/O"], OptimizationTimeout, cancellationToken).ConfigureAwait(false)).Succeeded;

    public Task<bool> SetPageFileAutomaticAsync(bool automatic, CancellationToken cancellationToken) => Task.Run(() =>
    {
        // Modifier Win32_ComputerSystem demande l'activation des privilèges de la connexion WMI.
        var scope = new ManagementScope(Cimv2, new ConnectionOptions { EnablePrivileges = true });
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem"));
        using var computer = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (computer is null)
        {
            return false;
        }

        computer["AutomaticManagedPagefile"] = automatic;
        computer.Put();
        return true;
    }, cancellationToken);

    /// <summary>MediaType de MSFT_PhysicalDisk : 3 = disque dur, 4 = SSD.</summary>
    internal static DiskMediaType MapMediaType(ushort mediaType) => mediaType switch
    {
        3 => DiskMediaType.Hdd,
        4 => DiskMediaType.Ssd,
        _ => DiskMediaType.Unknown,
    };

    private static DiskMediaType MediaTypeOfSystemDrive()
    {
        try
        {
            var letter = SystemDrive.TrimEnd(':');
            var partition = WmiHelper.Query(StorageNamespace, $"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'").FirstOrDefault();
            if (partition is null)
            {
                return DiskMediaType.Unknown;
            }

            var number = WmiHelper.Get<uint>(partition, "DiskNumber");
            var disk = WmiHelper.Query(StorageNamespace, $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{number}'").FirstOrDefault();
            return disk is null ? DiskMediaType.Unknown : MapMediaType(WmiHelper.Get<ushort>(disk, "MediaType"));
        }
        catch (ManagementException)
        {
            return DiskMediaType.Unknown;
        }
    }
}
