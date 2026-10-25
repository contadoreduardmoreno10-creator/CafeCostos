namespace CafeCostos.Core.Costeo;

/// <summary>
/// Costos de conversión de una etapa productiva (Mano de obra directa + CIF).
/// Se registran como valores TOTALES de la orden.
/// </summary>
public sealed record CostosConversion(decimal Energia, decimal ManoObra, decimal Otros)
{
    public static readonly CostosConversion Ninguno = new(0, 0, 0);
    public decimal Total => Energia + ManoObra + Otros;
}

/// <summary>Kilos de un lote de café seco que entran a una orden de tueste, ya valorizados.</summary>
public sealed record ConsumoMateriaPrima(int IdLote, decimal Kilos, decimal Costo);

/// <summary>Resultado completo del costeo de una orden de tostión.</summary>
public sealed record ResultadoTueste
{
    public decimal KgSecos { get; init; }
    public decimal KgTostados { get; init; }
    public decimal KgMerma { get; init; }
    /// <summary>Merma por evaporación en porcentaje (0-100).</summary>
    public decimal PctMerma { get; init; }
    /// <summary>Rendimiento: kilos tostados obtenidos por kilo seco (0-1).</summary>
    public decimal Rendimiento { get; init; }
    /// <summary>Kilos secos necesarios para obtener 1 kg tostado.</summary>
    public decimal FactorConversion { get; init; }

    public decimal CostoMateriaPrima { get; init; }
    public decimal CostoConversion { get; init; }
    public decimal CostoTotal { get; init; }

    /// <summary>Costo promedio ponderado del kilo seco que entró (mezcla de lotes).</summary>
    public decimal CostoKgSecoPromedio { get; init; }
    /// <summary>Costo del kilo tostado: absorbe merma + conversión.</summary>
    public decimal CostoKgTostado { get; init; }
    /// <summary>Valor del café que se evaporó y que fue absorbido por los kilos tostados.</summary>
    public decimal CostoMermaAbsorbida { get; init; }

    public IReadOnlyList<string> Advertencias { get; init; } = Array.Empty<string>();
}

/// <summary>Insumo de empaque consumido en una orden, ya valorizado al costo promedio.</summary>
public sealed record ConsumoInsumo(int IdInsumo, string Nombre, decimal Cantidad, decimal Costo);

/// <summary>Datos de entrada para costear una orden de molienda/fraccionamiento/empaque.</summary>
public sealed record EntradaEmpaque
{
    public required int GramosNetosPorUnidad { get; init; }
    public required int Unidades { get; init; }
    /// <summary>Kilos de café tostado retirados de la orden de tueste (incluye merma de molienda).</summary>
    public required decimal KgTostadoUsado { get; init; }
    /// <summary>Valor de los kilos tostados retirados (ya calculado con <see cref="CalculadoraCostos.CostoSalida"/>).</summary>
    public required decimal CostoCafeTostado { get; init; }
    public IReadOnlyList<ConsumoInsumo> Insumos { get; init; } = Array.Empty<ConsumoInsumo>();
    public CostosConversion Conversion { get; init; } = CostosConversion.Ninguno;
}

/// <summary>Resultado del costeo de una orden de empaque (lote de producto terminado).</summary>
public sealed record ResultadoEmpaque
{
    public int Unidades { get; init; }
    public decimal KgNetos { get; init; }
    public decimal KgTostadoUsado { get; init; }
    public decimal KgMermaMolienda { get; init; }
    public decimal PctMermaMolienda { get; init; }

    public decimal CostoCafe { get; init; }
    public decimal CostoInsumos { get; init; }
    public decimal CostoConversion { get; init; }
    public decimal CostoTotal { get; init; }

    public decimal CostoUnitario { get; init; }
    public decimal CostoCafePorUnidad { get; init; }
    public decimal CostoInsumosPorUnidad { get; init; }
    public decimal CostoConversionPorUnidad { get; init; }

    public IReadOnlyList<string> Advertencias { get; init; } = Array.Empty<string>();
}

/// <summary>Línea de la receta (lista de materiales) de una presentación.</summary>
public sealed record LineaReceta(int IdInsumo, string Nombre, decimal CantidadPorUnidad, decimal CostoPromedio);

/// <summary>Estimación del costo de una bolsa antes de producir (para fijar precios).</summary>
public sealed record EstimacionBolsa
{
    public decimal CostoCafe { get; init; }
    public decimal CostoInsumos { get; init; }
    public decimal CostoConversion { get; init; }
    public decimal CostoTotal { get; init; }
    public decimal PrecioVenta { get; init; }
    public decimal UtilidadBruta { get; init; }
    /// <summary>Margen bruto sobre precio de venta (0-100).</summary>
    public decimal MargenPct { get; init; }
    /// <summary>Precio mínimo para lograr el margen objetivo.</summary>
    public decimal PrecioSugerido { get; init; }
}
