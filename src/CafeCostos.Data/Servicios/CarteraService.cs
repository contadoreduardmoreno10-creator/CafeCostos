using CafeCostos.Core;
using CafeCostos.Core.Cartera;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

/// <summary>Cuentas por cobrar: facturas pendientes, abonos (recibos de caja) y edades de cartera.</summary>
public sealed class CarteraService
{
    private readonly ConexionFactory _db;
    public CarteraService(ConexionFactory db) => _db = db;

    public async Task<IReadOnlyList<Venta>> PendientesAsync(int? idCliente = null)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<Venta>(@"
SELECT IdVenta, NumeroFactura, Fecha, FechaVencimiento, IdCliente, Cliente, DocumentoCliente,
       Total, Saldo, DiasVencido, 'CREDITO' AS TipoPago, 'PENDIENTE' AS Estado
FROM dbo.vw_CarteraPendiente
WHERE (@idCliente IS NULL OR IdCliente = @idCliente)
ORDER BY DiasVencido DESC, FechaVencimiento, NumeroFactura;", new { idCliente })).AsList();
    }

    public async Task<ResumenEdades> ResumenEdadesAsync(DateTime? hoy = null)
    {
        var pendientes = await PendientesAsync();
        return CalculadoraCartera.Resumir(
            pendientes.Select(p => (p.FechaVencimiento ?? p.Fecha, p.Saldo)), hoy ?? DateTime.Today);
    }

    public async Task<IReadOnlyList<Abono>> AbonosAsync(int? idVenta = null, DateTime? desde = null, DateTime? hasta = null)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<Abono>(@"
SELECT a.IdAbono, a.NumeroRecibo, a.IdVenta, v.NumeroFactura, c.Nombre AS Cliente, a.Fecha, a.Valor,
       a.MedioPago, a.Referencia, a.Observaciones, a.Anulado
FROM dbo.AbonosCartera a
JOIN dbo.Ventas v ON v.IdVenta = a.IdVenta
JOIN dbo.Clientes c ON c.IdCliente = v.IdCliente
WHERE (@idVenta IS NULL OR a.IdVenta = @idVenta)
  AND (@desde IS NULL OR a.Fecha >= @desde) AND (@hasta IS NULL OR a.Fecha <= @hasta)
ORDER BY a.Fecha DESC, a.IdAbono DESC;", new { idVenta, desde = desde?.Date, hasta = hasta?.Date })).AsList();
    }

    /// <summary>Registra un abono (recibo de caja) y actualiza el saldo de la factura en la misma transacción.</summary>
    public Task<string> RegistrarAbonoAsync(NuevoAbono a) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        var venta = await cn.QuerySingleOrDefaultAsync<Venta>(
            "SELECT IdVenta, NumeroFactura, Fecha, Saldo, Estado FROM dbo.Ventas WITH (UPDLOCK, ROWLOCK) WHERE IdVenta = @id",
            new { id = a.IdVenta }, tx) ?? throw new ReglaNegocioException("Seleccione la factura a la que aplica el abono.");
        Validar.Que(venta.Estado == Estados.Pendiente, $"La factura {venta.NumeroFactura} no tiene saldo pendiente.");
        Validar.Que(a.Fecha.Date >= venta.Fecha.Date, "La fecha del abono no puede ser anterior a la fecha de la factura.");

        var valor = Redondeo.Dinero(a.Valor);
        var (nuevoSaldo, cancelada) = CalculadoraCartera.AplicarAbono(venta.Saldo, valor);
        var numero = await Consecutivos.SiguienteAsync(cn, tx, Consecutivos.Recibo);

        await cn.ExecuteAsync(@"
INSERT INTO dbo.AbonosCartera (NumeroRecibo, IdVenta, Fecha, Valor, MedioPago, Referencia, Observaciones)
VALUES (@numero, @IdVenta, @fecha, @valor, @MedioPago, @Referencia, @Observaciones);

UPDATE dbo.Ventas SET Saldo = @nuevoSaldo, Estado = @estado WHERE IdVenta = @IdVenta;",
            new
            {
                numero, a.IdVenta, fecha = a.Fecha.Date, valor, a.MedioPago, a.Referencia, a.Observaciones,
                nuevoSaldo, estado = cancelada ? Estados.Pagada : Estados.Pendiente
            }, tx);
        return numero;
    });

    /// <summary>Anula un abono y restituye el saldo de la factura.</summary>
    public Task AnularAbonoAsync(int idAbono) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        var abono = await cn.QuerySingleOrDefaultAsync<Abono>(
            "SELECT * FROM dbo.AbonosCartera WITH (UPDLOCK, ROWLOCK) WHERE IdAbono = @idAbono", new { idAbono }, tx)
            ?? throw new ReglaNegocioException("El abono no existe.");
        Validar.Que(!abono.Anulado, "El abono ya está anulado.");

        var venta = await cn.QuerySingleAsync<Venta>(
            "SELECT IdVenta, Total, Saldo, Estado FROM dbo.Ventas WITH (UPDLOCK, ROWLOCK) WHERE IdVenta = @id", new { id = abono.IdVenta }, tx);
        Validar.Que(venta.Estado != Estados.Anulada, "La factura está anulada.");
        Validar.Que(venta.Saldo + abono.Valor <= venta.Total, "Error de cuadre: el saldo superaría el total de la factura.");

        await cn.ExecuteAsync(@"
UPDATE dbo.AbonosCartera SET Anulado = 1, FechaAnulacion = SYSDATETIME() WHERE IdAbono = @idAbono;
UPDATE dbo.Ventas SET Saldo = Saldo + @valor, Estado = 'PENDIENTE' WHERE IdVenta = @idVenta;",
            new { idAbono, valor = abono.Valor, idVenta = abono.IdVenta }, tx);
        return true;
    });
}
