namespace CafeCostos.Core.Costeo;

/// <summary>
/// Motor de costeo por procesos con absorción total y trazabilidad por lote.
///
/// PRINCIPIOS CONTABLES APLICADOS
/// 1. Costeo por absorción: el producto terminado absorbe materia prima (café),
///    materiales directos (insumos de empaque), mano de obra directa y CIF.
/// 2. Mermas normales (evaporación en tostión y pérdida en molienda) NO se llevan a gasto:
///    se absorben en las unidades buenas, aumentando su costo unitario.
/// 3. Inventarios valorizados por "valor disponible" (no por costo unitario × cantidad).
///    Cada salida se valora proporcionalmente y la ÚLTIMA salida se lleva exactamente el saldo,
///    de modo que nunca quedan residuos de centavos en el inventario (cuadre perfecto).
/// 4. Insumos de empaque al costo promedio ponderado.
/// </summary>
public static class CalculadoraCostos
{
    /// <summary>Rango normal de merma en tostión (%). Fuera de él se emite advertencia, no error.</summary>
    public const decimal MermaTuesteMinimaNormal = 12m;
    public const decimal MermaTuesteMaximaNormal = 22m;
    /// <summary>Merma de molienda/empaque por encima de la cual se advierte (%).</summary>
    public const decimal MermaMoliendaMaximaNormal = 3m;

    // ------------------------------------------------------------------------------------
    // 1. VALORACIÓN DE SALIDAS DE INVENTARIO
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Valor de una salida de inventario valorado por saldo (promedio ponderado implícito).
    /// Si la salida agota la existencia, devuelve exactamente el valor disponible.
    /// </summary>
    public static decimal CostoSalida(decimal cantidadDisponible, decimal valorDisponible, decimal cantidadSalida)
    {
        Validar.Positivo(cantidadSalida, "Cantidad a retirar");
        Validar.Que(cantidadSalida <= cantidadDisponible,
            $"No hay existencias suficientes: disponible {cantidadDisponible:N3}, solicitado {cantidadSalida:N3}.");
        Validar.NoNegativo(valorDisponible, "Valor disponible");

        if (cantidadSalida == cantidadDisponible) return valorDisponible;
        return Redondeo.Dinero(valorDisponible * cantidadSalida / cantidadDisponible);
    }

    /// <summary>Costo unitario implícito de una existencia (valor / cantidad).</summary>
    public static decimal CostoPromedio(decimal cantidad, decimal valor) =>
        cantidad <= 0 ? 0m : Redondeo.CostoUnitario(valor / cantidad);

    // ------------------------------------------------------------------------------------
    // 2. TOSTIÓN: ABSORCIÓN DE MERMA POR EVAPORACIÓN
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Costea una orden de tostión.
    ///   Merma (kg)       = Kg secos − Kg tostados
    ///   % Merma          = Merma / Kg secos × 100
    ///   Costo total      = Σ costo de los lotes secos + costos de conversión
    ///   Costo kg tostado = Costo total / Kg tostados      ← aquí se absorbe la merma
    /// </summary>
    public static ResultadoTueste CalcularTueste(
        IReadOnlyCollection<ConsumoMateriaPrima> consumos,
        decimal kgTostados,
        CostosConversion conversion)
    {
        Validar.Que(consumos.Count > 0, "Debe seleccionar al menos un lote de café seco.");
        foreach (var c in consumos)
        {
            Validar.Positivo(c.Kilos, "Kilos del lote");
            Validar.NoNegativo(c.Costo, "Costo del lote");
        }
        Validar.Positivo(kgTostados, "Kilos tostados obtenidos");
        Validar.NoNegativo(conversion.Energia, "Energía / gas");
        Validar.NoNegativo(conversion.ManoObra, "Mano de obra");
        Validar.NoNegativo(conversion.Otros, "Otros costos");

        var kgSecos = Redondeo.Kilos(consumos.Sum(c => c.Kilos));
        kgTostados = Redondeo.Kilos(kgTostados);
        Validar.Que(kgTostados < kgSecos,
            $"Los kilos tostados ({kgTostados:N3}) deben ser menores que los kilos secos ({kgSecos:N3}): el tueste siempre pierde peso.");

        var costoMp = Redondeo.Dinero(consumos.Sum(c => c.Costo));
        var costoConv = Redondeo.Dinero(conversion.Total);
        var costoTotal = costoMp + costoConv;

        var kgMerma = kgSecos - kgTostados;
        var pctMerma = Redondeo.Porcentaje(kgMerma / kgSecos * 100m);
        var costoKgSeco = Redondeo.CostoUnitario(costoMp / kgSecos);

        var advertencias = new List<string>();
        if (pctMerma < MermaTuesteMinimaNormal)
            advertencias.Add($"La merma ({pctMerma:N2} %) es inferior a lo normal ({MermaTuesteMinimaNormal} %). Verifique la pesada o si el café quedó sub-tostado.");
        if (pctMerma > MermaTuesteMaximaNormal)
            advertencias.Add($"La merma ({pctMerma:N2} %) es superior a lo normal ({MermaTuesteMaximaNormal} %). Verifique la pesada o el punto de tueste.");

        return new ResultadoTueste
        {
            KgSecos = kgSecos,
            KgTostados = kgTostados,
            KgMerma = kgMerma,
            PctMerma = pctMerma,
            Rendimiento = Math.Round(kgTostados / kgSecos, 4, MidpointRounding.AwayFromZero),
            FactorConversion = Math.Round(kgSecos / kgTostados, 4, MidpointRounding.AwayFromZero),
            CostoMateriaPrima = costoMp,
            CostoConversion = costoConv,
            CostoTotal = costoTotal,
            CostoKgSecoPromedio = costoKgSeco,
            CostoKgTostado = Redondeo.CostoUnitario(costoTotal / kgTostados),
            CostoMermaAbsorbida = Redondeo.Dinero(costoMp * kgMerma / kgSecos),
            Advertencias = advertencias
        };
    }

