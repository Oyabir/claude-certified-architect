using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PcSante.Core.Health;
using PcSante.Core.Processes;

namespace PcSante.App.Infrastructure;

/// <summary>Tonalité d'un état : vert = tout va bien, orange = à surveiller, rouge = à corriger, neutre = sans jugement.</summary>
public enum Tone
{
    Neutral,
    Good,
    Warn,
    Critical,
}

/// <summary>
/// Code couleur constant, tiré des jetons du thème actif (Themes/PcSante.Colors.*.xaml) :
/// jamais de couleur en dur dans les écrans.
/// </summary>
public static class HealthBrushes
{
    public static Tone ToneOf(object? value) => value switch
    {
        Tone t => t,
        HealthColor c => c switch
        {
            HealthColor.Green => Tone.Good,
            HealthColor.Orange => Tone.Warn,
            _ => Tone.Critical,
        },
        IssueSeverity s => ToneOf(HealthScoreCalculator.ColorOf(s)),
        int score => ToneOf(HealthScoreCalculator.ColorOf(score)),
        Reputation r => r switch
        {
            Reputation.Useful => Tone.Good,
            Reputation.Unnecessary => Tone.Warn,
            Reputation.Suspicious => Tone.Critical,
            _ => Tone.Neutral,
        },
        bool ok => ok ? Tone.Good : Tone.Critical,
        _ => Tone.Neutral,
    };

    /// <summary>
    /// Pinceau d'une tonalité. Variante : « » (plein, graphiques), « Text » (texte lisible), « Soft » (fond clair),
    /// « Strong » (fond d'une pastille à texte blanc), « Icon » (icône sur fond clair).
    /// </summary>
    public static Brush Of(Tone tone, string variant = "")
    {
        var key = (tone, variant) switch
        {
            (Tone.Neutral, "Soft") => "ChipNeutral",
            (Tone.Neutral, "Text") => "TextSecondary",
            (Tone.Neutral, _) => "TextMuted",
            (Tone.Warn, "Strong" or "Icon") => "Warn" + variant,
            (_, "Text" or "Soft") => tone + variant,
            _ => tone.ToString(),
        };
        return Application.Current?.TryFindResource($"Pcs{key}Brush") as Brush ?? Brushes.Gray;
    }
}

/// <summary>
/// Convertit un état (couleur de santé, gravité, score, réputation, booléen) en pinceau du thème.
/// Paramètre facultatif : « Text » ou « Soft ».
/// </summary>
public sealed class HealthBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        HealthBrushes.Of(HealthBrushes.ToneOf(value), parameter as string ?? string.Empty);

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
