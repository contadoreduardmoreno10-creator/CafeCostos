using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

/// <summary>Presentaciones comerciales (250 g, 500 g…), su receta de insumos y simulador de precio.</summary>
public sealed class PresentacionService
{
    private readonly ConexionFactory _db;
    public PresentacionService(ConexionFactory db) => _db = db;

    public async Task<IReadOnlyList<Presentacion>> ListarAsync(bool soloActivas = false)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<Presentacion>(
            "SELECT * FROM dbo.Presentaciones " + (soloActivas ? "WHERE Activo = 1 " : "") +
            "ORDER BY GramosNetos, TipoMolienda;")).AsList();
    }

    public async Task<IReadOnlyList<RecetaInsumo>> RecetaAsync(int idPresentacion)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<RecetaInsumo>(@"
SELECT r.IdPresentacion, r.IdInsumo, i.Nombre AS NombreInsumo, i.Unidad, r.CantidadPorUnidad,
       CASE WHEN i.Stock > 0 THEN i.CostoPromedio ELSE i.UltimoCosto END AS CostoPromedio, i.Stock
FROM dbo.PresentacionInsumos r JOIN dbo.Insumos i ON i.IdInsumo = r.IdInsumo
WHERE r.IdPresentacion = @idPresentacion ORDER BY i.Nombre;", new { idPresentacion })).AsList();
    }

    /// <summary>Guarda la presentación y reemplaza completa su receta, en una transacción.</summary>
    public async Task<int> GuardarAsync(Presentacion p, IEnumerable<(int IdInsumo, decimal CantidadPorUnidad)> receta)
    {
        Validar.Texto(p.Nombre, "Nombre");
        Validar.Que(p.GramosNetos > 0, "Los gramos netos deben ser mayores que cero.");
        Validar.Que(p.TipoMolienda is "GRANO" or "MOLIDO", "Indique si es en grano o molido.");
        Validar.NoNegativo(p.PrecioVenta, "Precio de venta");
        var lineas = receta.Where(r => r.CantidadPorUnidad > 0).ToList();
        Validar.Que(lineas.Select(l => l.IdInsumo).Distinct().Count() == lineas.Count, "Un insumo aparece dos veces en la receta.");

        return await _db.EnTransaccionAsync(async (cn, tx) =>
        {
            var parametros = new
            {
                p.IdPresentacion, Nombre = p.Nombre.Trim(), p.GramosNetos, p.TipoMolienda,
                GradoMolienda = p.TipoMolienda == "MOLIDO" ? p.GradoMolienda : null, p.PrecioVenta, p.Activo
            };
            var id = p.IdPresentacion;
            if (id == 0)
                id = await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.Presentaciones (Nombre, GramosNetos, TipoMolienda, GradoMolienda, PrecioVenta, Activo)
VALUES (@Nombre, @GramosNetos, @TipoMolienda, @GradoMolienda, @PrecioVenta, @Activo);
SELECT CAST(SCOPE_IDENTITY() AS int);", parametros, tx);
            else
                await cn.ExecuteAsync(@"
UPDATE dbo.Presentaciones SET Nombre = @Nombre, GramosNetos = @GramosNetos, TipoMolienda = @TipoMolienda,
       GradoMolienda = @GradoMolienda, PrecioVenta = @PrecioVenta, Activo = @Activo
WHERE IdPresentacion = @IdPresentacion;", parametros, tx);

            await cn.ExecuteAsync("DELETE FROM dbo.PresentacionInsumos WHERE IdPresentacion = @id", new { id }, tx);
            foreach (var l in lineas)
                await cn.ExecuteAsync(
                    "INSERT INTO dbo.PresentacionInsumos (IdPresentacion, IdInsumo, CantidadPorUnidad) VALUES (@id, @IdInsumo, @CantidadPorUnidad)",
                    new { id, l.IdInsumo, l.CantidadPorUnidad }, tx);
            return id;
        });
    }

    /// <summary>
    /// Costo de referencia del kilo tostado para el simulador: promedio ponderado del café tostado
    /// disponible; si no hay existencias, el de la última tostión.
    /// </summary>
    public async Task<decimal> CostoKgTostadoReferenciaAsync()
    {
        await using var cn = await _db.AbrirAsync();
        return await cn.ExecuteScalarAsync<decimal?>(@"
SELECT CASE WHEN SUM(KgDisponibles) > 0 THEN SUM(ValorDisponible) / SUM(KgDisponibles) END
FROM dbo.OrdenesTueste WHERE Estado = 'ACTIVO';") is { } prom
            ? Redondeo.CostoUnitario(prom)
            : await cn.ExecuteScalarAsync<decimal?>(
                "SELECT TOP (1) CostoKgTostado FROM dbo.OrdenesTueste WHERE Estado <> 'ANULADO' ORDER BY Fecha DESC, IdOrdenTueste DESC") ?? 0m;
    }

    public async Task<EstimacionBolsa> EstimarAsync(Presentacion p, decimal costoKgTostado, decimal pctMerma,
                                                    decimal conversionPorUnidad, decimal margenObjetivo)
    {
        var receta = await RecetaAsync(p.IdPresentacion);
        return CalculadoraCostos.EstimarBolsa(costoKgTostado, p.GramosNetos, pctMerma,
            receta.Select(r => new LineaReceta(r.IdInsumo, r.NombreInsumo, r.CantidadPorUnidad, r.CostoPromedio)),
            conversionPorUnidad, p.PrecioVenta, margenObjetivo);
    }
}
