using PcSante.Core.Actions;
using PcSante.Core.Commands;

namespace PcSante.Service.Dispatch;

/// <summary>Lecture : aucune modification, non journalisée individuellement.</summary>
public interface IQueryHandler
{
    CommandId Command { get; }

    Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken);
}

/// <summary>Opération du service qui ne modifie pas Windows (licence, planification, mise à jour) : journalisée.</summary>
public interface IOperationHandler
{
    CommandId Command { get; }

    /// <summary>Faux si l'opération doit s'exécuter hors du verrou global (elle enchaîne elle-même des actions).</summary>
    bool UsesActionLock => true;

    /// <summary>Vrai si l'opération écrit elle-même son entrée d'audit (annulation).</summary>
    bool SelfAudited => false;

    Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken);
}

public sealed class DelegateQuery(CommandId command, Func<CommandParameters, CallerIdentity, CancellationToken, Task<CommandResult>> run) : IQueryHandler
{
    public CommandId Command { get; } = command;

    public Task<CommandResult> ExecuteAsync(CommandParameters parameters, CallerIdentity caller, CancellationToken cancellationToken) =>
        run(parameters, caller, cancellationToken);
}
