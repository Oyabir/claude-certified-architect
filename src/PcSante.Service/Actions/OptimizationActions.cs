using System.Globalization;
using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Optimization;
using PcSante.Core.Windows;

namespace PcSante.Service.Actions;

[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
internal sealed record StartupBackup(string Id, bool Enabled, string? UserSid);

/// <summary>Programme au démarrage : désactivation/réactivation réversible (clés StartupApproved).</summary>
public sealed class StartupItemAction(CommandId command, IStartupApi startup) : SystemAction
{
    public override CommandId Command { get; } = command;

    private bool Target => Command == CommandId.EnableStartupItem;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var items = await startup.ListAsync(context.Caller.UserSid, cancellationToken).ConfigureAwait(false);
        var item = items.FirstOrDefault(i => i.Id == context.Parameters.GetString("id"));
        if (item is null)
        {
            return CheckResult.Blocked(FailureReason.NotFound, "Result_StartupItemNotFound");
        }

        return item.Enabled == Target ? CheckResult.AlreadyDone("Result_AlreadyDone") : CheckResult.Proceed;
    }

    public override Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var id = context.Parameters.GetString("id");
        return Task.FromResult<BackupData?>(Backup.Of($"Démarrage : {id}", new StartupBackup(id, !Target, context.Caller.UserSid)));
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await startup.SetEnabledAsync(context.Parameters.GetString("id"), Target, context.Caller.UserSid, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok(Target ? "Result_StartupEnabled" : "Result_StartupDisabled")
            : ExecutionResult.Fail("Result_StartupFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var items = await startup.ListAsync(context.Caller.UserSid, cancellationToken).ConfigureAwait(false);
        return items.Any(i => i.Id == context.Parameters.GetString("id") && i.Enabled == Target);
    }

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        var b = Backup.Read<StartupBackup>(backup);
        return startup.SetEnabledAsync(b.Id, b.Enabled, b.UserSid, cancellationToken);
    }
}

[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
internal sealed record TaskBackup(string Path, bool Enabled);

public sealed class ThirdPartyTaskAction(CommandId command, IThirdPartyTaskApi tasks) : SystemAction
{
    public override CommandId Command { get; } = command;

    private bool Target => Command == CommandId.EnableThirdPartyTask;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var task = (await tasks.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(t => t.Path == context.Parameters.GetString("id"));
        if (task is null)
        {
            return CheckResult.Blocked(FailureReason.NotFound, "Result_TaskNotFound");
        }

        return task.Enabled == Target ? CheckResult.AlreadyDone("Result_AlreadyDone") : CheckResult.Proceed;
    }

    public override Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Parameters.GetString("id");
        return Task.FromResult<BackupData?>(Backup.Of($"Tâche : {path}", new TaskBackup(path, !Target)));
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await tasks.SetEnabledAsync(context.Parameters.GetString("id"), Target, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok(Target ? "Result_TaskEnabled" : "Result_TaskDisabled")
            : ExecutionResult.Fail("Result_TaskFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (await tasks.ListAsync(cancellationToken).ConfigureAwait(false)).Any(t => t.Path == context.Parameters.GetString("id") && t.Enabled == Target);
    }

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        var b = Backup.Read<TaskBackup>(backup);
        return tasks.SetEnabledAsync(b.Path, b.Enabled, cancellationToken);
    }
}

/// <summary>Nettoyage : irréversible, donc confirmé par l'utilisateur et précédé d'un point de restauration.</summary>
public sealed class CleanupAction(CommandId command, ICleanupApi cleanup) : SystemAction
{
    private long _before;
    private CleanupResult? _result;

    public override CommandId Command { get; } = command;

    public CleanupTarget Target => Command switch
    {
        CommandId.CleanTemporaryFiles => CleanupTarget.TemporaryFiles,
        CommandId.CleanWindowsUpdateCache => CleanupTarget.WindowsUpdateCache,
        CommandId.EmptyRecycleBin => CleanupTarget.RecycleBin,
        _ => CleanupTarget.BrowserCaches,
    };

    public static long BytesFor(CleanupEstimate estimate, CleanupTarget target) => target switch
    {
        CleanupTarget.TemporaryFiles => estimate.TemporaryFilesBytes,
        CleanupTarget.WindowsUpdateCache => estimate.WindowsUpdateCacheBytes,
        CleanupTarget.RecycleBin => estimate.RecycleBinBytes,
        _ => estimate.BrowserCachesBytes,
    };

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        _before = BytesFor(await cleanup.EstimateAsync(cancellationToken).ConfigureAwait(false), Target);
        return _before == 0 ? CheckResult.AlreadyDone("Result_NothingToClean") : CheckResult.Proceed;
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        _result = await cleanup.CleanAsync(Target, cancellationToken).ConfigureAwait(false);
        return ExecutionResult.Ok("Result_Cleaned", _result.BytesFreed.ToString(CultureInfo.InvariantCulture),
            _result.FilesSkipped.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Contrôle : l'espace occupé a diminué (les fichiers verrouillés peuvent rester).</summary>
    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var after = BytesFor(await cleanup.EstimateAsync(cancellationToken).ConfigureAwait(false), Target);
        return after < _before || (_result is { FilesDeleted: 0, FilesSkipped: > 0 } && after <= _before);
    }
}

public sealed class SetPowerPlanAction(IPowerApi power) : SystemAction
{
    public override CommandId Command => CommandId.SetPowerPlan;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var plan = context.Parameters.GetGuid("plan");
        var plans = await power.ListPlansAsync(cancellationToken).ConfigureAwait(false);
        if (plans.All(p => p.Id != plan))
        {
            return CheckResult.Blocked(FailureReason.NotFound, "Result_PowerPlanNotFound");
        }

        return await power.GetActivePlanAsync(cancellationToken).ConfigureAwait(false) == plan
            ? CheckResult.AlreadyDone("Result_AlreadyDone")
            : CheckResult.Proceed;
    }

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        await power.GetActivePlanAsync(cancellationToken).ConfigureAwait(false) is { } current
            ? new BackupData("Plan d'alimentation", current.ToString())
            : null;

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await power.SetActivePlanAsync(context.Parameters.GetGuid("plan"), cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_PowerPlanSet")
            : ExecutionResult.Fail("Result_PowerPlanFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await power.GetActivePlanAsync(cancellationToken).ConfigureAwait(false) == context.Parameters.GetGuid("plan");
    }

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return power.SetActivePlanAsync(Guid.Parse(backup.Payload), cancellationToken);
    }
}

