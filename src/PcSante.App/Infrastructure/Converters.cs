using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PcSante.Core.Health;
using PcSante.Core.Processes;

namespace PcSante.App.Infrastructure;

/// <summary>Code couleur constant : vert = tout va bien, orange = à surveiller, rouge = à corriger.</summary>
public static class HealthBrushes
{
    public static readonly SolidColorBrush Green = Freeze(Color.FromRgb(0x1E, 0x7B, 0x34));
    public static readonly SolidColorBrush Orange = Freeze(Color.FromRgb(0xB2, 0x5E, 0x00));
    public static readonly SolidColorBrush Red = Freeze(Color.FromRgb(0xB4, 0x23, 0x18));
    public static readonly SolidColorBrush Grey = Freeze(Color.FromRgb(0x5B, 0x64, 0x70));

    public static SolidColorBrush Of(HealthColor color) => color switch
    {
        HealthColor.Green => Green,
        HealthColor.Orange => Orange,
        _ => Red,
    };

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public sealed class HealthBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        HealthColor c => HealthBrushes.Of(c),
        IssueSeverity s => HealthBrushes.Of(HealthScoreCalculator.ColorOf(s)),
        int score => HealthBrushes.Of(HealthScoreCalculator.ColorOf(score)),
        Reputation r => r switch
        {
            Reputation.Useful => HealthBrushes.Green,
            Reputation.Unnecessary => HealthBrushes.Orange,
            Reputation.Suspicious => HealthBrushes.Red,
            _ => HealthBrushes.Grey,
        },
        bool ok => ok ? HealthBrushes.Green : HealthBrushes.Red,
        _ => HealthBrushes.Grey,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            bool b => b,
            null => false,
            string s => s.Length > 0,
            int i => i > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        return visible ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
