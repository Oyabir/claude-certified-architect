using PcSante.Core.Commands;

namespace PcSante.Core.Scheduling;

/// <summary>Modèles de tâches (section 4, M9) : les 3 du MVP, puis les modèles V2.</summary>
public enum ScheduledTemplateId
{
    DailyAntivirusScan,
    WeeklyCleanup,
    WeeklyRestorePoint,
    WeeklyUpdateCheck,
    MonthlyReport,
}

/// <param name="Day">Jour de la semaine (hebdomadaire) ; null = chaque jour, ou le 1er du mois si <paramref name="Monthly"/>.</param>
/// <param name="Monthly">Exécution le 1er de chaque mois.</param>
/// <param name="Language">Langue des documents produits par la tâche (rapport mensuel) : fr, en ou ar.</param>
public sealed record ScheduleSettings(DayOfWeek? Day, TimeOnly Time, bool OnlyWhenIdle, bool OnlyOnAcPower, bool Monthly = false, string? Language = null)
{
    public const string MonthStart = "MonthStart";

    public bool IsDaily => Day is null && !Monthly;
}

public sealed record TemplateDefinition(ScheduledTemplateId Id, IReadOnlyList<CommandId> Commands, ScheduleSettings Default);

public sealed record TemplateState(ScheduledTemplateId Id, bool Enabled, ScheduleSettings Settings, DateTimeOffset? LastRunAt, bool? LastRunSucceeded);

/// <summary>Vue d'un modèle de tâche : définition, réglages, dernière exécution.</summary>
public sealed record TemplateView(ScheduledTemplateId Id, bool Enabled, ScheduleSettings Settings, IReadOnlyList<CommandId> Commands, TaskRunEntry? LastRun);

public sealed record TaskRunEntry(ScheduledTemplateId Template, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, bool Succeeded, string MessageKey);

public static class ScheduledTemplates
{
    public static IReadOnlyList<TemplateDefinition> All { get; } =
    [
        new(ScheduledTemplateId.DailyAntivirusScan, [CommandId.StartQuickScan],
            new ScheduleSettings(null, new TimeOnly(12, 30), OnlyWhenIdle: true, OnlyOnAcPower: true)),
        new(ScheduledTemplateId.WeeklyCleanup, [CommandId.CleanTemporaryFiles, CommandId.EmptyRecycleBin],
            new ScheduleSettings(DayOfWeek.Sunday, new TimeOnly(11, 0), OnlyWhenIdle: true, OnlyOnAcPower: true)),
        new(ScheduledTemplateId.WeeklyRestorePoint, [CommandId.CreateRestorePoint],
            new ScheduleSettings(DayOfWeek.Monday, new TimeOnly(10, 0), OnlyWhenIdle: false, OnlyOnAcPower: false)),
        new(ScheduledTemplateId.WeeklyUpdateCheck, [CommandId.SearchUpdates],
            new ScheduleSettings(DayOfWeek.Wednesday, new TimeOnly(12, 0), OnlyWhenIdle: false, OnlyOnAcPower: false)),
        new(ScheduledTemplateId.MonthlyReport, [CommandId.GenerateMonthlyReport],
            new ScheduleSettings(null, new TimeOnly(9, 0), OnlyWhenIdle: false, OnlyOnAcPower: false, Monthly: true)),
    ];

    /// <summary>Les 3 tâches proposées au premier lancement (section 11) ; les autres restent au choix.</summary>
    public static IReadOnlyList<TemplateDefinition> Recommended { get; } =
        All.Where(t => t.Id is ScheduledTemplateId.DailyAntivirusScan or ScheduledTemplateId.WeeklyCleanup or ScheduledTemplateId.WeeklyRestorePoint).ToList();

    public static TemplateDefinition Get(ScheduledTemplateId id) => All.Single(t => t.Id == id);

    /// <summary>Convertit les paramètres de commande validés (day, time, onlyWhenIdle, onlyOnAcPower, language).</summary>
    public static ScheduleSettings FromParameters(CommandParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var day = parameters.GetString("day");
        return new ScheduleSettings(
            day is "Everyday" or ScheduleSettings.MonthStart ? null : Enum.Parse<DayOfWeek>(day),
            parameters.GetTime("time"),
            parameters.GetBool("onlyWhenIdle"),
            parameters.GetBool("onlyOnAcPower"),
            Monthly: day == ScheduleSettings.MonthStart,
            Language: parameters.GetOptionalString("language"));
    }
}

/// <summary>Enregistrement des modèles dans le Planificateur de tâches Windows.</summary>
public interface IScheduledTemplateApi
{
    Task<bool> RegisterAsync(ScheduledTemplateId template, ScheduleSettings settings, CancellationToken cancellationToken);

    Task<bool> UnregisterAsync(ScheduledTemplateId template, CancellationToken cancellationToken);

    Task<ScheduleSettings?> GetAsync(ScheduledTemplateId template, CancellationToken cancellationToken);
}
