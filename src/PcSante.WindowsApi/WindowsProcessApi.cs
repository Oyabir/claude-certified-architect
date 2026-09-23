using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using PcSante.Core.Windows;
using PcSante.WindowsApi.Native;

namespace PcSante.WindowsApi;

/// <summary>Processus : CPU et disque mesurés par différence entre deux échantillons.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessApi : IProcessApi
{
    private readonly object _gate = new();
    private Dictionary<int, (TimeSpan Cpu, ulong Io, long Ticks)> _previous = [];

    public Task<IReadOnlyList<ProcessSample>> SampleAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<ProcessSample>>(() =>
    {
        var services = ServicesByProcess();
        var now = Stopwatch.GetTimestamp();
        var processors = Environment.ProcessorCount;
        var current = new Dictionary<int, (TimeSpan, ulong, long)>();
        var samples = new List<ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == 0)
                    {
                        continue;
                    }

                    var (path, io) = QueryPathAndIo(process.Id);
                    TimeSpan cpu;
                    try
                    {
                        cpu = process.TotalProcessorTime;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        cpu = TimeSpan.Zero;
                    }

                    current[process.Id] = (cpu, io, now);
                    double cpuPercent = 0, diskRate = 0;
                    lock (_gate)
                    {
                        if (_previous.TryGetValue(process.Id, out var prev))
                        {
                            var elapsed = Stopwatch.GetElapsedTime(prev.Ticks, now).TotalSeconds;
                            if (elapsed > 0)
                            {
                                cpuPercent = Math.Clamp((cpu - prev.Cpu).TotalSeconds / elapsed / processors * 100, 0, 100);
                                diskRate = io >= prev.Io ? (io - prev.Io) / elapsed : 0;
                            }
                        }
                    }

                    samples.Add(new ProcessSample(process.Id, process.ProcessName, path, Math.Round(cpuPercent, 1), process.WorkingSet64, diskRate,
                        services.TryGetValue(process.Id, out var names) ? names : []));
                }
                catch (InvalidOperationException)
                {
                    // Processus terminé pendant l'énumération.
                }
            }
        }

        lock (_gate)
        {
            _previous = current;
        }

        return samples;
    }, cancellationToken);

    public async Task<ProcessSample?> GetAsync(int processId, CancellationToken cancellationToken)
    {
        if (!await IsRunningAsync(processId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            using var p = Process.GetProcessById(processId);
            var (path, _) = QueryPathAndIo(processId);
            return new ProcessSample(p.Id, p.ProcessName, path, 0, p.WorkingSet64, 0, []);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public Task<bool> StopAsync(int processId, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            p.Kill(entireProcessTree: true);
            return p.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }, cancellationToken);

    public Task<bool> IsRunningAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            return Task.FromResult(!p.HasExited);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return Task.FromResult(false);
        }
    }

    private static (string? Path, ulong Io) QueryPathAndIo(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return (null, 0);
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            var path = NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
            var io = NativeMethods.GetProcessIoCounters(handle, out var counters) ? counters.ReadTransferCount + counters.WriteTransferCount : 0;
            return (path, io);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static Dictionary<int, IReadOnlyList<string>> ServicesByProcess()
    {
        try
        {
            return WmiHelper.Query(@"root\cimv2", "SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId > 0")
                .GroupBy(s => Convert.ToInt32(s["ProcessId"], System.Globalization.CultureInfo.InvariantCulture))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(s => (string)s["Name"]).ToList());
        }
        catch (ManagementException)
        {
            return [];
        }
    }
}
