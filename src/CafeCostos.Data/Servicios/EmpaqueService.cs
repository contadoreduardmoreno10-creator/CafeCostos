using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Servicios;

/// <summary>
/// Molienda, fraccionamiento y empaque: convierte kilos tostados en bolsas terminadas,
/// consumiendo insumos según la receta de la presentación y generando un lote de producto terminado.
/// </summary>
public sealed class EmpaqueService
{
    private readonly ConexionFactory _db;
    public EmpaqueService(ConexionFactory db) => _db = db;

    internal const string SelectLote = @"
SELECT ie.IdInventarioEmpacado, ie.CodigoLote, ie.Fecha, ie.IdOrdenTueste, ot.Numero AS NumeroTueste,
       ie.IdPresentacion, p.Nombre AS Presentacion, p.GramosNetos, p.TipoMolienda, ie.Unidades, ie.KgNetos,
       ie.KgTostadoUsado, ie.KgMermaMolienda, ie.PctMermaMolienda, ie.CostoCafe, ie.CostoInsumos, ie.CostoMolienda,
       ie.CostoManoObra, ie.OtrosCostos, ie.CostoTotal, ie.CostoUnitario, ie.UnidadesDisponibles, ie.ValorDisponible,
       p.PrecioVenta, ie.FechaVencimiento, ie.Estado
FROM dbo.InventarioEmpacado ie
JOIN dbo.Presentaciones p ON p.IdPresentacion = ie.IdPresentacion
JOIN dbo.OrdenesTueste ot ON ot.IdOrdenTueste = ie.IdOrdenTueste ";

    public async Task<IReadOnlyList<LoteEmpacado>> ListarAsync(bool soloDisponibles = false)
    {
        await using var cn = await _db.AbrirAsync();
        var sql = SelectLote +
                  (soloDisponibles ? "WHERE ie.Estado = 'ACTIVO' AND ie.UnidadesDisponibles > 0 " : "") +
                  "ORDER BY ie.Fecha DESC, ie.IdInventarioEmpacado DESC;";
        return (await cn.QueryAsync<LoteEmpacado>(sql)).AsList();
    }

    public async Task<IReadOnlyList<RecetaInsumo>> RecetaAsync(int idPresentacion)
    {
        await using var cn = await _db.AbrirAsync();
        return (await LeerRecetaAsync(cn, null, idPresentacion, false)).AsList();
    }

    public async Task<IReadOnlyList<ConsumoInsumo>> InsumosDeLoteAsync(int idInventarioEmpacado)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<ConsumoInsumo>(@"
SELECT x.IdInsumo, i.Nombre, x.Cantidad, x.CostoTotal AS Costo
FROM dbo.InventarioEmpacadoInsumos x JOIN dbo.Insumos i ON i.IdInsumo = x.IdInsumo
WHERE x.IdInventarioEmpacado = @id ORDER BY i.Nombre;", new { id = idInventarioEmpacado })).AsList();
    }

    public async Task<ResultadoEmpaque> PrevisualizarAsync(NuevoEmpaque e)
    {
        await using var cn = await _db.AbrirAsync();
        var datos = await PrepararAsync(cn, null, e, bloquear: false);
        return datos.Resultado;
    }

