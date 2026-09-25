using System.Collections.Concurrent;
using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Optimization;
using PcSante.Core.Processes;
using PcSante.Core.Reporting;
using PcSante.Core.Scheduling;
using PcSante.Core.Windows;
using PcSante.Licensing;
using PcSante.Service.Actions;
using PcSante.Service.Data;
using PcSante.Service.Diagnostics;
using PcSante.Service.Dispatch;
using PcSante.Service.Updates;

namespace PcSante.Service.Queries;

/// <summary>Toutes les requêtes de lecture du catalogue.</summary>
public sealed class QueryRegistry(
    HealthAnalyzer analyzer,
    HistoryStore history,
    IMetricsProvider metrics,
    ISystemInfoApi system,
    IDefenderApi defender,
    IFirewallApi firewall,
    ILocalAccountsApi accounts,
    ISecurityCenterApi securityCenter,
    IBitLockerApi bitLocker,
    ISessionApi sessions,
    IServiceControlApi services,
    IWindowsUpdateApi updates,
    IProcessApi processes,
    ISignatureVerifier signatures,
    IStartupApi startup,
    IThirdPartyTaskApi tasks,
    ICleanupApi cleanup,
    IPowerApi power,
    IUndoStore undo,
    IScheduledTemplateApi scheduler,
    IAuditLog audit,
    LicenseManager license,
    AppUpdateService appUpdates,
    TimeProvider time)
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, SignatureInfo Info)> _signatureCache = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<IQueryHandler> All()
    {
        yield return Q(CommandId.RunHealthAnalysis, async (_, c, ct) => CommandResult.WithData(await analyzer.AnalyzeAsync(c.UserSid, ct).ConfigureAwait(false), "Result_AnalysisDone"));
        yield return Q(CommandId.GetLastHealthReport, async (_, _, ct) => CommandResult.WithData(await history.GetLastReportAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetLiveMetrics, (_, _, _) => Task.FromResult(CommandResult.WithData(metrics.Sample())));
        yield return Q(CommandId.GetScoreHistory, async (_, _, ct) => CommandResult.WithData(await history.GetScoresAsync(time.GetUtcNow().AddDays(-90), ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetSystemInfo, (_, _, _) => Task.FromResult(CommandResult.WithData(system.GetSystemInfo())));

        yield return Q(CommandId.GetDefenderStatus, async (_, _, ct) => CommandResult.WithData(await defender.GetStatusAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetThreatHistory, async (_, _, ct) => CommandResult.WithData(await defender.GetThreatHistoryAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetQuarantine, async (_, _, ct) => CommandResult.WithData(await defender.GetQuarantineAsync(ct).ConfigureAwait(false)));

        yield return Q(CommandId.GetFirewallStatus, async (_, _, ct) => CommandResult.WithData(await firewall.GetStatusAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetAntivirusProducts, async (_, _, ct) => CommandResult.WithData(await securityCenter.ListAntivirusAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetBitLockerStatus, async (_, _, ct) => CommandResult.WithData(await bitLocker.GetStatusAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetSessions, async (_, _, ct) => CommandResult.WithData(await sessions.ListAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetServiceProfileChanges, async (p, _, ct) =>
            CommandResult.WithData(await ServiceProfileAction.ChangesAsync(services, p.GetEnum<ServiceProfile>("profile"), ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetLocalAccounts, async (_, _, ct) => CommandResult.WithData(await accounts.ListAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetUpdateStatus, async (_, _, ct) => CommandResult.WithData(await updates.GetStatusAsync(ct).ConfigureAwait(false)));

        yield return Q(CommandId.GetProcesses, async (_, _, ct) => CommandResult.WithData(await GetProcessesAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetProcessHistory, async (_, _, ct) => CommandResult.WithData(await history.GetProcessHistoryAsync(ct).ConfigureAwait(false)));

        yield return Q(CommandId.GetStartupItems, async (_, c, ct) => CommandResult.WithData(await startup.ListAsync(c.UserSid, ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetThirdPartyTasks, async (_, _, ct) => CommandResult.WithData(await tasks.ListAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetCleanupEstimate, async (_, _, ct) => CommandResult.WithData(await cleanup.EstimateAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetPowerPlans, async (_, _, ct) => CommandResult.WithData(await power.ListPlansAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetUndoableActions, async (_, _, ct) => CommandResult.WithData(
            (await undo.ListAsync(false, ct).ConfigureAwait(false)).Select(u => new UndoView(u.Id, u.Command, u.CreatedAt, u.Backup.Description)).ToList()));

        yield return Q(CommandId.GetScheduledTemplates, async (_, _, ct) => CommandResult.WithData(await GetTemplatesAsync(ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetTaskRunLog, async (_, _, ct) => CommandResult.WithData(await history.GetTaskRunsAsync(100, ct).ConfigureAwait(false)));

        yield return Q(CommandId.GetReportData, async (p, _, ct) => CommandResult.WithData(
            await GetReportDataAsync(p.GetEnum<ReportPeriod>("period"), ct).ConfigureAwait(false)));
        yield return Q(CommandId.GetAuditLog, async (_, _, ct) => CommandResult.WithData(await audit.ReadAsync(time.GetUtcNow().AddDays(-90), 500, ct).ConfigureAwait(false)));

        yield return Q(CommandId.GetLicenseStatus, (_, _, _) => Task.FromResult(CommandResult.WithData(license.GetStatus())));
        yield return Q(CommandId.CheckAppUpdate, async (_, _, ct) => CommandResult.WithData((await appUpdates.CheckAsync(ct).ConfigureAwait(false)).Info));
    }

    public async Task<IReadOnlyList<ProcessView>> GetProcessesAsync(CancellationToken ct)
    {
        var samples = await processes.SampleAsync(ct).ConfigureAwait(false);
        return samples.Select(s =>
        {
            var signature = s.ExecutablePath is null ? SignatureInfo.Unsigned : Signature(s.ExecutablePath);
            return new ProcessView(s.ProcessId, s.Name, s.ExecutablePath, s.CpuPercent, s.MemoryBytes, s.DiskBytesPerSecond, s.ServiceNames,
                signature.IsSigned && signature.IsTrusted, signature.Publisher,
                ProcessReputation.Classify(s.Name, s.ExecutablePath, signature), ProcessReputation.IsProtected(s.Name));
        }).OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.MemoryBytes).ToList();
    }

    public async Task<IReadOnlyList<TemplateView>> GetTemplatesAsync(CancellationToken ct)
    {
        var runs = await history.GetTaskRunsAsync(200, ct).ConfigureAwait(false);
        var views = new List<TemplateView>();
        foreach (var t in ScheduledTemplates.All)
        {
            var settings = await scheduler.GetAsync(t.Id, ct).ConfigureAwait(false);
            views.Add(new TemplateView(t.Id, settings is not null, settings ?? t.Default, t.Commands, runs.FirstOrDefault(r => r.Template == t.Id)));
        }

        return views;
    }

    public async Task<ReportData> GetReportDataAsync(ReportPeriod period, CancellationToken ct)
    {
        var to = time.GetUtcNow();
        var from = period == ReportPeriod.Week ? to.AddDays(-7) : to.AddMonths(-1);
        var threats = await SafeAsync(() => defender.GetThreatHistoryAsync(ct), []).ConfigureAwait(false);
        var actions = (await audit.ReadAsync(from, 500, ct).ConfigureAwait(false))
            .Where(a => a.Outcome is AuditOutcome.Succeeded or AuditOutcome.Failed or AuditOutcome.RolledBack)
            .ToList();
        return new ReportData(
            period, from, to, system.GetSystemInfo().MachineName,
            await history.GetLastReportAsync(ct).ConfigureAwait(false),
            await history.GetScoresAsync(from, ct).ConfigureAwait(false),
            threats.Where(t => t.DetectedAt >= from).ToList(),
            actions);
    }

    private SignatureInfo Signature(string path)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_signatureCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            {
                return cached.Info;
            }

            var info = signatures.Verify(path);
            _signatureCache[path] = (stamp, info);
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SignatureInfo.Unsigned;
        }
    }

    private static async Task<T> SafeAsync<T>(Func<Task<T>> read, T fallback)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    private static DelegateQuery Q(CommandId id, Func<CommandParameters, CallerIdentity, CancellationToken, Task<CommandResult>> run) => new(id, run);
}
