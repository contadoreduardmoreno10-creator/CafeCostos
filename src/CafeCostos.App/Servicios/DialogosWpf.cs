using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CafeCostos.App.ViewModels;
using Microsoft.Win32;

namespace CafeCostos.App.Servicios;

/// <summary>Cuadros de diálogo nativos de Windows con textos en español.</summary>
public sealed class DialogosWpf : IDialogos
{
    private static Window? Duena => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? Application.Current?.MainWindow;

    public void Informar(string mensaje, string titulo = "CaféCostos") =>
        Mostrar(mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Advertir(string mensaje) =>
        Mostrar(mensaje, "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string mensaje) =>
        Mostrar(mensaje, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirmar(string mensaje, string titulo = "Confirmar") =>
        Mostrar(mensaje, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static MessageBoxResult Mostrar(string m, string t, MessageBoxButton b, MessageBoxImage i) =>
        Duena is { } w ? MessageBox.Show(w, m, t, b, i) : MessageBox.Show(m, t, b, i);

    public string? ElegirCarpeta(string titulo, string? carpetaInicial = null)
    {
        // OpenFolderDialog es nuevo en .NET 8 (no requiere Windows Forms).
        var dlg = new OpenFolderDialog { Title = titulo, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(carpetaInicial) && System.IO.Directory.Exists(carpetaInicial))
            dlg.InitialDirectory = carpetaInicial;
        return Abrir(dlg) ? dlg.FolderName : null;
    }

    public string? ElegirArchivoRespaldo(string? carpetaInicial = null)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleccione la copia de seguridad a restaurar",
            Filter = "Copias de CaféCostos (*.zip;*.bak)|*.zip;*.bak|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(carpetaInicial) && System.IO.Directory.Exists(carpetaInicial))
            dlg.InitialDirectory = carpetaInicial;
        return Abrir(dlg) ? dlg.FileName : null;
    }

    private static bool Abrir(CommonDialog dlg) => (Duena is { } w ? dlg.ShowDialog(w) : dlg.ShowDialog()) == true;

    public string? PedirTexto(string mensaje, string titulo, string valorInicial = "")
    {
        var caja = new TextBox { Text = valorInicial, Margin = new Thickness(0, 12, 0, 0) };
        var aceptar = new Button { Content = "Aceptar", IsDefault = true, MinWidth = 120, Style = (Style)Application.Current.FindResource("BotonPrimario") };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 120 };
        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        botones.Children.Add(cancelar);
        botones.Children.Add(aceptar);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = mensaje, TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        panel.Children.Add(caja);
        panel.Children.Add(botones);

        var ventana = new Window
        {
            Title = titulo,
            Content = panel,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = Duena,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("Fondo")
        };
        aceptar.Click += (_, _) => ventana.DialogResult = true;
        ventana.Loaded += (_, _) => { caja.Focus(); caja.SelectAll(); Keyboard.Focus(caja); };
        return ventana.ShowDialog() == true ? caja.Text.Trim() : null;
    }
}