    // ------------------------------------------------------------------------------------
    // 3. MOLIENDA, FRACCIONAMIENTO Y EMPAQUE: COSTO DE LA BOLSA FINAL
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Costea un lote de producto terminado.
    ///   Kg netos          = Unidades × gramos / 1000
    ///   Merma molienda    = Kg tostado usado − Kg netos   (absorbida)
    ///   Costo café        = valor de los kg tostados retirados
    ///   Costo insumos     = Σ cantidad × costo promedio (bolsa, válvula, etiqueta…)
    ///   Costo unitario    = (café + insumos + conversión) / unidades
    /// </summary>
    public static ResultadoEmpaque CalcularEmpaque(EntradaEmpaque e)
    {
        Validar.Que(e.GramosNetosPorUnidad > 0, "La presentación debe tener gramos netos mayores que cero.");
        Validar.Que(e.Unidades > 0, "Las unidades producidas deben ser mayores que cero.");
        Validar.Positivo(e.KgTostadoUsado, "Kilos de café tostado usados");
        Validar.NoNegativo(e.CostoCafeTostado, "Costo del café tostado");
        foreach (var i in e.Insumos)
        {
            Validar.NoNegativo(i.Cantidad, $"Cantidad de {i.Nombre}");
            Validar.NoNegativo(i.Costo, $"Costo de {i.Nombre}");
        }
        Validar.NoNegativo(e.Conversion.Total, "Costos de conversión");

        var kgNetos = Redondeo.Kilos(e.Unidades * e.GramosNetosPorUnidad / 1000m);
        var kgUsado = Redondeo.Kilos(e.KgTostadoUsado);
        Validar.Que(kgUsado >= kgNetos,
            $"Para {e.Unidades} unidades de {e.GramosNetosPorUnidad} g se necesitan al menos {kgNetos:N3} kg; solo se registraron {kgUsado:N3} kg.");

        var kgMerma = kgUsado - kgNetos;
        var pctMerma = Redondeo.Porcentaje(kgMerma / kgUsado * 100m);

        var costoCafe = Redondeo.Dinero(e.CostoCafeTostado);
        var costoInsumos = Redondeo.Dinero(e.Insumos.Sum(i => i.Costo));
        var costoConv = Redondeo.Dinero(e.Conversion.Total);
        var total = costoCafe + costoInsumos + costoConv;

        var adv = new List<string>();
        if (pctMerma > MermaMoliendaMaximaNormal)
            adv.Add($"La merma de molienda/empaque ({pctMerma:N2} %) supera el {MermaMoliendaMaximaNormal} % habitual.");

        return new ResultadoEmpaque
        {
            Unidades = e.Unidades,
            KgNetos = kgNetos,
            KgTostadoUsado = kgUsado,
            KgMermaMolienda = kgMerma,
            PctMermaMolienda = pctMerma,
            CostoCafe = costoCafe,
            CostoInsumos = costoInsumos,
            CostoConversion = costoConv,
            CostoTotal = total,
            CostoUnitario = Redondeo.CostoUnitario(total / e.Unidades),
            CostoCafePorUnidad = Redondeo.CostoUnitario(costoCafe / e.Unidades),
            CostoInsumosPorUnidad = Redondeo.CostoUnitario(costoInsumos / e.Unidades),
            CostoConversionPorUnidad = Redondeo.CostoUnitario(costoConv / e.Unidades),
            Advertencias = adv
        };
    }

