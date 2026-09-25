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
    Brand,
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
            (Tone.Brand, "Soft") => "BrandSoft",
            (Tone.Brand, "Text") => "BrandText",
            (Tone.Brand, _) => "Brand",
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

/// <summary>Convertit un état en tonalité (<see cref="Tone"/>).</summary>
public sealed class ToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => HealthBrushes.ToneOf(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Icône d'un état : coche (tout va bien), point d'exclamation (à surveiller), croix (à corriger).</summary>
public sealed class ToneIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => HealthBrushes.ToneOf(value) switch
    {
        Tone.Good or Tone.Brand => Application.Current?.TryFindResource("PcsIcon.check"),
        Tone.Warn => Application.Current?.TryFindResource("PcsIcon.alert-circle"),
        Tone.Critical => Application.Current?.TryFindResource("PcsIcon.x"),
        _ => null,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Choix exclusif (filtres, onglets segmentés) : vrai si la valeur vaut le paramètre ; cocher renvoie le paramètre.</summary>
public sealed class EqualsParameterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

/// <summary>Négation d'un booléen.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visible si toutes les valeurs liées sont vraies.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is not null && values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>Clé de ressource (« PcsIcon.home ») → ressource (icône).</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && key.Length > 0 ? Application.Current?.TryFindResource(key) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Valeur de 0 à 100 → fraction (barres, anneaux).</summary>
public sealed class PercentToFractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        int i => Math.Clamp(i, 0, 100) / 100.0,
        double d => Math.Clamp(d, 0, 100) / 100.0,
        _ => 0.0,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Texte en majuscules selon la langue (titres de groupe « ESSENTIEL »).</summary>
public sealed class UpperConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s.ToUpper(Localization.Loc.Culture) : value;

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
