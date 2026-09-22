using PcSante.Core.Commands;

namespace PcSante.Core.Actions;

/// <summary>
/// Action système du catalogue. Chaque action implémente les étapes du cycle imposé (section 13) :
/// vérifier, sauvegarder, exécuter, contrôler. La journalisation est assurée par <see cref="SystemActionPipeline"/>.
/// </summary>
public abstract class SystemAction
{
    public abstract CommandId Command { get; }

    /// <summary>1. Vérifier : préconditions, état actuel, disponibilité sur ce PC.</summary>
    public abstract Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken);

    /// <summary>2. Sauvegarder l'état précédent (uniquement si le catalogue exige une sauvegarde propre).</summary>
    public virtual Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BackupData?>(null);

    /// <summary>3. Exécuter.</summary>
    public abstract Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken);

    /// <summary>4. Contrôler le résultat réel sur le système.</summary>
    public abstract Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken);

    /// <summary>Annuler à partir de la sauvegarde propre.</summary>
    public virtual Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken) => Task.FromResult(false);
}
