using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;

namespace PcSante.Service;

/// <summary>Tâches longues (scans antivirus) exécutées en arrière-plan ; leur fin est consignée dans l'audit.</summary>
public sealed partial class BackgroundJobs(IAuditLog audit, TimeProvider time, ILogger<BackgroundJobs> logger) : IDisposable
{
    public const string ScanJob = "defender-scan";

    private readonly ConcurrentDictionary<string, Task> _running = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILogger<BackgroundJobs> _logger = logger;

    public bool IsRunning(string name) => _running.TryGetValue(name, out var t) && !t.IsCompleted;

    public bool TryStart(string name, CommandId command, CallerIdentity caller, Func<CancellationToken, Task<bool>> work, string successKey, string failureKey)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(work);
        if (IsRunning(name))
        {
            return false;
        }

        var task = Task.Run(async () =>
        {
            bool ok;
            string? details = null;
            try
            {
                ok = await work(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ok = false;
                details = ex.Message;
                LogJobFailed(ex, name);
            }

            await audit.WriteAsync(new AuditEntry
            {
                Timestamp = time.GetUtcNow(),
                Who = caller.UserName,
                ClientProcessId = caller.ProcessId,
                Command = $"{command}:completed",
                Outcome = ok ? AuditOutcome.Succeeded : AuditOutcome.Failed,
                Reason = ok ? successKey : failureKey,
                Details = details,
            }, CancellationToken.None).ConfigureAwait(false);
        });
        _running[name] = task;
        return true;
    }

    public Task WaitAsync(string name) => _running.TryGetValue(name, out var t) ? t : Task.CompletedTask;

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tâche d'arrière-plan {Name} en échec")]
    private partial void LogJobFailed(Exception ex, string name);
}
