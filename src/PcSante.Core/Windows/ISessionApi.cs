namespace PcSante.Core.Windows;

public enum SessionState
{
    Active,
    Locked,
    Disconnected,
    Other,
}

/// <summary>Session d'un utilisateur sur ce PC (M7) : locale (console) ou à distance (Bureau à distance, RDP).</summary>
public sealed record UserSession(int SessionId, string UserName, SessionState State, bool IsRemote, string? ClientAddress, DateTimeOffset? LogonTime);

/// <summary>Sessions ouvertes (WTS). Le texte des messages vient d'une liste fermée, jamais d'une saisie libre.</summary>
public interface ISessionApi
{
    Task<IReadOnlyList<UserSession>> ListAsync(CancellationToken cancellationToken);

    Task<bool> SendMessageAsync(int sessionId, string title, string message, CancellationToken cancellationToken);

    /// <summary>Déconnecte la session : l'utilisateur est renvoyé à l'écran de connexion, ses programmes restent ouverts.</summary>
    Task<bool> DisconnectAsync(int sessionId, CancellationToken cancellationToken);

    /// <summary>Ferme la session : ses programmes sont fermés (travail non enregistré perdu).</summary>
    Task<bool> LogOffAsync(int sessionId, CancellationToken cancellationToken);
}
