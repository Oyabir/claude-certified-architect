using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Localization;
using PcSante.Core.Settings;
using PcSante.Core.Ui;

namespace PcSante.App.ViewModels;

/// <summary>Paramètres : langue, thème, mode Simple/Avancé, mini-affichage.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(MainViewModel main)
        : base(main)
    {
        Load(main.Settings);
    }

    public override ScreenId Screen => ScreenId.Settings;

    public IReadOnlyList<Choice> Languages { get; } =
        [new("fr", "Français"), new("en", "English"), new("ar", "العربية")];

    public IReadOnlyList<Choice> Themes { get; } = Enum.GetNames<AppTheme>().Select(t => new Choice(t, Loc.T($"Theme_{t}"))).ToList();

    public IReadOnlyList<Choice> Positions { get; } = Enum.GetNames<OverlayPosition>().Select(p => new Choice(p, Loc.T($"Position_{p}"))).ToList();

    [ObservableProperty]
    private string _language = "fr";

    [ObservableProperty]
    private string _theme = nameof(AppTheme.System);

    [ObservableProperty]
    private bool _advancedMode;

    [ObservableProperty]
    private bool _overlayEnabled;

    [ObservableProperty]
    private bool _overlayTrayOnly;

    [ObservableProperty]
    private string _overlayPosition = nameof(Core.Settings.OverlayPosition.TopRight);

    [ObservableProperty]
    private int _overlayFontSize = 14;

    [ObservableProperty]
    private int _overlayOpacity = 80;

    [ObservableProperty]
    private bool _overlayHideFullScreen = true;

    [ObservableProperty]
    private bool _showCpu;

    [ObservableProperty]
    private bool _showMemory;

    [ObservableProperty]
    private bool _showDisk;

    [ObservableProperty]
    private bool _showNetwork;

    [ObservableProperty]
    private bool _showTemperature;

    [ObservableProperty]
    private bool _showBattery;

    public override Task LoadAsync()
    {
        Load(Main.Settings);
        return Task.CompletedTask;
    }

    /// <summary>Bouton principal : enregistrer. Langue, thème ou mode modifiés : l'interface est rechargée.</summary>
    [RelayCommand]
    private void Save()
    {
        var old = Main.Settings;
        var indicators = OverlayIndicators.None;
        indicators |= ShowCpu ? OverlayIndicators.Cpu : 0;
        indicators |= ShowMemory ? OverlayIndicators.Memory : 0;
        indicators |= ShowDisk ? OverlayIndicators.Disk : 0;
        indicators |= ShowNetwork ? OverlayIndicators.Network : 0;
        indicators |= ShowTemperature ? OverlayIndicators.Temperature : 0;
        indicators |= ShowBattery ? OverlayIndicators.Battery : 0;
        var updated = old with
        {
            Language = Language,
            Theme = Enum.Parse<AppTheme>(Theme),
            Mode = AdvancedMode ? DisplayMode.Advanced : DisplayMode.Simple,
            Overlay = new OverlaySettings
            {
                Enabled = OverlayEnabled,
                TrayIconOnly = OverlayTrayOnly,
                Position = Enum.Parse<OverlayPosition>(OverlayPosition),
                FontSize = OverlayFontSize,
                OpacityPercent = OverlayOpacity,
                HideInFullScreen = OverlayHideFullScreen,
                Indicators = indicators == OverlayIndicators.None ? OverlayIndicators.Cpu : indicators,
            },
        };
        var restart = updated.Language != old.Language || updated.Theme != old.Theme || updated.Mode != old.Mode;
        Main.SaveSettings(updated, restart);
        MainViewModel.ApplyOverlay(updated.Overlay);
        if (!restart)
        {
            Message.Show(Loc.T("Settings_Saved"), Infrastructure.MessageKind.Success);
        }
    }

    private void Load(UserSettings s)
    {
        Language = s.Language;
        Theme = s.Theme.ToString();
        AdvancedMode = s.Mode == DisplayMode.Advanced;
        OverlayEnabled = s.Overlay.Enabled;
        OverlayTrayOnly = s.Overlay.TrayIconOnly;
        OverlayPosition = s.Overlay.Position.ToString();
        OverlayFontSize = s.Overlay.FontSize;
        OverlayOpacity = s.Overlay.OpacityPercent;
        OverlayHideFullScreen = s.Overlay.HideInFullScreen;
        ShowCpu = s.Overlay.Indicators.HasFlag(OverlayIndicators.Cpu);
        ShowMemory = s.Overlay.Indicators.HasFlag(OverlayIndicators.Memory);
        ShowDisk = s.Overlay.Indicators.HasFlag(OverlayIndicators.Disk);
        ShowNetwork = s.Overlay.Indicators.HasFlag(OverlayIndicators.Network);
        ShowTemperature = s.Overlay.Indicators.HasFlag(OverlayIndicators.Temperature);
        ShowBattery = s.Overlay.Indicators.HasFlag(OverlayIndicators.Battery);
    }
}
