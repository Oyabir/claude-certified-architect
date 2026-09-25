using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.Core.Health;

public enum HealthCategory
{
    Security,
    Performance,
    Stability,
    Storage,
}

/// <summary>Gravité, directement liée au code couleur (section 11).</summary>
public enum IssueSeverity
{
    /// <summary>Information utile, n'affecte presque pas le score.</summary>
    Info,

    /// <summary>Orange : à surveiller.</summary>
    Warning,

    /// <summary>Rouge : à corriger.</summary>
    Critical,
}

public enum HealthColor
{
    Green,
    Orange,
    Red,
}

/// <summary>Correction proposée : une commande du catalogue ou, à défaut, l'écran où agir.</summary>
public sealed record IssueFix(CommandId? Command, IReadOnlyDictionary<string, string>? Parameters, ScreenId? Screen)
{
    public static IssueFix Run(CommandId command, IReadOnlyDictionary<string, string>? parameters = null) =>
        new(command, parameters, null);

    public static IssueFix Open(ScreenId screen) => new(null, null, screen);
}

/// <summary>
/// Problème détecté. Format imposé (section 11) : ce qui ne va pas (<c>Issue_{Code}_Title</c>),
/// pourquoi c'est important (<c>Issue_{Code}_Why</c>), bouton « Corriger » (<see cref="Fix"/>).
/// </summary>
public sealed record HealthIssue(
    string Code,
    HealthCategory Category,
    IssueSeverity Severity,
    IssueFix? Fix,
    IReadOnlyList<string> Args)
{
    public string TitleKey => $"Issue_{Code}_Title";

    public string WhyKey => $"Issue_{Code}_Why";
}

public sealed record SubScores(int Security, int Performance, int Stability, int Storage);

public sealed record HealthReport(
    DateTimeOffset AnalyzedAt,
    int Score,
    HealthColor Color,
    SubScores SubScores,
    IReadOnlyList<HealthIssue> Issues,
    TimeSpan Duration);

/// <summary>Faits mesurés sur le PC. Une donnée null = inconnue : aucune règle ne conclut sur de l'inconnu.</summary>
public sealed record SystemSnapshot
{
    public required DateTimeOffset CollectedAt { get; init; }

    public DefenderStatus? Defender { get; init; }

    public IReadOnlyList<FirewallProfileStatus>? Firewall { get; init; }

    public UpdateStatus? Updates { get; init; }

    public bool? RestoreEnabled { get; init; }

    /// <summary>Compte Invité intégré actif (null si inconnu).</summary>
    public bool? GuestAccountEnabled { get; init; }

    public DateTimeOffset? LastRestorePointAt { get; init; }

    public int? CrashesLast30Days { get; init; }

    public int? EnabledStartupItems { get; init; }

    public DriveSpace? SystemDrive { get; init; }

    public long? CleanableBytes { get; init; }

    public double? MemoryPercent { get; init; }
}

public sealed record ScorePoint(DateTimeOffset At, int Score, SubScores SubScores);