/// <summary>Enregistre/retire un modèle de tâche planifiée dans le Planificateur Windows.</summary>
public sealed class ScheduleTemplateAction(CommandId command, Core.Scheduling.IScheduledTemplateApi scheduler) : SystemAction
{
    public override CommandId Command { get; } = command;

    private bool Enable => Command == CommandId.EnableScheduledTemplate;

    private static Core.Scheduling.ScheduledTemplateId Template(ActionContext c) =>
        c.Parameters.GetEnum<Core.Scheduling.ScheduledTemplateId>("template");

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = await scheduler.GetAsync(Template(context), cancellationToken).ConfigureAwait(false);
        if (Enable)
        {
            return current is not null && current == Core.Scheduling.ScheduledTemplates.FromParameters(context.Parameters)
                ? CheckResult.AlreadyDone("Result_AlreadyDone")
                : CheckResult.Proceed;
        }

        return current is null ? CheckResult.AlreadyDone("Result_AlreadyDone") : CheckResult.Proceed;
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ok = Enable
            ? await scheduler.RegisterAsync(Template(context), Core.Scheduling.ScheduledTemplates.FromParameters(context.Parameters), cancellationToken).ConfigureAwait(false)
            : await scheduler.UnregisterAsync(Template(context), cancellationToken).ConfigureAwait(false);
        return ok
            ? ExecutionResult.Ok(Enable ? "Result_TaskScheduled" : "Result_TaskUnscheduled")
            : ExecutionResult.Fail("Result_TaskFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = await scheduler.GetAsync(Template(context), cancellationToken).ConfigureAwait(false);
        return Enable ? current is not null : current is null;
    }
}

/// <summary>
/// Profil de services (M4) : services automatiques de la liste prudente passés en « Manuel » (jamais désactivés).
/// Point de restauration et sauvegarde des modes précédents : « Annuler » les rétablit.
/// </summary>
public sealed class ServiceProfileAction(IServiceControlApi services) : SystemAction
{
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    private sealed record PreviousModes(Dictionary<string, ServiceStartMode> Modes);

    public override CommandId Command => CommandId.ApplyServiceProfile;

    /// <summary>Services du profil qui démarrent automatiquement aujourd'hui (absents du PC ignorés).</summary>
    public static async Task<IReadOnlyList<ServiceChange>> ChangesAsync(IServiceControlApi services, ServiceProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        var changes = new List<ServiceChange>();
        foreach (var name in ServiceProfiles.ServicesOf(profile))
        {
            if (await services.GetAsync(name, ct).ConfigureAwait(false) is { } info && ServiceProfiles.ShouldChange(info))
            {
                changes.Add(new ServiceChange(info.Name, info.DisplayName, info.StartMode));
            }
        }

        return changes;
    }

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await ChangesAsync(services, Profile(context), cancellationToken).ConfigureAwait(false)).Count == 0
            ? CheckResult.AlreadyDone("Result_ProfileNothingToDo")
            : CheckResult.Proceed;

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var changes = await ChangesAsync(services, Profile(context), cancellationToken).ConfigureAwait(false);
        return Backup.Of("Services", new PreviousModes(changes.ToDictionary(c => c.Name, c => c.From)));
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var changes = await ChangesAsync(services, Profile(context), cancellationToken).ConfigureAwait(false);
        foreach (var change in changes)
        {
            if (!await services.SetStartModeAsync(change.Name, ServiceStartMode.Manual, cancellationToken).ConfigureAwait(false))
            {
                return ExecutionResult.Fail("Result_ProfileFailed", change.Name);
            }
        }

        return new ExecutionResult(true, "Result_ProfileApplied", MessageArgs: [changes.Count.ToString(CultureInfo.InvariantCulture)]);
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await ChangesAsync(services, Profile(context), cancellationToken).ConfigureAwait(false)).Count == 0;

    public override async Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        var ok = true;
        foreach (var (name, mode) in Backup.Read<PreviousModes>(backup).Modes)
        {
            ok &= await services.SetStartModeAsync(name, mode, cancellationToken).ConfigureAwait(false);
        }

        return ok;
    }

    private static ServiceProfile Profile(ActionContext context) => context.Parameters.GetEnum<ServiceProfile>("profile");
}

