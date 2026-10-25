using CafeCostos.Core;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

/// <summary>Recepción y control de lotes de café seco (materia prima).</summary>
public sealed class LoteService
{
    private readonly ConexionFactory _db;
    public LoteService(ConexionFactory db) => _db = db;

    public async Task<IReadOnlyList<LoteCafeSeco>> ListarAsync(bool soloDisponibles = false)
    {
        await using var cn = await _db.AbrirAsync();
        var sql = @"
SELECT IdLote, Codigo, FechaRecepcion, Proveedor, Origen, Variedad, Humedad, KgRecibidos,
       CostoAdquisicion, CostoFlete, CostoTotal, CostoKg, KgDisponibles, ValorDisponible, Estado, Observaciones
FROM dbo.LotesCafeSeco " +
            (soloDisponibles ? "WHERE Estado = 'ACTIVO' AND KgDisponibles > 0 " : "") +
            "ORDER BY FechaRecepcion DESC, IdLote DESC;";
        return (await cn.QueryAsync<LoteCafeSeco>(sql)).AsList();
    }

    public async Task<string> RegistrarAsync(NuevoLote l)
    {
        Validar.Texto(l.Proveedor, "Proveedor");
        Validar.Positivo(l.KgRecibidos, "Kilos recibidos");
        Validar.NoNegativo(l.CostoAdquisicion, "Costo de adquisición");
        Validar.NoNegativo(l.CostoFlete, "Flete y otros costos");
        Validar.Que(l.Humedad is null or (>= 0 and <= 100), "La humedad debe estar entre 0 y 100 %.");

        var kg = Redondeo.Kilos(l.KgRecibidos);
        var adq = Redondeo.Dinero(l.CostoAdquisicion);
        var flete = Redondeo.Dinero(l.CostoFlete);

        return await _db.EnTransaccionAsync(async (cn, tx) =>
        {
            var codigo = await Consecutivos.SiguienteAsync(cn, tx, Consecutivos.Lote);
            await cn.ExecuteAsync(@"
INSERT INTO dbo.LotesCafeSeco
 (Codigo, FechaRecepcion, Proveedor, Origen, Variedad, Humedad, KgRecibidos, CostoAdquisicion, CostoFlete,
  KgDisponibles, ValorDisponible, Estado, Observaciones)
VALUES
 (@codigo, @FechaRecepcion, @Proveedor, @Origen, @Variedad, @Humedad, @kg, @adq, @flete,
  @kg, @valor, 'ACTIVO', @Observaciones);",
                new
                {
                    codigo, l.FechaRecepcion, Proveedor = l.Proveedor.Trim(), l.Origen, l.Variedad, l.Humedad,
                    kg, adq, flete, valor = adq + flete, l.Observaciones
                }, tx);
            return codigo;
        });
    }

    /// <summary>Anula un lote que todavía no se ha usado en ninguna tostión.</summary>
    public Task AnularAsync(int idLote) => _db.EnTransaccionAsync(async (cn, tx) =>
    {
        var lote = await cn.QuerySingleOrDefaultAsync<LoteCafeSeco>(
            "SELECT * FROM dbo.LotesCafeSeco WITH (UPDLOCK, ROWLOCK) WHERE IdLote = @idLote", new { idLote }, tx)
            ?? throw new ReglaNegocioException("El lote no existe.");

        Validar.Que(lote.Estado == Estados.Activo, "El lote ya está anulado o agotado.");
        Validar.Que(lote.KgDisponibles == lote.KgRecibidos,
            "No se puede anular: parte del lote ya se usó en tostión. Anule primero esas órdenes.");

        await cn.ExecuteAsync(
            "UPDATE dbo.LotesCafeSeco SET Estado = 'ANULADO', KgDisponibles = 0, ValorDisponible = 0 WHERE IdLote = @idLote",
            new { idLote }, tx);
        return true;
    });
}
