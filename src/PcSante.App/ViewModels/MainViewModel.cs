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

public sealed record NavItem(ScreenId Screen, string Label, Wpf.Ui.Controls.SymbolRegular Icon);

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

    public WelcomeViewModel? Welcome { get; private set; }

    public async Task StartAsync()
    {
        await RefreshLicenseAsync().ConfigureAwait(true);
        if (!Settings.FirstRunCompleted)
        {
            Welcome = new WelcomeViewModel(this) { Step = PendingWelcomeStep };
            OnPropertyChanged(nameof(Welcome));
            ShowWelcome = true;
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

    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

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
        foreach (var screen in ScreenCatalog.VisibleScreens(Settings.Mode))
        {
            Items.Add(new NavItem(screen, Loc.T(IsSimpleMode && screen == ScreenId.Optimization ? "Nav_Optimization_Simple" : $"Nav_{screen}"), IconOf(screen)));
        }

        FooterItems.Clear();
        foreach (var screen in ScreenCatalog.FooterScreens)
        {
            FooterItems.Add(new NavItem(screen, Loc.T($"Nav_{screen}"), IconOf(screen)));
        }

        _pages.Clear();
        if (current is { } c)
        {
            SelectedItem = Items.FirstOrDefault(i => i.Screen == c) ?? Items.FirstOrDefault();
        }
    }

    private static Wpf.Ui.Controls.SymbolRegular IconOf(ScreenId screen) => screen switch
    {
        ScreenId.Home => Wpf.Ui.Controls.SymbolRegular.Home24,
        ScreenId.Protection => Wpf.Ui.Controls.SymbolRegular.Shield24,
        ScreenId.Optimization => Wpf.Ui.Controls.SymbolRegular.Broom24,
        ScreenId.Performance => Wpf.Ui.Controls.SymbolRegular.DataArea24,
        ScreenId.Processes => Wpf.Ui.Controls.SymbolRegular.AppsList24,
        ScreenId.System => Wpf.Ui.Controls.SymbolRegular.Wrench24,
        ScreenId.Reports => Wpf.Ui.Controls.SymbolRegular.DocumentPdf24,
        ScreenId.Scheduling => Wpf.Ui.Controls.SymbolRegular.CalendarClock24,
        ScreenId.Settings => Wpf.Ui.Controls.SymbolRegular.Settings24,
        ScreenId.License => Wpf.Ui.Controls.SymbolRegular.Key24,
        _ => Wpf.Ui.Controls.SymbolRegular.Circle24,
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
