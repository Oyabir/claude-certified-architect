using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PcSante.Licensing;

namespace PcSante.WindowsApi;

/// <summary>Identifiants matériels (WMI + registre). Seul leur hachage quitte le PC.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareInfoProvider : IHardwareInfoProvider
{
    public HardwareIdentity Read() => new(
        Safe(() => First(@"root\cimv2", "SELECT SerialNumber, Product FROM Win32_BaseBoard", o =>
            $"{WmiHelper.Get<string>(o, "SerialNumber")}|{WmiHelper.Get<string>(o, "Product")}")),
        Safe(SystemDiskSerial),
        Safe(() => First(@"root\cimv2", "SELECT ProcessorId FROM Win32_Processor", o => WmiHelper.Get<string>(o, "ProcessorId"))),
        Safe(MachineGuid));

    private static string? SystemDiskSerial()
    {
        var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        foreach (var partition in WmiHelper.Query(@"root\cimv2", $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{drive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
        {
            var partitionId = WmiHelper.Get<string>(partition, "DeviceID");
            foreach (var disk in WmiHelper.Query(@"root\cimv2", $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
            {
                return WmiHelper.Get<string>(disk, "SerialNumber")?.Trim();
            }
        }

        return null;
    }

    private static string? MachineGuid()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string;
    }

    private static string? First(string scope, string query, Func<ManagementObject, string?> read) =>
        WmiHelper.Query(scope, query).Select(read).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Safe(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}

/// <summary>DPAPI niveau machine : le jeton chiffré est inutilisable s'il est copié sur un autre PC.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiMachineProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PcSante.LicenseState.v1");

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);

    public byte[]? Unprotect(byte[] data)
    {
        try
        {
            return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
