using PcSante.Core.Windows;

namespace PcSante.Core.Optimization;

public enum ServiceProfile
{
    Office,
    Gaming,
    Laptop,
}

/// <summary>Service qui passera en démarrage manuel avec le profil choisi.</summary>
public sealed record ServiceChange(string Name, string DisplayName, ServiceStartMode From);

/// <summary>
/// Profils de services (M4) : liste PRUDENTE (section 8, risque « optimisation qui casse Windows »).
/// Seuls des services démarrant automatiquement passent en « Manuel » : Windows les relance si un programme
/// en a besoin. Aucun service n'est désactivé ; l'état précédent est sauvegardé pour « Annuler ».
/// </summary>
public static class ServiceProfiles
{
    // Télémétrie, cartes hors ligne, partage Windows Media, démo magasin : inutiles au quotidien pour tous les profils.
    private static readonly string[] Common = ["DiagTrack", "MapsBroker", "WMPNetworkSvc", "RetailDemo"];

    // Services Xbox : utiles au jeu, superflus en bureautique et sur un portable (batterie).
    private static readonly string[] Xbox = ["XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"];

    public static IReadOnlyList<string> ServicesOf(ServiceProfile profile) => profile switch
    {
        ServiceProfile.Gaming => Common,
        _ => [.. Common, .. Xbox],
    };

    /// <summary>Un service n'est concerné que s'il démarre automatiquement aujourd'hui.</summary>
    public static bool ShouldChange(ServiceInfo service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed;
    }
}
