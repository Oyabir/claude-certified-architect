using Microsoft.Extensions.DependencyInjection;
using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Scheduling;
using PcSante.Licensing;
using PcSante.Service.Data;
using PcSante.Service.Dispatch;
using PcSante.Service.Updates;

namespace PcSante.Service;

/// <summary>Opérations de licence : le service seul parle au serveur et détient le jeton.</summary>
public sealed class LicenseOperation(CommandId command, LicenseManager license) : IOperationHandler
{
    public CommandId Command { get; } = command;

    public async Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var result = Command switch
        {
            CommandId.ActivateLicense => await license.ActivateAsync(parameters.GetString("key"), cancellationToken).ConfigureAwait(false),
            CommandId.TransferLicense => await license.TransferAsync(parameters.GetString("key"), cancellationToken).ConfigureAwait(false),
            CommandId.DeactivateLicense => await license.DeactivateAsync(cancellationToken).ConfigureAwait(false),
            _ => await license.RevalidateAsync(cancellationToken).ConfigureAwait(false),
        };
        var data = CommandResult.WithData(result.Status, result.MessageKey);
        return result.Success
            ? data
            : data with { Status = CommandStatus.Failed, Reason = result.Reason };
    }
}

/// <summary>Bouton « Annuler » : rejoue la sauvegarde propre d'une action (journalisé par le cycle).</summary>
public sealed class UndoOperation(SystemActionPipeline pipeline, IServiceProvider services) : IOperationHandler
{
    public CommandId Command => CommandId.UndoAction;

    public bool SelfAudited => true;

    public Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var catalog = services.GetRequiredService<CommandCatalog>();
        return pipeline.UndoAsync(parameters.GetGuid("undoId"), catalog.GetAction, caller, cancellationToken);
    }
}

/// <summary>
/// Exécution d'un modèle de tâche planifiée (déclenché par le Planificateur Windows via le named pipe).
/// Chaque commande du modèle repasse par le répartiteur : catalogue, licence, cycle complet, audit.
/// </summary>
public sealed class RunTemplateOperation(IServiceProvider services, HistoryStore history, TimeProvider time) : IOperationHandler
{
    public CommandId Command => CommandId.RunScheduledTemplate;

    public bool UsesActionLock => false;

    public async Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var dispatcher = services.GetRequiredService<CommandDispatcher>();
        var template = ScheduledTemplates.Get(parameters.GetEnum<ScheduledTemplateId>("template"));
        var started = time.GetUtcNow();
        var ok = true;
        string lastKey = "Result_TaskRunSucceeded";
        foreach (var command in template.Commands)
        {
            // L'utilisateur a donné son accord en activant le modèle (confirmation demandée à ce moment-là).
            var result = await dispatcher.DispatchAsync(command.ToString(), null, confirmed: true, CallerIdentity.Scheduler, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                ok = false;
                lastKey = result.MessageKey;
            }
        }

        await history.AddTaskRunAsync(new TaskRunEntry(template.Id, started, time.GetUtcNow(), ok, ok ? "Result_TaskRunSucceeded" : lastKey), cancellationToken).ConfigureAwait(false);
        return ok ? CommandResult.Success("Result_TaskRunSucceeded") : CommandResult.Failure(FailureReason.ExecutionFailed, lastKey);
    }
}

public sealed class InstallUpdateOperation(AppUpdateService updates) : IOperationHandler
{
    public CommandId Command => CommandId.InstallAppUpdate;

    public Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken) =>
        updates.InstallAsync(cancellationToken);
}
