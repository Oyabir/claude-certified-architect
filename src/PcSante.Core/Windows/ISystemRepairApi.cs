namespace PcSante.Core.Windows;

public enum RepairOutcome
{
    NoProblemFound,
    Repaired,
    ProblemsRemain,
    Failed,
}

public sealed record RepairResult(RepairOutcome Outcome, int ExitCode, string Summary);

/// <summary>SFC et DISM (exécutables système à chemin absolu, arguments fixes).</summary>
public interface ISystemRepairApi
{
    Task<RepairResult> RunSystemFileCheckAsync(CancellationToken cancellationToken);

    Task<RepairResult> RunDismRestoreHealthAsync(CancellationToken cancellationToken);
}
