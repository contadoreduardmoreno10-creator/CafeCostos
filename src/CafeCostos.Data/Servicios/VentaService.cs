using CafeCostos.Core;
using CafeCostos.Core.Cartera;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;
using CafeCostos.Core.Ventas;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

/// <summary>Comprobante de venta interno: descuenta producto terminado por lote, calcula costo de venta y crea la cartera.</summary>
public sealed class VentaService
{
    private readonly ConexionFactory _db;
    public VentaService(ConexionFactory db) => _db = db;

    private const string SelectVenta = @"
SELECT v.IdVenta, v.NumeroFactura, v.Fecha, v.IdCliente, c.Nombre AS Cliente, c.Documento AS DocumentoCliente,
       v.TipoPago, v.MedioPago, v.FechaVencimiento, v.Subtotal, v.Descuento, v.Total, v.CostoTotal, v.Saldo,
       v.Estado, v.Observaciones,
       CASE WHEN v.Estado = 'PENDIENTE' AND v.FechaVencimiento < CAST(GETDATE() AS date)
            THEN DATEDIFF(DAY, v.FechaVencimiento, CAST(GETDATE() AS date)) ELSE 0 END AS DiasVencido
FROM dbo.Ventas v JOIN dbo.Clientes c ON c.IdCliente = v.IdCliente ";

    public async Task<IReadOnlyList<Venta>> ListarAsync(DateTime desde, DateTime hasta, int? idCliente = null)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<Venta>(SelectVenta + @"
WHERE v.Fecha BETWEEN @desde AND @hasta AND (@idCliente IS NULL OR v.IdCliente = @idCliente)
ORDER BY v.Fecha DESC, v.IdVenta DESC;", new { desde = desde.Date, hasta = hasta.Date, idCliente })).AsList();
    }

    public async Task<Venta?> ObtenerAsync(int idVenta)
    {
        await using var cn = await _db.AbrirAsync();
        return await cn.QuerySingleOrDefaultAsync<Venta>(SelectVenta + "WHERE v.IdVenta = @idVenta;", new { idVenta });
    }

    public async Task<IReadOnlyList<DetalleVenta>> DetalleAsync(int idVenta)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<DetalleVenta>(@"
SELECT d.IdDetalleVenta, d.IdVenta, d.IdInventarioEmpacado, ie.CodigoLote, p.Nombre AS Presentacion,
       d.Cantidad, d.PrecioUnitario, d.Subtotal, d.CostoUnitario, d.CostoTotal
FROM dbo.DetalleVentas d
JOIN dbo.InventarioEmpacado ie ON ie.IdInventarioEmpacado = d.IdInventarioEmpacado
JOIN dbo.Presentaciones p ON p.IdPresentacion = ie.IdPresentacion
WHERE d.IdVenta = @idVenta ORDER BY d.IdDetalleVenta;", new { idVenta })).AsList();
    }

