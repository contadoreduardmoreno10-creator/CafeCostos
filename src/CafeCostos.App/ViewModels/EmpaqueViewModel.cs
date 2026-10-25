using System.Collections.ObjectModel;
using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

/// <summary>Pasos 3 y 4 del ciclo: molienda / fraccionamiento y empaque con costo final de la bolsa.</summary>
public sealed class EmpaqueViewModel : ViewModelBase
{
    public EmpaqueViewModel(ContextoApp ctx) : base(ctx)
    {
        SugerirKilosCommand = new AsyncCommand(SugerirKilosAsync);
        SugerirCostosCommand = new AsyncCommand(SugerirCostosAsync);
        RegistrarCommand = new AsyncCommand(RegistrarAsync, () => Previa is not null);
        LimpiarCommand = new RelayCommand(Limpiar);
        AnularCommand = new AsyncCommand(AnularAsync, () => Seleccionado is { Estado: Estados.Activo });
    }

    public override string Titulo => "Molienda, fraccionamiento y empaque";

    public ObservableCollection<OrdenTueste> TuestesDisponibles { get; } = new();
    public ObservableCollection<Presentacion> Presentaciones { get; } = new();
    public ObservableCollection<RecetaInsumo> Receta { get; } = new();
    public ObservableCollection<ConsumoInsumo> InsumosPrevia { get; } = new();
    public ObservableCollection<LoteEmpacado> Lotes { get; } = new();
    public ObservableCollection<ConsumoInsumo> InsumosSeleccionado { get; } = new();

    public AsyncCommand SugerirKilosCommand { get; }
    public AsyncCommand SugerirCostosCommand { get; }
    public AsyncCommand RegistrarCommand { get; }
    public RelayCommand LimpiarCommand { get; }
    public AsyncCommand AnularCommand { get; }

    private OrdenTueste? _tueste;
    public OrdenTueste? Tueste { get => _tueste; set { if (Set(ref _tueste, value)) Recalcular(); } }

    private Presentacion? _presentacion;
    public Presentacion? Presentacion
    {
        get => _presentacion;
        set { if (Set(ref _presentacion, value)) _ = CargarRecetaAsync(); }
    }

    private DateTime _fecha = DateTime.Today;
    public DateTime Fecha { get => _fecha; set => Set(ref _fecha, value); }
    private int _unidades;
    public int Unidades { get => _unidades; set { if (Set(ref _unidades, value)) Recalcular(); } }
    private decimal _kgUsado;
    public decimal KgUsado { get => _kgUsado; set { if (Set(ref _kgUsado, value)) Recalcular(); } }
    private decimal _molienda;
    public decimal Molienda { get => _molienda; set { if (Set(ref _molienda, value)) Recalcular(); } }
    private decimal _manoObra;
    public decimal ManoObra { get => _manoObra; set { if (Set(ref _manoObra, value)) Recalcular(); } }
    private decimal _otros;
    public decimal Otros { get => _otros; set { if (Set(ref _otros, value)) Recalcular(); } }
    private DateTime? _vencimiento = DateTime.Today.AddMonths(9);
    public DateTime? Vencimiento { get => _vencimiento; set => Set(ref _vencimiento, value); }
    private string? _observaciones;
    public string? Observaciones { get => _observaciones; set => Set(ref _observaciones, value); }

    // ---- Vista previa ----
    private ResultadoEmpaque? _previa;
    public ResultadoEmpaque? Previa { get => _previa; private set { if (Set(ref _previa, value)) { RegistrarCommand.Refrescar(); OnPropertyChanged(nameof(MargenPrevia)); } } }
    private string? _mensajePrevia = "Seleccione la orden de tueste, la presentación y las unidades.";
    public string? MensajePrevia { get => _mensajePrevia; private set => Set(ref _mensajePrevia, value); }
    public string Advertencias => Previa is null ? "" : string.Join("\n", Previa.Advertencias);

