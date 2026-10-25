using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using CafeCostos.Core;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Infraestructura;

/// <summary>
/// Prepara la base de datos sin intervención del usuario:
///   1. Verifica que LocalDB esté instalado y encendido (lo inicia si hace falta).
///   2. Crea la base de datos en %LOCALAPPDATA%\CafeCostos\Datos, o la re-adjunta si los
///      archivos ya existen (por ejemplo, tras reinstalar la aplicación).
///   3. Aplica en orden los scripts embebidos (Scripts\NNN_*.sql) que aún no se hayan aplicado.
/// Es idempotente: se ejecuta en cada arranque y en el instalador (--inicializar).
/// </summary>
public sealed partial class InicializadorBaseDatos
{
    private readonly ConfiguracionApp _config;

    public InicializadorBaseDatos(ConfiguracionApp config) => _config = config;

    public async Task AsegurarAsync(IProgress<string>? progreso = null)
    {
        _config.CrearCarpetas();

        progreso?.Report("Iniciando el motor de base de datos…");
        await AsegurarMotorAsync();

        progreso?.Report("Verificando la base de datos…");
        await AsegurarBaseDatosAsync();

        progreso?.Report("Actualizando la estructura de datos…");
        await AplicarScriptsAsync(progreso);

        progreso?.Report("Listo.");
    }

    // ----------------------------------------------------------------------------------
    private async Task AsegurarMotorAsync()
    {
        if (await PuedeConectarMasterAsync()) return;

        // LocalDB se detiene solo tras unos minutos sin uso; normalmente arranca al conectar,
        // pero si la instancia no existe (primer uso del perfil) hay que crearla/iniciarla.
        EjecutarSqlLocalDb("create MSSQLLocalDB -s");
        EjecutarSqlLocalDb("start MSSQLLocalDB");

        if (!await PuedeConectarMasterAsync())
            throw new ReglaNegocioException(
                "No se pudo iniciar el motor de base de datos (SQL Server LocalDB). " +
                "Reinstale CaféCostos o reinicie el computador e intente de nuevo.");
    }

    private async Task<bool> PuedeConectarMasterAsync()
    {
        try
        {
            await using var cn = new SqlConnection(_config.CadenaMaster);
            await cn.OpenAsync();
            return true;
        }
        catch (SqlException) { return false; }
    }

