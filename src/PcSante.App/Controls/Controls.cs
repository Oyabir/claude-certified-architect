using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PcSante.App.Infrastructure;

namespace PcSante.App.Controls;

/// <summary>Animations de l'interface : coupées quand Windows les désactive (« Afficher les animations »).</summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// <summary>Courbe des maquettes : cubic-bezier(0.2, 0, 0, 1), approchée par une sortie exponentielle.</summary>
    public static IEasingFunction Ease { get; } = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
}

/// <summary>Propriétés attachées des styles de boutons (icône, chargement, couleurs de survol).</summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.RegisterAttached(
        "IsLoading", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
        "PressedBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element?.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, Geometry? value) => element?.SetValue(IconProperty, value);

    public static bool GetIsLoading(DependencyObject element) => element is not null && (bool)element.GetValue(IsLoadingProperty);

    public static void SetIsLoading(DependencyObject element, bool value) => element?.SetValue(IsLoadingProperty, value);

    public static Brush? GetHoverBackground(DependencyObject element) => (Brush?)element?.GetValue(HoverBackgroundProperty);

    public static void SetHoverBackground(DependencyObject element, Brush? value) => element?.SetValue(HoverBackgroundProperty, value);

    public static Brush? GetPressedBackground(DependencyObject element) => (Brush?)element?.GetValue(PressedBackgroundProperty);

    public static void SetPressedBackground(DependencyObject element, Brush? value) => element?.SetValue(PressedBackgroundProperty, value);
}

/// <summary>
/// Icône au trait (grille 24 × 24, couleur = Foreground). Les icônes non directionnelles ne s'inversent pas en arabe ;
/// <see cref="Directional"/> = vrai pour les flèches et chevrons.
/// </summary>
public class LineIcon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(LineIcon), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LineIcon), new FrameworkPropertyMetadata(20.0));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(LineIcon), new FrameworkPropertyMetadata(1.8));

    public static readonly DependencyProperty DirectionalProperty = DependencyProperty.Register(
        nameof(Directional), typeof(bool), typeof(LineIcon), new FrameworkPropertyMetadata(false));

    static LineIcon()
    {
        FocusableProperty.OverrideMetadata(typeof(LineIcon), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(LineIcon), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool Directional
    {
        get => (bool)GetValue(DirectionalProperty);
        set => SetValue(DirectionalProperty, value);
    }
}

/// <summary>
/// Arc de cercle : rail complet + arc de 12 h dans le sens horaire. En arabe, le miroir de WPF le rend antihoraire.
/// </summary>
public class RingArc : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RingArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingArc), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingArc), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(RingArc), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;
        if (TrackBrush is not null)
        {
            drawingContext.DrawEllipse(null, new Pen(TrackBrush, Thickness), center, radius, radius);
        }

        var fraction = Math.Clamp(Fraction, 0, 1);
        if (Stroke is null || fraction <= 0)
        {
            return;
        }

        var pen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 0.999)
        {
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = (fraction * 360) - 90;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + (radius * Math.Cos(angle * Math.PI / 180)), center.Y + (radius * Math.Sin(angle * Math.PI / 180)));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>Indicateur d'activité : quart d'anneau qui tourne (immobile si Windows coupe les animations).</summary>
public class Spinner : RingArc
{
    private readonly RotateTransform _rotation = new();

    public Spinner()
    {
        Fraction = 0.28;
        Thickness = 2;
        RenderTransform = _rotation;
        RenderTransformOrigin = new Point(0.5, 0.5);
        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private void Update()
    {
        if (IsVisible && Motion.Enabled)
        {
            _rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}

/// <summary>
/// Anneau de score (§ 4) : couleur du seuil, animé de 0 à la valeur (600 ms), arc indéterminé pendant l'analyse.
/// </summary>
public class ScoreRing : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int?), typeof(ScoreRing), new FrameworkPropertyMetadata(null, (d, _) => ((ScoreRing)d).Animate()));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ScoreRing), new FrameworkPropertyMetadata(false, (d, _) => ((ScoreRing)d).Animate()));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(ScoreRing), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ScoreRing), new FrameworkPropertyMetadata(14.0));

    public static readonly DependencyProperty ValueFontSizeProperty = DependencyProperty.Register(
        nameof(ValueFontSize), typeof(double), typeof(ScoreRing), new FrameworkPropertyMetadata(52.0));

    public static readonly DependencyProperty DisplayedFractionProperty = DependencyProperty.Register(
        nameof(DisplayedFraction), typeof(double), typeof(ScoreRing), new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty SpinAngleProperty = DependencyProperty.Register(
        nameof(SpinAngle), typeof(double), typeof(ScoreRing), new FrameworkPropertyMetadata(0.0));

    static ScoreRing()
    {
        FocusableProperty.OverrideMetadata(typeof(ScoreRing), new FrameworkPropertyMetadata(false));
    }

    public ScoreRing()
    {
        Loaded += (_, _) => Animate();
    }

    public int? Value
    {
        get => (int?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty);
        set => SetValue(ValueFontSizeProperty, value);
    }

    public double DisplayedFraction
    {
        get => (double)GetValue(DisplayedFractionProperty);
        set => SetValue(DisplayedFractionProperty, value);
    }

    public double SpinAngle
    {
        get => (double)GetValue(SpinAngleProperty);
        set => SetValue(SpinAngleProperty, value);
    }

    private void Animate()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (IsIndeterminate)
        {
            BeginAnimation(DisplayedFractionProperty, null);
            DisplayedFraction = 0.25;
            BeginAnimation(SpinAngleProperty, Motion.Enabled
                ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
            return;
        }

        BeginAnimation(SpinAngleProperty, null);
        SpinAngle = 0;
        var target = Value is { } v ? Math.Clamp(v, 0, 100) / 100.0 : 0;
        if (Motion.Enabled)
        {
            BeginAnimation(DisplayedFractionProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(600)) { EasingFunction = Motion.Ease });
        }
        else
        {
            BeginAnimation(DisplayedFractionProperty, null);
            DisplayedFraction = target;
        }
    }
}

