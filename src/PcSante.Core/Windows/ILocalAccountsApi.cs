namespace PcSante.Core.Windows;

/// <summary>Compte local du PC (M6). Les identifiants de sécurité (SID) sont ceux de Windows, jamais saisis par l'utilisateur.</summary>
public sealed record LocalAccount(string Name, string Sid, bool Enabled, bool IsAdministrator)
{
    /// <summary>Compte Invité intégré (RID 501, quel que soit son nom dans la langue de Windows).</summary>
    public bool IsGuest => Sid.EndsWith("-501", StringComparison.Ordinal);

    /// <summary>Compte Administrateur intégré (RID 500).</summary>
    public bool IsBuiltInAdministrator => Sid.EndsWith("-500", StringComparison.Ordinal);
}

/// <summary>Comptes locaux : consultation et activation/désactivation (réversible).</summary>
public interface ILocalAccountsApi
{
    Task<IReadOnlyList<LocalAccount>> ListAsync(CancellationToken cancellationToken);

    Task<bool> SetEnabledAsync(string sid, bool enabled, CancellationToken cancellationToken);
}
