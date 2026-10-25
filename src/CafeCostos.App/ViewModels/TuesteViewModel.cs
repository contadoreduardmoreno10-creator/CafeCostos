using System.Collections.ObjectModel;
using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

public sealed class LineaTueste : ObservableObject
{
    public required LoteCafeSeco Lote { get; init; }
    private decimal _kilos;
    public decimal Kilos { get => _kilos; set { if (Set(ref _kilos, value)) OnPropertyChanged(nameof(CostoEstimado)); } }
    public decimal CostoEstimado => CalculadoraCostos.CostoSalida(Lote.KgDisponibles, Lote.ValorDisponible, Kilos);
}

/// <summary>Paso 2 del ciclo: tostión con absorción de merma.</summary>
public sealed class TuesteViewModel : ViewModelBase
{
    public TuesteViewModel(ContextoApp ctx) : base(ctx)
    {
        AgregarLoteCommand = new RelayCommand(AgregarLote, () => LoteElegido is not null && KgLote > 0);
        QuitarLineaCommand = new RelayCommand(p => { if (p is LineaTueste l) { Lineas.Remove(l); Recalcular(); } });
        SugerirCostosCommand = new AsyncCommand(SugerirCostosAsync);
        RegistrarCommand = new AsyncCommand(RegistrarAsync, () => Previa is not null);
        LimpiarCommand = new RelayCommand(Limpiar);
        AnularCommand = new AsyncCommand(AnularAsync, () => Seleccionada is { Estado: Estados.Activo });
    }

    public override string Titulo => "Tostión · Órdenes de tueste";

    public ObservableCollection<LoteCafeSeco> LotesDisponibles { get; } = new();
    public ObservableCollection<LineaTueste> Lineas { get; } = new();
    public ObservableCollection<OrdenTueste> Ordenes { get; } = new();
    public ObservableCollection<OrdenTuesteLote> DetalleSeleccionada { get; } = new();
    public IReadOnlyList<string> Perfiles { get; } = new[] { "Claro", "Medio", "Medio oscuro", "Oscuro", "Espresso" };

    public RelayCommand AgregarLoteCommand { get; }
    public RelayCommand QuitarLineaCommand { get; }
    public AsyncCommand SugerirCostosCommand { get; }
    public AsyncCommand RegistrarCommand { get; }
    public RelayCommand LimpiarCommand { get; }
    public AsyncCommand AnularCommand { get; }

    // ---- Selección de lotes ----
    private LoteCafeSeco? _loteElegido;
    public LoteCafeSeco? LoteElegido
    {
        get => _loteElegido;
        set
        {
            if (Set(ref _loteElegido, value))
            {
                KgLote = value is null ? 0 : value.KgDisponibles - Lineas.Where(l => l.Lote.IdLote == value.IdLote).Sum(l => l.Kilos);
                AgregarLoteCommand.Refrescar();
            }
        }
    }
    private decimal _kgLote;
    public decimal KgLote { get => _kgLote; set { if (Set(ref _kgLote, value)) AgregarLoteCommand.Refrescar(); } }

    // ---- Datos de la orden ----
    private DateTime _fecha = DateTime.Today;
    public DateTime Fecha { get => _fecha; set => Set(ref _fecha, value); }
    private string? _perfil = "Medio";
    public string? Perfil { get => _perfil; set => Set(ref _perfil, value); }
    private decimal _kgTostados;
    public decimal KgTostados { get => _kgTostados; set { if (Set(ref _kgTostados, value)) Recalcular(); } }
    private decimal _energia;
    public decimal Energia { get => _energia; set { if (Set(ref _energia, value)) Recalcular(); } }
    private decimal _manoObra;
    public decimal ManoObra { get => _manoObra; set { if (Set(ref _manoObra, value)) Recalcular(); } }
    private decimal _otros;
    public decimal Otros { get => _otros; set { if (Set(ref _otros, value)) Recalcular(); } }
    private string? _observaciones;
    public string? Observaciones { get => _observaciones; set => Set(ref _observaciones, value); }

    public decimal KgSecos => Lineas.Sum(l => l.Kilos);

    // ---- Vista previa del costeo ----
    private ResultadoTueste? _previa;
    public ResultadoTueste? Previa { get => _previa; private set { if (Set(ref _previa, value)) RegistrarCommand.Refrescar(); } }
    private string? _mensajePrevia = "Agregue lotes de café seco y los kilos tostados obtenidos.";
    public string? MensajePrevia { get => _mensajePrevia; private set => Set(ref _mensajePrevia, value); }
    public string Advertencias => Previa is null ? "" : string.Join("\n", Previa.Advertencias);

    private void Recalcular()
    {
        OnPropertyChanged(nameof(KgSecos));
        try
        {
            if (Lineas.Count == 0 || KgTostados <= 0)
            {
                Previa = null;
                MensajePrevia = Lineas.Count == 0 ? "Agregue al menos un lote de café seco." : "Digite los kilos tostados obtenidos.";
            }
            else
            {
                Previa = CalculadoraCostos.CalcularTueste(
                    Lineas.Select(l => new ConsumoMateriaPrima(l.Lote.IdLote, l.Kilos, l.CostoEstimado)).ToList(),
                    KgTostados, new CostosConversion(Energia, ManoObra, Otros));
                MensajePrevia = null;
            }
        }
        catch (ReglaNegocioException ex)
        {
            Previa = null;
            MensajePrevia = ex.Message;
        }
        OnPropertyChanged(nameof(Advertencias));
    }

