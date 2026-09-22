using System.Globalization;
using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Windows;

namespace PcSante.Service.Actions;

public sealed class SetFirewallProfileAction(CommandId command, IFirewallApi firewall) : SystemAction
{
    public override CommandId Command { get; } = command;

    private bool TargetEnabled => Command == CommandId.EnableFirewallProfile;

    private static FirewallProfile Profile(ActionContext context) => context.Parameters.GetEnum<FirewallProfile>("profile");

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var status = await firewall.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var profile = status.FirstOrDefault(p => p.Profile == Profile(context));
        if (profile is null)
        {
            return Checks.NotAvailable("Result_FirewallUnavailable");
        }

        return profile.Enabled == TargetEnabled
            ? CheckResult.AlreadyDone(TargetEnabled ? "Result_AlreadyProtected" : "Result_AlreadyDone")
            : CheckResult.Proceed;
    }

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        Backup.Of("Pare-feu", await firewall.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = Profile(context);
        return await firewall.SetProfileEnabledAsync(profile, TargetEnabled, cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok(TargetEnabled ? "Result_FirewallEnabled" : "Result_FirewallDisabled", profile.ToString())
            : ExecutionResult.Fail("Result_FirewallFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var status = await firewall.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.Any(p => p.Profile == Profile(context) && p.Enabled == TargetEnabled);
    }

    public override async Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        var ok = true;
        foreach (var p in Backup.Read<List<FirewallProfileStatus>>(backup))
        {
            ok &= await firewall.SetProfileEnabledAsync(p.Profile, p.Enabled, cancellationToken).ConfigureAwait(false);
        }

        return ok;
    }
}

/// <summary>Réinitialisation des règles : export complet préalable (annulable par import), plus point de restauration.</summary>
public sealed class ResetFirewallAction(IFirewallApi firewall, ServicePaths paths, TimeProvider time) : SystemAction
{
    public override CommandId Command => CommandId.ResetFirewallRules;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await firewall.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Count > 0
            ? CheckResult.Proceed
            : Checks.NotAvailable("Result_FirewallUnavailable");

    public override async Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Backups);
        var file = Path.Combine(paths.Backups, string.Create(CultureInfo.InvariantCulture, $"firewall-{time.GetUtcNow():yyyyMMdd-HHmmss}.wfw"));
        return await firewall.ExportPolicyAsync(file, cancellationToken).ConfigureAwait(false) && File.Exists(file)
            ? new BackupData("Pare-feu (règles)", file)
            : null;
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken) =>
        await firewall.ResetToDefaultAsync(cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_FirewallReset")
            : ExecutionResult.Fail("Result_FirewallFailed");

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken) =>
        (await firewall.GetStatusAsync(cancellationToken).ConfigureAwait(false)).All(p => p.Enabled);

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return File.Exists(backup.Payload)
            ? firewall.ImportPolicyAsync(backup.Payload, cancellationToken)
            : Task.FromResult(false);
    }
}
