namespace PcSante.Core.Windows;

/// <summary>Réparation réseau (M4/M6) : outils Windows fixes, sans script.</summary>
public interface INetworkRepairApi
{
    /// <summary>Vide le cache DNS (adresses de sites mémorisées) ; sans risque, effet immédiat.</summary>
    Task<bool> FlushDnsAsync(CancellationToken cancellationToken);

    /// <summary>Réinitialise Winsock et TCP/IP ; prend effet au prochain redémarrage.</summary>
    Task<bool> ResetNetworkStackAsync(CancellationToken cancellationToken);
}
