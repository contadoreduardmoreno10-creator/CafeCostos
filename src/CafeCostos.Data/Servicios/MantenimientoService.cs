using System.Globalization;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

public sealed record ResultadoMantenimiento(int Reorganizados, int Reconstruidos, int TemporalesBorrados,
                                            int RespaldosDepurados, bool LogReducido);

/// <summary>
/// Rutina interna de mantenimiento (se ejecuta sola al cerrar, como máximo una vez por semana):
///   • Desfragmenta índices (REORGANIZE/REBUILD conservando la compresión) y actualiza estadísticas.
///   • Borra archivos temporales viejos (extracciones de restauración, copias parciales).
///   • Reduce el archivo de registro (log) solo si creció de forma anormal.
///   • Conserva únicamente las N copias automáticas más recientes.
/// Nunca reduce (SHRINK) el archivo de datos: eso fragmenta los índices y no ahorra espacio real.
/// </summary>
public sealed class MantenimientoService
{
    private readonly ConexionFactory _db;
    private readonly ParametrosService _parametros;
    private readonly RespaldoService _respaldos;

    public MantenimientoService(ConexionFactory db, ParametrosService parametros, RespaldoService respaldos)
    {
        _db = db;
        _parametros = parametros;
        _respaldos = respaldos;
    }

    public async Task<bool> CorrespondeAsync(int dias = 7)
    {
        var ultimo = await _parametros.TextoAsync("Mantenimiento.Ultimo");
        return !DateTime.TryParse(ultimo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
               || (DateTime.Now - f).TotalDays >= dias;
    }

    public async Task<ResultadoMantenimiento> EjecutarAsync(string? carpetaAutomaticos = null, int conservar = 15)
    {
        int reorg, rebuild;
        bool logReducido = false;
        await using (var cn = await _db.AbrirAsync())
        {
            (reorg, rebuild) = await cn.QuerySingleAsync<(int, int)>("EXEC dbo.usp_MantenimientoIndices;", commandTimeout: 0);

            // Log > 256 MB en modelo SIMPLE es anormal (p. ej. tras una restauración grande): se reduce a 64 MB.
            var log = await cn.QuerySingleOrDefaultAsync<(int FileId, int Paginas)>(
                "SELECT TOP (1) file_id, size FROM sys.database_files WHERE type = 1;");
            if (log.FileId > 0 && log.Paginas / 128 > 256)          // 128 páginas de 8 KB = 1 MB
            {
                await cn.ExecuteAsync($"CHECKPOINT; DBCC SHRINKFILE ({log.FileId}, 64) WITH NO_INFOMSGS;", commandTimeout: 0);
                logReducido = true;
            }
        }

        var temporales = LimpiarTemporales(_db.Configuracion.CarpetaTemporal, TimeSpan.FromDays(1));
        var depurados = carpetaAutomaticos is null ? 0 : _respaldos.Depurar(carpetaAutomaticos, conservar);

        await _parametros.GuardarAsync("Mantenimiento.Ultimo", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
        return new ResultadoMantenimiento(reorg, rebuild, temporales, depurados, logReducido);
    }

    private static int LimpiarTemporales(string carpeta, TimeSpan antiguedad)
    {
        if (!Directory.Exists(carpeta)) return 0;
        var borrados = 0;
        foreach (var f in Directory.EnumerateFiles(carpeta))
        {
            try
            {
                if (DateTime.Now - File.GetLastWriteTime(f) > antiguedad)
                {
                    File.Delete(f);
                    borrados++;
                }
            }
            catch { /* en uso */ }
        }
        return borrados;
    }
}
