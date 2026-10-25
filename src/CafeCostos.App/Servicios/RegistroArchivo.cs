using System.IO;
using CafeCostos.App.ViewModels;

namespace CafeCostos.App.Servicios;

/// <summary>Registro de errores técnicos en %LOCALAPPDATA%\CafeCostos\Registros (un archivo por mes).</summary>
public sealed class RegistroArchivo : IRegistro
{
    private readonly string _carpeta;
    private readonly object _candado = new();

    public RegistroArchivo(string carpeta) => _carpeta = carpeta;

    public void Info(string mensaje) => Escribir("INFO ", mensaje);
    public void Error(Exception ex) => Escribir("ERROR", ex.ToString());

    private void Escribir(string nivel, string texto)
    {
        try
        {
            lock (_candado)
            {
                Directory.CreateDirectory(_carpeta);
                var archivo = Path.Combine(_carpeta, $"cafecostos_{DateTime.Now:yyyyMM}.log");
                File.AppendAllText(archivo, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {nivel} {texto}{Environment.NewLine}");
            }
        }
        catch { /* el registro nunca debe tumbar la aplicación */ }
    }
}
