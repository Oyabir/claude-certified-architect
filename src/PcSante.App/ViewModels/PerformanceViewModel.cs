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

/// <summary>
/// Une jauge temps réel avec son historique (60 dernières mesures), dessinée sur une zone de 300 × 100 :
/// les mesures les plus récentes à droite. Tant qu'il y a moins de deux mesures : « Collecte des données… ».
/// </summary>
public sealed partial class MetricSeries(string label, string icon = "PcsIcon.cpu") : ObservableObject
{
    public const int Capacity = 60;
    private const double Width = 300;
    private readonly Queue<double> _values = new();

    public string Label { get; } = label;

    public string Icon { get; } = icon;

    [ObservableProperty]
    private string _value = "—";

    /// <summary>Précision à droite du titre (« 10,6 Go sur 15,8 Go »).</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private PointCollection _points = [];

    [ObservableProperty]
    private bool _isCollecting = true;

    /// <param name="percent">Valeur ramenée sur 0–100 pour le graphique.</param>
    public void Add(double percent, string display)
    {
        Value = display;
        Push(percent);
    }

    /// <summary>Ajoute une mesure sans changer le texte (seconde courbe du réseau).</summary>
    public void Push(double percent)
    {
        _values.Enqueue(Math.Clamp(percent, 0, 100));
        while (_values.Count > Capacity)
        {
            _values.Dequeue();
        }

        Redraw();
    }

    /// <summary>Échelle automatique (réseau) : toutes les mesures sont recalculées par rapport au nouveau maximum.</summary>
    public void Rescale(double factor)
    {
        var values = _values.Select(v => Math.Clamp(v * factor, 0, 100)).ToList();
        _values.Clear();
        values.ForEach(_values.Enqueue);
        Redraw();
    }

    private void Redraw()
    {
        var step = Width / (Capacity - 1);
        var start = Capacity - _values.Count;
        var line = new PointCollection();
        var i = 0;
        foreach (var v in _values)
        {
            line.Add(new Point((start + i) * step, 100 - v));
            i++;
        }

        line.Freeze();
        Points = line;
        IsCollecting = _values.Count < 2;
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

    /// <summary>« Ce que votre PC utilise en ce moment · 2 dernières minutes ».</summary>
    public string SubtitleWithWindow => Subtitle.TrimEnd('.', ' ', '。') + " · " + Loc.T("Performance_Window");

    public MetricSeries Cpu { get; } = new(Loc.T("Metric_Cpu"), "PcsIcon.cpu") { Note = Loc.T("Metric_CpuNote") };

    public MetricSeries Memory { get; } = new(Loc.T("Metric_Memory"), "PcsIcon.memory");

    public MetricSeries Disk { get; } = new(Loc.T("Metric_Disk"), "PcsIcon.hard-drive") { Note = Loc.T("Metric_DiskNote") };

    /// <summary>Réseau : réception (courbe de marque) et envoi (seconde courbe), échelle automatique.</summary>
    public MetricSeries Network { get; } = new(Loc.T("Metric_Network"), "PcsIcon.wifi") { Note = Loc.T("Metric_NetworkNote") };

    public MetricSeries NetworkSent { get; } = new(Loc.T("Metric_Network"), "PcsIcon.wifi");

    [ObservableProperty]
    private string _temperature = string.Empty;

    /// <summary>Température en % d'une échelle 0–100 °C (barre).</summary>
    [ObservableProperty]
    private double _temperatureLevel;

    [ObservableProperty]
    private bool _hasTemperature;

    [ObservableProperty]
    private string _battery = string.Empty;

    [ObservableProperty]
    private string _batteryValue = string.Empty;

    [ObservableProperty]
    private string _batteryNote = string.Empty;

    [ObservableProperty]
    private double _batteryLevel;

    [ObservableProperty]
    private bool _hasBattery;

    /// <summary>Batterie sous 25 % : barre orange et mot « Faible ».</summary>
    [ObservableProperty]
    private bool _batteryLow;

    /// <summary>Aperçu du mini-affichage avec les mesures du moment.</summary>
    [ObservableProperty]
    private string _overlayPreview = string.Empty;

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
        Memory.Note = Loc.F("Metric_MemoryNote", Loc.Bytes(m.MemoryUsedBytes), Loc.Bytes(m.MemoryTotalBytes));
        Memory.Value = Loc.F("Metric_Percent", Math.Round(m.MemoryPercent));
        var peak = Math.Max(m.NetworkReceivedBytesPerSecond, m.NetworkSentBytesPerSecond);
        if (peak > _networkPeak)
        {
            // Échelle automatique : les courbes déjà tracées sont ramenées au nouveau maximum.
            var factor = (double)_networkPeak / peak;
            Network.Rescale(factor);
            NetworkSent.Rescale(factor);
            _networkPeak = peak;
        }

        Network.Add(100.0 * m.NetworkReceivedBytesPerSecond / _networkPeak, Loc.F("Metric_NetworkValue", Loc.Bytes(m.NetworkReceivedBytesPerSecond), Loc.Bytes(m.NetworkSentBytesPerSecond)));
        NetworkSent.Push(100.0 * m.NetworkSentBytesPerSecond / _networkPeak);
        Temperature = m.CpuTemperatureCelsius is { } t ? Loc.F("Metric_TemperatureValue", Math.Round(t)) : Loc.T("Common_NotAvailable");
        HasTemperature = m.CpuTemperatureCelsius is not null;
        TemperatureLevel = Math.Clamp(m.CpuTemperatureCelsius ?? 0, 0, 100);
        Battery = m.BatteryPercent is { } b
            ? Loc.F(m.OnAcPower == true ? "Metric_BatteryCharging" : "Metric_BatteryValue", b)
            : Loc.T("Metric_NoBattery");
        HasBattery = m.BatteryPercent is not null;
        BatteryLevel = m.BatteryPercent ?? 0;
        BatteryLow = m.BatteryPercent is < 25;
        BatteryValue = m.BatteryPercent is { } level ? Loc.F("Metric_Percent", level) : Loc.T("Metric_NoBattery");
        BatteryNote = m.BatteryPercent is null ? string.Empty
            : string.Join(" · ", new[] { BatteryLow ? Loc.T("Metric_BatteryLow") : null, Loc.T(m.OnAcPower == true ? "Metric_OnAc" : "Metric_OnBattery") }.Where(x => x is not null));
        OverlayPreview = string.Join("   ", new[]
        {
            Loc.F("Overlay_Cpu", Math.Round(m.CpuPercent)),
            Loc.F("Overlay_Ram", Math.Round(m.MemoryPercent)),
            m.CpuTemperatureCelsius is { } c ? Loc.F("Overlay_Temp", Math.Round(c)) : null,
            m.BatteryPercent is { } bat ? Loc.F("Overlay_Bat", bat) : null,
        }.Where(x => x is not null));
    }
}
