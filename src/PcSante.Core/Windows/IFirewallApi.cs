namespace PcSante.Core.Windows;

public enum FirewallProfile
{
    Domain,
    Private,
    Public,
}

public sealed record FirewallProfileStatus(FirewallProfile Profile, bool Enabled);

/// <summary>Pare-feu Windows (COM INetFwPolicy2, netsh pour export/import/réinitialisation).</summary>
public interface IFirewallApi
{
    Task<IReadOnlyList<FirewallProfileStatus>> GetStatusAsync(CancellationToken cancellationToken);

    Task<bool> SetProfileEnabledAsync(FirewallProfile profile, bool enabled, CancellationToken cancellationToken);

    /// <summary>Exporte toute la configuration (règles comprises) dans un fichier .wfw.</summary>
    Task<bool> ExportPolicyAsync(string filePath, CancellationToken cancellationToken);

    Task<bool> ImportPolicyAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Remet les règles par défaut de Windows.</summary>
    Task<bool> ResetToDefaultAsync(CancellationToken cancellationToken);
}
