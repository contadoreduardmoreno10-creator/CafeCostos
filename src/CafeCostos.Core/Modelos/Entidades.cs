namespace CafeCostos.Core.Modelos;

// Clases planas mapeadas por Dapper (nombres de propiedades = nombres de columnas).

public static class Estados
{
    public const string Activo = "ACTIVO";
    public const string Agotado = "AGOTADO";
    public const string Anulado = "ANULADO";
    public const string Pendiente = "PENDIENTE";
    public const string Pagada = "PAGADA";
    public const string Anulada = "ANULADA";
}

public sealed class LoteCafeSeco
{
    public int IdLote { get; set; }
    public string Codigo { get; set; } = "";
    public DateTime FechaRecepcion { get; set; }
    public string Proveedor { get; set; } = "";
    public string? Origen { get; set; }
    public string? Variedad { get; set; }
    public decimal? Humedad { get; set; }
    public decimal KgRecibidos { get; set; }
    public decimal CostoAdquisicion { get; set; }
    public decimal CostoFlete { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal CostoKg { get; set; }
    public decimal KgDisponibles { get; set; }
    public decimal ValorDisponible { get; set; }
    public string Estado { get; set; } = Estados.Activo;
    public string? Observaciones { get; set; }
    public string Descripcion => $"{Codigo} · {Proveedor} · {KgDisponibles:N2} kg disp. · {CostoKg:C0}/kg";
}

public sealed class OrdenTueste
{
    public int IdOrdenTueste { get; set; }
    public string Numero { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string? PerfilTueste { get; set; }
    public decimal KgSecos { get; set; }
    public decimal KgTostados { get; set; }
    public decimal PctMerma { get; set; }
    public decimal CostoCafeSeco { get; set; }
    public decimal CostoEnergia { get; set; }
    public decimal CostoManoObra { get; set; }
    public decimal OtrosCostos { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal CostoKgTostado { get; set; }
    public decimal KgDisponibles { get; set; }
    public decimal ValorDisponible { get; set; }
    public string Estado { get; set; } = Estados.Activo;
    public string? Observaciones { get; set; }
    public string? LotesOrigen { get; set; }
    public string Descripcion => $"{Numero} · {Fecha:dd/MM/yyyy} · {PerfilTueste} · {KgDisponibles:N2} kg disp. · {CostoKgTostado:C0}/kg";
}

public sealed class OrdenTuesteLote
{
    public int IdOrdenTueste { get; set; }
    public int IdLote { get; set; }
    public string CodigoLote { get; set; } = "";
    public string Proveedor { get; set; } = "";
    public decimal KgUsados { get; set; }
    public decimal CostoKg { get; set; }
    public decimal CostoTotal { get; set; }
}

public sealed class Presentacion
{
    public int IdPresentacion { get; set; }
    public string Nombre { get; set; } = "";
    public int GramosNetos { get; set; }
    public string TipoMolienda { get; set; } = "GRANO";
    public string? GradoMolienda { get; set; }
    public decimal PrecioVenta { get; set; }
    public bool Activo { get; set; } = true;
    public string Descripcion => $"{Nombre} ({GramosNetos} g · {(TipoMolienda == "GRANO" ? "Grano" : "Molido")})";
}

public sealed class RecetaInsumo
{
    public int IdPresentacion { get; set; }
    public int IdInsumo { get; set; }
    public string NombreInsumo { get; set; } = "";
    public string Unidad { get; set; } = "";
    public decimal CantidadPorUnidad { get; set; }
    public decimal CostoPromedio { get; set; }
    public decimal Stock { get; set; }
}

public sealed class Insumo
{
    public int IdInsumo { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Unidad { get; set; } = "und";
    public decimal Stock { get; set; }
    public decimal ValorStock { get; set; }
    public decimal CostoPromedio { get; set; }
    public decimal UltimoCosto { get; set; }
    public decimal StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
    public bool BajoMinimo => Stock <= StockMinimo;
    public string Descripcion => $"{Nombre} ({Stock:N0} {Unidad})";
}

public sealed class MovimientoInsumo
{
    public long IdMovimiento { get; set; }
    public int IdInsumo { get; set; }
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal ValorTotal { get; set; }
    public decimal SaldoCantidad { get; set; }
    public decimal SaldoValor { get; set; }
    public string? Documento { get; set; }
    public string? Detalle { get; set; }
}

/// <summary>Lote de producto terminado (fila de InventarioEmpacado).</summary>
public sealed class LoteEmpacado
{
    public int IdInventarioEmpacado { get; set; }
    public string CodigoLote { get; set; } = "";
    public DateTime Fecha { get; set; }
    public int IdOrdenTueste { get; set; }
    public string NumeroTueste { get; set; } = "";
    public int IdPresentacion { get; set; }
    public string Presentacion { get; set; } = "";
    public int GramosNetos { get; set; }
    public string TipoMolienda { get; set; } = "";
    public int Unidades { get; set; }
    public decimal KgNetos { get; set; }
    public decimal KgTostadoUsado { get; set; }
    public decimal KgMermaMolienda { get; set; }
    public decimal PctMermaMolienda { get; set; }
    public decimal CostoCafe { get; set; }
    public decimal CostoInsumos { get; set; }
    public decimal CostoMolienda { get; set; }
    public decimal CostoManoObra { get; set; }
    public decimal OtrosCostos { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal CostoUnitario { get; set; }
    public int UnidadesDisponibles { get; set; }
    public decimal ValorDisponible { get; set; }
    public decimal PrecioVenta { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string Estado { get; set; } = Estados.Activo;
    public decimal MargenPct => PrecioVenta > 0 ? Math.Round((PrecioVenta - CostoUnitario) / PrecioVenta * 100m, 1) : 0;
    public string Descripcion => $"{CodigoLote} · {Presentacion} · {UnidadesDisponibles} und · costo {CostoUnitario:C0}";
}

public sealed class Cliente
{
    public int IdCliente { get; set; }
    public string? Documento { get; set; }
    public string Nombre { get; set; } = "";
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Email { get; set; }
    public decimal? CupoCredito { get; set; }
    public int DiasCredito { get; set; } = 30;
    public bool EsGenerico { get; set; }
    public bool Activo { get; set; } = true;
    public decimal SaldoPendiente { get; set; }
    public decimal SaldoVencido { get; set; }
    public string Descripcion => string.IsNullOrWhiteSpace(Documento) ? Nombre : $"{Nombre} · {Documento}";
}

public sealed class Venta
{
    public int IdVenta { get; set; }
    public string NumeroFactura { get; set; } = "";
    public DateTime Fecha { get; set; }
    public int IdCliente { get; set; }
    public string Cliente { get; set; } = "";
    public string? DocumentoCliente { get; set; }
    public string TipoPago { get; set; } = "CONTADO";
    public string? MedioPago { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Total { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal Saldo { get; set; }
    public string Estado { get; set; } = Estados.Pendiente;
    public string? Observaciones { get; set; }
    public decimal Utilidad => Total - CostoTotal;
    public int DiasVencido { get; set; }
    public bool Vencida => DiasVencido > 0;
}

public sealed class DetalleVenta
{
    public int IdDetalleVenta { get; set; }
    public int IdVenta { get; set; }
    public int IdInventarioEmpacado { get; set; }
    public string CodigoLote { get; set; } = "";
    public string Presentacion { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
}

public sealed class Abono
{
    public int IdAbono { get; set; }
    public string NumeroRecibo { get; set; } = "";
    public int IdVenta { get; set; }
    public string NumeroFactura { get; set; } = "";
    public string Cliente { get; set; } = "";
    public DateTime Fecha { get; set; }
    public decimal Valor { get; set; }
    public string? MedioPago { get; set; }
    public string? Referencia { get; set; }
    public string? Observaciones { get; set; }
    public bool Anulado { get; set; }
}

public sealed class ResumenPanel
{
    public decimal VentasHoy { get; set; }
    public decimal VentasMes { get; set; }
    public decimal UtilidadMes { get; set; }
    public decimal CarteraTotal { get; set; }
    public decimal CarteraVencida { get; set; }
    public decimal ValorCafeSeco { get; set; }
    public decimal KgCafeSeco { get; set; }
    public decimal ValorCafeTostado { get; set; }
    public decimal KgCafeTostado { get; set; }
    public decimal ValorProductoTerminado { get; set; }
    public int UnidadesProductoTerminado { get; set; }
    public decimal ValorInsumos { get; set; }
    public int InsumosBajoMinimo { get; set; }
    public decimal MermaPromedioMes { get; set; }
    public decimal InventarioTotal => ValorCafeSeco + ValorCafeTostado + ValorProductoTerminado + ValorInsumos;
}

public sealed class LineaTrazabilidad
{
    public string NumeroFactura { get; set; } = "";
    public DateTime FechaVenta { get; set; }
    public string Cliente { get; set; } = "";
    public string CodigoLoteEmpacado { get; set; } = "";
    public string Presentacion { get; set; } = "";
    public int Cantidad { get; set; }
    public string NumeroTueste { get; set; } = "";
    public DateTime FechaTueste { get; set; }
    public string CodigoLoteSeco { get; set; } = "";
    public string Proveedor { get; set; } = "";
    public string? Origen { get; set; }
}

public sealed class InfoRespaldo
{
    public string Ruta { get; set; } = "";
    public DateTime Fecha { get; set; }
    public long TamanoBytes { get; set; }
    public string Tamano => TamanoBytes switch
    {
        < 1024 * 1024 => $"{TamanoBytes / 1024.0:N0} KB",
        _ => $"{TamanoBytes / 1024.0 / 1024.0:N1} MB"
    };
    public string Nombre => Path.GetFileName(Ruta);
}
