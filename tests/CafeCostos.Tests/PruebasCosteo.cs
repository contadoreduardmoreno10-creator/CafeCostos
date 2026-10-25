using CafeCostos.Core;
using CafeCostos.Core.Cartera;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Ventas;
using Xunit;

namespace CafeCostos.Tests;

/// <summary>
/// Caso de referencia usado en la documentación:
///   Lote seco: 100 kg a $2.000.000 ($20.000/kg)
///   Tueste:    100 kg → 82 kg (merma 18 %), energía $60.000, mano de obra $40.000
///   Empaque:   100 bolsas de 250 g usando 25,5 kg tostado, insumos $1.300/und, conversión $30.000
/// </summary>
public class PruebasCosteo
{
    private static ResultadoTueste TuesteReferencia() =>
        CalculadoraCostos.CalcularTueste(
            new[] { new ConsumoMateriaPrima(1, 100m, 2_000_000m) },
            82m,
            new CostosConversion(60_000m, 40_000m, 0m));

    [Fact]
    public void Tueste_calcula_merma_y_absorbe_su_costo()
    {
        var r = TuesteReferencia();
        Assert.Equal(18m, r.KgMerma);
        Assert.Equal(18m, r.PctMerma);
        Assert.Equal(2_100_000m, r.CostoTotal);
        Assert.Equal(25_609.7561m, r.CostoKgTostado);       // 2.100.000 / 82
        Assert.Equal(20_000m, r.CostoKgSecoPromedio);
        Assert.Equal(360_000m, r.CostoMermaAbsorbida);      // 18 kg × $20.000
        Assert.Empty(r.Advertencias);
    }

    [Fact]
    public void Tueste_con_mezcla_de_lotes_promedia_ponderado()
    {
        var r = CalculadoraCostos.CalcularTueste(
            new[]
            {
                new ConsumoMateriaPrima(1, 60m, 1_200_000m),   // $20.000/kg
                new ConsumoMateriaPrima(2, 40m, 1_000_000m),   // $25.000/kg
            },
            84m, CostosConversion.Ninguno);
        Assert.Equal(22_000m, r.CostoKgSecoPromedio);
        Assert.Equal(16m, r.PctMerma);
        Assert.Equal(26_190.4762m, r.CostoKgTostado);       // 2.200.000 / 84
    }

    [Fact]
    public void Tueste_rechaza_kilos_tostados_mayores_o_iguales_a_secos()
    {
        Assert.Throws<ReglaNegocioException>(() =>
            CalculadoraCostos.CalcularTueste(new[] { new ConsumoMateriaPrima(1, 50m, 1m) }, 50m, CostosConversion.Ninguno));
    }

    [Fact]
    public void Tueste_advierte_merma_anormal()
    {
        var r = CalculadoraCostos.CalcularTueste(new[] { new ConsumoMateriaPrima(1, 100m, 1m) }, 70m, CostosConversion.Ninguno);
        Assert.Single(r.Advertencias);
    }

    [Fact]
    public void Salidas_parciales_agotan_el_valor_exacto_sin_residuos()
    {
        decimal kg = 82m, valor = 2_100_000m, acumulado = 0m;
        foreach (var retiro in new[] { 25.5m, 30m, 0.333m, 26.167m })
        {
            var costo = CalculadoraCostos.CostoSalida(kg, valor, retiro);
            acumulado += costo; kg -= retiro; valor -= costo;
        }
        Assert.Equal(0m, kg);
        Assert.Equal(0m, valor);
        Assert.Equal(2_100_000m, acumulado);
    }

    [Fact]
    public void Salida_mayor_a_existencia_se_rechaza()
    {
        Assert.Throws<ReglaNegocioException>(() => CalculadoraCostos.CostoSalida(10m, 100m, 10.001m));
    }

    [Fact]
    public void Empaque_calcula_costo_de_bolsa_con_merma_de_molienda()
    {
        var t = TuesteReferencia();
        var costoCafe = CalculadoraCostos.CostoSalida(t.KgTostados, t.CostoTotal, 25.5m);
        Assert.Equal(653_048.78m, costoCafe);

        var r = CalculadoraCostos.CalcularEmpaque(new EntradaEmpaque
        {
            GramosNetosPorUnidad = 250,
            Unidades = 100,
            KgTostadoUsado = 25.5m,
            CostoCafeTostado = costoCafe,
            Insumos = new[]
            {
                new ConsumoInsumo(1, "Bolsa", 100, 80_000m),
                new ConsumoInsumo(2, "Válvula", 100, 35_000m),
                new ConsumoInsumo(3, "Etiqueta", 100, 15_000m),
            },
            Conversion = new CostosConversion(10_000m, 20_000m, 0m)
        });

        Assert.Equal(25m, r.KgNetos);
        Assert.Equal(0.5m, r.KgMermaMolienda);
        Assert.Equal(1.961m, r.PctMermaMolienda);
        Assert.Equal(130_000m, r.CostoInsumos);
        Assert.Equal(813_048.78m, r.CostoTotal);
        Assert.Equal(8_130.4878m, r.CostoUnitario);
        Assert.Equal(1_300m, r.CostoInsumosPorUnidad);
    }

