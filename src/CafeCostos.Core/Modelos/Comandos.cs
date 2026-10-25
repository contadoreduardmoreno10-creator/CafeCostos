using CafeCostos.Core.Ventas;

namespace CafeCostos.Core.Modelos;

// Comandos de entrada (lo que el usuario captura en pantalla) para cada operación de negocio.

public sealed class NuevoLote
{
    public DateTime FechaRecepcion { get; set; } = DateTime.Today;
    public string Proveedor { get; set; } = "";
    public string? Origen { get; set; }
    public string? Variedad { get; set; }
    public decimal? Humedad { get; set; }
    public decimal KgRecibidos { get; set; }
    public decimal CostoAdquisicion { get; set; }
    public decimal CostoFlete { get; set; }
    public string? Observaciones { get; set; }
}

public sealed record ConsumoLoteSolicitado(int IdLote, decimal Kilos);

public sealed class NuevaOrdenTueste
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string? PerfilTueste { get; set; }
    public List<ConsumoLoteSolicitado> Lotes { get; set; } = new();
    public decimal KgTostados { get; set; }
    public decimal CostoEnergia { get; set; }
    public decimal CostoManoObra { get; set; }
    public decimal OtrosCostos { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class NuevoEmpaque
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    public int IdOrdenTueste { get; set; }
    public int IdPresentacion { get; set; }
    public int Unidades { get; set; }
    public decimal KgTostadoUsado { get; set; }
    public decimal CostoMolienda { get; set; }
    public decimal CostoManoObra { get; set; }
    public decimal OtrosCostos { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class CompraInsumo
{
    public int IdInsumo { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public decimal Cantidad { get; set; }
    public decimal ValorTotal { get; set; }
    public string? Documento { get; set; }
    public string? Proveedor { get; set; }
}

public sealed record LineaVentaSolicitada(int IdInventarioEmpacado, int Cantidad, decimal PrecioUnitario);

public sealed class NuevaVenta
{
    public DateTime Fecha { get; set; } = DateTime.Today;
    public int IdCliente { get; set; }
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;
    public string? MedioPago { get; set; } = "Efectivo";
    public int? DiasCredito { get; set; }
    public List<LineaVentaSolicitada> Lineas { get; set; } = new();
    public decimal Descuento { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class NuevoAbono
{
    public int IdVenta { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public decimal Valor { get; set; }
    public string? MedioPago { get; set; } = "Efectivo";
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
}
