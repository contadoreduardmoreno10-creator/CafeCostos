using Dapper;
using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Servicios;

/// <summary>Numeración consecutiva sin huecos ni duplicados (se toma dentro de la transacción).</summary>
internal static class Consecutivos
{
    public const string Lote = "LOTE";
    public const string Tueste = "TUESTE";
    public const string Empaque = "EMPAQUE";
    public const string Factura = "FACTURA";
    public const string Recibo = "RECIBO";

    public static async Task<string> SiguienteAsync(SqlConnection cn, SqlTransaction tx, string tipo)
    {
        var (prefijo, numero) = await cn.QuerySingleAsync<(string, int)>(@"
UPDATE dbo.Consecutivos WITH (ROWLOCK)
   SET Siguiente = Siguiente + 1
OUTPUT deleted.Prefijo, deleted.Siguiente
 WHERE Tipo = @tipo;", new { tipo }, tx);
        return $"{prefijo}{numero:D6}";
    }
}
