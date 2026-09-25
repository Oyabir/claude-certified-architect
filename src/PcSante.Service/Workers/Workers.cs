using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcSante.Core.Windows;
using PcSante.Ipc;
using PcSante.Licensing;
using PcSante.Service.Data;
using PcSante.Service.Diagnostics;

namespace PcSante.Service.Workers;

/// <summary>Serveur du named pipe.</summary>
public sealed class PipeServerWorker(PipeServer server) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => server.RunAsync(stoppingToken);
}

/// <summary>
/// Historique des processus (7 jours, M3) : un échantillon toutes les 10 minutes, 15 processus les plus gourmands.
/// Fréquence volontairement faible pour rester sous 1 % de CPU au repos.
/// </summary>
public sealed partial class ProcessHistoryWorker(IProcessApi processes, HistoryStore history, ILogger<ProcessHistoryWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly ILogger<ProcessHistoryWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var samples = await processes.SampleAsync(stoppingToken).ConfigureAwait(false);
                var top = samples.OrderByDescending(s => s.CpuPercent).Take(10)
                    .Concat(samples.OrderByDescending(s => s.MemoryBytes).Take(10))
                    .DistinctBy(s => s.ProcessId)
                    .Take(15)
                    .Select(s => (s.Name, Math.Round(s.CpuPercent, 1), s.MemoryBytes));
                await history.AddProcessSamplesAsync(top, stoppingToken).ConfigureAwait(false);
                await history.PruneAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échantillonnage des processus impossible")]
    private partial void LogFailed(Exception ex);
}

/// <summary>Revalidation de la licence tous les 7 jours (vérifiée toutes les 6 heures).</summary>
public sealed partial class LicenseWorker(LicenseManager license, ILogger<LicenseWorker> logger) : BackgroundService
{
    private readonly ILogger<LicenseWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                if (license.IsRevalidationDue())
                {
                    var result = await license.RevalidateAsync(stoppingToken).ConfigureAwait(false);
                    LogRevalidation(result.MessageKey);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Revalidation de la licence : {Result}")]
    private partial void LogRevalidation(string result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revalidation de la licence impossible")]
    private partial void LogFailed(Exception ex);
}

/// <summary>Relevé des connexions Bureau à distance toutes les 2 minutes (adresses inhabituelles, M7).</summary>
public sealed partial class RemoteSessionWorker(ISessionApi sessions, RemoteAccessTracker tracker, ILogger<RemoteSessionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly ILogger<RemoteSessionWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var fresh = await tracker.RecordAsync(await sessions.ListAsync(stoppingToken).ConfigureAwait(false), stoppingToken).ConfigureAwait(false);
                if (fresh.Count > 0)
                {
                    LogNewRemoteAddress(string.Join(", ", fresh));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connexion Bureau à distance depuis une adresse nouvelle : {Addresses}")]
    private partial void LogNewRemoteAddress(string addresses);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relevé des sessions impossible")]
    private partial void LogFailed(Exception ex);
}
