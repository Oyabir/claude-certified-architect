using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;
using StartMode = PcSante.Core.Windows.ServiceStartMode;

namespace PcSante.WindowsApi;

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceControlApi : IServiceControlApi
{
    public Task<ServiceInfo?> GetAsync(string serviceName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            var mode = sc.StartType switch
            {
                System.ServiceProcess.ServiceStartMode.Automatic => IsDelayed(serviceName) ? StartMode.AutomaticDelayed : StartMode.Automatic,
                System.ServiceProcess.ServiceStartMode.Manual => StartMode.Manual,
                System.ServiceProcess.ServiceStartMode.Disabled => StartMode.Disabled,
                _ => StartMode.Unknown,
            };
            return (ServiceInfo?)new ServiceInfo(sc.ServiceName, sc.DisplayName, mode, sc.Status != ServiceControllerStatus.Stopped, null);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }, cancellationToken);

    public Task<bool> SetStartModeAsync(string serviceName, StartMode mode, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var startType = mode switch
        {
            StartMode.Automatic or StartMode.AutomaticDelayed => 2u,
            StartMode.Manual => 3u,
            StartMode.Disabled => 4u,
            _ => NativeMethods.ServiceNoChange,
        };
        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var service = NativeMethods.OpenService(manager, serviceName, NativeMethods.ServiceChangeConfig | NativeMethods.ServiceQueryConfig);
            if (service == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var ok = NativeMethods.ChangeServiceConfig(service, NativeMethods.ServiceNoChange, startType, NativeMethods.ServiceNoChange,
                    null, null, IntPtr.Zero, null, null, null, null);
                if (ok && mode is StartMode.Automatic or StartMode.AutomaticDelayed)
                {
                    var delayed = new NativeMethods.ServiceDelayedAutoStartInfo { DelayedAutostart = mode == StartMode.AutomaticDelayed ? 1 : 0 };
                    ok = NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigDelayedAutoStartInfo, ref delayed);
                }

                return ok;
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(manager);
        }
    }, cancellationToken);

    public Task<bool> StopAsync(string serviceName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                return true;
            }

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }, cancellationToken);

    public Task<bool> StartAsync(string serviceName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                return true;
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }, cancellationToken);

    private static bool IsDelayed(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key?.GetValue("DelayedAutostart") is int v && v == 1;
    }
}
