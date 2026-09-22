using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Windows;

namespace PcSante.Service.Actions;

/// <summary>Base des protections Defender activables en un clic (temps réel, cloud, dossiers contrôlés).</summary>
public abstract class DefenderProtectionAction(IDefenderApi defender) : SystemAction
{
    protected IDefenderApi Defender { get; } = defender;

    protected abstract bool IsEnabled(DefenderStatus status);

    protected abstract Task<bool> EnableAsync(CancellationToken ct);

    protected abstract Task<bool> RestoreAsync(DefenderPreferences previous, CancellationToken ct);

    protected abstract string SuccessKey { get; }

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var status = await Defender.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || !status.IsActiveAntivirus)
        {
            return Checks.NotAvailable("Result_DefenderNotActive");
        }

        return IsEnabled(status) ? CheckResult.AlreadyDone("Result_AlreadyProtected") : CheckResult.Proceed;
    }

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        Backup.Of("Defender", await Defender.GetPreferencesAsync(cancellationToken).ConfigureAwait(false));

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await EnableAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok(SuccessKey)
            : ExecutionResult.Fail("Result_DefenderRefused");

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        IsEnabled(await Defender.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken) =>
        RestoreAsync(Backup.Read<DefenderPreferences>(backup), cancellationToken);
}

public sealed class EnableRealtimeProtectionAction(IDefenderApi defender) : DefenderProtectionAction(defender)
{
    public override CommandId Command => CommandId.EnableRealtimeProtection;

    protected override string SuccessKey => "Result_RealtimeEnabled";

    protected override bool IsEnabled(DefenderStatus status) => status.RealTimeProtectionEnabled;

    protected override Task<bool> EnableAsync(CancellationToken ct) => Defender.SetRealTimeProtectionAsync(true, ct);

    protected override Task<bool> RestoreAsync(DefenderPreferences previous, CancellationToken ct) =>
        Defender.SetRealTimeProtectionAsync(previous.RealTimeProtection, ct);
}

public sealed class EnableCloudProtectionAction(IDefenderApi defender) : DefenderProtectionAction(defender)
{
    public override CommandId Command => CommandId.EnableCloudProtection;

    protected override string SuccessKey => "Result_CloudEnabled";

    protected override bool IsEnabled(DefenderStatus status) => status.CloudProtectionEnabled;

    protected override Task<bool> EnableAsync(CancellationToken ct) => Defender.SetCloudReportingLevelAsync(2, ct);

    protected override Task<bool> RestoreAsync(DefenderPreferences previous, CancellationToken ct) =>
        Defender.SetCloudReportingLevelAsync(previous.CloudReportingLevel, ct);
}

public sealed class EnableControlledFolderAccessAction(IDefenderApi defender) : DefenderProtectionAction(defender)
{
    public override CommandId Command => CommandId.EnableControlledFolderAccess;

    protected override string SuccessKey => "Result_RansomwareEnabled";

    protected override bool IsEnabled(DefenderStatus status) => status.ControlledFolderAccessEnabled;

    protected override Task<bool> EnableAsync(CancellationToken ct) => Defender.SetControlledFolderAccessAsync(1, ct);

    protected override Task<bool> RestoreAsync(DefenderPreferences previous, CancellationToken ct) =>
        Defender.SetControlledFolderAccessAsync(previous.ControlledFolderAccess, ct);
}

/// <summary>Scans : lancés en arrière-plan, résultat consigné dans le journal à la fin.</summary>
public sealed class ScanAction(CommandId command, IDefenderApi defender, BackgroundJobs jobs) : SystemAction
{
    public override CommandId Command { get; } = command;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var status = await defender.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || !status.IsActiveAntivirus)
        {
            return Checks.NotAvailable("Result_DefenderNotActive");
        }

        return jobs.IsRunning(BackgroundJobs.ScanJob)
            ? CheckResult.Blocked(FailureReason.PreconditionFailed, "Result_ScanAlreadyRunning")
            : CheckResult.Proceed;
    }

    public override Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (type, path) = Command switch
        {
            CommandId.StartQuickScan => (DefenderScanType.Quick, (string?)null),
            CommandId.StartFullScan => (DefenderScanType.Full, null),
            _ => (DefenderScanType.Custom, context.Parameters.GetString("path")),
        };
        var started = jobs.TryStart(BackgroundJobs.ScanJob, Command, context.Caller,
            ct => defender.RunScanAsync(type, path, ct), "Result_ScanFinished", "Result_ScanFailed");
        return Task.FromResult(started
            ? ExecutionResult.Background("Result_ScanStarted")
            : ExecutionResult.Fail("Result_ScanAlreadyRunning"));
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);
}

public sealed class UpdateSignaturesAction(IDefenderApi defender) : SystemAction
{
    public override CommandId Command => CommandId.UpdateSignatures;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var status = await defender.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.IsAvailable && status.IsActiveAntivirus ? CheckResult.Proceed : Checks.NotAvailable("Result_DefenderNotActive");
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await defender.UpdateSignaturesAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_SignaturesUpdated")
            : ExecutionResult.Fail("Result_SignaturesFailed");

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await defender.GetStatusAsync(cancellationToken).ConfigureAwait(false)).SignaturesUpdatedAt is not null;
}

public sealed class RestoreQuarantinedItemAction(IDefenderApi defender) : SystemAction
{
    public override CommandId Command => CommandId.RestoreQuarantinedItem;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var items = await defender.GetQuarantineAsync(cancellationToken).ConfigureAwait(false);
        return items.Any(i => i.Id == context.Parameters.GetString("id"))
            ? CheckResult.Proceed
            : CheckResult.Blocked(FailureReason.NotFound, "Result_QuarantineItemNotFound");
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await defender.RestoreFromQuarantineAsync(context.Parameters.GetString("id"), cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_QuarantineRestored")
            : ExecutionResult.Fail("Result_QuarantineRestoreFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var items = await defender.GetQuarantineAsync(cancellationToken).ConfigureAwait(false);
        return items.All(i => i.Id != context.Parameters.GetString("id"));
    }
}
