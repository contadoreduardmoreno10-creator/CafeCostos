using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Servicios;

/// <summary>Órdenes de tostión: consumo de lotes secos, merma por evaporación y costo del kilo tostado.</summary>
public sealed class TuesteService
{
    private readonly ConexionFactory _db;
    public TuesteService(ConexionFactory db) => _db = db;

    private const string SelectOrden = @"
SELECT o.IdOrdenTueste, o.Numero, o.Fecha, o.PerfilTueste, o.KgSecos, o.KgTostados, o.PctMerma,
       o.CostoCafeSeco, o.CostoEnergia, o.CostoManoObra, o.OtrosCostos, o.CostoTotal, o.CostoKgTostado,
       o.KgDisponibles, o.ValorDisponible, o.Estado, o.Observaciones,
       (SELECT STRING_AGG(l.Codigo, ', ') FROM dbo.OrdenTuesteLotes x
          JOIN dbo.LotesCafeSeco l ON l.IdLote = x.IdLote WHERE x.IdOrdenTueste = o.IdOrdenTueste) AS LotesOrigen
FROM dbo.OrdenesTueste o ";

    public async Task<IReadOnlyList<OrdenTueste>> ListarAsync(bool soloDisponibles = false)
    {
        await using var cn = await _db.AbrirAsync();
        var sql = SelectOrden +
                  (soloDisponibles ? "WHERE o.Estado = 'ACTIVO' AND o.KgDisponibles > 0 " : "") +
                  "ORDER BY o.Fecha DESC, o.IdOrdenTueste DESC;";
        return (await cn.QueryAsync<OrdenTueste>(sql)).AsList();
    }

    public async Task<IReadOnlyList<OrdenTuesteLote>> DetalleAsync(int idOrden)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<OrdenTuesteLote>(@"
SELECT x.IdOrdenTueste, x.IdLote, l.Codigo AS CodigoLote, l.Proveedor, x.KgUsados, x.CostoKg, x.CostoTotal
FROM dbo.OrdenTuesteLotes x JOIN dbo.LotesCafeSeco l ON l.IdLote = x.IdLote
WHERE x.IdOrdenTueste = @idOrden ORDER BY l.Codigo;", new { idOrden })).AsList();
    }

    /// <summary>Calcula el costeo SIN guardar (vista previa en pantalla mientras el usuario digita).</summary>
    public async Task<ResultadoTueste> PrevisualizarAsync(NuevaOrdenTueste orden)
    {
        await using var cn = await _db.AbrirAsync();
        var consumos = await CostearConsumosAsync(cn, null, orden, bloquear: false);
        return CalculadoraCostos.CalcularTueste(consumos.Select(c => c.Consumo).ToList(), orden.KgTostados, Conversion(orden));
    }

