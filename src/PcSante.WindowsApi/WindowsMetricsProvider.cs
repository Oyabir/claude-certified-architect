using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

/// <summary>
/// Mesures instantanées à faible coût : GetSystemTimes (CPU), GlobalMemoryStatusEx (RAM), compteur
/// « PhysicalDisk % Idle Time » (disque), statistiques des interfaces (réseau), WMI thermique (mis en cache 30 s).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMetricsProvider : IMetricsProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private PerformanceCounter? _diskIdle;
    private ulong _lastIdle, _lastTotal;
    private long _lastRx, _lastTx, _lastNetTicks;
    private (DateTimeOffset At, double? Value) _temperature = (DateTimeOffset.MinValue, null);
    private bool _temperatureUnavailable;

    public WindowsMetricsProvider(TimeProvider time)
    {
        _time = time;
        try
        {
            _diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
            _diskIdle.NextValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _diskIdle = null;
        }
    }

    public LiveMetrics Sample()
    {
        lock (_gate)
        {
            var memory = new NativeMethods.MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
            NativeMethods.GlobalMemoryStatusEx(ref memory);
            var (rx, tx) = NetworkRates();
            var power = NativeMethods.GetSystemPowerStatus(out var ps) ? ps : default;
            return new LiveMetrics
            {
                At = _time.GetUtcNow(),
                CpuPercent = Math.Round(CpuPercent(), 1),
                MemoryPercent = memory.MemoryLoad,
                MemoryTotalBytes = (long)memory.TotalPhys,
                MemoryUsedBytes = (long)(memory.TotalPhys - memory.AvailPhys),
                DiskActivityPercent = _diskIdle is null ? 0 : Math.Round(Math.Clamp(100 - _diskIdle.NextValue(), 0, 100), 1),
                NetworkReceivedBytesPerSecond = rx,
                NetworkSentBytesPerSecond = tx,
                CpuTemperatureCelsius = Temperature(),
                BatteryPercent = (power.BatteryFlag & 128) != 0 || power.BatteryLifePercent == 255 ? null : power.BatteryLifePercent,
                OnAcPower = power.AcLineStatus switch { 1 => true, 0 => false, _ => null },
            };
        }
    }

    private double CpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var total = kernel.Value + user.Value;
        var idleDelta = idle.Value - _lastIdle;
        var totalDelta = total - _lastTotal;
        var first = _lastTotal == 0;
        _lastIdle = idle.Value;
        _lastTotal = total;
        return first || totalDelta == 0 ? 0 : Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private (long Rx, long Tx) NetworkRates()
    {
        long rx = 0, tx = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var stats = nic.GetIPStatistics();
            rx += stats.BytesReceived;
            tx += stats.BytesSent;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastNetTicks == 0 ? 0 : Stopwatch.GetElapsedTime(_lastNetTicks, now).TotalSeconds;
        var result = elapsed <= 0 ? (0L, 0L) : ((long)Math.Max(0, (rx - _lastRx) / elapsed), (long)Math.Max(0, (tx - _lastTx) / elapsed));
        _lastRx = rx;
        _lastTx = tx;
        _lastNetTicks = now;
        return result;
    }

    private double? Temperature()
    {
        if (_temperatureUnavailable)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        if (now - _temperature.At < TimeSpan.FromSeconds(30))
        {
            return _temperature.Value;
        }

        try
        {
            var values = WmiHelper.Query(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature")
                .Select(o => WmiHelper.Get<uint>(o, "CurrentTemperature"))
                .Where(v => v > 2732)
                .Select(v => (v / 10.0) - 273.15)
                .ToList();
            _temperature = (now, values.Count == 0 ? null : Math.Round(values.Max(), 0));
        }
        catch (ManagementException)
        {
            // Beaucoup de PC n'exposent pas cette classe : on n'essaie plus (évite un coût CPU inutile).
            _temperatureUnavailable = true;
            _temperature = (now, null);
        }

        return _temperature.Value;
    }

    public void Dispose() => _diskIdle?.Dispose();
}
