using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcSante.App.Localization;
using PcSante.Core.Commands;
using PcSante.Core.Ui;
using PcSante.Core.Windows;

namespace PcSante.App.ViewModels;

/// <summary>Une jauge temps réel avec son historique (60 dernières mesures).</summary>
public sealed partial class MetricSeries(string label) : ObservableObject
{
    private const int Capacity = 60;
    private readonly Queue<double> _values = new();

    public string Label { get; } = label;

    [ObservableProperty]
    private string _value = "—";

    [ObservableProperty]
    private PointCollection _points = [];

    /// <param name="percent">Valeur ramenée sur 0–100 pour le graphique.</param>
    public void Add(double percent, string display)
    {
        Value = display;
        _values.Enqueue(Math.Clamp(percent, 0, 100));
        while (_values.Count > Capacity)
        {
            _values.Dequeue();
        }

        var points = new PointCollection();
        var i = 0;
        foreach (var v in _values)
        {
            points.Add(new Point(i * 5, 100 - v));
            i++;
        }

        points.Freeze();
        Points = points;
    }
}

/// <summary>Performance : graphiques temps réel (rafraîchis toutes les 2 s uniquement quand l'écran est affiché).</summary>
[SupportedOSPlatform("windows")]
public sealed partial class PerformanceViewModel : PageViewModel
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private long _networkPeak = 1024 * 1024;

    public PerformanceViewModel(MainViewModel main)
        : base(main)
    {
        _timer.Tick += async (_, _) => await TickAsync().ConfigureAwait(true);
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage) && main.CurrentPage != this)
            {
                _timer.Stop();
            }
        };
    }

    public override ScreenId Screen => ScreenId.Performance;

    public MetricSeries Cpu { get; } = new(Loc.T("Metric_Cpu"));

    public MetricSeries Memory { get; } = new(Loc.T("Metric_Memory"));

    public MetricSeries Disk { get; } = new(Loc.T("Metric_Disk"));

    public MetricSeries Network { get; } = new(Loc.T("Metric_Network"));

    [ObservableProperty]
    private string _temperature = string.Empty;

    [ObservableProperty]
    private string _battery = string.Empty;

    public bool OverlayEnabled => Main.Settings.Overlay.Enabled;

    public string OverlayButton => Loc.T(OverlayEnabled ? "Performance_HideOverlay" : "Performance_ShowOverlay");

    public override async Task LoadAsync()
    {
        await TickAsync().ConfigureAwait(true);
        _timer.Start();
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        var overlay = Main.Settings.Overlay with { Enabled = !Main.Settings.Overlay.Enabled };
        Main.SaveSettings(Main.Settings with { Overlay = overlay }, restartUi: false);
        MainViewModel.ApplyOverlay(overlay);
        OnPropertyChanged(nameof(OverlayEnabled));
        OnPropertyChanged(nameof(OverlayButton));
        Message.Show(Loc.T(overlay.Enabled ? "Performance_OverlayShown" : "Performance_OverlayHidden"), Infrastructure.MessageKind.Success);
    }

    private async Task TickAsync()
    {
        var m = await Query<LiveMetrics>(CommandId.GetLiveMetrics).ConfigureAwait(true);
        if (m is null)
        {
            _timer.Stop();
            return;
        }

        Cpu.Add(m.CpuPercent, Loc.F("Metric_Percent", Math.Round(m.CpuPercent)));
        Memory.Add(m.MemoryPercent, Loc.F("Metric_MemoryValue", Math.Round(m.MemoryPercent), Loc.Bytes(m.MemoryUsedBytes), Loc.Bytes(m.MemoryTotalBytes)));
        Disk.Add(m.DiskActivityPercent, Loc.F("Metric_Percent", Math.Round(m.DiskActivityPercent)));
        var net = m.NetworkReceivedBytesPerSecond + m.NetworkSentBytesPerSecond;
        _networkPeak = Math.Max(_networkPeak, net);
        Network.Add(100.0 * net / _networkPeak, Loc.F("Metric_NetworkValue", Loc.Bytes(m.NetworkReceivedBytesPerSecond), Loc.Bytes(m.NetworkSentBytesPerSecond)));
        Temperature = m.CpuTemperatureCelsius is { } t ? Loc.F("Metric_TemperatureValue", t) : Loc.T("Common_NotAvailable");
        Battery = m.BatteryPercent is { } b
            ? Loc.F(m.OnAcPower == true ? "Metric_BatteryCharging" : "Metric_BatteryValue", b)
            : Loc.T("Metric_NoBattery");
    }
}
