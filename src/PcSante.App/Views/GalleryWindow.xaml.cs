#if DEBUG
using System.Windows;

namespace PcSante.App.Views;

/// <summary>Galerie des composants (Debug seulement, option « --galerie »).</summary>
public partial class GalleryWindow : Window
{
    public GalleryWindow() => InitializeComponent();
}
#endif
