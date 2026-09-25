using System.Globalization;
using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Windows;

namespace PcSante.Service.Actions;

public sealed class SearchUpdatesAction(IWindowsUpdateApi updates) : SystemAction
{
    public override CommandId Command => CommandId.SearchUpdates;

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(CheckResult.Proceed);

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var found = await updates.SearchAsync(cancellationToken).ConfigureAwait(false);
        return found.Count == 0
            ? ExecutionResult.Ok("Result_NoUpdates")
            : ExecutionResult.Ok("Result_UpdatesFound", found.Count.ToString(CultureInfo.InvariantCulture));
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

public sealed class InstallUpdatesAction(IWindowsUpdateApi updates) : SystemAction
{
    public override CommandId Command => CommandId.InstallUpdates;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await updates.SearchAsync(cancellationToken).ConfigureAwait(false)).Count == 0
            ? CheckResult.AlreadyDone("Result_NoUpdates")
            : CheckResult.Proceed;

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var result = await updates.InstallAsync(cancellationToken).ConfigureAwait(false);
        if (result.Installed == 0 && result.Failed > 0)
        {
            return ExecutionResult.Fail("Result_UpdatesFailed");
        }

        var key = result.RebootRequired ? "Result_UpdatesInstalledReboot" : "Result_UpdatesInstalled";
        return ExecutionResult.Ok(key, result.Installed.ToString(CultureInfo.InvariantCulture), result.Failed.ToString(CultureInfo.InvariantCulture));
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Réparation d'une mise à jour bloquée : le cache est renommé (sauvegarde), jamais supprimé.</summary>
public sealed class RepairWindowsUpdateAction(IWindowsUpdateApi updates, TimeProvider time) : SystemAction
{
    // Les actions sont exécutées une à une (verrou global) : l'état entre les étapes est sûr.
    private string? _backupPath;

    public override CommandId Command => CommandId.RepairWindowsUpdate;

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(CheckResult.Proceed);

