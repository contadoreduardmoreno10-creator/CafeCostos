using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CafeCostos.Data.Infraestructura;

public sealed class ConexionFactory
{
    private readonly ConfiguracionApp _config;

    static ConexionFactory()
    {
        // Las columnas date de SQL se leen como DateTime; nada adicional que registrar.
        SqlMapper.Settings.CommandTimeout = 120;
    }

    public ConexionFactory(ConfiguracionApp config) => _config = config;

    public ConfiguracionApp Configuracion => _config;

    public async Task<SqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        var cn = new SqlConnection(_config.CadenaBaseDatos);
        await cn.OpenAsync(ct);
        return cn;
    }

    public async Task<SqlConnection> AbrirMasterAsync(CancellationToken ct = default)
    {
        var cn = new SqlConnection(_config.CadenaMaster);
        await cn.OpenAsync(ct);
        return cn;
    }

    /// <summary>
    /// Ejecuta una operación de negocio dentro de una transacción.
    /// Si algo falla, se revierte TODO (inventarios, costos y cartera quedan como estaban).
    /// </summary>
    public async Task<T> EnTransaccionAsync<T>(Func<SqlConnection, SqlTransaction, Task<T>> operacion,
                                               IsolationLevel aislamiento = IsolationLevel.ReadCommitted)
    {
        await using var cn = await AbrirAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(aislamiento);
        try
        {
            var resultado = await operacion(cn, tx);
            await tx.CommitAsync();
            return resultado;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* la conexión pudo haberse cerrado */ }
            throw;
        }
    }
}