/// <summary>Puce de statut : icône + mot + couleur (jamais la couleur seule).</summary>
public class StatusChip : Control
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Tone), typeof(StatusChip), new FrameworkPropertyMetadata(Tone.Neutral));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusChip), new FrameworkPropertyMetadata(string.Empty));

    static StatusChip()
    {
        FocusableProperty.OverrideMetadata(typeof(StatusChip), new FrameworkPropertyMetadata(false));
    }

    public Tone Tone
    {
        get => (Tone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>Étiquette (« Important », « Conseillé », « Recommandé »).</summary>
public class TagLabel : StatusChip
{
}

/// <summary>Tuile d'icône : icône colorée sur fond clair de la même tonalité.</summary>
public class IconTile : Control
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Tone), typeof(IconTile), new FrameworkPropertyMetadata(Tone.Brand));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(IconTile), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconTile), new FrameworkPropertyMetadata(44.0));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(IconTile), new FrameworkPropertyMetadata(22.0));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(IconTile), new FrameworkPropertyMetadata(new CornerRadius(12)));

    static IconTile()
    {
        FocusableProperty.OverrideMetadata(typeof(IconTile), new FrameworkPropertyMetadata(false));
    }

    public Tone Tone
    {
        get => (Tone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
}

/// <summary>Tuile de sous-score (Sécurité, Performance…) : icône, libellé, valeur et barre de la couleur du seuil.</summary>
public class SubScoreTile : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SubScoreTile), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(SubScoreTile), new FrameworkPropertyMetadata(0));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(SubScoreTile), new FrameworkPropertyMetadata(null));

    static SubScoreTile()
    {
        FocusableProperty.OverrideMetadata(typeof(SubScoreTile), new FrameworkPropertyMetadata(false));
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}

/// <summary>
/// Ligne de réglage ou de problème : tuile d'icône, titre (+ étiquette), description, et à droite le contenu
/// (statut, interrupteur ou bouton d'action).
/// </summary>
public class ListRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ListRow), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ListRow), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ListRow), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Tone), typeof(ListRow), new FrameworkPropertyMetadata(Tone.Brand));

    public static readonly DependencyProperty TagTextProperty = DependencyProperty.Register(
        nameof(TagText), typeof(string), typeof(ListRow), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty TagToneProperty = DependencyProperty.Register(
        nameof(TagTone), typeof(Tone), typeof(ListRow), new FrameworkPropertyMetadata(Tone.Warn));

    public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
        nameof(ShowDivider), typeof(bool), typeof(ListRow), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty IconTileSizeProperty = DependencyProperty.Register(
        nameof(IconTileSize), typeof(double), typeof(ListRow), new FrameworkPropertyMetadata(44.0));

    static ListRow()
    {
        FocusableProperty.OverrideMetadata(typeof(ListRow), new FrameworkPropertyMetadata(false));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Tone Tone
    {
        get => (Tone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string TagText
    {
        get => (string)GetValue(TagTextProperty);
        set => SetValue(TagTextProperty, value);
    }

    public Tone TagTone
    {
        get => (Tone)GetValue(TagToneProperty);
        set => SetValue(TagToneProperty, value);
    }

    public bool ShowDivider
    {
        get => (bool)GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }

    public double IconTileSize
    {
        get => (double)GetValue(IconTileSizeProperty);
        set => SetValue(IconTileSizeProperty, value);
    }
}

/// <summary>État vide : icône dans une tuile + phrase rassurante (« Aucune menace détectée »).</summary>
public class EmptyState : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmptyState), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(EmptyState), new FrameworkPropertyMetadata(null));

    static EmptyState()
    {
        FocusableProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
