using System.Collections.ObjectModel;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

public sealed class LineaRecetaVm : ObservableObject
{
    public required int IdInsumo { get; init; }
    public required string Nombre { get; init; }
    private decimal _cantidad;
    public decimal Cantidad { get => _cantidad; set => Set(ref _cantidad, value); }
}

/// <summary>Presentaciones comerciales, receta de insumos por bolsa y simulador de precio.</summary>
public sealed class PresentacionesViewModel : ViewModelBase
{
    public PresentacionesViewModel(ContextoApp ctx) : base(ctx)
    {
        NuevoCommand = new RelayCommand(Nuevo);
        GuardarCommand = new AsyncCommand(GuardarAsync);
        AgregarInsumoCommand = new RelayCommand(AgregarInsumo, () => InsumoElegido is not null);
        QuitarInsumoCommand = new RelayCommand(p => { if (p is LineaRecetaVm l) Receta.Remove(l); });
        SimularCommand = new AsyncCommand(SimularAsync, () => Edicion.IdPresentacion > 0);
        UsarPrecioSugeridoCommand = new RelayCommand(() => { if (Estimacion is not null) Edicion.PrecioVenta = Estimacion.PrecioSugerido; OnPropertyChanged(nameof(Edicion)); });
        Nuevo();
    }

    public override string Titulo => "Presentaciones y precios";

    public ObservableCollection<Presentacion> Presentaciones { get; } = new();
    public ObservableCollection<Insumo> InsumosDisponibles { get; } = new();
    public ObservableCollection<LineaRecetaVm> Receta { get; } = new();
    public IReadOnlyList<string> TiposMolienda { get; } = new[] { "GRANO", "MOLIDO" };
    public IReadOnlyList<string> GradosMolienda { get; } = new[] { "Fino (espresso)", "Medio (filtro)", "Grueso (prensa)" };

    public RelayCommand NuevoCommand { get; }
    public AsyncCommand GuardarCommand { get; }
    public RelayCommand AgregarInsumoCommand { get; }
    public RelayCommand QuitarInsumoCommand { get; }
    public AsyncCommand SimularCommand { get; }
    public RelayCommand UsarPrecioSugeridoCommand { get; }

    private Presentacion? _seleccionada;
    public Presentacion? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (!Set(ref _seleccionada, value) || value is null) return;
            Edicion = new Presentacion
            {
                IdPresentacion = value.IdPresentacion, Nombre = value.Nombre, GramosNetos = value.GramosNetos,
                TipoMolienda = value.TipoMolienda, GradoMolienda = value.GradoMolienda, PrecioVenta = value.PrecioVenta, Activo = value.Activo
            };
            _ = CargarRecetaAsync();
        }
    }

    private Presentacion _edicion = new();
    public Presentacion Edicion
    {
        get => _edicion;
        private set { if (Set(ref _edicion, value)) { SimularCommand.Refrescar(); Estimacion = null; } }
    }

    private Insumo? _insumoElegido;
    public Insumo? InsumoElegido { get => _insumoElegido; set { if (Set(ref _insumoElegido, value)) AgregarInsumoCommand.Refrescar(); } }
    private decimal _cantidadInsumo = 1;
    public decimal CantidadInsumo { get => _cantidadInsumo; set => Set(ref _cantidadInsumo, value); }

    // ---- Simulador ----
    private decimal _costoKgTostado;
    public decimal CostoKgTostado { get => _costoKgTostado; set => Set(ref _costoKgTostado, value); }
    private decimal _mermaMolienda = 1;
    public decimal MermaMolienda { get => _mermaMolienda; set => Set(ref _mermaMolienda, value); }
    private decimal _conversionUnidad;
    public decimal ConversionUnidad { get => _conversionUnidad; set => Set(ref _conversionUnidad, value); }
    private decimal _margenObjetivo = 40;
    public decimal MargenObjetivo { get => _margenObjetivo; set => Set(ref _margenObjetivo, value); }
    private EstimacionBolsa? _estimacion;
    public EstimacionBolsa? Estimacion { get => _estimacion; private set => Set(ref _estimacion, value); }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var id = Seleccionada?.IdPresentacion;
        Presentaciones.Clear();
        foreach (var p in await Ctx.Presentaciones.ListarAsync()) Presentaciones.Add(p);
        InsumosDisponibles.Clear();
        foreach (var i in await Ctx.Insumos.ListarAsync(soloActivos: true)) InsumosDisponibles.Add(i);

        CostoKgTostado = await Ctx.Presentaciones.CostoKgTostadoReferenciaAsync();
        MermaMolienda = await Ctx.Parametros.DecimalAsync("Costeo.MermaMoliendaEsperada", 1m);
        MargenObjetivo = await Ctx.Parametros.DecimalAsync("Costeo.MargenObjetivo", 40m);
        ConversionUnidad = await Ctx.Parametros.DecimalAsync("Costeo.ManoObraEmpaquePorUnidad");

        if (id is not null) Seleccionada = Presentaciones.FirstOrDefault(p => p.IdPresentacion == id);
    });

    private async Task CargarRecetaAsync()
    {
        Receta.Clear();
        if (Edicion.IdPresentacion == 0) return;
        foreach (var r in await Ctx.Presentaciones.RecetaAsync(Edicion.IdPresentacion))
            Receta.Add(new LineaRecetaVm { IdInsumo = r.IdInsumo, Nombre = r.NombreInsumo, Cantidad = r.CantidadPorUnidad });
    }

    private void Nuevo()
    {
        _seleccionada = null;
        OnPropertyChanged(nameof(Seleccionada));
        Edicion = new Presentacion { GramosNetos = 250, TipoMolienda = "GRANO", Activo = true };
        Receta.Clear();
    }

    private void AgregarInsumo()
    {
        if (InsumoElegido is null) return;
        if (CantidadInsumo <= 0) { Ctx.Dialogos.Advertir("La cantidad por bolsa debe ser mayor que cero."); return; }
        var ya = Receta.FirstOrDefault(r => r.IdInsumo == InsumoElegido.IdInsumo);
        if (ya is not null) ya.Cantidad = CantidadInsumo;
        else Receta.Add(new LineaRecetaVm { IdInsumo = InsumoElegido.IdInsumo, Nombre = InsumoElegido.Nombre, Cantidad = CantidadInsumo });
        InsumoElegido = null;
        CantidadInsumo = 1;
    }

    private async Task GuardarAsync()
    {
        int id = 0;
        if (Receta.Count == 0 &&
            !Ctx.Dialogos.Confirmar("La presentación no tiene insumos (bolsa, válvula, etiqueta). ¿Guardar de todas formas?")) return;
        if (!await EjecutarAsync(async () =>
            id = await Ctx.Presentaciones.GuardarAsync(Edicion, Receta.Select(r => (r.IdInsumo, r.Cantidad))))) return;

        Ctx.Dialogos.Informar("Presentación guardada.");
        _seleccionada = null;
        await CargarAsync();
        Seleccionada = Presentaciones.FirstOrDefault(p => p.IdPresentacion == id);
    }

    private Task SimularAsync() => EjecutarAsync(async () =>
    {
        Estimacion = await Ctx.Presentaciones.EstimarAsync(Edicion, CostoKgTostado, MermaMolienda, ConversionUnidad, MargenObjetivo);
    });
}
