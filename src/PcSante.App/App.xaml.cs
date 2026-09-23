using System.Runtime.Versioning;
using System.Windows;
using PcSante.App.Infrastructure;
using PcSante.App.Localization;
using PcSante.App.ViewModels;
using PcSante.Core.Settings;
using Wpf.Ui.Appearance;

namespace PcSante.App;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private ServiceClient? _service;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _service = new ServiceClient();
        OpenMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _service?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    /// <summary>Ouvre (ou recrée après un changement de langue, de thème ou de mode) la fenêtre principale.</summary>
    private void OpenMainWindow()
    {
        var store = new SettingsStore(SettingsStore.DefaultPath());
        var settings = store.Load();
        Loc.SetLanguage(settings.Language);
        ApplyTheme(settings.Theme);

        var viewModel = new MainViewModel(_service!, store);
        var window = new MainWindow(viewModel)
        {
            FlowDirection = Loc.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag),
        };
        viewModel.RestartRequested += (_, _) =>
        {
            OpenMainWindow();
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (Windows.Count == 0)
            {
                Shutdown();
            }
        };
        MainWindow = window;
        window.Show();
        _ = viewModel.StartAsync();
    }

    private static void ApplyTheme(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}
