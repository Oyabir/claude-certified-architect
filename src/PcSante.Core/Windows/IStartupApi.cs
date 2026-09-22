namespace PcSante.Core.Windows;

public enum StartupLocation
{
    MachineRun,
    MachineRun32,
    UserRun,
    MachineStartupFolder,
    UserStartupFolder,
}

/// <summary>Programme lancé au démarrage. <see cref="Id"/> est stable et opaque (ex. « UserRun|OneDrive »).</summary>
public sealed record StartupItem(string Id, string Name, string Command, StartupLocation Location, bool Enabled, string? ExecutablePath);

/// <summary>
/// Programmes au démarrage. L'activation/désactivation passe par les clés « StartupApproved »,
/// exactement comme le Gestionnaire des tâches : rien n'est supprimé, tout est réversible.
/// </summary>
public interface IStartupApi
{
    /// <param name="userSid">SID de l'utilisateur de la session (clés HKEY_USERS\SID), ou null.</param>
    Task<IReadOnlyList<StartupItem>> ListAsync(string? userSid, CancellationToken cancellationToken);

    Task<bool> SetEnabledAsync(string id, bool enabled, string? userSid, CancellationToken cancellationToken);
}

public sealed record ThirdPartyTask(string Path, string Name, string? Author, bool Enabled);

/// <summary>Tâches planifiées d'éditeurs tiers (hors dossier \Microsoft et hors PC Santé).</summary>
public interface IThirdPartyTaskApi
{
    Task<IReadOnlyList<ThirdPartyTask>> ListAsync(CancellationToken cancellationToken);

    Task<bool> SetEnabledAsync(string path, bool enabled, CancellationToken cancellationToken);
}
