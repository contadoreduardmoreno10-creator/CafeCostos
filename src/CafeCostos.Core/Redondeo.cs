namespace CafeCostos.Core;

/// <summary>
/// Política única de redondeo del sistema.
/// Se usa MidpointRounding.AwayFromZero porque es el mismo criterio que aplica SQL Server
/// al convertir decimal a menor escala; así C# y la base de datos nunca difieren en un centavo.
/// </summary>
public static class Redondeo
{
    /// <summary>Valores monetarios (pesos con 2 decimales).</summary>
    public static decimal Dinero(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    /// <summary>Costos unitarios (por kilo, por unidad) con 4 decimales.</summary>
    public static decimal CostoUnitario(decimal valor) => Math.Round(valor, 4, MidpointRounding.AwayFromZero);

    /// <summary>Pesos en kilogramos con 3 decimales (gramo).</summary>
    public static decimal Kilos(decimal valor) => Math.Round(valor, 3, MidpointRounding.AwayFromZero);

    /// <summary>Porcentajes expresados de 0 a 100 con 3 decimales.</summary>
    public static decimal Porcentaje(decimal valor) => Math.Round(valor, 3, MidpointRounding.AwayFromZero);
}
