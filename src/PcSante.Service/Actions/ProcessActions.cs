using PcSante.Core.Actions;
using PcSante.Core.Commands;
using PcSante.Core.Processes;
using PcSante.Core.Windows;

namespace PcSante.Service.Actions;

/// <summary>
/// Arrêt d'un programme. Le service tourne en SYSTEM : il ne ferme que les programmes de l'utilisateur
/// qui le demande (jamais ceux d'un autre utilisateur) et jamais un processus indispensable.
/// </summary>
public sealed class StopProcessAction(IProcessApi processes, ISystemInfoApi system) : SystemAction
{
    public override CommandId Command => CommandId.StopProcess;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var pid = context.Parameters.GetInt("pid");
        if (pid == Environment.ProcessId)
        {
            return CheckResult.Blocked(FailureReason.ProtectedItem, "Result_ProtectedProcess");
        }

        var process = await processes.GetAsync(pid, cancellationToken).ConfigureAwait(false);
        if (process is null)
        {
            return CheckResult.AlreadyDone("Result_ProcessAlreadyStopped");
        }

        if (ProcessReputation.IsProtected(process.Name))
        {
            return CheckResult.Blocked(FailureReason.ProtectedItem, "Result_ProtectedProcess");
        }

        var (_, ownerSid) = system.GetProcessOwner(pid);
        return ownerSid is not null && string.Equals(ownerSid, context.Caller.UserSid, StringComparison.OrdinalIgnoreCase)
            ? CheckResult.Proceed
            : CheckResult.Blocked(FailureReason.ProtectedItem, "Result_ProcessOtherUser");
    }

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await processes.StopAsync(context.Parameters.GetInt("pid"), cancellationToken).ConfigureAwait(false)
            ? ExecutionResult.Ok("Result_ProcessStopped")
            : ExecutionResult.Fail("Result_ProcessStopFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !await processes.IsRunningAsync(context.Parameters.GetInt("pid"), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Désactive le service Windows associé à un processus (mode de démarrage sauvegardé, annulable).</summary>
public sealed class DisableServiceAction(IServiceControlApi services) : SystemAction
{
    /// <summary>Services indispensables à Windows ou à la sécurité : jamais désactivés.</summary>
    public static readonly IReadOnlySet<string> ProtectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc", "MpsSvc", "BFE", "mpssvc",
        "wuauserv", "UsoSvc", "BITS", "CryptSvc", "TrustedInstaller", "EventLog", "RpcSs", "RpcEptMapper",
        "DcomLaunch", "PlugPlay", "Power", "ProfSvc", "Schedule", "SamSs", "LSM", "Winmgmt", "Dhcp", "Dnscache",
        "nsi", "NlaSvc", "netprofm", "AudioSrv", "AudioEndpointBuilder", "Themes", "UserManager", "gpsvc",
        "CoreMessagingRegistrar", "BrokerInfrastructure", "SystemEventsBroker", "TimeBrokerSvc", "StateRepository",
        "VaultSvc", "KeyIso", "EFS", "BDESVC", "WinHttpAutoProxySvc", "LanmanWorkstation", "Spooler",
        "PcSanteService",
    };

    private ServiceInfo? _before;

    public override CommandId Command => CommandId.DisableServiceForProcess;

    public override async Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Parameters.GetString("service");
        if (ProtectedServices.Contains(name))
        {
            return CheckResult.Blocked(FailureReason.ProtectedItem, "Result_ProtectedService");
        }

        _before = await services.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (_before is null)
        {
            return CheckResult.Blocked(FailureReason.NotFound, "Result_ServiceNotFound");
        }

        return _before.StartMode == ServiceStartMode.Disabled && !_before.IsRunning
            ? CheckResult.AlreadyDone("Result_AlreadyDone")
            : CheckResult.Proceed;
    }

    public override Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BackupData?>(_before is null ? null : Backup.Of($"Service {_before.DisplayName}", _before));

    public override async Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Parameters.GetString("service");
        var disabled = await services.SetStartModeAsync(name, ServiceStartMode.Disabled, cancellationToken).ConfigureAwait(false);
        var stopped = await services.StopAsync(name, cancellationToken).ConfigureAwait(false);
        return disabled && stopped ? ExecutionResult.Ok("Result_ServiceDisabled") : ExecutionResult.Fail("Result_ServiceDisableFailed");
    }

    public override async Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var after = await services.GetAsync(context.Parameters.GetString("service"), cancellationToken).ConfigureAwait(false);
        return after is { StartMode: ServiceStartMode.Disabled, IsRunning: false };
    }

    public override async Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        var before = Backup.Read<ServiceInfo>(backup);
        var ok = await services.SetStartModeAsync(before.Name, before.StartMode, cancellationToken).ConfigureAwait(false);
        if (ok && before.IsRunning)
        {
            ok = await services.StartAsync(before.Name, cancellationToken).ConfigureAwait(false);
        }

        return ok;
    }
}
