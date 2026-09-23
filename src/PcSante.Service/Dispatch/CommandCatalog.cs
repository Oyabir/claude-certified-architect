using PcSante.Core.Actions;
using PcSante.Core.Commands;

namespace PcSante.Service.Dispatch;

/// <summary>
/// Catalogue fermé des commandes du service (section 5) : chaque identifiant du catalogue a exactement
/// un gestionnaire, et aucun gestionnaire n'existe hors catalogue. Vérifié au démarrage.
/// </summary>
public sealed class CommandCatalog
{
    private readonly Dictionary<CommandId, object> _handlers = [];

    public CommandCatalog(IEnumerable<IQueryHandler> queries, IEnumerable<IOperationHandler> operations, IEnumerable<SystemAction> actions)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var q in queries)
        {
            Add(q.Command, q, CommandKind.Query);
        }

        foreach (var o in operations)
        {
            Add(o.Command, o, CommandKind.Action);
        }

        foreach (var a in actions)
        {
            Add(a.Command, a, CommandKind.Action);
        }

        var missing = CommandDefinitions.All.Keys.Where(k => !_handlers.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Commandes du catalogue sans gestionnaire : " + string.Join(", ", missing));
        }
    }

    public IReadOnlyCollection<CommandId> Commands => _handlers.Keys;

    public object Get(CommandId id) => _handlers[id];

    public SystemAction? GetAction(CommandId id) => _handlers.GetValueOrDefault(id) as SystemAction;

    private void Add(CommandId id, object handler, CommandKind expectedKind)
    {
        if (!CommandDefinitions.TryGet(id, out var descriptor))
        {
            throw new InvalidOperationException($"Gestionnaire hors catalogue : {id}");
        }

        if (descriptor.Kind != expectedKind)
        {
            throw new InvalidOperationException($"Type de gestionnaire incohérent pour {id}");
        }

        if (!_handlers.TryAdd(id, handler))
        {
            throw new InvalidOperationException($"Gestionnaire en double : {id}");
        }
    }
}
