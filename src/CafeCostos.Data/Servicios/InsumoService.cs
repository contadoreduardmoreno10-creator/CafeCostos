using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

/// <summary>Insumos de empaque con kardex valorizado a promedio ponderado.</summary>
public sealed class InsumoService
{
    private readonly ConexionFactory _db;
    public InsumoService(ConexionFactory db) => _db = db;

    public async Task<IReadOnlyList<Insumo>> ListarAsync(bool soloActivos = false)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<Insumo>(
            "SELECT * FROM dbo.Insumos " + (soloActivos ? "WHERE Activo = 1 " : "") + "ORDER BY Nombre;")).AsList();
    }

    public async Task<IReadOnlyList<MovimientoInsumo>> KardexAsync(int idInsumo, int maximo = 500)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<MovimientoInsumo>(@"
SELECT TOP (@maximo) * FROM dbo.MovimientosInsumo WHERE IdInsumo = @idInsumo
ORDER BY Fecha DESC, IdMovimiento DESC;", new { idInsumo, maximo })).AsList();
    }

    /// <summary>Crea o actualiza los datos maestros de un insumo (no toca saldos).</summary>
    public async Task<int> GuardarAsync(Insumo i)
    {
        Validar.Texto(i.Nombre, "Nombre");
        Validar.Texto(i.Unidad, "Unidad");
        Validar.NoNegativo(i.StockMinimo, "Stock mínimo");
        Validar.NoNegativo(i.UltimoCosto, "Costo de referencia");

        return await _db.EnTransaccionAsync(async (cn, tx) =>
        {
            if (i.IdInsumo == 0)
            {
                var codigo = await cn.ExecuteScalarAsync<string>(@"
SELECT 'INS-' + RIGHT('000' + CAST(ISNULL(MAX(IdInsumo), 0) + 1 AS varchar(10)), 3) FROM dbo.Insumos WITH (TABLOCKX);", transaction: tx);
                return await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.Insumos (Codigo, Nombre, Unidad, UltimoCosto, StockMinimo, Activo)
VALUES (@codigo, @Nombre, @Unidad, @UltimoCosto, @StockMinimo, @Activo);
SELECT CAST(SCOPE_IDENTITY() AS int);",
                    new { codigo, Nombre = i.Nombre.Trim(), i.Unidad, i.UltimoCosto, i.StockMinimo, i.Activo }, tx);
            }

            await cn.ExecuteAsync(@"
UPDATE dbo.Insumos SET Nombre = @Nombre, Unidad = @Unidad, StockMinimo = @StockMinimo, Activo = @Activo,
       UltimoCosto = CASE WHEN Stock = 0 THEN @UltimoCosto ELSE UltimoCosto END
WHERE IdInsumo = @IdInsumo;", new { i.IdInsumo, Nombre = i.Nombre.Trim(), i.Unidad, i.StockMinimo, i.Activo, i.UltimoCosto }, tx);
            return i.IdInsumo;
        });
    }

    /// <summary>Compra de insumos: aumenta stock y recalcula el costo promedio ponderado.</summary>
    public Task RegistrarCompraAsync(CompraInsumo c) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        var ins = await cn.QuerySingleOrDefaultAsync<Insumo>(
            "SELECT * FROM dbo.Insumos WITH (UPDLOCK, ROWLOCK) WHERE IdInsumo = @id", new { id = c.IdInsumo }, tx)
            ?? throw new ReglaNegocioException("Seleccione el insumo comprado.");

        var cantidad = Redondeo.Kilos(c.Cantidad);
        var valor = Redondeo.Dinero(c.ValorTotal);
        var (stock, saldoValor, _) = CalculadoraCostos.EntradaInsumo(ins.Stock, ins.ValorStock, cantidad, valor);
        var costoUnit = Redondeo.CostoUnitario(valor / cantidad);

        await cn.ExecuteAsync(@"
UPDATE dbo.Insumos SET Stock = @stock, ValorStock = @saldoValor, UltimoCosto = @costoUnit WHERE IdInsumo = @id;
INSERT INTO dbo.MovimientosInsumo
 (IdInsumo, Fecha, Tipo, Cantidad, CostoUnitario, ValorTotal, SaldoCantidad, SaldoValor, Documento, Detalle)
VALUES (@id, @fecha, 'COMPRA', @cantidad, @costoUnit, @valor, @stock, @saldoValor, @doc, @detalle);",
            new
            {
                id = ins.IdInsumo, stock, saldoValor, costoUnit, cantidad, valor,
                fecha = c.Fecha.Date.Add(DateTime.Now.TimeOfDay), doc = c.Documento,
                detalle = string.IsNullOrWhiteSpace(c.Proveedor) ? "Compra" : $"Compra a {c.Proveedor}"
            }, tx);
        return true;
    });

    /// <summary>Ajuste por conteo físico (faltantes o sobrantes).</summary>
    public Task AjustarConteoAsync(int idInsumo, decimal conteoFisico, string motivo) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        Validar.Texto(motivo, "Motivo del ajuste");
        var ins = await cn.QuerySingleAsync<Insumo>(
            "SELECT * FROM dbo.Insumos WITH (UPDLOCK, ROWLOCK) WHERE IdInsumo = @idInsumo", new { idInsumo }, tx);
        var (difCant, difValor) = CalculadoraCostos.AjusteConteo(ins.Stock, ins.ValorStock, conteoFisico, ins.UltimoCosto);
        Validar.Que(difCant != 0, "El conteo coincide con el sistema; no hay nada que ajustar.");

        var stock = ins.Stock + difCant;
        var saldo = ins.ValorStock + difValor;
        await cn.ExecuteAsync(@"
UPDATE dbo.Insumos SET Stock = @stock, ValorStock = @saldo WHERE IdInsumo = @idInsumo;
INSERT INTO dbo.MovimientosInsumo
 (IdInsumo, Fecha, Tipo, Cantidad, CostoUnitario, ValorTotal, SaldoCantidad, SaldoValor, Documento, Detalle)
VALUES (@idInsumo, SYSDATETIME(), 'AJUSTE', @difCant, @cu, @difValor, @stock, @saldo, 'CONTEO', @motivo);",
            new { idInsumo, stock, saldo, difCant, difValor, cu = CalculadoraCostos.CostoPromedio(Math.Abs(difCant), Math.Abs(difValor)), motivo }, tx);
        return true;
    });
}
