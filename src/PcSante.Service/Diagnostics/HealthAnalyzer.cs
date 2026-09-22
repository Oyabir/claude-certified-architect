using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PcSante.Core.Health;
using PcSante.Core.Windows;
using PcSante.Service.Data;

namespace PcSante.Service.Diagnostics;

/// <summary>
/// Analyse complète (objectif &lt; 60 s) : collecte parallèle des faits, chaque collecteur borné à 20 s.
/// Un collecteur en échec laisse la donnée « inconnue » : aucune règle n'en tire de conclusion.
/// </summary>
public sealed partial class HealthAnalyzer(
    IDefenderApi defender,
    IFirewallApi firewall,
    IWindowsUpdateApi updates,
    IRestorePointApi restore,
    ISystemInfoApi system,
    IStartupApi startup,
    ICleanupApi cleanup,
    IMetricsProvider metrics,
    HistoryStore history,
    TimeProvider time,
    ILogger<HealthAnalyzer> logger)
{
    public static readonly TimeSpan CollectorTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<HealthAnalyzer> _logger = logger;

    public async Task<HealthReport> AnalyzeAsync(string? userSid, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var now = time.GetUtcNow();

        var defenderTask = Collect("defender", ct => defender.GetStatusAsync(ct), cancellationToken);
        var firewallTask = Collect("firewall", ct => firewall.GetStatusAsync(ct), cancellationToken);
        var updatesTask = Collect("updates", ct => updates.GetStatusAsync(ct), cancellationToken);
        var restoreEnabledTask = Collect<bool?>("restore", async ct => await restore.IsEnabledAsync(ct).ConfigureAwait(false), cancellationToken);
        var restoreListTask = Collect("restorelist", ct => restore.ListAsync(ct), cancellationToken);
        var startupTask = Collect("startup", ct => startup.ListAsync(userSid, ct), cancellationToken);
        var cleanupTask = Collect("cleanup", ct => cleanup.EstimateAsync(ct), cancellationToken);
        var crashesTask = Collect<int?>("crashes", _ => Task.FromResult<int?>(system.CountCrashesSince(now.AddDays(-30))), cancellationToken);
        var driveTask = Collect("drive", _ => Task.FromResult(system.GetSystemDrive()), cancellationToken);
        var metricsTask = Collect("metrics", _ => Task.FromResult(metrics.Sample()), cancellationToken);

        await Task.WhenAll(defenderTask, firewallTask, updatesTask, restoreEnabledTask, restoreListTask,
            startupTask, cleanupTask, crashesTask, driveTask, metricsTask).ConfigureAwait(false);

        var snapshot = new SystemSnapshot
        {
            CollectedAt = now,
            Defender = defenderTask.Result,
            Firewall = firewallTask.Result,
            Updates = updatesTask.Result,
            RestoreEnabled = restoreEnabledTask.Result,
            LastRestorePointAt = restoreListTask.Result is { Count: > 0 } points ? points.Max(p => p.CreatedAt) : null,
            CrashesLast30Days = crashesTask.Result,
            EnabledStartupItems = startupTask.Result?.Count(s => s.Enabled),
            SystemDrive = driveTask.Result,
            CleanableBytes = cleanupTask.Result?.TotalBytes,
            MemoryPercent = metricsTask.Result?.MemoryPercent,
        };

        var issues = DiagnosticRules.EvaluateAll(snapshot);
        var report = HealthScoreCalculator.BuildReport(now, issues, watch.Elapsed);
        await history.SaveReportAsync(report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private async Task<T?> Collect<T>(string name, Func<CancellationToken, Task<T>> collect, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CollectorTimeout);
        try
        {
            return await Task.Run(() => collect(timeout.Token), timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCollectorFailed(name, ex.GetType().Name);
            return default;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Collecte « {Name} » indisponible ({Error}) : donnée ignorée")]
    private partial void LogCollectorFailed(string name, string error);
}