    /// <summary>
    /// Registra la venta en una transacción:
    ///   bloquea lotes → valoriza costo de venta → descuenta unidades y valor → crea factura y detalle
    ///   → si es a crédito, la factura queda como cuenta por cobrar (Saldo = Total).
    /// </summary>
    public Task<(int IdVenta, string Numero, TotalesVenta Totales)> RegistrarAsync(NuevaVenta v) =>
        _db.EnTransaccionAsync(async (cn, tx) =>
        {
            var lineas = CalculadoraVenta.Consolidar(v.Lineas.Select(l => (l.IdInventarioEmpacado, l.Cantidad, l.PrecioUnitario)));
            Validar.Que(lineas.Count > 0, "Agregue al menos un producto a la venta.");

            var cliente = await cn.QuerySingleOrDefaultAsync<Cliente>(
                "SELECT * FROM dbo.Clientes WITH (UPDLOCK, ROWLOCK) WHERE IdCliente = @id", new { id = v.IdCliente }, tx)
                ?? throw new ReglaNegocioException("Seleccione el cliente.");
            Validar.Que(cliente.Activo, "El cliente está inactivo.");

            // Valorización de cada línea al costo del lote (con bloqueo de fila)
            var costeadas = new List<LineaVentaCosteada>();
            foreach (var (idLote, cantidad, precio) in lineas)
            {
                var lote = await cn.QuerySingleOrDefaultAsync<LoteEmpacado>(@"
SELECT IdInventarioEmpacado, CodigoLote, UnidadesDisponibles, ValorDisponible, Estado
FROM dbo.InventarioEmpacado WITH (UPDLOCK, ROWLOCK) WHERE IdInventarioEmpacado = @idLote", new { idLote }, tx)
                    ?? throw new ReglaNegocioException("Uno de los lotes de producto no existe.");
                Validar.Que(lote.Estado == Estados.Activo && cantidad <= lote.UnidadesDisponibles,
                    $"El lote {lote.CodigoLote} solo tiene {lote.UnidadesDisponibles} unidades disponibles.");
                var costo = CalculadoraCostos.CostoSalida(lote.UnidadesDisponibles, lote.ValorDisponible, cantidad);
                costeadas.Add(new LineaVentaCosteada(idLote, cantidad, Redondeo.Dinero(precio), costo));
            }

            var totales = CalculadoraVenta.Totales(costeadas, v.Descuento, v.TipoPago);

            DateTime? vencimiento = null;
            if (v.TipoPago == TipoPago.Credito)
            {
                var saldo = await cn.ExecuteScalarAsync<decimal>(
                    "SELECT ISNULL(SUM(Saldo), 0) FROM dbo.Ventas WHERE IdCliente = @id AND Estado = 'PENDIENTE'", new { id = cliente.IdCliente }, tx);
                CalculadoraVenta.ValidarCredito(cliente.EsGenerico, cliente.Activo, cliente.CupoCredito, saldo, totales.Total, 0);
                vencimiento = CalculadoraCartera.FechaVencimiento(v.Fecha, v.DiasCredito ?? cliente.DiasCredito);
            }

            var numero = await Consecutivos.SiguienteAsync(cn, tx, Consecutivos.Factura);
            var idVenta = await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.Ventas
 (NumeroFactura, Fecha, IdCliente, TipoPago, MedioPago, FechaVencimiento, Subtotal, Descuento, CostoTotal, Saldo, Estado, Observaciones)
VALUES
 (@numero, @fecha, @idCliente, @tipo, @medio, @vencimiento, @Subtotal, @Descuento, @CostoTotal, @SaldoInicial, @estado, @obs);
SELECT CAST(SCOPE_IDENTITY() AS int);",
                new
                {
                    numero, fecha = v.Fecha.Date, idCliente = cliente.IdCliente, tipo = CalculadoraVenta.Codigo(v.TipoPago),
                    medio = v.TipoPago == TipoPago.Contado ? v.MedioPago : null, vencimiento,
                    totales.Subtotal, totales.Descuento, totales.CostoTotal, totales.SaldoInicial,
                    estado = totales.SaldoInicial > 0 ? Estados.Pendiente : Estados.Pagada, obs = v.Observaciones
                }, tx);

            foreach (var l in costeadas)
            {
                var ok = await cn.ExecuteAsync(@"
UPDATE dbo.InventarioEmpacado
   SET UnidadesDisponibles = UnidadesDisponibles - @Cantidad,
       ValorDisponible     = ValorDisponible - @CostoTotal,
       Estado = CASE WHEN UnidadesDisponibles - @Cantidad = 0 THEN 'AGOTADO' ELSE Estado END
 WHERE IdInventarioEmpacado = @IdInventarioEmpacado AND UnidadesDisponibles >= @Cantidad;

INSERT INTO dbo.DetalleVentas (IdVenta, IdInventarioEmpacado, Cantidad, PrecioUnitario, CostoUnitario, CostoTotal)
VALUES (@idVenta, @IdInventarioEmpacado, @Cantidad, @PrecioUnitario, @cu, @CostoTotal);",
                    new { idVenta, l.IdInventarioEmpacado, l.Cantidad, l.PrecioUnitario, l.CostoTotal,
                          cu = CalculadoraCostos.CostoPromedio(l.Cantidad, l.CostoTotal) }, tx);
                Validar.Que(ok == 2, "El inventario cambió mientras se registraba la venta. Intente de nuevo.");
            }

            return (idVenta, numero, totales);
        });

    /// <summary>Anula una venta sin abonos: devuelve las unidades (y su valor exacto) a cada lote.</summary>
    public Task AnularAsync(int idVenta, string motivo) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        Validar.Texto(motivo, "Motivo de anulación");
        var venta = await cn.QuerySingleOrDefaultAsync<Venta>(
            "SELECT * FROM dbo.Ventas WITH (UPDLOCK, ROWLOCK) WHERE IdVenta = @idVenta", new { idVenta }, tx)
            ?? throw new ReglaNegocioException("La venta no existe.");
        Validar.Que(venta.Estado != Estados.Anulada, "La venta ya está anulada.");
        var abonos = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.AbonosCartera WHERE IdVenta = @idVenta AND Anulado = 0", new { idVenta }, tx);
        Validar.Que(abonos == 0, "La factura tiene abonos registrados. Anule primero los abonos en Cartera.");

        await cn.ExecuteAsync(@"
UPDATE ie SET ie.UnidadesDisponibles = ie.UnidadesDisponibles + d.Cantidad,
              ie.ValorDisponible     = ie.ValorDisponible + d.CostoTotal,
              ie.Estado = 'ACTIVO'
FROM dbo.InventarioEmpacado ie WITH (UPDLOCK, ROWLOCK)
JOIN dbo.DetalleVentas d ON d.IdInventarioEmpacado = ie.IdInventarioEmpacado
WHERE d.IdVenta = @idVenta AND ie.Estado <> 'ANULADO';

UPDATE dbo.Ventas SET Estado = 'ANULADA', Saldo = 0, MotivoAnulacion = @motivo, FechaAnulacion = SYSDATETIME()
WHERE IdVenta = @idVenta;", new { idVenta, motivo }, tx);
        return true;
    });
}
