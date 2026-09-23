using PcSante.Core.Ui;

namespace PcSante.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum OverlayPosition
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}

[Flags]
public enum OverlayIndicators
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Disk = 4,
    Network = 8,
    Temperature = 16,
    Battery = 32,
    All = Cpu | Memory | Disk | Network | Temperature | Battery,
}

public sealed record OverlaySettings
{
    public bool Enabled { get; init; }

    /// <summary>Icône dans la zone de notification au lieu de la barre.</summary>
    public bool TrayIconOnly { get; init; }

    public OverlayPosition Position { get; init; } = OverlayPosition.TopRight;

    /// <summary>Taille du texte en pixels (14 minimum, section 11).</summary>
    public int FontSize { get; init; } = 14;

    /// <summary>Opacité de 30 à 100 %.</summary>
    public int OpacityPercent { get; init; } = 80;

    public OverlayIndicators Indicators { get; init; } = OverlayIndicators.Cpu | OverlayIndicators.Memory | OverlayIndicators.Disk | OverlayIndicators.Network;

    public bool HideInFullScreen { get; init; } = true;

    public OverlaySettings Normalized() => this with
    {
        FontSize = Math.Clamp(FontSize, 14, 32),
        OpacityPercent = Math.Clamp(OpacityPercent, 30, 100),
    };
}

/// <summary>Préférences de l'utilisateur (fichier JSON dans %LOCALAPPDATA%).</summary>
public sealed record UserSettings
{
    /// <summary>fr (défaut), en ou ar.</summary>
    public string Language { get; init; } = "fr";

    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Mode Simple par défaut (section 11).</summary>
    public DisplayMode Mode { get; init; } = DisplayMode.Simple;

    public bool FirstRunCompleted { get; init; }

    public OverlaySettings Overlay { get; init; } = new();

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["fr", "en", "ar"];

    public UserSettings Normalized() => this with
    {
        Language = SupportedLanguages.Contains(Language) ? Language : "fr",
        Overlay = (Overlay ?? new OverlaySettings()).Normalized(),
    };
}
