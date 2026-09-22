using System.Runtime.Versioning;
using System.Text;
using PcSante.Core.Windows;

namespace PcSante.WindowsApi;

/// <summary>SFC /scannow et DISM /RestoreHealth. Résultat déterminé par le code de sortie et la sortie (FR/EN).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemRepairApi : ISystemRepairApi
{
    public async Task<RepairResult> RunSystemFileCheckAsync(CancellationToken cancellationToken)
    {
        var result = await SystemTools.RunAsync(SystemTools.Sfc, ["/scannow"], TimeSpan.FromHours(2), cancellationToken, Encoding.Unicode).ConfigureAwait(false);
        return InterpretSfc(result);
    }

    public async Task<RepairResult> RunDismRestoreHealthAsync(CancellationToken cancellationToken)
    {
        var result = await SystemTools.RunAsync(SystemTools.Dism, ["/Online", "/Cleanup-Image", "/RestoreHealth", "/NoRestart"], TimeSpan.FromHours(2), cancellationToken).ConfigureAwait(false);
        return result.ExitCode switch
        {
            0 when Contains(result.Output, "no component store corruption", "aucune altération") => new RepairResult(RepairOutcome.NoProblemFound, 0, Summary(result.Output)),
            0 => new RepairResult(RepairOutcome.Repaired, 0, Summary(result.Output)),
            3010 => new RepairResult(RepairOutcome.Repaired, 3010, Summary(result.Output)),
            _ => new RepairResult(RepairOutcome.Failed, result.ExitCode, Summary(result.Output)),
        };
    }

    internal static RepairResult InterpretSfc(ToolResult result)
    {
        var text = result.Output;
        if (Contains(text, "did not find any integrity violations", "n'a trouvé aucune violation", "n’a trouvé aucune violation"))
        {
            return new RepairResult(RepairOutcome.NoProblemFound, result.ExitCode, Summary(text));
        }

        if (Contains(text, "unable to fix", "n'a pas pu réparer", "n’a pas pu réparer", "impossible de réparer"))
        {
            return new RepairResult(RepairOutcome.ProblemsRemain, result.ExitCode, Summary(text));
        }

        if (Contains(text, "successfully repaired", "a réparé", "les a réparés"))
        {
            return new RepairResult(RepairOutcome.Repaired, result.ExitCode, Summary(text));
        }

        return result.ExitCode == 0
            ? new RepairResult(RepairOutcome.NoProblemFound, 0, Summary(text))
            : new RepairResult(RepairOutcome.Failed, result.ExitCode, Summary(text));
    }

    private static bool Contains(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Summary(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(4));
    }
}
