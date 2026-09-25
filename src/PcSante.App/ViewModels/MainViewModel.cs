using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.Core;
using PcSante.Core.Commands;
using PcSante.Core.Licensing;
using PcSante.Core.Settings;
using PcSante.Core.Ui;

namespace PcSante.App.ViewModels;

/// <summary>Élément de la barre latérale : groupe ESSENTIEL ou AVANCÉ, icône, pastille (nombre de points à regarder).</summary>
public sealed partial class NavItem(ScreenId screen, string label, string icon, string group) : ObservableObject
{
    public ScreenId Screen { get; } = screen;

    public string Label { get; } = label;

    /// <summary>Clé de l'icône (Themes/PcSante.Icons.xaml, « PcsIcon.… »).</summary>
    public string Icon { get; } = icon;

    public string Group { get; } = group;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badge;

    public bool HasBadge => Badge > 0;
}

/// <summary>Fenêtre principale : navigation latérale (mode Simple / Avancé), écran courant, message de résultat.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<ScreenId, PageViewModel> _pages = [];

    public MainViewModel(ServiceClient service, SettingsStore settingsStore)
    {
        Service = service;
        SettingsStore = settingsStore;
        Settings = settingsStore.Load();
        RebuildNavigation();
    }

    public ServiceClient Service { get; }

    public SettingsStore SettingsStore { get; }

    public ResultMessage Message { get; } = new();

    public string ProductName => ProductInfo.Name;

    public event EventHandler? RestartRequested;

    /// <summary>Étape à laquelle reprendre l'assistant après un rechargement (changement de langue).</summary>
    public static int PendingWelcomeStep { get; set; } = 1;

    [ObservableProperty]
    private UserSettings _settings;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private NavItem? _selectedItem;

    /// <summary>Licence ou Paramètres (bas de la barre latérale), même style que les autres éléments.</summary>
    [ObservableProperty]
    private NavItem? _selectedFooterItem;

    /// <summary>Nombre de points à regarder, affiché en pastille sur « Accueil ».</summary>
    private int _attentionCount;

    private bool _rebuilding;

    [ObservableProperty]
    private bool _serviceAvailable = true;

    [ObservableProperty]
    private LicenseStatus? _license;

    [ObservableProperty]
    private bool _showWelcome;

    public ObservableCollection<NavItem> Items { get; } = [];

    public ObservableCollection<NavItem> FooterItems { get; } = [];

    public bool IsSimpleMode => Settings.Mode == DisplayMode.Simple;

    public bool IsPremium => License?.IsPremium == true;

    /// <summary>La commande exige Premium et l'offre active est Gratuite : l'interface le dit avant d'agir.</summary>
    public bool NeedsPremium(CommandId command) => !IsPremium && CommandDefinitions.Get(command).Tier != RequiredTier.Free;

    /// <summary>Explique que l'action fait partie de Premium, avec le bouton « Passer à Premium ».</summary>
    public void ShowPremiumRequired() => Message.Show(CommandResult.Refused(FailureReason.LicenseRequired, "Result_PremiumRequired"));

    public string LicenseBadge => IsPremium ? Loc.T($"Tier_{License!.EffectiveTier}") : Loc.T("Tier_Free");

    /// <summary>Sous-titre de la carte d'offre : « Licence active sur ce PC » ou « Votre offre ».</summary>
    public string LicenseCaption => IsPremium ? Loc.T("Nav_LicenseActive") : Loc.T("Nav_Offer");

    public void SetAttentionCount(int count)
    {
        _attentionCount = count;
        foreach (var item in Items)
        {
            item.Badge = item.Screen == ScreenId.Home ? count : 0;
        }
    }

    public WelcomeViewModel? Welcome { get; private set; }

    public async Task StartAsync()
    {
        await RefreshLicenseAsync().ConfigureAwait(true);
        if (!Settings.FirstRunCompleted)
        {
            Welcome = new WelcomeViewModel(this) { Step = PendingWelcomeStep };
            OnPropertyChanged(nameof(Welcome));
            ShowWelcome = true;
            await Welcome.SkipLicenseIfActiveAsync().ConfigureAwait(true);
            return;
        }

        SelectedItem = Items.FirstOrDefault();
    }

    public async Task RefreshLicenseAsync()
    {
        var result = await Service.RunAsync(CommandId.GetLicenseStatus).ConfigureAwait(true);
        ServiceAvailable = result.Reason != FailureReason.ServiceUnavailable;
        License = result.GetData<LicenseStatus>();
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(LicenseBadge));
        OnPropertyChanged(nameof(LicenseCaption));
    }

    /// <summary>
    /// Ouvre un écran. Les écrans du bas (Licence, Paramètres) et ceux ouverts depuis un problème
    /// (écran avancé en mode Simple) s'ouvrent sans sélection dans la liste principale.
    /// </summary>
    public void Navigate(ScreenId screen)
    {
        var item = Items.FirstOrDefault(i => i.Screen == screen);
        if (item is not null)
        {
            SelectedItem = item;
            return;
        }

        var footer = FooterItems.FirstOrDefault(i => i.Screen == screen);
        if (footer is not null)
        {
            SelectedFooterItem = footer;
            return;
        }

        SelectedItem = null;
        Message.Close();
        CurrentPage = GetPage(screen);
        _ = CurrentPage.LoadAsync();
    }

    [RelayCommand]
    private void NavigateTo(ScreenId screen) => Navigate(screen);

    public void SaveSettings(UserSettings settings, bool restartUi)
    {
        Settings = settings.Normalized();
        SettingsStore.Save(Settings);
        OnPropertyChanged(nameof(IsSimpleMode));
        if (restartUi)
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        RebuildNavigation();
    }

    public void CompleteWelcome()
    {
        SaveSettings(Settings with { FirstRunCompleted = true }, restartUi: false);
        ShowWelcome = false;
        SelectedItem = Items.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RetryServiceAsync()
    {
        await RefreshLicenseAsync().ConfigureAwait(true);
        if (ServiceAvailable && CurrentPage is not null)
        {
            await CurrentPage.LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void GoToLicense()
    {
        Navigate(ScreenId.License);
    }

    [RelayCommand]
    private void ToggleDetails() => Message.ShowDetails = !Message.ShowDetails;

    [RelayCommand]
    private void CloseMessage() => Message.Close();

    partial void OnSelectedFooterItemChanged(NavItem? value)
    {
        if (value is null || _rebuilding)
        {
            return;
        }

        SelectedItem = null;
        Message.Close();
        CurrentPage = GetPage(value.Screen);
        _ = CurrentPage.LoadAsync();
    }

    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedFooterItem = null;

        Message.Close();
        CurrentPage = GetPage(value.Screen);
        _ = CurrentPage.LoadAsync();
    }

    private PageViewModel GetPage(ScreenId screen)
    {
        if (!_pages.TryGetValue(screen, out var page))
        {
            page = screen switch
            {
                ScreenId.Home => new HomeViewModel(this),
                ScreenId.Protection => new ProtectionViewModel(this),
                ScreenId.Optimization => new OptimizationViewModel(this),
                ScreenId.Performance => new PerformanceViewModel(this),
                ScreenId.Processes => new ProcessesViewModel(this),
                ScreenId.System => new SystemViewModel(this),
                ScreenId.Reports => new ReportsViewModel(this),
                ScreenId.Scheduling => new SchedulingViewModel(this),
                ScreenId.Sessions => new SessionsViewModel(this),
                ScreenId.Settings => new SettingsViewModel(this),
                _ => new LicenseViewModel(this),
            };
            _pages[screen] = page;
        }

        return page;
    }

    private void RebuildNavigation()
    {
        var current = SelectedItem?.Screen;
        Items.Clear();
        // Groupe ESSENTIEL (écrans du mode Simple) puis AVANCÉ, masqué en mode Simple.
        var visible = ScreenCatalog.VisibleScreens(Settings.Mode);
        var essential = visible.Where(s => ScreenCatalog.GetAvailability(s, DisplayMode.Simple) == ScreenAvailability.Visible);
        var advanced = visible.Where(s => ScreenCatalog.GetAvailability(s, DisplayMode.Simple) != ScreenAvailability.Visible);
        foreach (var screen in essential)
        {
            Items.Add(new NavItem(screen, Loc.T(IsSimpleMode && screen == ScreenId.Optimization ? "Nav_Optimization_Simple" : $"Nav_{screen}"), IconOf(screen), Loc.T("Nav_Essential"))
            {
                Badge = screen == ScreenId.Home ? _attentionCount : 0,
            });
        }

        foreach (var screen in advanced)
        {
            Items.Add(new NavItem(screen, Loc.T($"Nav_{screen}"), IconOf(screen), Loc.T("Nav_Advanced")));
        }

        var footerScreen = SelectedFooterItem?.Screen;
        FooterItems.Clear();
        foreach (var screen in ScreenCatalog.FooterScreens)
        {
            FooterItems.Add(new NavItem(screen, Loc.T($"Nav_{screen}"), IconOf(screen), string.Empty));
        }

        // L'écran du bas reste sélectionné (Paramètres enregistrés sans rechargement de la page).
        _rebuilding = true;
        SelectedFooterItem = footerScreen is { } f ? FooterItems.FirstOrDefault(i => i.Screen == f) : null;
        _rebuilding = false;

        _pages.Clear();
        if (current is { } c)
        {
            SelectedItem = Items.FirstOrDefault(i => i.Screen == c) ?? Items.FirstOrDefault();
        }
    }

    private static string IconOf(ScreenId screen) => screen switch
    {
        ScreenId.Home => "PcsIcon.home",
        ScreenId.Protection => "PcsIcon.shield",
        ScreenId.Optimization => "PcsIcon.sparkle",
        ScreenId.Performance => "PcsIcon.pulse",
        ScreenId.Processes => "PcsIcon.list",
        ScreenId.System => "PcsIcon.monitor",
        ScreenId.Reports => "PcsIcon.report",
        ScreenId.Scheduling => "PcsIcon.calendar",
        ScreenId.Sessions => "PcsIcon.users",
        ScreenId.Settings => "PcsIcon.sliders",
        ScreenId.License => "PcsIcon.key",
        _ => "PcsIcon.home",
    };

    /// <summary>Démarre ou arrête le mini-affichage selon les réglages.</summary>
    public static void ApplyOverlay(OverlaySettings overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        foreach (var p in Process.GetProcessesByName("PcSante.Overlay"))
        {
            using (p)
            {
                if (!overlay.Enabled)
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(2000))
                    {
                        p.Kill();
                    }
                }
            }
        }

        var exe = Path.Combine(AppContext.BaseDirectory, "PcSante.Overlay.exe");
        if (overlay.Enabled && Process.GetProcessesByName("PcSante.Overlay").Length == 0 && File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }
    }
}
