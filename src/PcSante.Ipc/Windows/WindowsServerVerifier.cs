using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;

namespace PcSante.Ipc.Windows;

/// <summary>
/// Côté client : le processus qui a créé le pipe doit être l'exécutable du service installé.
/// Empêche un programme malveillant de se faire passer pour le service.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServerVerifier(string expectedServiceExecutable) : IServerVerifier
{
    public bool IsGenuineService(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var path = process.MainModule?.FileName;
            return path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedServiceExecutable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Un processus SYSTEM n'est pas toujours lisible par un utilisateur standard :
            // dans ce cas on vérifie au moins que le serveur tourne en session 0 (services).
            try
            {
                using var process = Process.GetProcessById((int)pid);
                return process.SessionId == 0;
            }
            catch (Exception inner) when (inner is ArgumentException or InvalidOperationException or Win32Exception)
            {
                return false;
            }
        }
    }
}
