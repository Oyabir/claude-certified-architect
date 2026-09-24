namespace PcSante.Core.Health;

/// <summary>
/// Calcul du score de santé (0 à 100) et des 4 sous-scores.
/// Un score (global ou sous-score) ne peut pas être vert s'il reste un problème orange, ni orange s'il reste
/// un problème rouge : sa couleur est toujours cohérente avec la liste des problèmes concernés.
/// </summary>
public static class HealthScoreCalculator
{
    public const int GreenThreshold = 80;
    public const int OrangeThreshold = 50;

    private const double SecurityWeight = 0.35;
    private const double PerformanceWeight = 0.25;
    private const double StabilityWeight = 0.20;
    private const double StorageWeight = 0.20;

    public static int Penalty(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Critical => 45,
        IssueSeverity.Warning => 20,
        IssueSeverity.Info => 5,
        _ => 0,
    };

    public static SubScores ComputeSubScores(IEnumerable<HealthIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var list = issues as IReadOnlyCollection<HealthIssue> ?? issues.ToList();
        return new SubScores(
            Sub(list, HealthCategory.Security),
            Sub(list, HealthCategory.Performance),
            Sub(list, HealthCategory.Stability),
            Sub(list, HealthCategory.Storage));
    }

    public static int ComputeScore(SubScores sub, IEnumerable<HealthIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(sub);
        ArgumentNullException.ThrowIfNull(issues);
        var weighted = (sub.Security * SecurityWeight)
            + (sub.Performance * PerformanceWeight)
            + (sub.Stability * StabilityWeight)
            + (sub.Storage * StorageWeight);
        return CapBySeverity((int)Math.Round(weighted, MidpointRounding.AwayFromZero), issues.ToList());
    }

    public static HealthColor ColorOf(int score) => score switch
    {
        >= GreenThreshold => HealthColor.Green,
        >= OrangeThreshold => HealthColor.Orange,
        _ => HealthColor.Red,
    };

    public static HealthColor ColorOf(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Critical => HealthColor.Red,
        IssueSeverity.Warning => HealthColor.Orange,
        _ => HealthColor.Green,
    };

    public static HealthReport BuildReport(DateTimeOffset at, IReadOnlyList<HealthIssue> issues, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var ordered = issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Category)
            .ToList();
        var sub = ComputeSubScores(ordered);
        var score = ComputeScore(sub, ordered);
        return new HealthReport(at, score, ColorOf(score), sub, ordered, duration);
    }

    private static int Sub(IReadOnlyCollection<HealthIssue> issues, HealthCategory category)
    {
        var inCategory = issues.Where(i => i.Category == category).ToList();
        return CapBySeverity(100 - inCategory.Sum(i => Penalty(i.Severity)), inCategory);
    }

    private static int CapBySeverity(int score, IReadOnlyCollection<HealthIssue> issues)
    {
        if (issues.Any(i => i.Severity == IssueSeverity.Critical))
        {
            score = Math.Min(score, OrangeThreshold - 1);
        }
        else if (issues.Any(i => i.Severity == IssueSeverity.Warning))
        {
            score = Math.Min(score, GreenThreshold - 1);
        }

        return Math.Clamp(score, 0, 100);
    }
}
