using PcSante.Core.Commands;

namespace PcSante.Core.Scheduling;

/// <summary>Les 3 modèles de tâches du MVP (section 7).</summary>
public enum ScheduledTemplateId
{
    DailyAntivirusScan,
    WeeklyCleanup,
    WeeklyRestorePoint,
}

public sealed record ScheduleSettings(DayOfWeek? Day, TimeOnly Time, bool OnlyWhenIdle, bool OnlyOnAcPower)
{
    public bool IsDaily => Day is null;
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
    ];

    public static TemplateDefinition Get(ScheduledTemplateId id) => All.Single(t => t.Id == id);

    /// <summary>Convertit les paramètres de commande validés (day, time, onlyWhenIdle, onlyOnAcPower).</summary>
    public static ScheduleSettings FromParameters(CommandParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var day = parameters.GetString("day");
        return new ScheduleSettings(
            day == "Everyday" ? null : Enum.Parse<DayOfWeek>(day),
            parameters.GetTime("time"),
            parameters.GetBool("onlyWhenIdle"),
            parameters.GetBool("onlyOnAcPower"));
    }
}

/// <summary>Enregistrement des modèles dans le Planificateur de tâches Windows.</summary>
public interface IScheduledTemplateApi
{
    Task<bool> RegisterAsync(ScheduledTemplateId template, ScheduleSettings settings, CancellationToken cancellationToken);

    Task<bool> UnregisterAsync(ScheduledTemplateId template, CancellationToken cancellationToken);

    Task<ScheduleSettings?> GetAsync(ScheduledTemplateId template, CancellationToken cancellationToken);
}
