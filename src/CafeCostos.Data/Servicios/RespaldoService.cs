using System.IO.Compression;
using CafeCostos.Core;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Servicios;

/// <summary>
/// Copias de seguridad y restauración.
///
/// IMPORTANTE (limitación de licencia de Microsoft): «BACKUP … WITH COMPRESSION» NO está disponible
/// en SQL Server Express ni en LocalDB. Por eso el servicio:
///   • Detecta la edición del motor.
///   • En Standard/Enterprise/Developer usa la compresión nativa: BACKUP DATABASE … WITH COMPRESSION.
///   • En Express/LocalDB genera el .bak normal (con CHECKSUM) y lo comprime en un .zip
///     (Deflate nivel máximo), que logra una reducción equivalente (≈ 80-90 % en datos contables).
/// El .bak queda verificado con RESTORE VERIFYONLY antes de entregarse.
/// </summary>
public sealed class RespaldoService
{
    public const string ExtensionZip = ".zip";
    public const string ExtensionBak = ".bak";
    private const string Prefijo = "CafeCostos_Respaldo_";

    private readonly ConexionFactory _db;
    private readonly ConfiguracionApp _cfg;
    private readonly InicializadorBaseDatos _inicializador;

    public RespaldoService(ConexionFactory db, InicializadorBaseDatos inicializador)
    {
        _db = db;
        _cfg = db.Configuracion;
        _inicializador = inicializador;
    }

    // ==================================================================================
    // CREAR COPIA
    // ==================================================================================

