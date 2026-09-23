using System.Globalization;
using PcSante.Core.Audit;
using PcSante.Core.Commands;

namespace PcSante.Core.Actions;

/// <summary>
/// Cycle obligatoire de toute action système (sections 4 et 13) :
/// 1. vérifier, 2. point de restauration ou sauvegarde, 3. exécuter, 4. contrôler, 5. journaliser.
/// Si la sauvegarde échoue, rien n'est exécuté. Si le contrôle échoue et qu'une sauvegarde propre existe,
/// l'état précédent est restauré automatiquement.
/// </summary>
public sealed class SystemActionPipeline(
    RestorePointGuard restorePoints,
    IUndoStore undoStore,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<CommandResult> RunAsync(SystemAction action, ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        var descriptor = context.Descriptor;
        if (action.Command != descriptor.Id)
        {
            throw new InvalidOperationException("Action et descripteur incohérents.");
        }

        // 1. Vérifier
        CheckResult check;
        try
        {
            check = await action.CheckAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await FinishAsync(context, AuditOutcome.Failed, CommandResult.Failure(
                FailureReason.PreconditionFailed, "Result_CheckFailed", ex.Message), cancellationToken).ConfigureAwait(false);
        }

        switch (check.Outcome)
        {
            case CheckOutcome.AlreadyDone:
                return await FinishAsync(context, AuditOutcome.AlreadyDone, new CommandResult
                {
                    Status = CommandStatus.AlreadyDone,
                    MessageKey = check.MessageKey ?? "Result_AlreadyDone",
                }, cancellationToken).ConfigureAwait(false);
            case CheckOutcome.Blocked:
                return await FinishAsync(context, AuditOutcome.Refused, CommandResult.Refused(
                    check.Reason == FailureReason.None ? FailureReason.PreconditionFailed : check.Reason,
                    check.MessageKey ?? "Result_Blocked",
                    check.Details), cancellationToken).ConfigureAwait(false);
        }

        // 2. Point de restauration et/ou sauvegarde propre
        if (descriptor.Safeguard is SafeguardKind.RestorePoint or SafeguardKind.RestorePointAndOwnBackup)
        {
            var label = string.Create(CultureInfo.InvariantCulture, $"{ProductInfo.Name} - {descriptor.Id}");
            bool ok;
            try
            {
                ok = await restorePoints.EnsureAsync(label, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ok = false;
                label = ex.Message;
            }

            if (!ok)
            {
                return await FinishAsync(context, AuditOutcome.Refused, CommandResult.Failure(
                    FailureReason.SafeguardFailed, "Result_RestorePointFailed", label), cancellationToken).ConfigureAwait(false);
            }
        }

        BackupData? backup = null;
        Guid? undoId = null;
        if (descriptor.IsUndoable)
        {
            try
            {
                backup = await action.BackupAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FinishAsync(context, AuditOutcome.Refused, CommandResult.Failure(
                    FailureReason.SafeguardFailed, "Result_BackupFailed", ex.Message), cancellationToken).ConfigureAwait(false);
            }

            if (backup is null)
            {
                return await FinishAsync(context, AuditOutcome.Refused, CommandResult.Failure(
                    FailureReason.SafeguardFailed, "Result_BackupFailed"), cancellationToken).ConfigureAwait(false);
            }

            undoId = await undoStore.SaveAsync(descriptor.Id, backup, cancellationToken).ConfigureAwait(false);
        }

        // 3. Exécuter
        ExecutionResult execution;
        try
        {
            execution = await action.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            execution = ExecutionResult.Fail("Result_ExecutionFailed", ex.Message);
        }

        if (!execution.Success)
        {
            var rolledBack = await TryRollbackAsync(action, backup, undoId, cancellationToken).ConfigureAwait(false);
            return await FinishAsync(context, rolledBack ? AuditOutcome.RolledBack : AuditOutcome.Failed, CommandResult.Failure(
                FailureReason.ExecutionFailed, execution.MessageKey, execution.Details), cancellationToken).ConfigureAwait(false);
        }

        if (execution.StartedInBackground)
        {
            return await FinishAsync(context, AuditOutcome.Started, new CommandResult
            {
                Status = CommandStatus.Started,
                MessageKey = execution.MessageKey,
                MessageArgs = execution.MessageArgs ?? [],
                UndoId = undoId,
            }, cancellationToken).ConfigureAwait(false);
        }

        // 4. Contrôler
        bool verified;
        try
        {
            verified = await action.VerifyAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            verified = false;
            execution = execution with { Details = ex.Message };
        }

        if (!verified)
        {
            var rolledBack = await TryRollbackAsync(action, backup, undoId, cancellationToken).ConfigureAwait(false);
            return await FinishAsync(context, rolledBack ? AuditOutcome.RolledBack : AuditOutcome.Failed, CommandResult.Failure(
                FailureReason.VerificationFailed,
                rolledBack ? "Result_VerificationFailedRolledBack" : "Result_VerificationFailed",
                execution.Details), cancellationToken).ConfigureAwait(false);
        }

        // 5. Journaliser (dans FinishAsync)
        return await FinishAsync(context, AuditOutcome.Succeeded, new CommandResult
        {
            Status = CommandStatus.Succeeded,
            MessageKey = execution.MessageKey,
            MessageArgs = execution.MessageArgs ?? [],
            TechnicalDetails = execution.Details,
            UndoId = undoId,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Annule une action à partir de sa sauvegarde (bouton « Annuler »).</summary>
    public async Task<CommandResult> UndoAsync(Guid undoId, Func<CommandId, SystemAction?> resolveAction, CallerIdentity caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolveAction);
        var context = new ActionContext(CommandDefinitions.Get(CommandId.UndoAction), CommandParameters.Empty, caller);
        var record = await undoStore.GetAsync(undoId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Undone)
        {
            return await FinishAsync(context, AuditOutcome.Refused,
                CommandResult.Refused(FailureReason.NotFound, "Result_UndoNotFound"), cancellationToken).ConfigureAwait(false);
        }

        var action = resolveAction(record.Command);
        if (action is null)
        {
            return await FinishAsync(context, AuditOutcome.Refused,
                CommandResult.Refused(FailureReason.NotFound, "Result_UndoNotFound"), cancellationToken).ConfigureAwait(false);
        }

        bool ok;
        string? details = null;
        try
        {
            ok = await action.UndoAsync(record.Backup, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ok = false;
            details = ex.Message;
        }

        if (ok)
        {
            await undoStore.MarkUndoneAsync(undoId, cancellationToken).ConfigureAwait(false);
        }

        return await FinishAsync(context, ok ? AuditOutcome.Succeeded : AuditOutcome.Failed,
            ok ? CommandResult.Success("Result_Undone", record.Backup.Description)
               : CommandResult.Failure(FailureReason.ExecutionFailed, "Result_UndoFailed", details),
            cancellationToken, $"undo:{record.Command}").ConfigureAwait(false);
    }

    private async Task<bool> TryRollbackAsync(SystemAction action, BackupData? backup, Guid? undoId, CancellationToken cancellationToken)
    {
        if (backup is null || undoId is null)
        {
            return false;
        }

        try
        {
            if (await action.UndoAsync(backup, cancellationToken).ConfigureAwait(false))
            {
                await undoStore.MarkUndoneAsync(undoId.Value, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le retour arrière a échoué : le point de restauration reste la solution de secours.
        }

        return false;
    }

    private async Task<CommandResult> FinishAsync(
        ActionContext context,
        AuditOutcome outcome,
        CommandResult result,
        CancellationToken cancellationToken,
        string? commandLabel = null)
    {
        await audit.WriteAsync(new AuditEntry
        {
            Timestamp = time.GetUtcNow(),
            Who = context.Caller.UserName,
            ClientProcessId = context.Caller.ProcessId,
            Command = commandLabel ?? context.Descriptor.Id.ToString(),
            Parameters = AuditFormatting.FormatParameters(context.Parameters),
            Outcome = outcome,
            Reason = result.Reason == FailureReason.None ? result.MessageKey : $"{result.Reason}:{result.MessageKey}",
            Details = result.TechnicalDetails,
            UndoId = result.UndoId,
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
