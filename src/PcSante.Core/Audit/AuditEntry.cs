namespace PcSante.Core.Audit;

public enum AuditOutcome
{
    Succeeded,
    AlreadyDone,
    Started,
    Failed,
    Refused,
    RolledBack,
}

/// <summary>Entrée du journal d'audit : qui, quoi, quand, résultat (section 5).</summary>
public sealed record AuditEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Utilisateur Windows à l'origine de la demande (ou « Planificateur »).</summary>
    public required string Who { get; init; }

    public int? ClientProcessId { get; init; }

    public required string Command { get; init; }

    /// <summary>Paramètres acceptés (jamais de clé de licence en clair).</summary>
    public string? Parameters { get; init; }

    public required AuditOutcome Outcome { get; init; }

    public string? Reason { get; init; }

    public string? Details { get; init; }

    public Guid? UndoId { get; init; }
}

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEntry>> ReadAsync(DateTimeOffset since, int max, CancellationToken cancellationToken = default);
}