/// <summary>
/// Effets visuels allégés pour l'utilisateur appelant (son profil, pas celui de SYSTEM) ; effet à la prochaine
/// ouverture de session. Point de restauration et sauvegarde des valeurs précédentes pour « Annuler ».
/// </summary>
public sealed class LightenVisualEffectsAction(IVisualEffectsApi visualEffects) : SystemAction
{
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    private sealed record Previous(string Sid, VisualEffectsSettings Settings);

    public override CommandId Command => CommandId.LightenVisualEffects;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Caller.UserSid is not { } sid || await visualEffects.ReadAsync(sid, cancellationToken).ConfigureAwait(false) is not { } current)
        {
            return Checks.NotAvailable("Result_VisualEffectsUnavailable");
        }

        return current.IsLight ? CheckResult.AlreadyDone("Result_VisualEffectsAlreadyLight") : CheckResult.Proceed;
    }

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sid = context.Caller.UserSid!;
        return await visualEffects.ReadAsync(sid, cancellationToken).ConfigureAwait(false) is { } current
            ? Backup.Of("Effets visuels", new Previous(sid, current))
            : null;
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await visualEffects.WriteAsync(context.Caller.UserSid!, VisualEffectsSettings.Light, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_VisualEffectsLightened")
            : ExecutionResult.Fail("Result_VisualEffectsUnavailable");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await visualEffects.ReadAsync(context.Caller.UserSid!, cancellationToken).ConfigureAwait(false) is { IsLight: true };
    }

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        var previous = Backup.Read<Previous>(backup);
        return visualEffects.WriteAsync(previous.Sid, previous.Settings, cancellationToken);
    }
}

/// <summary>Optimisation du disque système (TRIM ou défragmentation selon le support), en arrière-plan.</summary>
public sealed class OptimizeSystemDriveAction(IDiskOptimizationApi disk, BackgroundJobs jobs) : SystemAction
{
    public override CommandId Command => CommandId.OptimizeSystemDrive;

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.IsRunning(BackgroundJobs.DiskOptimizationJob)
            ? CheckResult.Blocked(FailureReason.PreconditionFailed, "Result_DiskOptimizationRunning")
            : CheckResult.Proceed);

    public override Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = jobs.TryStart(BackgroundJobs.DiskOptimizationJob, Command, context.Caller,
            ct => disk.OptimizeSystemDriveAsync(ct), "Result_DiskOptimized", "Result_DiskOptimizationFailed");
        return Task.FromResult(started
            ? ExecutionResult.Background("Result_DiskOptimizationStarted")
            : ExecutionResult.Fail("Result_DiskOptimizationRunning"));
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Fichier d'échange rendu à la gestion automatique de Windows (conseillé) ; effet au redémarrage, annulable.</summary>
public sealed class PageFileAutomaticAction(IDiskOptimizationApi disk) : SystemAction
{
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    private sealed record Previous(bool Automatic);

    public override CommandId Command => CommandId.SetPageFileAutomatic;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        await disk.GetSystemDiskAsync(cancellationToken).ConfigureAwait(false) switch
        {
            null => Checks.NotAvailable("Result_PageFileUnavailable"),
            { PageFileAutomatic: true } => CheckResult.AlreadyDone("Result_PageFileAlreadyAutomatic"),
            _ => CheckResult.Proceed,
        };

    public override Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BackupData?>(Backup.Of("Fichier d'échange", new Previous(false)));

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await disk.SetPageFileAutomaticAsync(true, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_PageFileAutomatic")
            : ExecutionResult.Fail("Result_PageFileUnavailable");

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        await disk.GetSystemDiskAsync(cancellationToken).ConfigureAwait(false) is { PageFileAutomatic: true };

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken) =>
        disk.SetPageFileAutomaticAsync(Backup.Read<Previous>(backup).Automatic, cancellationToken);
}