    [Fact]
    public void Empaque_rechaza_kilos_insuficientes()
    {
        Assert.Throws<ReglaNegocioException>(() => CalculadoraCostos.CalcularEmpaque(new EntradaEmpaque
        {
            GramosNetosPorUnidad = 500, Unidades = 10, KgTostadoUsado = 4.9m, CostoCafeTostado = 1
        }));
    }

    [Fact]
    public void Kg_sugerido_considera_merma_esperada()
    {
        Assert.Equal(25.253m, CalculadoraCostos.KgTostadoSugerido(250, 100, 1m));
    }

    [Fact]
    public void Estimacion_de_bolsa_y_precio_sugerido()
    {
        var e = CalculadoraCostos.EstimarBolsa(
            costoKgTostado: 25_600m, gramosNetos: 500, pctMermaMolienda: 0m,
            receta: new[] { new LineaReceta(1, "Bolsa", 1, 1_000m), new LineaReceta(2, "Válvula", 1, 400m) },
            conversionPorUnidad: 300m, precioVenta: 25_000m, margenObjetivoPct: 40m);
        Assert.Equal(14_500m, e.CostoTotal);
        Assert.Equal(10_500m, e.UtilidadBruta);
        Assert.Equal(42m, e.MargenPct);
        Assert.Equal(24_200m, e.PrecioSugerido);           // 14.500 / 0,6 = 24.166,67 → 24.200
    }

    [Fact]
    public void Insumo_promedio_ponderado()
    {
        var (stock, valor, prom) = CalculadoraCostos.EntradaInsumo(100m, 80_000m, 300m, 270_000m);
        Assert.Equal(400m, stock);
        Assert.Equal(350_000m, valor);
        Assert.Equal(875m, prom);
    }

    [Fact]
    public void Ajuste_por_conteo_faltante_y_sobrante()
    {
        Assert.Equal((-10m, -8_750m), CalculadoraCostos.AjusteConteo(400m, 350_000m, 390m, 900m));
        Assert.Equal((5m, 4_375m), CalculadoraCostos.AjusteConteo(400m, 350_000m, 405m, 900m));
        Assert.Equal((5m, 4_500m), CalculadoraCostos.AjusteConteo(0m, 0m, 5m, 900m));
    }

    [Fact]
    public void Venta_totales_y_utilidad()
    {
        var t = CalculadoraVenta.Totales(new[]
        {
            new LineaVentaCosteada(1, 10, 18_000m, 81_304.88m),
            new LineaVentaCosteada(2, 5, 30_000m, 75_000m),
        }, 10_000m, TipoPago.Credito);
        Assert.Equal(330_000m, t.Subtotal);
        Assert.Equal(320_000m, t.Total);
        Assert.Equal(156_304.88m, t.CostoTotal);
        Assert.Equal(163_695.12m, t.UtilidadBruta);
        Assert.Equal(320_000m, t.SaldoInicial);
    }

    [Fact]
    public void Credito_respeta_cupo_y_consumidor_final()
    {
        Assert.Throws<ReglaNegocioException>(() => CalculadoraVenta.ValidarCredito(true, true, null, 0, 100, 0));
        Assert.Throws<ReglaNegocioException>(() => CalculadoraVenta.ValidarCredito(false, true, 500_000m, 450_000m, 60_000m, 0));
        CalculadoraVenta.ValidarCredito(false, true, 500_000m, 450_000m, 50_000m, 0);
        CalculadoraVenta.ValidarCredito(false, true, null, 9_000_000m, 50_000m, 0);
    }

    [Fact]
    public void Consolidar_lineas_del_mismo_lote()
    {
        var r = CalculadoraVenta.Consolidar(new[] { (1, 2, 100m), (2, 1, 50m), (1, 3, 100m) });
        Assert.Equal(2, r.Count);
        Assert.Equal(5, r.Single(x => x.IdInventarioEmpacado == 1).Cantidad);
    }

    [Fact]
    public void Cartera_edades_y_abonos()
    {
        var hoy = new DateTime(2026, 10, 24);
        Assert.Equal(RangoEdad.PorVencer, CalculadoraCartera.Clasificar(hoy, hoy));
        Assert.Equal(RangoEdad.De1a30, CalculadoraCartera.Clasificar(hoy.AddDays(-30), hoy));
        Assert.Equal(RangoEdad.De31a60, CalculadoraCartera.Clasificar(hoy.AddDays(-31), hoy));
        Assert.Equal(RangoEdad.MasDe90, CalculadoraCartera.Clasificar(hoy.AddDays(-91), hoy));

        var res = CalculadoraCartera.Resumir(new[] { (hoy.AddDays(5), 100m), (hoy.AddDays(-45), 50m) }, hoy);
        Assert.Equal(150m, res.Total);
        Assert.Equal(50m, res.Vencida);

        Assert.Equal((40m, false), CalculadoraCartera.AplicarAbono(100m, 60m));
        Assert.Equal((0m, true), CalculadoraCartera.AplicarAbono(100m, 100m));
        Assert.Throws<ReglaNegocioException>(() => CalculadoraCartera.AplicarAbono(100m, 100.01m));
    }
}
