namespace PcSante.Core.Windows;

public sealed record RestorePointInfo(int SequenceNumber, string Description, DateTimeOffset CreatedAt);

/// <summary>Points de restauration Windows (SystemRestore WMI).</summary>
public interface IRestorePointApi
{
    /// <summary>La protection du système est-elle active sur le lecteur système ?</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);

    Task<bool> EnableAsync(CancellationToken cancellationToken);

    /// <summary>Crée un point de restauration. Renvoie faux si Windows l'a refusé.</summary>
    Task<bool> CreateAsync(string description, CancellationToken cancellationToken);

    Task<IReadOnlyList<RestorePointInfo>> ListAsync(CancellationToken cancellationToken);
}
