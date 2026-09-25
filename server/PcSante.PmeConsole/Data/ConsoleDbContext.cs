using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PcSante.PmeConsole.Data;

public enum ManagerRole
{
    /// <summary>Gérant : gère les postes, les utilisateurs et le code d'inscription.</summary>
    Owner,

    /// <summary>Lecteur : consulte seulement.</summary>
    Viewer,
}

public sealed class OrganizationEntity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Empreinte du code d'inscription (le code n'est jamais conservé).</summary>
    public required string EnrollmentCodeHash { get; set; }

    /// <summary>4 derniers caractères du code, pour que le gérant le reconnaisse.</summary>
    public required string EnrollmentCodeHint { get; set; }

    /// <summary>Nombre de postes de l'abonnement (offre PME, par poste).</summary>
    public int Seats { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastMonthlyReportAt { get; set; }
}

public sealed class ManagerEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public required string PasswordHash { get; set; }

    public ManagerRole Role { get; set; }

    public bool MustChangePassword { get; set; }

    public int FailedLogins { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class DeviceEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string MachineName { get; set; }

    /// <summary>SHA-256 du secret du poste (32 octets aléatoires) ; le secret n'est jamais conservé.</summary>
    public required string SecretHash { get; set; }

    public DateTimeOffset EnrolledAt { get; set; }

    public DateTimeOffset? LastReportAt { get; set; }

    public DateTimeOffset? AnalyzedAt { get; set; }

    public int? Score { get; set; }

    public int? Security { get; set; }

    public int? Performance { get; set; }

    public int? Stability { get; set; }

    public int? Storage { get; set; }

    /// <summary>Problèmes du dernier rapport (JSON, codes techniques seulement).</summary>
    public string? IssuesJson { get; set; }

    public string? WindowsVersion { get; set; }

    public string? AppVersion { get; set; }

    public bool Revoked { get; set; }
}

public sealed class AlertEntity
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid DeviceId { get; set; }

    /// <summary>ScoreRed, CriticalIssue ou Silent.</summary>
    public required string Kind { get; set; }

    /// <summary>Code du problème pour CriticalIssue.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? NotifiedAt { get; set; }
}

public sealed class ScoreHistoryEntity
{
    public long Id { get; set; }

    public Guid DeviceId { get; set; }

    public DateTimeOffset At { get; set; }

    public int Score { get; set; }
}

/// <summary>Base de la console (SQLite en local, PostgreSQL en production).</summary>
public sealed class ConsoleDbContext(DbContextOptions<ConsoleDbContext> options) : DbContext(options)
{
    public DbSet<OrganizationEntity> Organizations => Set<OrganizationEntity>();

    public DbSet<ManagerEntity> Managers => Set<ManagerEntity>();

    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

    public DbSet<AlertEntity> Alerts => Set<AlertEntity>();

    public DbSet<ScoreHistoryEntity> ScoreHistory => Set<ScoreHistoryEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<ManagerRole>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OrganizationEntity>(e =>
        {
            e.Property(o => o.Name).HasMaxLength(128);
            e.Property(o => o.EnrollmentCodeHash).HasMaxLength(64);
            e.Property(o => o.EnrollmentCodeHint).HasMaxLength(8);
            e.HasIndex(o => o.EnrollmentCodeHash).IsUnique();
        });
        modelBuilder.Entity<ManagerEntity>(e =>
        {
            e.Property(m => m.Email).HasMaxLength(254);
            e.Property(m => m.DisplayName).HasMaxLength(128);
            e.Property(m => m.PasswordHash).HasMaxLength(256);
            e.HasIndex(m => m.Email).IsUnique();
        });
        modelBuilder.Entity<DeviceEntity>(e =>
        {
            e.Property(d => d.MachineName).HasMaxLength(128);
            e.Property(d => d.SecretHash).HasMaxLength(64);
            e.Property(d => d.WindowsVersion).HasMaxLength(128);
            e.Property(d => d.AppVersion).HasMaxLength(32);
            e.Property(d => d.IssuesJson).HasMaxLength(32_000);
            e.HasIndex(d => d.OrganizationId);
        });
        modelBuilder.Entity<AlertEntity>(e =>
        {
            e.Property(a => a.Kind).HasMaxLength(32);
            e.Property(a => a.Detail).HasMaxLength(64);
            e.HasIndex(a => new { a.OrganizationId, a.ResolvedAt });
        });
        modelBuilder.Entity<ScoreHistoryEntity>().HasIndex(s => new { s.DeviceId, s.At });
    }
}
