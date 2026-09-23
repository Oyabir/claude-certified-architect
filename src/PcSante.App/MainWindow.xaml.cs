using System.Runtime.Versioning;
using PcSante.App.ViewModels;

namespace PcSante.App;

[SupportedOSPlatform("windows")]
public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
