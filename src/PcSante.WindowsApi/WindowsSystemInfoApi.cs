using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemInfoApi : ISystemInfoApi
{
    private const int TokenUserClass = 1;

    public SystemInfo GetSystemInfo()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : 0;
        var product = key?.GetValue("ProductName") as string ?? "Windows";

        // Windows 11 conserve « Windows 10 » dans ProductName : on corrige d'après le numéro de build.
        if (build >= 22000)
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var edition = key?.GetValue("EditionID") as string ?? string.Empty;
        return new SystemInfo(
            product,
            key?.GetValue("DisplayVersion") as string ?? string.Empty,
            build,
            edition.StartsWith("Core", StringComparison.OrdinalIgnoreCase),
            Environment.MachineName,
            Environment.Is64BitOperatingSystem);
    }

    public DriveSpace GetSystemDrive()
    {
        var name = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var drive = new DriveInfo(name);
        return new DriveSpace(name, drive.TotalSize, drive.AvailableFreeSpace);
    }

    public int CountCrashesSince(DateTimeOffset since)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var count = 0;
        var minidump = Path.Combine(windows, "Minidump");
        if (Directory.Exists(minidump))
        {
            count += Directory.EnumerateFiles(minidump, "*.dmp").Count(f => File.GetLastWriteTimeUtc(f) >= since.UtcDateTime);
        }

        var full = Path.Combine(windows, "MEMORY.DMP");
        if (count == 0 && File.Exists(full) && File.GetLastWriteTimeUtc(full) >= since.UtcDateTime)
        {
            count = 1;
        }

        return count;
    }

    public (string? UserName, string? Sid) GetProcessOwner(int processId)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return (null, null);
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out var token))
            {
                return (null, null);
            }

            try
            {
                NativeMethods.GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out var length);
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!NativeMethods.GetTokenInformation(token, TokenUserClass, buffer, length, out _))
                    {
                        return (null, null);
                    }

                    var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                    string? name;
                    try
                    {
                        name = sid.Translate(typeof(NTAccount)).Value;
                    }
                    catch (IdentityNotMappedException)
                    {
                        name = sid.Value;
                    }

                    return (name, sid.Value);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        catch (Win32Exception)
        {
            return (null, null);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