    /// <summary>
    /// Registra la orden en UNA transacción:
    ///  descuenta kilos y valor de cada lote seco → costea → crea la orden con su inventario de tostado.
    /// </summary>
    public Task<(string Numero, ResultadoTueste Resultado)> RegistrarAsync(NuevaOrdenTueste orden) =>
        _db.EnTransaccionAsync(async (cn, tx) =>
        {
            var consumos = await CostearConsumosAsync(cn, tx, orden, bloquear: true);
            var r = CalculadoraCostos.CalcularTueste(consumos.Select(c => c.Consumo).ToList(), orden.KgTostados, Conversion(orden));

            foreach (var c in consumos)
            {
                var afectadas = await cn.ExecuteAsync(@"
UPDATE dbo.LotesCafeSeco
   SET KgDisponibles   = KgDisponibles - @kg,
       ValorDisponible = ValorDisponible - @costo,
       Estado = CASE WHEN KgDisponibles - @kg = 0 THEN 'AGOTADO' ELSE Estado END
 WHERE IdLote = @id AND KgDisponibles >= @kg AND Estado = 'ACTIVO';",
                    new { kg = c.Consumo.Kilos, costo = c.Consumo.Costo, id = c.Consumo.IdLote }, tx);
                Validar.Que(afectadas == 1, $"El lote {c.Codigo} cambió mientras se registraba. Intente de nuevo.");
            }

            var numero = await Consecutivos.SiguienteAsync(cn, tx, Consecutivos.Tueste);
            var idOrden = await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.OrdenesTueste
 (Numero, Fecha, PerfilTueste, KgSecos, KgTostados, PctMerma, CostoCafeSeco, CostoEnergia, CostoManoObra,
  OtrosCostos, CostoKgTostado, KgDisponibles, ValorDisponible, Estado, Observaciones)
VALUES
 (@numero, @Fecha, @PerfilTueste, @KgSecos, @KgTostados, @PctMerma, @CostoMateriaPrima, @energia, @mo,
  @otros, @CostoKgTostado, @KgTostados, @CostoTotal, 'ACTIVO', @Observaciones);
SELECT CAST(SCOPE_IDENTITY() AS int);",
                new
                {
                    numero, orden.Fecha, orden.PerfilTueste, r.KgSecos, r.KgTostados, r.PctMerma, r.CostoMateriaPrima,
                    energia = Redondeo.Dinero(orden.CostoEnergia), mo = Redondeo.Dinero(orden.CostoManoObra),
                    otros = Redondeo.Dinero(orden.OtrosCostos), r.CostoKgTostado, r.CostoTotal, orden.Observaciones
                }, tx);

            foreach (var c in consumos)
                await cn.ExecuteAsync(@"
INSERT INTO dbo.OrdenTuesteLotes (IdOrdenTueste, IdLote, KgUsados, CostoKg, CostoTotal)
VALUES (@idOrden, @IdLote, @Kilos, @costoKg, @Costo);",
                    new { idOrden, c.Consumo.IdLote, c.Consumo.Kilos, costoKg = CalculadoraCostos.CostoPromedio(c.Consumo.Kilos, c.Consumo.Costo), c.Consumo.Costo }, tx);

            // Cuadre: el inventario de tostado nace con el costo total exacto de la orden.
            var valor = await cn.ExecuteScalarAsync<decimal>(
                "SELECT ValorDisponible - CostoTotal FROM dbo.OrdenesTueste WHERE IdOrdenTueste = @idOrden", new { idOrden }, tx);
            Validar.Que(valor == 0, "Error de cuadre en el costeo de la tostión. La operación se canceló.");

            return (numero, r);
        });

    /// <summary>Anula una tostión que aún no se ha empacado: devuelve kilos y valor a los lotes secos.</summary>
    public Task AnularAsync(int idOrden) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        var o = await cn.QuerySingleOrDefaultAsync<OrdenTueste>(
            "SELECT * FROM dbo.OrdenesTueste WITH (UPDLOCK, ROWLOCK) WHERE IdOrdenTueste = @idOrden", new { idOrden }, tx)
            ?? throw new ReglaNegocioException("La orden no existe.");
        Validar.Que(o.Estado != Estados.Anulado, "La orden ya está anulada.");
        Validar.Que(o.KgDisponibles == o.KgTostados,
            "No se puede anular: parte del café tostado ya fue empacado. Anule primero esos lotes de empaque.");

        await cn.ExecuteAsync(@"
UPDATE l SET l.KgDisponibles   = l.KgDisponibles + x.KgUsados,
             l.ValorDisponible = l.ValorDisponible + x.CostoTotal,
             l.Estado = 'ACTIVO'
FROM dbo.LotesCafeSeco l WITH (UPDLOCK, ROWLOCK)
JOIN dbo.OrdenTuesteLotes x ON x.IdLote = l.IdLote
WHERE x.IdOrdenTueste = @idOrden;

UPDATE dbo.OrdenesTueste SET Estado = 'ANULADO', KgDisponibles = 0, ValorDisponible = 0
WHERE IdOrdenTueste = @idOrden;", new { idOrden }, tx);
        return true;
    });

    // ----------------------------------------------------------------------------------
    private sealed record ConsumoLeido(ConsumoMateriaPrima Consumo, string Codigo);

    private static CostosConversion Conversion(NuevaOrdenTueste o) =>
        new(o.CostoEnergia, o.CostoManoObra, o.OtrosCostos);

    /// <summary>Lee los lotes (con bloqueo si se va a guardar) y valoriza los kilos solicitados.</summary>
    private static async Task<List<ConsumoLeido>> CostearConsumosAsync(
        SqlConnection cn, SqlTransaction? tx, NuevaOrdenTueste orden, bool bloquear)
    {
        Validar.Que(orden.Lotes.Count > 0, "Agregue al menos un lote de café seco.");
        Validar.Que(orden.Lotes.Select(l => l.IdLote).Distinct().Count() == orden.Lotes.Count,
            "Un mismo lote aparece dos veces. Sume los kilos en una sola línea.");

        var hint = bloquear ? "WITH (UPDLOCK, ROWLOCK)" : "";
        var resultado = new List<ConsumoLeido>();
        foreach (var solicitud in orden.Lotes)
        {
            var kilos = Redondeo.Kilos(solicitud.Kilos);
            Validar.Positivo(kilos, "Kilos del lote");
            var lote = await cn.QuerySingleOrDefaultAsync<LoteCafeSeco>(
                $"SELECT IdLote, Codigo, KgDisponibles, ValorDisponible, Estado FROM dbo.LotesCafeSeco {hint} WHERE IdLote = @id",
                new { id = solicitud.IdLote }, tx)
                ?? throw new ReglaNegocioException("Uno de los lotes seleccionados no existe.");
            Validar.Que(lote.Estado == Estados.Activo, $"El lote {lote.Codigo} no está activo.");
            Validar.Que(kilos <= lote.KgDisponibles,
                $"El lote {lote.Codigo} solo tiene {lote.KgDisponibles:N3} kg disponibles.");

            var costo = CalculadoraCostos.CostoSalida(lote.KgDisponibles, lote.ValorDisponible, kilos);
            resultado.Add(new ConsumoLeido(new ConsumoMateriaPrima(lote.IdLote, kilos, costo), lote.Codigo));
        }
        return resultado;
    }
}
