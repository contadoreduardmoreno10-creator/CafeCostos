namespace CafeCostos.Core.Ventas;

public enum TipoPago { Contado, Credito }

/// <summary>Línea de venta ya valorizada al costo (salida del lote de producto terminado).</summary>
public sealed record LineaVentaCosteada(int IdInventarioEmpacado, int Cantidad, decimal PrecioUnitario, decimal CostoTotal)
{
    public decimal Subtotal => Redondeo.Dinero(Cantidad * PrecioUnitario);
}

public sealed record TotalesVenta(
    decimal Subtotal, decimal Descuento, decimal Total, decimal CostoTotal,
    decimal UtilidadBruta, decimal MargenPct, decimal SaldoInicial);

public static class CalculadoraVenta
{
    public static string Codigo(TipoPago t) => t == TipoPago.Credito ? "CREDITO" : "CONTADO";

    public static TipoPago Desde(string codigo) =>
        string.Equals(codigo, "CREDITO", StringComparison.OrdinalIgnoreCase) ? TipoPago.Credito : TipoPago.Contado;

    /// <summary>Calcula totales, utilidad bruta y saldo inicial de cartera de una factura.</summary>
    public static TotalesVenta Totales(IReadOnlyCollection<LineaVentaCosteada> lineas, decimal descuento, TipoPago tipo)
    {
        Validar.Que(lineas.Count > 0, "La factura debe tener al menos un producto.");
        foreach (var l in lineas)
        {
            Validar.Que(l.Cantidad > 0, "Las cantidades vendidas deben ser mayores que cero.");
            Validar.NoNegativo(l.PrecioUnitario, "Precio unitario");
        }
        Validar.NoNegativo(descuento, "Descuento");

        var subtotal = lineas.Sum(l => l.Subtotal);
        descuento = Redondeo.Dinero(descuento);
        Validar.Que(descuento <= subtotal, "El descuento no puede superar el subtotal de la factura.");

        var total = subtotal - descuento;
        var costo = Redondeo.Dinero(lineas.Sum(l => l.CostoTotal));
        var utilidad = total - costo;
        return new TotalesVenta(
            subtotal, descuento, total, costo, utilidad,
            total > 0 ? Redondeo.Porcentaje(utilidad / total * 100m) : 0m,
            tipo == TipoPago.Credito ? total : 0m);
    }

    /// <summary>
    /// Valida el otorgamiento de crédito.
    /// cupo = null significa "sin límite"; cupo = 0 significa "cliente sin crédito".
    /// </summary>
    public static void ValidarCredito(bool clienteGenerico, bool clienteActivo, decimal? cupo,
                                      decimal saldoPendienteActual, decimal totalNuevo, int facturasVencidas)
    {
        Validar.Que(clienteActivo, "El cliente está inactivo.");
        Validar.Que(!clienteGenerico, "No se puede vender a crédito al «Consumidor final». Seleccione o cree el cliente.");
        if (cupo is null) return;
        Validar.Que(cupo > 0, "Este cliente no tiene cupo de crédito asignado.");
        var nuevoSaldo = saldoPendienteActual + totalNuevo;
        Validar.Que(nuevoSaldo <= cupo,
            $"La venta supera el cupo de crédito. Cupo: {cupo:C0} · Saldo actual: {saldoPendienteActual:C0} · Disponible: {Math.Max(0, cupo.Value - saldoPendienteActual):C0}.");
        _ = facturasVencidas; // Se informa en la UI como advertencia; no bloquea.
    }

    /// <summary>Agrupa líneas repetidas del mismo lote para que la factura tenga una sola línea por lote.</summary>
    public static IReadOnlyList<(int IdInventarioEmpacado, int Cantidad, decimal PrecioUnitario)> Consolidar(
        IEnumerable<(int IdInventarioEmpacado, int Cantidad, decimal PrecioUnitario)> lineas)
    {
        var lista = lineas.ToList();
        foreach (var g in lista.GroupBy(l => l.IdInventarioEmpacado))
            Validar.Que(g.Select(x => x.PrecioUnitario).Distinct().Count() == 1,
                "El mismo lote aparece con precios distintos en la factura. Unifique el precio.");
        return lista.GroupBy(l => l.IdInventarioEmpacado)
                    .Select(g => (g.Key, g.Sum(x => x.Cantidad), g.First().PrecioUnitario))
                    .ToList();
    }
}
