namespace CafeCostos.Core.Cartera;

public enum RangoEdad { PorVencer, De1a30, De31a60, De61a90, MasDe90 }

public sealed record ResumenEdades(
    decimal PorVencer, decimal De1a30, decimal De31a60, decimal De61a90, decimal MasDe90)
{
    public decimal Total => PorVencer + De1a30 + De31a60 + De61a90 + MasDe90;
    public decimal Vencida => De1a30 + De31a60 + De61a90 + MasDe90;
}

public static class CalculadoraCartera
{
    public static DateTime FechaVencimiento(DateTime fechaFactura, int diasCredito)
    {
        Validar.Que(diasCredito >= 0, "Los días de crédito no pueden ser negativos.");
        return fechaFactura.Date.AddDays(diasCredito);
    }

    /// <summary>Días de mora (0 si aún no vence).</summary>
    public static int DiasVencido(DateTime fechaVencimiento, DateTime hoy) =>
        Math.Max(0, (hoy.Date - fechaVencimiento.Date).Days);

    public static RangoEdad Clasificar(DateTime fechaVencimiento, DateTime hoy) =>
        DiasVencido(fechaVencimiento, hoy) switch
        {
            0 => RangoEdad.PorVencer,
            <= 30 => RangoEdad.De1a30,
            <= 60 => RangoEdad.De31a60,
            <= 90 => RangoEdad.De61a90,
            _ => RangoEdad.MasDe90
        };

    public static string Texto(RangoEdad r) => r switch
    {
        RangoEdad.PorVencer => "Por vencer",
        RangoEdad.De1a30 => "1 a 30 días",
        RangoEdad.De31a60 => "31 a 60 días",
        RangoEdad.De61a90 => "61 a 90 días",
        _ => "Más de 90 días"
    };

    public static ResumenEdades Resumir(IEnumerable<(DateTime FechaVencimiento, decimal Saldo)> facturas, DateTime hoy)
    {
        decimal a = 0, b = 0, c = 0, d = 0, e = 0;
        foreach (var (venc, saldo) in facturas)
        {
            switch (Clasificar(venc, hoy))
            {
                case RangoEdad.PorVencer: a += saldo; break;
                case RangoEdad.De1a30: b += saldo; break;
                case RangoEdad.De31a60: c += saldo; break;
                case RangoEdad.De61a90: d += saldo; break;
                default: e += saldo; break;
            }
        }
        return new ResumenEdades(a, b, c, d, e);
    }

    /// <summary>Aplica un abono y devuelve el nuevo saldo y si la factura queda cancelada.</summary>
    public static (decimal NuevoSaldo, bool Cancelada) AplicarAbono(decimal saldoActual, decimal abono)
    {
        abono = Redondeo.Dinero(abono);
        Validar.Positivo(abono, "Valor del abono");
        Validar.Que(saldoActual > 0, "La factura no tiene saldo pendiente.");
        Validar.Que(abono <= saldoActual,
            $"El abono ({abono:C0}) supera el saldo pendiente de la factura ({saldoActual:C0}).");
        var nuevo = saldoActual - abono;
        return (nuevo, nuevo == 0);
    }
}
