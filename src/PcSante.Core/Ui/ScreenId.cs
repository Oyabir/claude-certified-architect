namespace PcSante.Core.Ui;

/// <summary>Écrans de l'application (section 4) et écrans transverses.</summary>
public enum ScreenId
{
    Home,
    Protection,
    Performance,
    Processes,
    Optimization,
    System,
    Sessions,
    Reports,
    Scheduling,
    Settings,
    License,
}

public enum DisplayMode
{
    Simple,
    Advanced,
}

/// <summary>État d'un écran dans la navigation.</summary>
public enum ScreenAvailability
{
    Visible,
    HiddenInSimpleMode,
    /// <summary>Hors MVP : non affiché (Sessions = V2).</summary>
    NotInThisVersion,
}

/// <summary>Règles de navigation du mode Simple / Avancé (section 11).</summary>
public static class ScreenCatalog
{
    /// <summary>Ordre d'affichage dans la barre latérale.</summary>
    public static IReadOnlyList<ScreenId> NavigationOrder { get; } =
    [
        ScreenId.Home, ScreenId.Protection, ScreenId.Optimization, ScreenId.Performance, ScreenId.Processes,
        ScreenId.System, ScreenId.Sessions, ScreenId.Scheduling, ScreenId.Reports,
    ];

    /// <summary>Écrans affichés en permanence en bas de la barre latérale.</summary>
    public static IReadOnlyList<ScreenId> FooterScreens { get; } = [ScreenId.License, ScreenId.Settings];

    public static ScreenAvailability GetAvailability(ScreenId screen, DisplayMode mode) => screen switch
    {
        ScreenId.Sessions => ScreenAvailability.NotInThisVersion,
        ScreenId.Home or ScreenId.Protection or ScreenId.Optimization or ScreenId.Reports
            or ScreenId.Settings or ScreenId.License => ScreenAvailability.Visible,
        _ => mode == DisplayMode.Advanced ? ScreenAvailability.Visible : ScreenAvailability.HiddenInSimpleMode,
    };

    public static IReadOnlyList<ScreenId> VisibleScreens(DisplayMode mode) =>
        NavigationOrder.Where(s => GetAvailability(s, mode) == ScreenAvailability.Visible).ToList();
}