    // ---- Lista de órdenes ----
    private OrdenTueste? _seleccionada;
    public OrdenTueste? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (!Set(ref _seleccionada, value)) return;
            AnularCommand.Refrescar();
            _ = CargarDetalleAsync();
        }
    }

    private async Task CargarDetalleAsync()
    {
        DetalleSeleccionada.Clear();
        if (Seleccionada is null) return;
        foreach (var d in await Ctx.Tueste.DetalleAsync(Seleccionada.IdOrdenTueste)) DetalleSeleccionada.Add(d);
    }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        LotesDisponibles.Clear();
        foreach (var l in await Ctx.Lotes.ListarAsync(soloDisponibles: true)) LotesDisponibles.Add(l);
        Ordenes.Clear();
        foreach (var o in await Ctx.Tueste.ListarAsync()) Ordenes.Add(o);
        // Si los lotes cambiaron (p. ej. se anuló algo), se descartan las líneas que ya no son válidas.
        foreach (var l in Lineas.ToList())
            if (LotesDisponibles.All(x => x.IdLote != l.Lote.IdLote)) Lineas.Remove(l);
        Recalcular();
    });

    private void AgregarLote()
    {
        if (LoteElegido is null) return;
        var yaAgregado = Lineas.FirstOrDefault(l => l.Lote.IdLote == LoteElegido.IdLote);
        var total = (yaAgregado?.Kilos ?? 0) + Redondeo.Kilos(KgLote);
        if (KgLote <= 0 || total > LoteElegido.KgDisponibles)
        {
            Ctx.Dialogos.Advertir($"El lote {LoteElegido.Codigo} solo tiene {LoteElegido.KgDisponibles:N3} kg disponibles.");
            return;
        }
        if (yaAgregado is null) Lineas.Add(new LineaTueste { Lote = LoteElegido, Kilos = total });
        else yaAgregado.Kilos = total;
        LoteElegido = null;
        KgLote = 0;
        Recalcular();
    }

    private async Task SugerirCostosAsync()
    {
        if (KgSecos <= 0) { Ctx.Dialogos.Advertir("Primero agregue los lotes de café seco."); return; }
        var energia = await Ctx.Parametros.DecimalAsync("Costeo.EnergiaPorKgSeco");
        var mo = await Ctx.Parametros.DecimalAsync("Costeo.ManoObraTuestePorKgSeco");
        if (energia == 0 && mo == 0)
        {
            Ctx.Dialogos.Informar("Aún no ha configurado tarifas por kilo. Puede hacerlo en Configuración › Costos de producción.");
            return;
        }
        Energia = Redondeo.Dinero(energia * KgSecos);
        ManoObra = Redondeo.Dinero(mo * KgSecos);
    }

    private async Task RegistrarAsync()
    {
        if (Previa is null) return;
        var p = Previa;
        var resumen =
            $"Kilos secos: {p.KgSecos:N2} kg\nKilos tostados: {p.KgTostados:N2} kg\n" +
            $"Merma: {p.KgMerma:N2} kg ({p.PctMerma:N2} %)\n\nCosto total: {p.CostoTotal:C0}\n" +
            $"Costo por kg tostado: {p.CostoKgTostado:C0}" +
            (p.Advertencias.Count > 0 ? "\n\n⚠ " + string.Join("\n⚠ ", p.Advertencias) : "") +
            "\n\n¿Registrar la orden de tueste?";
        if (!Ctx.Dialogos.Confirmar(resumen, "Confirmar tostión")) return;

        string? numero = null;
        ResultadoTueste? r = null;
        var ok = await EjecutarAsync(async () =>
        {
            (numero, r) = await Ctx.Tueste.RegistrarAsync(new NuevaOrdenTueste
            {
                Fecha = Fecha, PerfilTueste = Perfil, KgTostados = KgTostados,
                CostoEnergia = Energia, CostoManoObra = ManoObra, OtrosCostos = Otros, Observaciones = Observaciones,
                Lotes = Lineas.Select(l => new ConsumoLoteSolicitado(l.Lote.IdLote, l.Kilos)).ToList()
            });
        });
        if (!ok) return;
        Ctx.Dialogos.Informar($"Orden {numero} registrada.\nCosto del kilo tostado: {r!.CostoKgTostado:C0}");
        Limpiar();
        await CargarAsync();
    }

    private void Limpiar()
    {
        Lineas.Clear();
        Fecha = DateTime.Today; Perfil = "Medio";
        KgTostados = 0; Energia = 0; ManoObra = 0; Otros = 0; Observaciones = null;
        LoteElegido = null;
        Recalcular();
    }

    private async Task AnularAsync()
    {
        if (Seleccionada is null) return;
        if (!Ctx.Dialogos.Confirmar($"¿Anular la orden {Seleccionada.Numero}? El café seco volverá a sus lotes.")) return;
        if (await EjecutarAsync(() => Ctx.Tueste.AnularAsync(Seleccionada.IdOrdenTueste)))
            await CargarAsync();
    }
}
