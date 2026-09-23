using System.Globalization;
using PcSante.Core.Settings;
using PcSante.Core.Windows;

namespace PcSante.Core.Ui;

/// <summary>Libellés courts du mini-affichage (fournis par les ressources de l'exécutable Overlay).</summary>
public sealed record OverlayLabels(string Cpu, string Memory, string Disk, string Network, string Temperature, string Battery, string NotAvailable);

/// <summary>Texte du mini-affichage (M5) : uniquement les indicateurs choisis, format compact.</summary>
public static class OverlayFormatter
{
    public static string Format(LiveMetrics metrics, OverlayIndicators indicators, OverlayLabels labels, CultureInfo culture, string separator = "   ")
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(culture);
        var parts = new List<string>(6);
        if (indicators.HasFlag(OverlayIndicators.Cpu))
        {
            parts.Add(string.Format(culture, "{0} {1:0} %", labels.Cpu, metrics.CpuPercent));
        }

        if (indicators.HasFlag(OverlayIndicators.Memory))
        {
            parts.Add(string.Format(culture, "{0} {1:0} %", labels.Memory, metrics.MemoryPercent));
        }

        if (indicators.HasFlag(OverlayIndicators.Disk))
        {
            parts.Add(string.Format(culture, "{0} {1:0} %", labels.Disk, metrics.DiskActivityPercent));
        }

        if (indicators.HasFlag(OverlayIndicators.Network))
        {
            parts.Add(string.Format(culture, "{0} ↓{1} ↑{2}", labels.Network, Rate(metrics.NetworkReceivedBytesPerSecond, culture), Rate(metrics.NetworkSentBytesPerSecond, culture)));
        }

        if (indicators.HasFlag(OverlayIndicators.Temperature))
        {
            parts.Add(metrics.CpuTemperatureCelsius is { } t
                ? string.Format(culture, "{0} {1:0} °C", labels.Temperature, t)
                : $"{labels.Temperature} {labels.NotAvailable}");
        }

        if (indicators.HasFlag(OverlayIndicators.Battery) && metrics.BatteryPercent is { } b)
        {
            parts.Add(string.Format(culture, "{0} {1} %{2}", labels.Battery, b, metrics.OnAcPower == true ? " ⚡" : string.Empty));
        }

        return string.Join(separator, parts);
    }

    /// <summary>Débit compact : 850 K, 1,2 M (octets par seconde).</summary>
    public static string Rate(long bytesPerSecond, CultureInfo culture) => bytesPerSecond switch
    {
        < 1024 => string.Format(culture, "{0} B", bytesPerSecond),
        < 1024 * 1024 => string.Format(culture, "{0:0} K", bytesPerSecond / 1024.0),
        _ => string.Format(culture, "{0:0.0} M", bytesPerSecond / (1024.0 * 1024)),
    };
}
