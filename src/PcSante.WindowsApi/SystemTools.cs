using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace PcSante.WindowsApi;

public sealed record ToolResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Lancement des outils système Windows : chemin ABSOLU dans System32 (ou dossier Defender),
/// arguments passés un par un (aucun interpréteur de commandes, aucune injection possible), durée bornée.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SystemTools
{
    public static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    public static string Sfc => Path.Combine(System32, "sfc.exe");

    public static string Dism => Path.Combine(System32, "Dism.exe");

    public static string Netsh => Path.Combine(System32, "netsh.exe");

    public static string Ipconfig => Path.Combine(System32, "ipconfig.exe");

    public static string Defrag => Path.Combine(System32, "defrag.exe");

    public static string MsiExec => Path.Combine(System32, "msiexec.exe");

    public static string MpCmdRun => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");

    public static async Task<ToolResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken, Encoding? outputEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            return new ToolResult(-1, "Outil introuvable : " + executable);
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding ?? Encoding.UTF8,
            WorkingDirectory = System32,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Déjà terminé.
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ToolResult(-2, "Délai dépassé");
        }

        lock (output)
        {
            // SFC écrit en UTF-16 avec des caractères nuls quand la sortie est redirigée.
            return new ToolResult(process.ExitCode, output.ToString().Replace("\0", string.Empty, StringComparison.Ordinal));
        }
    }
}