    private static void EjecutarSqlLocalDb(string argumentos)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("SqlLocalDB.exe", argumentos)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit(60_000);
        }
        catch
        {
            // Si SqlLocalDB.exe no está en el PATH, la verificación posterior informará el error.
        }
    }

    // ----------------------------------------------------------------------------------
    private async Task AsegurarBaseDatosAsync()
    {
        await using var cn = new SqlConnection(_config.CadenaMaster);
        await cn.OpenAsync();

        var existe = await cn.ExecuteScalarAsync<int?>(
            "SELECT database_id FROM sys.databases WHERE name = @n", new { n = ConfiguracionApp.NombreBaseDatos });
        if (existe is not null) return;

        string sql;
        if (File.Exists(_config.ArchivoDatos))
        {
            // Los archivos existen pero la instancia no los conoce (reinstalación / perfil recreado).
            sql = File.Exists(_config.ArchivoLog)
                ? $"CREATE DATABASE [{ConfiguracionApp.NombreBaseDatos}] ON (FILENAME = N'{Esc(_config.ArchivoDatos)}'), (FILENAME = N'{Esc(_config.ArchivoLog)}') FOR ATTACH;"
                : $"CREATE DATABASE [{ConfiguracionApp.NombreBaseDatos}] ON (FILENAME = N'{Esc(_config.ArchivoDatos)}') FOR ATTACH_REBUILD_LOG;";
        }
        else
        {
            sql = $@"
CREATE DATABASE [{ConfiguracionApp.NombreBaseDatos}]
ON PRIMARY (NAME = N'CafeCostos', FILENAME = N'{Esc(_config.ArchivoDatos)}', SIZE = 16MB, FILEGROWTH = 16MB)
LOG ON     (NAME = N'CafeCostos_log', FILENAME = N'{Esc(_config.ArchivoLog)}', SIZE = 8MB, FILEGROWTH = 16MB)
COLLATE Modern_Spanish_CI_AS;";
        }
        await cn.ExecuteAsync(sql, commandTimeout: 300);

        // Opciones recomendadas para un equipo monousuario:
        //  SIMPLE: el log no crece indefinidamente (los respaldos son completos).
        //  AUTO_CLOSE OFF / AUTO_SHRINK OFF: evitan lentitud y fragmentación.
        //  CHECKSUM: detecta corrupción de páginas en disco.
        await cn.ExecuteAsync($@"
ALTER DATABASE [{ConfiguracionApp.NombreBaseDatos}] SET RECOVERY SIMPLE;
ALTER DATABASE [{ConfiguracionApp.NombreBaseDatos}] SET AUTO_CLOSE OFF;
ALTER DATABASE [{ConfiguracionApp.NombreBaseDatos}] SET AUTO_SHRINK OFF;
ALTER DATABASE [{ConfiguracionApp.NombreBaseDatos}] SET PAGE_VERIFY CHECKSUM;
ALTER DATABASE [{ConfiguracionApp.NombreBaseDatos}] SET AUTO_UPDATE_STATISTICS_ASYNC ON;");
    }

    private static string Esc(string ruta) => ruta.Replace("'", "''");

    // ----------------------------------------------------------------------------------
    private async Task AplicarScriptsAsync(IProgress<string>? progreso)
    {
        await using var cn = new SqlConnection(_config.CadenaBaseDatos);
        await cn.OpenAsync();

        await cn.ExecuteAsync(@"
IF OBJECT_ID(N'dbo.VersionEsquema', N'U') IS NULL
    CREATE TABLE dbo.VersionEsquema
    (
        Version          int           NOT NULL CONSTRAINT PK_VersionEsquema PRIMARY KEY,
        Descripcion      nvarchar(200) NOT NULL,
        FechaAplicacion  datetime2(0)  NOT NULL CONSTRAINT DF_VersionEsquema_Fecha DEFAULT (SYSDATETIME())
    );");

        var actual = await cn.ExecuteScalarAsync<int?>("SELECT MAX(Version) FROM dbo.VersionEsquema") ?? 0;

        foreach (var (version, nombre, contenido) in ScriptsEmbebidos().Where(s => s.Version > actual))
        {
            progreso?.Report($"Aplicando actualización {version:D3}…");
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
            try
            {
                foreach (var lote in DividirEnLotes(contenido))
                    await cn.ExecuteAsync(lote, transaction: tx, commandTimeout: 600);

                await cn.ExecuteAsync(
                    "INSERT INTO dbo.VersionEsquema (Version, Descripcion) VALUES (@version, @nombre)",
                    new { version, nombre }, tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    /// <summary>Scripts embebidos con nombre NNN_Descripcion.sql, en orden numérico.</summary>
    internal static IEnumerable<(int Version, string Nombre, string Contenido)> ScriptsEmbebidos()
    {
        var asm = Assembly.GetExecutingAssembly();
        var lista = new List<(int Version, string Nombre, string Contenido)>();
        foreach (var recurso in asm.GetManifestResourceNames())
        {
            if (!recurso.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) continue;
            var archivo = recurso.Split('.')[^2];                  // Scripts.001_Esquema.sql → 001_Esquema
            var m = PatronVersion().Match(archivo);
            if (!m.Success) continue;
            using var s = asm.GetManifestResourceStream(recurso)!;
            using var r = new StreamReader(s);
            lista.Add((int.Parse(m.Groups[1].Value), archivo, r.ReadToEnd()));
        }
        return lista.OrderBy(x => x.Version);
    }

    /// <summary>Divide un script por separadores «GO» (en su propia línea).</summary>
    internal static IEnumerable<string> DividirEnLotes(string script) =>
        SeparadorGo().Split(script)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0);

    [GeneratedRegex(@"^(\d{3})_")]
    private static partial Regex PatronVersion();

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();
}