    /// <summary>Margen bruto estimado frente al precio de lista de la presentación.</summary>
    public string MargenPrevia => Previa is null || Presentacion is not { PrecioVenta: > 0 } p
        ? "Defina el precio de venta en «Presentaciones y precios» para ver el margen."
        : $"Precio de lista {p.PrecioVenta:C0} → utilidad {p.PrecioVenta - Previa.CostoUnitario:C0} por bolsa " +
          $"({(p.PrecioVenta - Previa.CostoUnitario) / p.PrecioVenta * 100m:N1} %)";

    private void Recalcular()
    {
        InsumosPrevia.Clear();
        try
        {
            if (Tueste is null || Presentacion is null || Unidades <= 0 || KgUsado <= 0)
            {
                Previa = null;
                MensajePrevia = "Seleccione la orden de tueste, la presentación, las unidades y los kilos usados.";
                return;
            }
            Validar.Que(KgUsado <= Tueste.KgDisponibles, $"La orden {Tueste.Numero} solo tiene {Tueste.KgDisponibles:N3} kg disponibles.");

            var insumos = new List<ConsumoInsumo>();
            foreach (var r in Receta)
            {
                var cant = CalculadoraCostos.CantidadInsumoRequerida(r.CantidadPorUnidad, Unidades);
                Validar.Que(cant <= r.Stock, $"No hay suficiente «{r.NombreInsumo}»: se necesitan {cant:N0} y hay {r.Stock:N0}.");
                insumos.Add(new ConsumoInsumo(r.IdInsumo, r.NombreInsumo, cant, Redondeo.Dinero(cant * r.CostoPromedio)));
            }
            foreach (var i in insumos) InsumosPrevia.Add(i);

            Previa = CalculadoraCostos.CalcularEmpaque(new EntradaEmpaque
            {
                GramosNetosPorUnidad = Presentacion.GramosNetos,
                Unidades = Unidades,
                KgTostadoUsado = KgUsado,
                CostoCafeTostado = CalculadoraCostos.CostoSalida(Tueste.KgDisponibles, Tueste.ValorDisponible, KgUsado),
                Insumos = insumos,
                Conversion = new CostosConversion(Molienda, ManoObra, Otros)
            });
            MensajePrevia = null;
        }
        catch (ReglaNegocioException ex)
        {
            Previa = null;
            MensajePrevia = ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(Advertencias));
            OnPropertyChanged(nameof(MargenPrevia));
        }
    }

    private async Task CargarRecetaAsync()
    {
        Receta.Clear();
        if (Presentacion is not null)
            foreach (var r in await Ctx.Empaque.RecetaAsync(Presentacion.IdPresentacion)) Receta.Add(r);
        Recalcular();
    }

    // ---- Lotes empacados ----
    private LoteEmpacado? _seleccionado;
    public LoteEmpacado? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (!Set(ref _seleccionado, value)) return;
            AnularCommand.Refrescar();
            _ = CargarInsumosSeleccionadoAsync();
        }
    }

    private async Task CargarInsumosSeleccionadoAsync()
    {
        InsumosSeleccionado.Clear();
        if (Seleccionado is null) return;
        foreach (var i in await Ctx.Empaque.InsumosDeLoteAsync(Seleccionado.IdInventarioEmpacado)) InsumosSeleccionado.Add(i);
    }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var idTueste = Tueste?.IdOrdenTueste;
        var idPres = Presentacion?.IdPresentacion;

        TuestesDisponibles.Clear();
        foreach (var t in await Ctx.Tueste.ListarAsync(soloDisponibles: true)) TuestesDisponibles.Add(t);
        Presentaciones.Clear();
        foreach (var p in await Ctx.Presentaciones.ListarAsync(soloActivas: true)) Presentaciones.Add(p);
        Lotes.Clear();
        foreach (var l in await Ctx.Empaque.ListarAsync()) Lotes.Add(l);

        Tueste = TuestesDisponibles.FirstOrDefault(t => t.IdOrdenTueste == idTueste);
        Presentacion = Presentaciones.FirstOrDefault(p => p.IdPresentacion == idPres);
        await CargarRecetaAsync();
    });

    private async Task SugerirKilosAsync()
    {
        if (Presentacion is null || Unidades <= 0) { Ctx.Dialogos.Advertir("Seleccione la presentación y las unidades."); return; }
        var merma = await Ctx.Parametros.DecimalAsync("Costeo.MermaMoliendaEsperada", 1m);
        KgUsado = CalculadoraCostos.KgTostadoSugerido(Presentacion.GramosNetos, Unidades, merma);
    }

    private async Task SugerirCostosAsync()
    {
        if (Presentacion is null || Unidades <= 0) { Ctx.Dialogos.Advertir("Seleccione la presentación y las unidades."); return; }
        var porKg = await Ctx.Parametros.DecimalAsync("Costeo.MoliendaPorKg");
        var porUnidad = await Ctx.Parametros.DecimalAsync("Costeo.ManoObraEmpaquePorUnidad");
        var kgNetos = Unidades * Presentacion.GramosNetos / 1000m;
        Molienda = Presentacion.TipoMolienda == "MOLIDO" ? Redondeo.Dinero(porKg * kgNetos) : 0;
        ManoObra = Redondeo.Dinero(porUnidad * Unidades);
    }

    private async Task RegistrarAsync()
    {
        if (Previa is null || Presentacion is null || Tueste is null) return;
        var p = Previa;
        var msg = $"{p.Unidades} × {Presentacion.Descripcion}\nCafé usado: {p.KgTostadoUsado:N3} kg (merma {p.PctMermaMolienda:N2} %)\n\n" +
                  $"Café: {p.CostoCafe:C0}\nInsumos: {p.CostoInsumos:C0}\nConversión: {p.CostoConversion:C0}\n" +
                  $"Costo total: {p.CostoTotal:C0}\n\nCOSTO POR BOLSA: {p.CostoUnitario:C0}" +
                  (p.Advertencias.Count > 0 ? "\n\n⚠ " + string.Join("\n⚠ ", p.Advertencias) : "") +
                  "\n\n¿Registrar el lote de producto terminado?";
        if (!Ctx.Dialogos.Confirmar(msg, "Confirmar empaque")) return;

        string? codigo = null;
        ResultadoEmpaque? r = null;
        var ok = await EjecutarAsync(async () =>
        {
            (codigo, r) = await Ctx.Empaque.RegistrarAsync(new NuevoEmpaque
            {
                Fecha = Fecha, IdOrdenTueste = Tueste.IdOrdenTueste, IdPresentacion = Presentacion.IdPresentacion,
                Unidades = Unidades, KgTostadoUsado = KgUsado, CostoMolienda = Molienda, CostoManoObra = ManoObra,
                OtrosCostos = Otros, FechaVencimiento = Vencimiento, Observaciones = Observaciones
            });
        });
        if (!ok) return;
        Ctx.Dialogos.Informar($"Lote {codigo} registrado: {r!.Unidades} unidades a {r.CostoUnitario:C0} cada una.");
        Limpiar();
        await CargarAsync();
    }

    private void Limpiar()
    {
        Unidades = 0; KgUsado = 0; Molienda = 0; ManoObra = 0; Otros = 0; Observaciones = null;
        Fecha = DateTime.Today; Vencimiento = DateTime.Today.AddMonths(9);
        Recalcular();
    }

    private async Task AnularAsync()
    {
        if (Seleccionado is null) return;
        if (!Ctx.Dialogos.Confirmar($"¿Anular el lote {Seleccionado.CodigoLote}? El café regresa a la tostión y los insumos al inventario.")) return;
        if (await EjecutarAsync(() => Ctx.Empaque.AnularAsync(Seleccionado.IdInventarioEmpacado)))
            await CargarAsync();
    }
}
