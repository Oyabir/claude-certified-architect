using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using PcSante.Ipc.Security;

namespace PcSante.Ipc.Windows;

/// <summary>Identifie le processus client par son PID (noyau), jamais par une déclaration du client.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClientProcessInspector(Func<int, (string? UserName, string? Sid)> ownerResolver) : IClientProcessInspector
{
    public ClientProcessFacts Inspect(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid == 0)
        {
            return new ClientProcessFacts(0, null, null, null);
        }

        string? path = null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            path = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            path = null;
        }

        var (user, sid) = ownerResolver((int)pid);
        return new ClientProcessFacts((int)pid, path, user, sid);
    }
}