    public override Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        _backupPath = updates.ProposeBackupPath(time.GetUtcNow());
        return Task.FromResult<BackupData?>(new BackupData("Windows Update (cache)", _backupPath));
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var backupPath = _backupPath ?? updates.ProposeBackupPath(time.GetUtcNow());
        return await updates.ResetComponentsAsync(backupPath, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_UpdateRepaired")
            : ExecutionResult.Fail("Result_UpdateRepairFailed");
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        updates.AreServicesHealthyAsync(cancellationToken);

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return updates.RestoreComponentsAsync(backup.Payload, cancellationToken);
    }
}

public sealed class RepairAction(CommandId command, ISystemRepairApi repair) : SystemAction
{
    public override CommandId Command { get; } = command;

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(CheckResult.Proceed);

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var result = Command == CommandId.RunSystemFileCheck
            ? await repair.RunSystemFileCheckAsync(cancellationToken).ConfigureAwait(false)
            : await repair.RunDismRestoreHealthAsync(cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            RepairOutcome.NoProblemFound => ExecutionResult.Ok("Result_RepairNothingFound"),
            RepairOutcome.Repaired => new ExecutionResult(true, "Result_RepairDone", result.Summary),
            RepairOutcome.ProblemsRemain => new ExecutionResult(true, Command == CommandId.RunSystemFileCheck ? "Result_RepairRemainsTryDism" : "Result_RepairRemains", result.Summary),
            _ => ExecutionResult.Fail("Result_RepairFailed", result.Summary),
        };
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

public sealed class CreateRestorePointAction(IRestorePointApi restore, TimeProvider time) : SystemAction
{
    public override CommandId Command => CommandId.CreateRestorePoint;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        await restore.IsEnabledAsync(cancellationToken).ConfigureAwait(false)
            ? CheckResult.Proceed
            : CheckResult.Blocked(FailureReason.PreconditionFailed, "Result_RestoreDisabled");

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await restore.CreateAsync($"{Core.ProductInfo.Name}", cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_RestorePointCreated")
            : ExecutionResult.Fail("Result_RestorePointFailed");

    /// <summary>Windows n'autorise qu'un point par 24 h : un point de moins de 24 h suffit.</summary>
    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return (await restore.ListAsync(cancellationToken).ConfigureAwait(false)).Any(p => now - p.CreatedAt < TimeSpan.FromHours(24));
    }
}

public sealed class EnableSystemRestoreAction(IRestorePointApi restore) : SystemAction
{
    public override CommandId Command => CommandId.EnableSystemRestore;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        await restore.IsEnabledAsync(cancellationToken).ConfigureAwait(false)
            ? CheckResult.AlreadyDone("Result_AlreadyDone")
            : CheckResult.Proceed;

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await restore.EnableAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_RestoreEnabled")
            : ExecutionResult.Fail("Result_RestoreEnableFailed");

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        restore.IsEnabledAsync(cancellationToken);
}

/// <summary>Vidage du cache DNS : corrige les sites qui ne s'ouvrent plus après un changement d'adresse.</summary>
public sealed class FlushDnsAction(INetworkRepairApi network) : SystemAction
{
    public override CommandId Command => CommandId.FlushDnsCache;

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(CheckResult.Proceed);

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await network.FlushDnsAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_DnsFlushed")
            : ExecutionResult.Fail("Result_NetworkRepairFailed");

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>
/// Réinitialisation de Winsock et TCP/IP (point de restauration avant). Effet au redémarrage : le résultat le dit,
/// le PC n'est jamais redémarré sans l'utilisateur.
/// </summary>
public sealed class ResetNetworkStackAction(INetworkRepairApi network) : SystemAction
{
    public override CommandId Command => CommandId.ResetNetworkStack;

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(CheckResult.Proceed);

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await network.ResetNetworkStackAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_NetworkResetRestart")
            : ExecutionResult.Fail("Result_NetworkRepairFailed");

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Désactivation du compte Invité (réversible : l'état précédent est sauvegardé pour « Annuler »).</summary>
public sealed class DisableGuestAccountAction(ILocalAccountsApi accounts) : SystemAction
{
    private sealed record GuestState(string Sid);

    public override CommandId Command => CommandId.DisableGuestAccount;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var guest = await GuestAsync(cancellationToken).ConfigureAwait(false);
        return guest switch
        {
            null => Checks.NotAvailable("Result_NoGuestAccount"),
            { Enabled: false } => CheckResult.AlreadyDone("Result_GuestAlreadyDisabled"),
            _ => CheckResult.Proceed,
        };
    }

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        await GuestAsync(cancellationToken).ConfigureAwait(false) is { } guest ? Backup.Of("Compte Invité", new GuestState(guest.Sid)) : null;

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await GuestAsync(cancellationToken).ConfigureAwait(false) is { } guest && await accounts.SetEnabledAsync(guest.Sid, false, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_GuestDisabled")
            : ExecutionResult.Fail("Result_AccountFailed");

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        await GuestAsync(cancellationToken).ConfigureAwait(false) is { Enabled: false };

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken) =>
        accounts.SetEnabledAsync(Backup.Read<GuestState>(backup).Sid, true, cancellationToken);

    private async Task<LocalAccount?> GuestAsync(CancellationToken cancellationToken) =>
        (await accounts.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(a => a.IsGuest);
}

/// <summary>
/// Chiffrement BitLocker du disque système : administrateur seulement, puce TPM prête, et clé de récupération
/// existante ET enregistrée par l'utilisateur (paramètre keySaved, donné par l'interface après l'enregistrement).
/// Le chiffrement se poursuit en arrière-plan ; il ne se défait pas par un point de restauration.
/// </summary>
public sealed class EnableBitLockerAction(IBitLockerApi bitLocker, ILocalAccountsApi accounts) : SystemAction
{
    public override CommandId Command => CommandId.EnableBitLocker;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var status = await bitLocker.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.Supported)
        {
            return Checks.NotAvailable("Result_BitLockerNotSupported");
        }

        if (!await accounts.IsAdministratorAsync(context.Caller.UserSid, cancellationToken).ConfigureAwait(false))
        {
            return CheckResult.Blocked(FailureReason.PreconditionFailed, "Result_AdminRequired");
        }

        if (status.State is BitLockerState.On or BitLockerState.Encrypting)
        {
            return CheckResult.AlreadyDone("Result_BitLockerAlreadyOn");
        }

        if (status.State != BitLockerState.Off)
        {
            return CheckResult.Blocked(FailureReason.PreconditionFailed, "Result_BitLockerBusy");
        }

        if (!status.TpmReady)
        {
            return Checks.NotAvailable("Result_TpmNotReady");
        }

        return status.HasRecoveryKey && context.Parameters.GetBool("keySaved")
            ? CheckResult.Proceed
            : CheckResult.Blocked(FailureReason.PreconditionFailed, "Result_BitLockerKeyFirst");
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await bitLocker.StartEncryptionAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Background("Result_BitLockerStarted")
            : ExecutionResult.Fail("Result_BitLockerFailed");

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await bitLocker.GetStatusAsync(cancellationToken).ConfigureAwait(false)).State is BitLockerState.Encrypting or BitLockerState.On;
}
