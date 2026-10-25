using CafeCostos.Core;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

public sealed class ClienteService
{
    private readonly ConexionFactory _db;
    public ClienteService(ConexionFactory db) => _db = db;

    public async Task<IReadOnlyList<Cliente>> ListarAsync(string? filtro = null, bool soloActivos = false)
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<Cliente>(@"
SELECT c.*,
       ISNULL(s.Saldo, 0)   AS SaldoPendiente,
       ISNULL(s.Vencido, 0) AS SaldoVencido
FROM dbo.Clientes c
OUTER APPLY (SELECT SUM(v.Saldo) AS Saldo,
                    SUM(CASE WHEN v.FechaVencimiento < CAST(GETDATE() AS date) THEN v.Saldo ELSE 0 END) AS Vencido
             FROM dbo.Ventas v WHERE v.IdCliente = c.IdCliente AND v.Estado = 'PENDIENTE') s
WHERE (@filtro IS NULL OR c.Nombre LIKE '%' + @filtro + '%' OR c.Documento LIKE @filtro + '%')
  AND (@soloActivos = 0 OR c.Activo = 1)
ORDER BY c.EsGenerico DESC, c.Nombre;",
            new { filtro = string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim(), soloActivos })).AsList();
    }

    public async Task<int> GuardarAsync(Cliente c)
    {
        Validar.Texto(c.Nombre, "Nombre del cliente");
        Validar.Que(c.DiasCredito is >= 0 and <= 365, "Los días de crédito deben estar entre 0 y 365.");
        Validar.Que(c.CupoCredito is null or >= 0, "El cupo de crédito no puede ser negativo.");

        await using var cn = await _db.AbrirAsync();
        var doc = string.IsNullOrWhiteSpace(c.Documento) ? null : c.Documento.Trim();
        if (doc is not null)
        {
            var otro = await cn.ExecuteScalarAsync<string?>(
                "SELECT Nombre FROM dbo.Clientes WHERE Documento = @doc AND IdCliente <> @IdCliente",
                new { doc, c.IdCliente });
            Validar.Que(otro is null, $"Ya existe un cliente con ese documento: {otro}.");
        }

        var p = new
        {
            c.IdCliente, Documento = doc, Nombre = c.Nombre.Trim(), c.Telefono, c.Direccion, c.Ciudad, c.Email,
            c.CupoCredito, c.DiasCredito, c.Activo
        };
        if (c.IdCliente == 0)
            return await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.Clientes (Documento, Nombre, Telefono, Direccion, Ciudad, Email, CupoCredito, DiasCredito, Activo)
VALUES (@Documento, @Nombre, @Telefono, @Direccion, @Ciudad, @Email, @CupoCredito, @DiasCredito, @Activo);
SELECT CAST(SCOPE_IDENTITY() AS int);", p);

        Validar.Que(!c.EsGenerico || c.Activo, "El cliente «Consumidor final» no se puede desactivar.");
        await cn.ExecuteAsync(@"
UPDATE dbo.Clientes SET Documento = @Documento, Nombre = @Nombre, Telefono = @Telefono, Direccion = @Direccion,
       Ciudad = @Ciudad, Email = @Email, CupoCredito = @CupoCredito, DiasCredito = @DiasCredito, Activo = @Activo
WHERE IdCliente = @IdCliente AND EsGenerico = 0;

UPDATE dbo.Clientes SET Nombre = @Nombre, Telefono = @Telefono, Direccion = @Direccion, Ciudad = @Ciudad, Email = @Email
WHERE IdCliente = @IdCliente AND EsGenerico = 1;", p);
        return c.IdCliente;
    }
}