    /// <summary>Crea una copia de seguridad comprimida con fecha y hora en la carpeta indicada (disco, USB…).</summary>
    public async Task<InfoRespaldo> CrearAsync(string carpetaDestino, IProgress<string>? progreso = null, string? sufijo = null)
    {
        Validar.Texto(carpetaDestino, "Carpeta de destino");
        Directory.CreateDirectory(carpetaDestino);
        Directory.CreateDirectory(_cfg.CarpetaTemporal);

        var marca = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var nombreBase = Prefijo + marca + (string.IsNullOrEmpty(sufijo) ? "" : "_" + sufijo);
        var bakTemporal = Path.Combine(_cfg.CarpetaTemporal, nombreBase + ExtensionBak);

        try
        {
            progreso?.Report("Generando la copia de la base de datos…");
            var compresionNativa = await SoportaCompresionNativaAsync();
            await EjecutarBackupAsync(bakTemporal, compresionNativa);

            progreso?.Report("Verificando la integridad de la copia…");
            await VerificarBakAsync(bakTemporal);

            string archivoFinal;
            if (compresionNativa)
            {
                archivoFinal = Path.Combine(carpetaDestino, nombreBase + ExtensionBak);
                progreso?.Report("Copiando al destino…");
                await CopiarConVerificacionAsync(bakTemporal, archivoFinal);
            }
            else
            {
                progreso?.Report("Comprimiendo la copia…");
                var zipTemporal = Path.Combine(_cfg.CarpetaTemporal, nombreBase + ExtensionZip);
                await Task.Run(() =>
                {
                    if (File.Exists(zipTemporal)) File.Delete(zipTemporal);
                    using var zip = ZipFile.Open(zipTemporal, ZipArchiveMode.Create);
                    zip.CreateEntryFromFile(bakTemporal, nombreBase + ExtensionBak, CompressionLevel.SmallestSize);
                });
                archivoFinal = Path.Combine(carpetaDestino, nombreBase + ExtensionZip);
                progreso?.Report("Copiando al destino…");
                await CopiarConVerificacionAsync(zipTemporal, archivoFinal);
                File.Delete(zipTemporal);
            }

            progreso?.Report("Copia de seguridad terminada.");
            var fi = new FileInfo(archivoFinal);
            return new InfoRespaldo { Ruta = fi.FullName, Fecha = fi.LastWriteTime, TamanoBytes = fi.Length };
        }
        catch (UnauthorizedAccessException)
        {
            throw new ReglaNegocioException("No hay permiso para escribir en la carpeta elegida. Elija otra carpeta o USB.");
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070070)) // ERROR_DISK_FULL
        {
            throw new ReglaNegocioException("No hay espacio suficiente en el destino. Libere espacio o use otra memoria USB.");
        }
        finally
        {
            TryDelete(bakTemporal);
        }
    }

    /// <summary>EngineEdition 4 = Express (incluye LocalDB). En ella no existe compresión de backups.</summary>
    public async Task<bool> SoportaCompresionNativaAsync()
    {
        await using var cn = await _db.AbrirMasterAsync();
        var edicion = await cn.ExecuteScalarAsync<int>("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)");
        return edicion is 2 or 3; // 2 = Standard, 3 = Enterprise/Developer
    }

    private async Task EjecutarBackupAsync(string rutaBak, bool comprimir)
    {
        await using var cn = await _db.AbrirMasterAsync();
        var opciones = "INIT, FORMAT, CHECKSUM, STATS = 10" + (comprimir ? ", COMPRESSION" : "");
        await cn.ExecuteAsync(
            $"BACKUP DATABASE [{ConfiguracionApp.NombreBaseDatos}] TO DISK = @ruta WITH {opciones}, NAME = @nombre;",
            new { ruta = rutaBak, nombre = "CafeCostos - Copia completa " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") },
            commandTimeout: 0);
    }

    private async Task VerificarBakAsync(string rutaBak)
    {
        await using var cn = await _db.AbrirMasterAsync();
        await cn.ExecuteAsync("RESTORE VERIFYONLY FROM DISK = @ruta WITH CHECKSUM;", new { ruta = rutaBak }, commandTimeout: 0);
    }

    private static async Task CopiarConVerificacionAsync(string origen, string destino)
    {
        var temporalDestino = destino + ".parcial";
        await using (var src = new FileStream(origen, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
        await using (var dst = new FileStream(temporalDestino, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            await src.CopyToAsync(dst);
            await dst.FlushAsync();
        }
        if (new FileInfo(temporalDestino).Length != new FileInfo(origen).Length)
            throw new ReglaNegocioException("La copia al destino quedó incompleta. Verifique la memoria USB.");
        File.Move(temporalDestino, destino, overwrite: true);
    }

    // ==================================================================================
    // RESTAURAR COPIA
    // ==================================================================================

    public sealed record InfoArchivoRespaldo(string BaseDatos, DateTime FechaCopia, string Servidor, decimal TamanoMB);

    /// <summary>Paso 1 del asistente: lee y valida el archivo sin tocar la base actual.</summary>
    public async Task<InfoArchivoRespaldo> InspeccionarAsync(string archivo)
    {
        var bak = PrepararBak(archivo, out var temporal);
        try
        {
            await using var cn = await _db.AbrirMasterAsync();
            var cab = (await cn.QueryAsync("RESTORE HEADERONLY FROM DISK = @ruta;", new { ruta = bak }, commandTimeout: 0))
                      .Cast<IDictionary<string, object>>().FirstOrDefault()
                      ?? throw new ReglaNegocioException("El archivo no contiene una copia de seguridad válida.");
            var nombre = cab["DatabaseName"]?.ToString() ?? "";
            Validar.Que(string.Equals(nombre, ConfiguracionApp.NombreBaseDatos, StringComparison.OrdinalIgnoreCase),
                $"Este archivo es una copia de otra base de datos («{nombre}»), no de CaféCostos.");
            await cn.ExecuteAsync("RESTORE VERIFYONLY FROM DISK = @ruta WITH CHECKSUM;", new { ruta = bak }, commandTimeout: 0);

            return new InfoArchivoRespaldo(
                nombre,
                Convert.ToDateTime(cab["BackupFinishDate"]),
                cab["ServerName"]?.ToString() ?? "",
                Math.Round(Convert.ToDecimal(cab["BackupSize"]) / 1024m / 1024m, 1));
        }
        catch (SqlException ex)
        {
            throw new ReglaNegocioException("El archivo está dañado o no es una copia de CaféCostos. Detalle: " + ex.Message);
        }
        finally
        {
            if (temporal) TryDelete(bak);
        }
    }

    /// <summary>
    /// Paso 3 del asistente: restaura. Antes crea automáticamente una copia de la información actual
    /// en «carpetaSeguridad» por si el usuario se equivocó de archivo.
    /// </summary>
    public async Task<InfoRespaldo> RestaurarAsync(string archivo, string carpetaSeguridad, IProgress<string>? progreso = null)
    {
        progreso?.Report("Guardando una copia de seguridad de la información actual…");
        var copiaPrevia = await CrearAsync(carpetaSeguridad, null, "antes_de_restaurar");

        var bak = PrepararBak(archivo, out var temporal);
        try
        {
            progreso?.Report("Restaurando la información…");
            SqlConnection.ClearAllPools();

            await using (var cn = await _db.AbrirMasterAsync())
            {
                var archivos = (await cn.QueryAsync("RESTORE FILELISTONLY FROM DISK = @ruta;", new { ruta = bak }, commandTimeout: 0))
                               .Cast<IDictionary<string, object>>().ToList();
                var logicoDatos = archivos.First(f => f["Type"]?.ToString() == "D")["LogicalName"].ToString()!;
                var logicoLog = archivos.First(f => f["Type"]?.ToString() == "L")["LogicalName"].ToString()!;

                var bd = ConfiguracionApp.NombreBaseDatos;
                await cn.ExecuteAsync($@"
IF DB_ID(N'{bd}') IS NOT NULL
    ALTER DATABASE [{bd}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

RESTORE DATABASE [{bd}] FROM DISK = @ruta
WITH REPLACE, RECOVERY, CHECKSUM,
     MOVE @logicoDatos TO @mdf,
     MOVE @logicoLog   TO @ldf;

ALTER DATABASE [{bd}] SET MULTI_USER;",
                    new { ruta = bak, logicoDatos, logicoLog, mdf = _cfg.ArchivoDatos, ldf = _cfg.ArchivoLog },
                    commandTimeout: 0);
            }

            // Las conexiones del grupo apuntaban a la base anterior: se descartan.
            SqlConnection.ClearAllPools();

            // Una copia antigua puede tener una versión anterior del esquema: se actualiza.
            progreso?.Report("Actualizando la estructura de datos…");
            await _inicializador.AsegurarAsync();
            progreso?.Report("Restauración terminada.");
            return copiaPrevia;
        }
        catch (SqlException ex)
        {
            await IntentarMultiUsuarioAsync();
            throw new ReglaNegocioException(
                "No se pudo restaurar la copia. Su información actual no se modificó o puede recuperarse desde: "
                + copiaPrevia.Ruta + Environment.NewLine + "Detalle: " + ex.Message);
        }
        finally
        {
            if (temporal) TryDelete(bak);
        }
    }

    private async Task IntentarMultiUsuarioAsync()
    {
        try
        {
            await using var cn = await _db.AbrirMasterAsync();
            await cn.ExecuteAsync($"IF DB_ID(N'{ConfiguracionApp.NombreBaseDatos}') IS NOT NULL ALTER DATABASE [{ConfiguracionApp.NombreBaseDatos}] SET MULTI_USER;");
        }
        catch { /* sin acción */ }
    }

    /// <summary>Si el archivo es .zip extrae el .bak a la carpeta temporal.</summary>
    private string PrepararBak(string archivo, out bool esTemporal)
    {
        Validar.Que(File.Exists(archivo), "El archivo seleccionado no existe.");
        esTemporal = false;
        if (!archivo.EndsWith(ExtensionZip, StringComparison.OrdinalIgnoreCase)) return archivo;

        Directory.CreateDirectory(_cfg.CarpetaTemporal);
        using var zip = ZipFile.OpenRead(archivo);
        var entrada = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(ExtensionBak, StringComparison.OrdinalIgnoreCase))
                      ?? throw new ReglaNegocioException("El archivo .zip no contiene una copia de seguridad (.bak).");
        var destino = Path.Combine(_cfg.CarpetaTemporal, "restaurar_" + Guid.NewGuid().ToString("N") + ExtensionBak);
        entrada.ExtractToFile(destino, overwrite: true);
        esTemporal = true;
        return destino;
    }

    // ==================================================================================
    // LISTADO Y LIMPIEZA
    // ==================================================================================

    public IReadOnlyList<InfoRespaldo> Listar(string carpeta)
    {
        if (!Directory.Exists(carpeta)) return Array.Empty<InfoRespaldo>();
        return Directory.EnumerateFiles(carpeta, Prefijo + "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(ExtensionZip, StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(ExtensionBak, StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new InfoRespaldo { Ruta = f.FullName, Fecha = f.LastWriteTime, TamanoBytes = f.Length })
            .ToList();
    }

    /// <summary>Conserva solo las N copias más recientes de una carpeta (usado para las automáticas).</summary>
    public int Depurar(string carpeta, int conservar)
    {
        if (conservar < 1 || !Directory.Exists(carpeta)) return 0;
        var sobrantes = Directory.EnumerateFiles(carpeta, Prefijo + "*", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).Skip(conservar).ToList();
        foreach (var f in sobrantes) TryDelete(f.FullName);
        return sobrantes.Count;
    }

    private static void TryDelete(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); } catch { /* archivo en uso: se limpia en el mantenimiento */ }
    }
}
