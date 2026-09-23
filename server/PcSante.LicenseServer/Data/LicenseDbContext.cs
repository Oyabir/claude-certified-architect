using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PcSante.Core.Licensing;

namespace PcSante.LicenseServer.Data;

public sealed class LicenseEntity
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 de la clé normalisée : la clé elle-même n'est jamais stockée.</summary>
    public required string KeyHash { get; set; }

    public required string KeyHint { get; set; }

    public LicenseTier Tier { get; set; }

    public int Seats { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    /// <summary>Remise à zéro des transferts par l'administrateur.</summary>
    public DateTimeOffset? TransfersResetAt { get; set; }

    public string? Note { get; set; }

    public List<ActivationEntity> Activations { get; set; } = [];

    public List<TransferEntity> Transfers { get; set; } = [];
}

public sealed class ActivationEntity
{
    public Guid Id { get; set; }

    public Guid LicenseId { get; set; }

    public LicenseEntity? License { get; set; }

    public required string Fp1 { get; set; }

    public required string Fp2 { get; set; }

    public required string Fp3 { get; set; }

    public required string Fp4 { get; set; }

    public DateTimeOffset ActivatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    public string? DeactivationReason { get; set; }

    public string? AppVersion { get; set; }

    public bool IsActive => DeactivatedAt is null;

    public string[] Fingerprint
    {
        get => [Fp1, Fp2, Fp3, Fp4];
        set => (Fp1, Fp2, Fp3, Fp4) = (value[0], value[1], value[2], value[3]);
    }
}

public sealed class TransferEntity
{
    public Guid Id { get; set; }

    public Guid LicenseId { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid FromActivationId { get; set; }

    public Guid ToActivationId { get; set; }
}

/// <summary>Journal des tentatives refusées (section 12).</summary>
public sealed class RefusedAttemptEntity
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Endpoint { get; set; }

    public string? IpAddress { get; set; }

    public string? KeyHint { get; set; }

    public required string Reason { get; set; }
}

public sealed class LicenseDbContext(DbContextOptions<LicenseDbContext> options) : DbContext(options)
{
    public DbSet<LicenseEntity> Licenses => Set<LicenseEntity>();

    public DbSet<ActivationEntity> Activations => Set<ActivationEntity>();

    public DbSet<TransferEntity> Transfers => Set<TransferEntity>();

    public DbSet<RefusedAttemptEntity> RefusedAttempts => Set<RefusedAttemptEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Dates stockées en entier (UTC) : tri et comparaison possibles sous SQLite comme sous PostgreSQL.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<LicenseTier>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<LicenseEntity>(e =>
        {
            e.HasIndex(l => l.KeyHash).IsUnique();
            e.Property(l => l.KeyHash).HasMaxLength(64);
            e.Property(l => l.KeyHint).HasMaxLength(8);
            e.Property(l => l.RevokedReason).HasMaxLength(256);
            e.Property(l => l.Note).HasMaxLength(256);
            e.HasMany(l => l.Activations).WithOne(a => a.License).HasForeignKey(a => a.LicenseId);
            e.HasMany(l => l.Transfers).WithOne().HasForeignKey(t => t.LicenseId);
        });
        modelBuilder.Entity<ActivationEntity>(e =>
        {
            e.Ignore(a => a.Fingerprint);
            e.Ignore(a => a.IsActive);
            e.Property(a => a.Fp1).HasMaxLength(64);
            e.Property(a => a.Fp2).HasMaxLength(64);
            e.Property(a => a.Fp3).HasMaxLength(64);
            e.Property(a => a.Fp4).HasMaxLength(64);
            e.Property(a => a.AppVersion).HasMaxLength(32);
            e.Property(a => a.DeactivationReason).HasMaxLength(64);
        });
        modelBuilder.Entity<RefusedAttemptEntity>(e =>
        {
            e.HasIndex(r => r.At);
            e.Property(r => r.Endpoint).HasMaxLength(32);
            e.Property(r => r.IpAddress).HasMaxLength(64);
            e.Property(r => r.KeyHint).HasMaxLength(8);
            e.Property(r => r.Reason).HasMaxLength(64);
        });
    }
}
