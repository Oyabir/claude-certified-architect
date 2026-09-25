using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
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
        var preview = false;
#if DEBUG
        preview = e.Args.Contains("--apercu", StringComparer.Ordinal);
#endif
        _service = new ServiceClient(preview);
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
            FontFamily = (FontFamily)Resources[Loc.IsRightToLeft ? "PcsFontArabic" : "PcsFontLatin"],
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

    /// <summary>
    /// Applique le thème (clair, sombre ou celui de Windows) : dictionnaire de couleurs PC Santé correspondant,
    /// et couleur de marque imposée aux contrôles WPF-UI (plus aucune dépendance à l'accent de Windows).
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => ApplicationThemeManager.GetSystemTheme() is SystemTheme.Dark or SystemTheme.HCBlack or SystemTheme.Glow or SystemTheme.CapturedMotion,
        };
        var applicationTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(applicationTheme, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);

        var colors = new ResourceDictionary { Source = new Uri(dark ? "Themes/PcSante.Colors.Dark.xaml" : "Themes/PcSante.Colors.Light.xaml", UriKind.Relative) };
        var merged = Resources.MergedDictionaries;
        var current = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("PcSante.Colors.", StringComparison.Ordinal) == true);
        if (current is null)
        {
            merged.Add(colors);
        }
        else
        {
            merged[merged.IndexOf(current)] = colors;
        }

        var brand = (Color)colors["PcsColorBrand"];
        var brandHover = (Color)colors["PcsColorBrandHover"];
        ApplicationAccentColorManager.Apply(brand, brand, brand, brandHover);
    }
}
