using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PcSante.App.Views;

public partial class HomeView
{
    public HomeView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Responsive.Apply(ActualWidth, SideColumn, SidePanel, SubScoreColumn, SubScoreList);
    }

    /// <summary>« Voir le détail » : amène la liste des problèmes à l'écran et place le focus sur la première action.</summary>
    private void OnShowDetails(object sender, RoutedEventArgs e)
    {
        IssuesCard.BringIntoView();
        IssuesCard.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
}

/// <summary>
/// Fenêtre étroite (moins de 1200 px) : la colonne latérale passe sous le contenu principal
/// et les sous-scores sous la conclusion (dossier de refonte § 5).
/// </summary>
internal static class Responsive
{
    public const double NarrowWidth = 1200 - 248;

    public static void Apply(double width, ColumnDefinition sideColumn, FrameworkElement sidePanel,
        ColumnDefinition subScoreColumn, FrameworkElement subScores)
    {
        var narrow = width < NarrowWidth;
        sideColumn.Width = narrow ? new GridLength(0) : new GridLength(380);
        Grid.SetColumn(sidePanel, narrow ? 0 : 1);
        Grid.SetRow(sidePanel, narrow ? 1 : 0);
        sidePanel.Margin = narrow ? new Thickness(0, 24, 0, 0) : new Thickness(24, 0, 0, 0);

        subScoreColumn.Width = narrow ? new GridLength(0) : new GridLength(440);
        Grid.SetColumn(subScores, narrow ? 1 : 2);
        Grid.SetRow(subScores, narrow ? 1 : 0);
        subScores.Margin = narrow ? new Thickness(33, 16, 33, 0) : new Thickness(0);
    }
}

public partial class ProtectionView
{
    public ProtectionView() => InitializeComponent();
}

public partial class OptimizationView
{
    public OptimizationView() => InitializeComponent();
}

public partial class PerformanceView
{
    public PerformanceView() => InitializeComponent();
}

public partial class ProcessesView
{
    public ProcessesView() => InitializeComponent();
}

public partial class SessionsView
{
    public SessionsView() => InitializeComponent();
}

public partial class SystemView
{
    public SystemView() => InitializeComponent();
}

public partial class ReportsView
{
    public ReportsView() => InitializeComponent();
}

public partial class SchedulingView
{
    public SchedulingView() => InitializeComponent();
}

public partial class SettingsView
{
    public SettingsView() => InitializeComponent();
}

public partial class LicenseView
{
    public LicenseView() => InitializeComponent();
}

public partial class WelcomeView
{
    public WelcomeView() => InitializeComponent();
}