    public Task<(string CodigoLote, ResultadoEmpaque Resultado)> RegistrarAsync(NuevoEmpaque e) =>
        _db.EnTransaccionAsync(async (cn, tx) =>
        {
            var d = await PrepararAsync(cn, tx, e, bloquear: true);
            var r = d.Resultado;
            var codigo = await Consecutivos.SiguienteAsync(cn, tx, Consecutivos.Empaque);
            var ahora = DateTime.Now;

            // 1. Retirar café tostado de la orden de tueste
            var ok = await cn.ExecuteAsync(@"
UPDATE dbo.OrdenesTueste
   SET KgDisponibles = KgDisponibles - @kg, ValorDisponible = ValorDisponible - @valor,
       Estado = CASE WHEN KgDisponibles - @kg = 0 THEN 'AGOTADO' ELSE Estado END
 WHERE IdOrdenTueste = @id AND KgDisponibles >= @kg AND Estado = 'ACTIVO';",
                new { kg = r.KgTostadoUsado, valor = r.CostoCafe, id = e.IdOrdenTueste }, tx);
            Validar.Que(ok == 1, "La orden de tueste cambió mientras se registraba. Intente de nuevo.");

            // 2. Crear el lote de producto terminado
            var idLote = await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.InventarioEmpacado
 (CodigoLote, Fecha, IdOrdenTueste, IdPresentacion, Unidades, KgNetos, KgTostadoUsado, PctMermaMolienda,
  CostoCafe, CostoInsumos, CostoMolienda, CostoManoObra, OtrosCostos, CostoUnitario,
  UnidadesDisponibles, ValorDisponible, FechaVencimiento, Estado, Observaciones)
VALUES
 (@codigo, @Fecha, @IdOrdenTueste, @IdPresentacion, @Unidades, @KgNetos, @KgTostadoUsado, @PctMermaMolienda,
  @CostoCafe, @CostoInsumos, @molienda, @mo, @otros, @CostoUnitario,
  @Unidades, @CostoTotal, @FechaVencimiento, 'ACTIVO', @Observaciones);
SELECT CAST(SCOPE_IDENTITY() AS int);",
                new
                {
                    codigo, e.Fecha, e.IdOrdenTueste, e.IdPresentacion, r.Unidades, r.KgNetos, r.KgTostadoUsado,
                    r.PctMermaMolienda, r.CostoCafe, r.CostoInsumos,
                    molienda = Redondeo.Dinero(e.CostoMolienda), mo = Redondeo.Dinero(e.CostoManoObra),
                    otros = Redondeo.Dinero(e.OtrosCostos), r.CostoUnitario, r.CostoTotal, e.FechaVencimiento, e.Observaciones
                }, tx);

            // 3. Consumir insumos (kardex) y guardar su detalle
            foreach (var i in d.Insumos)
            {
                var saldo = await cn.QuerySingleAsync<(decimal Stock, decimal ValorStock)>(@"
UPDATE dbo.Insumos
   SET Stock = Stock - @cant, ValorStock = ValorStock - @costo
OUTPUT inserted.Stock, inserted.ValorStock
 WHERE IdInsumo = @id AND Stock >= @cant;",
                    new { cant = i.Cantidad, costo = i.Costo, id = i.IdInsumo }, tx);

                await cn.ExecuteAsync(@"
INSERT INTO dbo.MovimientosInsumo
 (IdInsumo, Fecha, Tipo, Cantidad, CostoUnitario, ValorTotal, SaldoCantidad, SaldoValor, Documento, Detalle)
VALUES (@IdInsumo, @ahora, 'CONSUMO', -@Cantidad, @cu, -@Costo, @Stock, @ValorStock, @codigo, @detalle);

INSERT INTO dbo.InventarioEmpacadoInsumos (IdInventarioEmpacado, IdInsumo, Cantidad, CostoUnitario, CostoTotal)
VALUES (@idLote, @IdInsumo, @Cantidad, @cu, @Costo);",
                    new
                    {
                        i.IdInsumo, ahora, i.Cantidad, cu = CalculadoraCostos.CostoPromedio(i.Cantidad, i.Costo), i.Costo,
                        saldo.Stock, saldo.ValorStock, codigo, detalle = $"Empaque {codigo}", idLote
                    }, tx);
            }

            return (codigo, r);
        });

    /// <summary>Anula un lote empacado sin ventas: devuelve el café a la tostión y los insumos al inventario.</summary>
    public Task AnularAsync(int idInventarioEmpacado) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        var lote = await cn.QuerySingleOrDefaultAsync<LoteEmpacado>(
            "SELECT * FROM dbo.InventarioEmpacado WITH (UPDLOCK, ROWLOCK) WHERE IdInventarioEmpacado = @id",
            new { id = idInventarioEmpacado }, tx) ?? throw new ReglaNegocioException("El lote no existe.");
        Validar.Que(lote.Estado != Estados.Anulado, "El lote ya está anulado.");
        Validar.Que(lote.UnidadesDisponibles == lote.Unidades,
            "No se puede anular: ya se vendieron unidades de este lote. Anule primero esas ventas.");

        var tueste = await cn.QuerySingleAsync<OrdenTueste>(
            "SELECT * FROM dbo.OrdenesTueste WITH (UPDLOCK, ROWLOCK) WHERE IdOrdenTueste = @id", new { id = lote.IdOrdenTueste }, tx);
        Validar.Que(tueste.Estado != Estados.Anulado, "La orden de tueste de origen está anulada.");

        await cn.ExecuteAsync(@"
UPDATE dbo.OrdenesTueste
   SET KgDisponibles = KgDisponibles + @kg, ValorDisponible = ValorDisponible + @valor, Estado = 'ACTIVO'
 WHERE IdOrdenTueste = @id;",
            new { kg = lote.KgTostadoUsado, valor = lote.CostoCafe, id = lote.IdOrdenTueste }, tx);

        var insumos = await cn.QueryAsync<(int IdInsumo, decimal Cantidad, decimal CostoTotal)>(
            "SELECT IdInsumo, Cantidad, CostoTotal FROM dbo.InventarioEmpacadoInsumos WHERE IdInventarioEmpacado = @id",
            new { id = idInventarioEmpacado }, tx);
        var ahora = DateTime.Now;
        foreach (var i in insumos)
        {
            var saldo = await cn.QuerySingleAsync<(decimal Stock, decimal ValorStock)>(@"
UPDATE dbo.Insumos WITH (ROWLOCK) SET Stock = Stock + @Cantidad, ValorStock = ValorStock + @CostoTotal
OUTPUT inserted.Stock, inserted.ValorStock WHERE IdInsumo = @IdInsumo;", new { i.IdInsumo, i.Cantidad, i.CostoTotal }, tx);
            await cn.ExecuteAsync(@"
INSERT INTO dbo.MovimientosInsumo
 (IdInsumo, Fecha, Tipo, Cantidad, CostoUnitario, ValorTotal, SaldoCantidad, SaldoValor, Documento, Detalle)
VALUES (@IdInsumo, @ahora, 'ANULACION', @Cantidad, @cu, @CostoTotal, @Stock, @ValorStock, @doc, @detalle);",
                new
                {
                    i.IdInsumo, ahora, i.Cantidad, cu = CalculadoraCostos.CostoPromedio(i.Cantidad, i.CostoTotal), i.CostoTotal,
                    saldo.Stock, saldo.ValorStock, doc = lote.CodigoLote, detalle = $"Anulación empaque {lote.CodigoLote}"
                }, tx);
        }

        await cn.ExecuteAsync(@"
UPDATE dbo.InventarioEmpacado SET Estado = 'ANULADO', UnidadesDisponibles = 0, ValorDisponible = 0
WHERE IdInventarioEmpacado = @id;", new { id = idInventarioEmpacado }, tx);
        return true;
    });

    // ----------------------------------------------------------------------------------
    private sealed record Preparacion(ResultadoEmpaque Resultado, IReadOnlyList<ConsumoInsumo> Insumos);

    private static async Task<IEnumerable<RecetaInsumo>> LeerRecetaAsync(
        SqlConnection cn, SqlTransaction? tx, int idPresentacion, bool bloquear) =>
        await cn.QueryAsync<RecetaInsumo>($@"
SELECT r.IdPresentacion, r.IdInsumo, i.Nombre AS NombreInsumo, i.Unidad, r.CantidadPorUnidad,
       i.CostoPromedio, i.Stock
FROM dbo.PresentacionInsumos r
JOIN dbo.Insumos i {(bloquear ? "WITH (UPDLOCK, ROWLOCK)" : "")} ON i.IdInsumo = r.IdInsumo
WHERE r.IdPresentacion = @idPresentacion
ORDER BY i.Nombre;", new { idPresentacion }, tx);

    private static async Task<Preparacion> PrepararAsync(SqlConnection cn, SqlTransaction? tx, NuevoEmpaque e, bool bloquear)
    {
        Validar.Que(e.IdOrdenTueste > 0, "Seleccione la orden de tueste de donde sale el café.");
        Validar.Que(e.IdPresentacion > 0, "Seleccione la presentación.");
        Validar.Que(e.Unidades > 0, "Indique cuántas unidades se empacaron.");

        var hint = bloquear ? "WITH (UPDLOCK, ROWLOCK)" : "";
        var tueste = await cn.QuerySingleOrDefaultAsync<OrdenTueste>(
            $"SELECT IdOrdenTueste, Numero, KgDisponibles, ValorDisponible, Estado FROM dbo.OrdenesTueste {hint} WHERE IdOrdenTueste = @id",
            new { id = e.IdOrdenTueste }, tx) ?? throw new ReglaNegocioException("La orden de tueste no existe.");
        Validar.Que(tueste.Estado == Estados.Activo, $"La orden {tueste.Numero} no tiene café disponible.");

        var pres = await cn.QuerySingleOrDefaultAsync<Presentacion>(
            "SELECT * FROM dbo.Presentaciones WHERE IdPresentacion = @id", new { id = e.IdPresentacion }, tx)
            ?? throw new ReglaNegocioException("La presentación no existe.");
        Validar.Que(pres.Activo, "La presentación está inactiva.");

        var kgUsado = Redondeo.Kilos(e.KgTostadoUsado);
        Validar.Positivo(kgUsado, "Kilos de café tostado usados");
        Validar.Que(kgUsado <= tueste.KgDisponibles,
            $"La orden {tueste.Numero} solo tiene {tueste.KgDisponibles:N3} kg tostados disponibles.");
        var costoCafe = CalculadoraCostos.CostoSalida(tueste.KgDisponibles, tueste.ValorDisponible, kgUsado);

        var receta = (await LeerRecetaAsync(cn, tx, e.IdPresentacion, bloquear)).ToList();
        var insumos = new List<ConsumoInsumo>();
        foreach (var r in receta)
        {
            var cantidad = CalculadoraCostos.CantidadInsumoRequerida(r.CantidadPorUnidad, e.Unidades);
            Validar.Que(cantidad <= r.Stock,
                $"No hay suficiente «{r.NombreInsumo}»: se necesitan {cantidad:N0} {r.Unidad} y hay {r.Stock:N0}. Registre la compra en Insumos.");
            var valor = await cn.ExecuteScalarAsync<decimal>(
                "SELECT ValorStock FROM dbo.Insumos WHERE IdInsumo = @id", new { id = r.IdInsumo }, tx);
            insumos.Add(new ConsumoInsumo(r.IdInsumo, r.NombreInsumo, cantidad, CalculadoraCostos.CostoSalida(r.Stock, valor, cantidad)));
        }

        var resultado = CalculadoraCostos.CalcularEmpaque(new EntradaEmpaque
        {
            GramosNetosPorUnidad = pres.GramosNetos,
            Unidades = e.Unidades,
            KgTostadoUsado = kgUsado,
            CostoCafeTostado = costoCafe,
            Insumos = insumos,
            Conversion = new CostosConversion(e.CostoMolienda, e.CostoManoObra, e.OtrosCostos)
        });
        return new Preparacion(resultado, insumos);
    }
}
