using PcSante.Core.Commands;

namespace PcSante.Core.Actions;

public sealed record UndoRecord(
    Guid Id,
    CommandId Command,
    DateTimeOffset CreatedAt,
    BackupData Backup,
    bool Undone);

/// <summary>Stockage des sauvegardes permettant le bouton « Annuler » par action.</summary>
public interface IUndoStore
{
    Task<Guid> SaveAsync(CommandId command, BackupData backup, CancellationToken cancellationToken = default);

    Task<UndoRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UndoRecord>> ListAsync(bool includeUndone, CancellationToken cancellationToken = default);

    Task MarkUndoneAsync(Guid id, CancellationToken cancellationToken = default);
}
