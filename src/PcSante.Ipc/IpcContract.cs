using PcSante.Core.Commands;

namespace PcSante.Ipc;

/// <summary>Demande envoyée par l'interface ou le mini-affichage. Le nom de commande est validé contre le catalogue fermé.</summary>
public sealed record IpcRequest
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public required string RequestId { get; init; }

    public required string Command { get; init; }

    public Dictionary<string, string>? Parameters { get; init; }

    /// <summary>L'utilisateur a confirmé l'action (fermeture de programme, suppression de fichiers, etc.).</summary>
    public bool Confirmed { get; init; }
}

public sealed record IpcResponse
{
    public required string RequestId { get; init; }

    public required CommandResult Result { get; init; }
}

/// <summary>Identité vérifiée du processus client.</summary>
public sealed record ClientInfo(int ProcessId, string ExecutablePath, string UserName, string? UserSid);

/// <summary>Traite une demande déjà authentifiée (implémenté par le service).</summary>
public interface IRequestHandler
{
    Task<CommandResult> HandleAsync(IpcRequest request, ClientInfo client, CancellationToken cancellationToken);
}