    /// <summary>Cantidad de un insumo requerida para producir N unidades según la receta.</summary>
    public static decimal CantidadInsumoRequerida(decimal cantidadPorUnidad, int unidades) =>
        Redondeo.Kilos(cantidadPorUnidad * unidades);

    /// <summary>Kilos de café tostado sugeridos para producir N unidades con una merma esperada.</summary>
    public static decimal KgTostadoSugerido(int gramosNetos, int unidades, decimal pctMermaEsperada)
    {
        Validar.Que(pctMermaEsperada is >= 0 and < 100, "La merma esperada debe estar entre 0 y 100 %.");
        var neto = unidades * gramosNetos / 1000m;
        return Redondeo.Kilos(neto / (1 - pctMermaEsperada / 100m));
    }

    // ------------------------------------------------------------------------------------
    // 4. SIMULADOR DE PRECIO POR PRESENTACIÓN
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Estima el costo de una bolsa a partir del costo actual del kilo tostado,
    /// la merma de molienda esperada, la receta de insumos y la conversión por unidad.
    /// </summary>
    public static EstimacionBolsa EstimarBolsa(
        decimal costoKgTostado, int gramosNetos, decimal pctMermaMolienda,
        IEnumerable<LineaReceta> receta, decimal conversionPorUnidad,
        decimal precioVenta, decimal margenObjetivoPct)
    {
        Validar.NoNegativo(costoKgTostado, "Costo kg tostado");
        Validar.Que(gramosNetos > 0, "Los gramos netos deben ser mayores que cero.");
        Validar.Que(margenObjetivoPct is >= 0 and < 100, "El margen objetivo debe estar entre 0 y 99 %.");

        var kgPorBolsa = gramosNetos / 1000m / (1 - pctMermaMolienda / 100m);
        var cafe = Redondeo.CostoUnitario(kgPorBolsa * costoKgTostado);
        var insumos = Redondeo.CostoUnitario(receta.Sum(r => r.CantidadPorUnidad * r.CostoPromedio));
        var conv = Redondeo.CostoUnitario(conversionPorUnidad);
        var total = cafe + insumos + conv;
        var utilidad = precioVenta - total;

        return new EstimacionBolsa
        {
            CostoCafe = cafe,
            CostoInsumos = insumos,
            CostoConversion = conv,
            CostoTotal = total,
            PrecioVenta = precioVenta,
            UtilidadBruta = Redondeo.Dinero(utilidad),
            MargenPct = precioVenta > 0 ? Redondeo.Porcentaje(utilidad / precioVenta * 100m) : 0m,
            PrecioSugerido = RedondearPrecio(total / (1 - margenObjetivoPct / 100m))
        };
    }

    /// <summary>Redondea un precio hacia arriba al múltiplo de 100 pesos (práctica comercial en COP).</summary>
    public static decimal RedondearPrecio(decimal precio, decimal multiplo = 100m) =>
        precio <= 0 ? 0 : Math.Ceiling(precio / multiplo) * multiplo;

    // ------------------------------------------------------------------------------------
    // 5. INSUMOS: PROMEDIO PONDERADO
    // ------------------------------------------------------------------------------------

    /// <summary>Nuevo saldo de un insumo tras una compra (promedio ponderado).</summary>
    public static (decimal Stock, decimal Valor, decimal CostoPromedio) EntradaInsumo(
        decimal stockActual, decimal valorActual, decimal cantidad, decimal valorCompra)
    {
        Validar.Positivo(cantidad, "Cantidad comprada");
        Validar.NoNegativo(valorCompra, "Valor de la compra");
        var stock = Redondeo.Kilos(stockActual + cantidad);
        var valor = Redondeo.Dinero(valorActual + valorCompra);
        return (stock, valor, CostoPromedio(stock, valor));
    }

    /// <summary>
    /// Ajuste por conteo físico. Devuelve la diferencia de cantidad y su valor (con signo).
    /// Faltantes se valoran al saldo; sobrantes al costo promedio vigente o, si no hay, al último costo.
    /// </summary>
    public static (decimal DiferenciaCantidad, decimal DiferenciaValor) AjusteConteo(
        decimal stockActual, decimal valorActual, decimal conteoFisico, decimal ultimoCosto)
    {
        Validar.NoNegativo(conteoFisico, "Conteo físico");
        var diferencia = Redondeo.Kilos(conteoFisico - stockActual);
        if (diferencia == 0) return (0, 0);
        if (diferencia < 0)
            return (diferencia, -CostoSalida(stockActual, valorActual, -diferencia));

        var costoRef = stockActual > 0 ? valorActual / stockActual : ultimoCosto;
        return (diferencia, Redondeo.Dinero(diferencia * costoRef));
    }
}
