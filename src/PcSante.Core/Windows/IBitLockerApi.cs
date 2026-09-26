namespace PcSante.Core.Windows;

public enum BitLockerState
{
    Off,
    On,
    Encrypting,
    Decrypting,
    Paused,
    Unknown,
}

/// <summary>
/// État du chiffrement du disque système (M6, éditions Pro). <see cref="Supported"/> est faux sur Windows Famille
/// ou si le disque système n'est pas chiffrable : la fonction est alors masquée.
/// </summary>
public sealed record BitLockerStatus(bool Supported, bool TpmReady, BitLockerState State, int Percentage, bool HasRecoveryKey)
{
    public static BitLockerStatus NotSupported { get; } = new(false, false, BitLockerState.Unknown, 0, false);
}

/// <summary>Clé de récupération (mot de passe numérique de 48 chiffres) et son identifiant affiché par Windows au démarrage.</summary>
public sealed record BitLockerRecoveryKey(string KeyId, string Password);

public interface IBitLockerApi
{
    Task<BitLockerStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Renvoie la clé de récupération du disque système, en la créant si elle n'existe pas encore.</summary>
    Task<BitLockerRecoveryKey?> EnsureRecoveryKeyAsync(CancellationToken cancellationToken);

    /// <summary>Protège la clé par la puce TPM puis lance le chiffrement de l'espace utilisé (en arrière-plan, plusieurs heures possibles).</summary>
    Task<bool> StartEncryptionAsync(CancellationToken cancellationToken);
}
