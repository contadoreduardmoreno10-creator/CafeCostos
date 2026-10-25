using System.Globalization;
using CafeCostos.Data.Infraestructura;
using Dapper;

namespace CafeCostos.Data.Servicios;

/// <summary>Parámetros de configuración guardados en la base de datos (viajan con los respaldos).</summary>
public sealed class ParametrosService
{
    private readonly ConexionFactory _db;
    public ParametrosService(ConexionFactory db) => _db = db;

    public async Task<Dictionary<string, string>> TodosAsync()
    {
        await using var cn = await _db.AbrirAsync();
        return (await cn.QueryAsync<(string Clave, string? Valor)>("SELECT Clave, Valor FROM dbo.Parametros"))
            .ToDictionary(x => x.Clave, x => x.Valor ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> TextoAsync(string clave, string porDefecto = "")
    {
        await using var cn = await _db.AbrirAsync();
        return await cn.ExecuteScalarAsync<string?>("SELECT Valor FROM dbo.Parametros WHERE Clave = @clave", new { clave })
               ?? porDefecto;
    }

    public async Task<decimal> DecimalAsync(string clave, decimal porDefecto = 0)
    {
        var t = await TextoAsync(clave);
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : porDefecto;
    }

    public async Task<bool> BoolAsync(string clave, bool porDefecto = false)
    {
        var t = await TextoAsync(clave);
        return t.Length == 0 ? porDefecto : t == "1";
    }

    public async Task GuardarAsync(string clave, string? valor)
    {
        await using var cn = await _db.AbrirAsync();
        await cn.ExecuteAsync(@"
UPDATE dbo.Parametros SET Valor = @valor WHERE Clave = @clave;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Parametros (Clave, Valor) VALUES (@clave, @valor);", new { clave, valor });
    }

    public Task GuardarAsync(string clave, decimal valor) =>
        GuardarAsync(clave, valor.ToString(CultureInfo.InvariantCulture));

    public Task GuardarAsync(string clave, bool valor) => GuardarAsync(clave, valor ? "1" : "0");
}
