using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PcSante.Service.Data;

public sealed class AuditRow
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public required string Who { get; set; }

    public int? ClientProcessId { get; set; }

    public required string Command { get; set; }

    public string? Parameters { get; set; }

    public required string Outcome { get; set; }

    public string? Reason { get; set; }

    public string? Details { get; set; }

    public Guid? UndoId { get; set; }
}

public sealed class UndoRow
{
    public Guid Id { get; set; }

    public required string Command { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public required string Description { get; set; }

    public required string Payload { get; set; }

    public bool Undone { get; set; }
}

public sealed class ScoreRow
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public int Score { get; set; }

    public int Security { get; set; }

    public int Performance { get; set; }

    public int Stability { get; set; }

    public int Storage { get; set; }
}

public sealed class ProcessSampleRow
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Name { get; set; }

    public double CpuPercent { get; set; }

    public long MemoryBytes { get; set; }
}

public sealed class TaskRunRow
{
    public long Id { get; set; }

    public required string Template { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public bool Succeeded { get; set; }

    public required string MessageKey { get; set; }
}

public sealed class KeyValueRow
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}

/// <summary>Base locale du service (historique, journal d'audit, configuration).</summary>
public sealed class ServiceDbContext(DbContextOptions<ServiceDbContext> options) : DbContext(options)
{
    public DbSet<AuditRow> Audit => Set<AuditRow>();

    public DbSet<UndoRow> Undo => Set<UndoRow>();

    public DbSet<ScoreRow> Scores => Set<ScoreRow>();

    public DbSet<ProcessSampleRow> ProcessSamples => Set<ProcessSampleRow>();

    public DbSet<TaskRunRow> TaskRuns => Set<TaskRunRow>();

    public DbSet<KeyValueRow> KeyValues => Set<KeyValueRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<AuditRow>().HasIndex(a => a.Timestamp);
        modelBuilder.Entity<ScoreRow>().HasIndex(s => s.At);
        modelBuilder.Entity<ProcessSampleRow>().HasIndex(p => p.At);
        modelBuilder.Entity<TaskRunRow>().HasIndex(t => t.StartedAt);
        modelBuilder.Entity<KeyValueRow>().HasKey(k => k.Key);
    }
}
