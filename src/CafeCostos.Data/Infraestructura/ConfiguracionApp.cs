using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Infraestructura;

/// <summary>
/// Rutas y cadenas de conexión. Todo vive en %LOCALAPPDATA%\CafeCostos porque LocalDB
/// se ejecuta con la cuenta del usuario: así tiene permisos de escritura garantizados
/// (sin UAC) y puede respaldar a cualquier carpeta o USB que el usuario pueda abrir.
/// </summary>
public sealed class ConfiguracionApp
{
    public const string NombreBaseDatos = "CafeCostos";

    public string Instancia { get; init; } = @"(localdb)\MSSQLLocalDB";
    public string CarpetaRaiz { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeCostos");

    public string CarpetaDatos => Path.Combine(CarpetaRaiz, "Datos");
    public string CarpetaTemporal => Path.Combine(CarpetaRaiz, "Temp");
    public string CarpetaRegistros => Path.Combine(CarpetaRaiz, "Registros");

    /// <summary>Carpeta por defecto de respaldos (Documentos\CaféCostos Respaldos).</summary>
    public string CarpetaRespaldosPorDefecto =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CaféCostos Respaldos");

    public string ArchivoDatos => Path.Combine(CarpetaDatos, NombreBaseDatos + ".mdf");
    public string ArchivoLog => Path.Combine(CarpetaDatos, NombreBaseDatos + "_log.ldf");

    public string CadenaMaster => Construir("master");
    public string CadenaBaseDatos => Construir(NombreBaseDatos);

    private string Construir(string catalogo) => new SqlConnectionStringBuilder
    {
        DataSource = Instancia,
        InitialCatalog = catalogo,
        IntegratedSecurity = true,
        Encrypt = false,
        TrustServerCertificate = true,
        ConnectTimeout = 60,          // el primer arranque de LocalDB puede tardar
        ApplicationName = "CafeCostos",
        Pooling = true
    }.ConnectionString;

    public void CrearCarpetas()
    {
        Directory.CreateDirectory(CarpetaDatos);
        Directory.CreateDirectory(CarpetaTemporal);
        Directory.CreateDirectory(CarpetaRegistros);
    }
}
