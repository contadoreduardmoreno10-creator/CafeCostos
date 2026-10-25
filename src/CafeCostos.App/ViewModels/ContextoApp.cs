using CafeCostos.Data.Infraestructura;
using CafeCostos.Data.Servicios;

namespace CafeCostos.App.ViewModels;

/// <summary>Interacción con el usuario, implementada por la capa WPF (permite probar los ViewModels).</summary>
public interface IDialogos
{
    void Informar(string mensaje, string titulo = "CaféCostos");
    void Advertir(string mensaje);
    void Error(string mensaje);
    bool Confirmar(string mensaje, string titulo = "Confirmar");
    string? ElegirCarpeta(string titulo, string? carpetaInicial = null);
    string? ElegirArchivoRespaldo(string? carpetaInicial = null);
    string? PedirTexto(string mensaje, string titulo, string valorInicial = "");
}

public interface IImpresion
{
    Task ImprimirVentaAsync(int idVenta);
    Task ImprimirReciboAsync(int idAbono);
}

public interface IRegistro
{
    void Info(string mensaje);
    void Error(Exception ex);
}

/// <summary>Raíz de composición: todos los servicios que usan las pantallas.</summary>
public sealed class ContextoApp
{
    public required ConfiguracionApp Configuracion { get; init; }
    public required LoteService Lotes { get; init; }
    public required TuesteService Tueste { get; init; }
    public required EmpaqueService Empaque { get; init; }
    public required InsumoService Insumos { get; init; }
    public required PresentacionService Presentaciones { get; init; }
    public required ClienteService Clientes { get; init; }
    public required VentaService Ventas { get; init; }
    public required CarteraService Cartera { get; init; }
    public required ReporteService Reportes { get; init; }
    public required ParametrosService Parametros { get; init; }
    public required RespaldoService Respaldos { get; init; }
    public required MantenimientoService Mantenimiento { get; init; }
    public required IDialogos Dialogos { get; init; }
    public required IImpresion Impresion { get; init; }
    public required IRegistro Registro { get; init; }

    public static ContextoApp Crear(ConfiguracionApp cfg, IDialogos dialogos, IImpresion? impresion, IRegistro registro,
                                    Func<ContextoApp, IImpresion>? crearImpresion = null)
    {
        var db = new ConexionFactory(cfg);
        var inicializador = new InicializadorBaseDatos(cfg);
        var parametros = new ParametrosService(db);
        var respaldos = new RespaldoService(db, inicializador);
        ContextoApp? ctx = null;
        ctx = new ContextoApp
        {
            Configuracion = cfg,
            Lotes = new LoteService(db),
            Tueste = new TuesteService(db),
            Empaque = new EmpaqueService(db),
            Insumos = new InsumoService(db),
            Presentaciones = new PresentacionService(db),
            Clientes = new ClienteService(db),
            Ventas = new VentaService(db),
            Cartera = new CarteraService(db),
            Reportes = new ReporteService(db),
            Parametros = parametros,
            Respaldos = respaldos,
            Mantenimiento = new MantenimientoService(db, parametros, respaldos),
            Dialogos = dialogos,
            Impresion = impresion ?? new ImpresionDiferida(() => ctx!, crearImpresion),
            Registro = registro
        };
        return ctx;
    }

    /// <summary>Permite que la impresión (que necesita el contexto) se cree después del contexto.</summary>
    private sealed class ImpresionDiferida : IImpresion
    {
        private readonly Lazy<IImpresion> _real;
        public ImpresionDiferida(Func<ContextoApp> ctx, Func<ContextoApp, IImpresion>? fabrica) =>
            _real = new Lazy<IImpresion>(() => fabrica?.Invoke(ctx()) ?? throw new InvalidOperationException("Impresión no configurada."));
        public Task ImprimirVentaAsync(int idVenta) => _real.Value.ImprimirVentaAsync(idVenta);
        public Task ImprimirReciboAsync(int idAbono) => _real.Value.ImprimirReciboAsync(idAbono);
    }

    /// <summary>Carpeta de respaldos configurada (o la carpeta por defecto en Documentos).</summary>
    public async Task<string> CarpetaRespaldosAsync()
    {
        var c = await Parametros.TextoAsync("Respaldo.Carpeta");
        return string.IsNullOrWhiteSpace(c) ? Configuracion.CarpetaRespaldosPorDefecto : c;
    }
}
