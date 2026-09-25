using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows.Threading;
using PcSante.App.Infrastructure;
using PcSante.App.ViewModels;

namespace PcSante.App;

[SupportedOSPlatform("windows")]
public partial class MainWindow
{
    /// <summary>Un message de réussite reste affiché 4 s (les autres restent jusqu'à ce que l'utilisateur les ferme).</summary>
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (viewModel.Message.IsSuccess)
            {
                viewModel.Message.Close();
            }
        };
        viewModel.Message.PropertyChanged += OnMessageChanged;
        Closed += (_, _) =>
        {
            _toastTimer.Stop();
            viewModel.Message.PropertyChanged -= OnMessageChanged;
        };
    }

    private void OnMessageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ResultMessage message && e.PropertyName is nameof(ResultMessage.IsOpen) or nameof(ResultMessage.Text))
        {
            _toastTimer.Stop();
            if (message.IsOpen && message.IsSuccess)
            {
                _toastTimer.Start();
            }
        }
    }
}
