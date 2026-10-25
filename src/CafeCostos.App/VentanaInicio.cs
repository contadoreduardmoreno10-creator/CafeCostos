using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CafeCostos.App;

/// <summary>Ventana de bienvenida mientras arranca el motor de base de datos (puede tardar unos segundos la primera vez).</summary>
public sealed class VentanaInicio : Window
{
    private readonly TextBlock _mensaje;

    public VentanaInicio()
    {
        Width = 460;
        Height = 300;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Title = "CaféCostos";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Recursos/cafe.ico"));

        _mensaje = new TextBlock
        {
            Text = "Preparando…",
            Foreground = (Brush)FindResource("BarraTexto"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Recursos/cafe.png")),
            Width = 88, Height = 88
        });
        panel.Children.Add(new TextBlock
        {
            Text = "CaféCostos",
            Foreground = Brushes.White,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        });
        panel.Children.Add(_mensaje);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 220, Margin = new Thickness(0, 18, 0, 0) });

        Content = new Border
        {
            Background = (Brush)FindResource("Barra"),
            CornerRadius = new CornerRadius(16),
            Child = panel
        };
    }

    public string Mensaje
    {
        get => _mensaje.Text;
        set => _mensaje.Text = value;
    }
}
