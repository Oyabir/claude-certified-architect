using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PcSante.Core;
using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Processes;
using PcSante.Core.Scheduling;

namespace PcSante.Service.Data;

/// <summary>Journal d'audit en base SQLite (et dans le journal Serilog).</summary>
public sealed class EfAuditLog(IDbContextFactory<ServiceDbContext> factory) : IAuditLog
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Audit.Add(new AuditRow
        {
            Timestamp = entry.Timestamp,
            Who = entry.Who,
            ClientProcessId = entry.ClientProcessId,
            Command = entry.Command,
            Parameters = entry.Parameters,
            Outcome = entry.Outcome.ToString(),
            Reason = entry.Reason,
            Details = entry.Details is { Length: > 2000 } d ? d[..2000] : entry.Details,
            UndoId = entry.UndoId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAsync(DateTimeOffset since, int max, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Audit.AsNoTracking().Where(a => a.Timestamp >= since)
            .OrderByDescending(a => a.Timestamp).Take(max).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new AuditEntry
        {
            Timestamp = r.Timestamp,
            Who = r.Who,
            ClientProcessId = r.ClientProcessId,
            Command = r.Command,
            Parameters = r.Parameters,
            Outcome = Enum.TryParse<AuditOutcome>(r.Outcome, out var o) ? o : AuditOutcome.Failed,
            Reason = r.Reason,
            Details = r.Details,
            UndoId = r.UndoId,
        }).ToList();
    }
}

public sealed class EfUndoStore(IDbContextFactory<ServiceDbContext> factory, TimeProvider time) : IUndoStore
{
    public async Task<Guid> SaveAsync(CommandId command, BackupData backup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = new UndoRow
        {
            Id = Guid.NewGuid(),
            Command = command.ToString(),
            CreatedAt = time.GetUtcNow(),
            Description = backup.Description,
            Payload = backup.Payload,
        };
        db.Undo.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return row.Id;
    }

    public async Task<UndoRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Undo.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<UndoRecord>> ListAsync(bool includeUndone, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Undo.AsNoTracking().Where(u => includeUndone || !u.Undone)
            .OrderByDescending(u => u.CreatedAt).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task MarkUndoneAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Undo.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is not null)
        {
            row.Undone = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static UndoRecord Map(UndoRow r) =>
        new(r.Id, Enum.Parse<CommandId>(r.Command), r.CreatedAt, new BackupData(r.Description, r.Payload), r.Undone);
}

/// <summary>Historique : score, rapport de santé, échantillons de processus, journal des tâches, réglages.</summary>
public sealed class HistoryStore(IDbContextFactory<ServiceDbContext> factory, TimeProvider time)
{
    public static readonly TimeSpan ProcessHistoryRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan ScoreRetention = TimeSpan.FromDays(400);

    public async Task SaveReportAsync(HealthReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Scores.Add(new ScoreRow
        {
            At = report.AnalyzedAt,
            Score = report.Score,
            Security = report.SubScores.Security,
            Performance = report.SubScores.Performance,
            Stability = report.SubScores.Stability,
            Storage = report.SubScores.Storage,
        });
        await UpsertAsync(db, "LastHealthReport", JsonSerializer.Serialize(report, PcSanteJson.Options), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<HealthReport?> GetLastReportAsync(CancellationToken ct)
    {
        var json = await GetValueAsync("LastHealthReport", ct).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<HealthReport>(json, PcSanteJson.Options);
    }

    public async Task<IReadOnlyList<ScorePoint>> GetScoresAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.Scores.AsNoTracking().Where(s => s.At >= since).OrderBy(s => s.At).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new ScorePoint(r.At, r.Score, new SubScores(r.Security, r.Performance, r.Stability, r.Storage))).ToList();
    }

    public async Task AddProcessSamplesAsync(IEnumerable<(string Name, double Cpu, long Memory)> samples, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var now = time.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        foreach (var (name, cpu, memory) in samples)
        {
            db.ProcessSamples.Add(new ProcessSampleRow { At = now, Name = name, CpuPercent = cpu, MemoryBytes = memory });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Processus gourmands en permanence sur 7 jours (M3).</summary>
    public async Task<IReadOnlyList<ProcessHistoryEntry>> GetProcessHistoryAsync(CancellationToken ct)
    {
        var since = time.GetUtcNow() - ProcessHistoryRetention;
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.ProcessSamples.AsNoTracking().Where(p => p.At >= since).ToListAsync(ct).ConfigureAwait(false);
        var sampleTimes = rows.Select(r => r.At).Distinct().Count();
        return rows.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessHistoryEntry(
                g.Key,
                Math.Round(g.Average(r => r.CpuPercent), 1),
                (long)g.Average(r => r.MemoryBytes),
                g.Count(),
                sampleTimes == 0 ? 0 : Math.Round((double)g.Count(r => r.CpuPercent >= 10 || r.MemoryBytes >= 500L * 1024 * 1024) / sampleTimes, 2)))
            .OrderByDescending(e => e.AverageCpuPercent).ThenByDescending(e => e.AverageMemoryBytes)
            .Take(30)
            .ToList();
    }

    public async Task AddTaskRunAsync(TaskRunEntry run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.TaskRuns.Add(new TaskRunRow
        {
            Template = run.Template.ToString(),
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Succeeded = run.Succeeded,
            MessageKey = run.MessageKey,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TaskRunEntry>> GetTaskRunsAsync(int max, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.TaskRuns.AsNoTracking().OrderByDescending(t => t.StartedAt).Take(max).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new TaskRunEntry(Enum.Parse<ScheduledTemplateId>(r.Template), r.StartedAt, r.FinishedAt, r.Succeeded, r.MessageKey)).ToList();
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return (await db.KeyValues.AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, ct).ConfigureAwait(false))?.Value;
    }

    public async Task SetValueAsync(string key, string? value, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (value is null)
        {
            await db.KeyValues.Where(k => k.Key == key).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            return;
        }

        await UpsertAsync(db, key, value, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Purge : processus au-delà de 7 jours, scores au-delà de 400 jours, audit au-delà de 2 ans.</summary>
    public async Task PruneAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var processLimit = now - ProcessHistoryRetention;
        var scoreLimit = now - ScoreRetention;
        var auditLimit = now.AddYears(-2);
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.ProcessSamples.Where(p => p.At < processLimit).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Scores.Where(s => s.At < scoreLimit).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Audit.Where(a => a.Timestamp < auditLimit).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.TaskRuns.Where(t => t.StartedAt < auditLimit).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertAsync(ServiceDbContext db, string key, string value, CancellationToken ct)
    {
        var row = await db.KeyValues.FirstOrDefaultAsync(k => k.Key == key, ct).ConfigureAwait(false);
        if (row is null)
        {
            db.KeyValues.Add(new KeyValueRow { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }
}
