using System.ComponentModel;
using System.IO;
using System.Windows;
using CafeCostos.App.ViewModels;

namespace CafeCostos.App;

public partial class MainWindow : Window
{
    private readonly ContextoApp _ctx;
    private bool _cierreListo;

    public MainWindow(ContextoApp ctx)
    {
        _ctx = ctx;
        InitializeComponent();
    }

    /// <summary>
    /// Al cerrar: si está activado y aún no se hizo hoy, crea la copia automática del día
    /// en «…\Automáticas», depura las copias viejas y ejecuta el mantenimiento semanal.
    /// </summary>
    private async void AlCerrar(object? sender, CancelEventArgs e)
    {
        if (_cierreListo) return;
        e.Cancel = true;
        CapaCierre.Visibility = Visibility.Visible;
        IsEnabled = false;
        try
        {
            if (await _ctx.Parametros.BoolAsync("Respaldo.AutomaticoAlCerrar", true))
            {
                var ultimo = await _ctx.Parametros.TextoAsync("Respaldo.UltimoAutomatico");
                var hoy = DateTime.Today.ToString("yyyy-MM-dd");
                if (ultimo != hoy)
                {
                    var carpeta = Path.Combine(await _ctx.CarpetaRespaldosAsync(), "Automáticas");
                    await _ctx.Respaldos.CrearAsync(carpeta, new Progress<string>(m => TextoCierre.Text = m), "auto");
                    await _ctx.Parametros.GuardarAsync("Respaldo.UltimoAutomatico", hoy);
                    var conservar = (int)await _ctx.Parametros.DecimalAsync("Respaldo.Conservar", 15);
                    _ctx.Respaldos.Depurar(carpeta, conservar);
                }
            }

            if (await _ctx.Mantenimiento.CorrespondeAsync())
            {
                TextoCierre.Text = "Optimizando la base de datos…";
                await _ctx.Mantenimiento.EjecutarAsync();
            }
        }
        catch (Exception ex)
        {
            // Nunca impedir el cierre por un fallo del respaldo; se avisa y se registra.
            _ctx.Registro.Error(ex);
            MessageBox.Show(this,
                "No se pudo crear la copia automática al cerrar:\n" + ex.Message +
                "\n\nCree una copia manual la próxima vez que abra el programa.",
                "Copia de seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _cierreListo = true;
        Close();
    }
}
