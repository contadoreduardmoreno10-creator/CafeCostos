using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

public sealed class FilaAlmacenamiento
{
    public string Tabla { get; set; } = "";
    public string? Compresion { get; set; }
    public long Filas { get; set; }
    public decimal TamanoMB { get; set; }
}

public sealed class VentaPorPresentacion
{
    public string Presentacion { get; set; } = "";
    public int Unidades { get; set; }
    public decimal Ventas { get; set; }
    public decimal Costo { get; set; }
    public decimal Utilidad => Ventas - Costo;
    public decimal MargenPct => Ventas > 0 ? Math.Round(Utilidad / Ventas * 100m, 1) : 0;
}

public sealed class ReporteService
{
    private readonly ConexionFactory _db;
    public ReporteService(ConexionFactory db) => _db = db;

    public async Task<ResumenPanel> ResumenPanelAsync(DateTime? hoy = null)
    {
        var dia = (hoy ?? DateTime.Today).Date;
        var inicioMes = new DateTime(dia.Year, dia.Month, 1);
        await using var cn = await _db.AbrirAsync();
        return await cn.QuerySingleAsync<ResumenPanel>(@"
SELECT
 (SELECT ISNULL(SUM(Total), 0) FROM dbo.Ventas WHERE Estado <> 'ANULADA' AND Fecha = @dia) AS VentasHoy,
 (SELECT ISNULL(SUM(Total), 0) FROM dbo.Ventas WHERE Estado <> 'ANULADA' AND Fecha >= @inicioMes AND Fecha <= @dia) AS VentasMes,
 (SELECT ISNULL(SUM(Total - CostoTotal), 0) FROM dbo.Ventas WHERE Estado <> 'ANULADA' AND Fecha >= @inicioMes AND Fecha <= @dia) AS UtilidadMes,
 (SELECT ISNULL(SUM(Saldo), 0) FROM dbo.Ventas WHERE Estado = 'PENDIENTE') AS CarteraTotal,
 (SELECT ISNULL(SUM(Saldo), 0) FROM dbo.Ventas WHERE Estado = 'PENDIENTE' AND FechaVencimiento < @dia) AS CarteraVencida,
 (SELECT ISNULL(SUM(ValorDisponible), 0) FROM dbo.LotesCafeSeco WHERE Estado = 'ACTIVO') AS ValorCafeSeco,
 (SELECT ISNULL(SUM(KgDisponibles), 0) FROM dbo.LotesCafeSeco WHERE Estado = 'ACTIVO') AS KgCafeSeco,
 (SELECT ISNULL(SUM(ValorDisponible), 0) FROM dbo.OrdenesTueste WHERE Estado = 'ACTIVO') AS ValorCafeTostado,
 (SELECT ISNULL(SUM(KgDisponibles), 0) FROM dbo.OrdenesTueste WHERE Estado = 'ACTIVO') AS KgCafeTostado,
 (SELECT ISNULL(SUM(ValorDisponible), 0) FROM dbo.InventarioEmpacado WHERE Estado = 'ACTIVO') AS ValorProductoTerminado,
 (SELECT ISNULL(SUM(UnidadesDisponibles), 0) FROM dbo.InventarioEmpacado WHERE Estado = 'ACTIVO') AS UnidadesProductoTerminado,
 (SELECT ISNULL(SUM(ValorStock), 0) FROM dbo.Insumos) AS ValorInsumos,
 (SELECT COUNT(*) FROM dbo.Insumos WHERE Activo = 1 AND Stock <= StockMinimo) AS InsumosBajoMinimo,
 (SELECT ISNULL(SUM(KgSecos - KgTostados) * 100.0 / NULLIF(SUM(KgSecos), 0), 0)
    FROM dbo.OrdenesTueste WHERE Estado <> 'ANULADO' AND Fecha >= @inicioMes AND Fecha <= @dia) AS MermaPromedioMes;",
            new { dia, inicioMes });
    }

    public async Task<IReadOnlyList<VentaPorPresentacion>> VentasPorPresentacionAsync(DateTime desde, DateTime hasta)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<VentaPorPresentacion>(@"
SELECT p.Nombre AS Presentacion, SUM(d.Cantidad) AS Unidades,
       SUM(d.Subtotal) AS Ventas, SUM(d.CostoTotal) AS Costo
FROM dbo.DetalleVentas d
JOIN dbo.Ventas v ON v.IdVenta = d.IdVenta
JOIN dbo.InventarioEmpacado ie ON ie.IdInventarioEmpacado = d.IdInventarioEmpacado
JOIN dbo.Presentaciones p ON p.IdPresentacion = ie.IdPresentacion
WHERE v.Estado <> 'ANULADA' AND v.Fecha BETWEEN @desde AND @hasta
GROUP BY p.Nombre ORDER BY SUM(d.Subtotal) DESC;", new { desde = desde.Date, hasta = hasta.Date })).AsList();
    }

    public async Task<IReadOnlyList<LineaTrazabilidad>> TrazabilidadAsync(string? buscar)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<LineaTrazabilidad>(@"
SELECT TOP (500) * FROM dbo.vw_Trazabilidad
WHERE @b IS NULL OR NumeroFactura LIKE '%' + @b + '%' OR CodigoLoteEmpacado LIKE '%' + @b + '%'
   OR NumeroTueste LIKE '%' + @b + '%' OR CodigoLoteSeco LIKE '%' + @b + '%' OR Cliente LIKE '%' + @b + '%'
ORDER BY FechaVenta DESC, NumeroFactura DESC;",
            new { b = string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim() })).AsList();
    }

    public async Task<IReadOnlyList<FilaAlmacenamiento>> EstadoAlmacenamientoAsync()
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<FilaAlmacenamiento>(
            "SELECT Tabla, Compresion, ISNULL(Filas, 0) AS Filas, ISNULL(TamanoMB, 0) AS TamanoMB FROM dbo.vw_EstadoAlmacenamiento ORDER BY TamanoMB DESC, Tabla;")).AsList();
    }
}
