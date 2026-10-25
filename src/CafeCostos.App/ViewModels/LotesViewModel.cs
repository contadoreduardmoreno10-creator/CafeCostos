using System.Collections.ObjectModel;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

/// <summary>Paso 1 del ciclo: recepción de lotes de café seco.</summary>
public sealed class LotesViewModel : ViewModelBase
{
    public LotesViewModel(ContextoApp ctx) : base(ctx)
    {
        RegistrarCommand = new AsyncCommand(RegistrarAsync);
        LimpiarCommand = new RelayCommand(Limpiar);
        AnularCommand = new AsyncCommand(AnularAsync, () => Seleccionado is { Estado: Estados.Activo });
    }

    public override string Titulo => "Café seco · Recepción de lotes";

    public ObservableCollection<LoteCafeSeco> Lotes { get; } = new();
    public AsyncCommand RegistrarCommand { get; }
    public RelayCommand LimpiarCommand { get; }
    public AsyncCommand AnularCommand { get; }

    private LoteCafeSeco? _seleccionado;
    public LoteCafeSeco? Seleccionado
    {
        get => _seleccionado;
        set { if (Set(ref _seleccionado, value)) AnularCommand.Refrescar(); }
    }

    private bool _soloDisponibles = true;
    public bool SoloDisponibles
    {
        get => _soloDisponibles;
        set { if (Set(ref _soloDisponibles, value)) _ = CargarAsync(); }
    }

    // ---- Formulario ----
    private DateTime _fecha = DateTime.Today;
    public DateTime Fecha { get => _fecha; set => Set(ref _fecha, value); }
    private string _proveedor = "";
    public string Proveedor { get => _proveedor; set => Set(ref _proveedor, value); }
    private string? _origen;
    public string? Origen { get => _origen; set => Set(ref _origen, value); }
    private string? _variedad;
    public string? Variedad { get => _variedad; set => Set(ref _variedad, value); }
    private decimal? _humedad;
    public decimal? Humedad { get => _humedad; set => Set(ref _humedad, value); }
    private decimal _kilos;
    public decimal Kilos { get => _kilos; set { if (Set(ref _kilos, value)) Recalcular(); } }
    private decimal _costo;
    public decimal Costo { get => _costo; set { if (Set(ref _costo, value)) Recalcular(); } }
    private decimal _flete;
    public decimal Flete { get => _flete; set { if (Set(ref _flete, value)) Recalcular(); } }
    private string? _observaciones;
    public string? Observaciones { get => _observaciones; set => Set(ref _observaciones, value); }

    public decimal CostoTotal => Costo + Flete;
    public decimal CostoKg => CalculadoraCostos.CostoPromedio(Kilos, CostoTotal);

    public decimal TotalKgDisponibles => Lotes.Where(l => l.Estado == Estados.Activo).Sum(l => l.KgDisponibles);
    public decimal TotalValorDisponible => Lotes.Where(l => l.Estado == Estados.Activo).Sum(l => l.ValorDisponible);

    private void Recalcular()
    {
        OnPropertyChanged(nameof(CostoTotal));
        OnPropertyChanged(nameof(CostoKg));
    }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var lista = await Ctx.Lotes.ListarAsync(SoloDisponibles);
        Lotes.Clear();
        foreach (var l in lista) Lotes.Add(l);
        OnPropertyChanged(nameof(TotalKgDisponibles));
        OnPropertyChanged(nameof(TotalValorDisponible));
    });

    private async Task RegistrarAsync()
    {
        string? codigo = null;
        var ok = await EjecutarAsync(async () =>
        {
            codigo = await Ctx.Lotes.RegistrarAsync(new NuevoLote
            {
                FechaRecepcion = Fecha, Proveedor = Proveedor, Origen = Origen, Variedad = Variedad, Humedad = Humedad,
                KgRecibidos = Kilos, CostoAdquisicion = Costo, CostoFlete = Flete, Observaciones = Observaciones
            });
        });
        if (!ok) return;
        Ctx.Dialogos.Informar($"Lote {codigo} registrado.\n{Kilos:N2} kg a {CostoKg:C0} por kilo.");
        Limpiar();
        await CargarAsync();
    }

    private void Limpiar()
    {
        Fecha = DateTime.Today;
        Proveedor = ""; Origen = null; Variedad = null; Humedad = null;
        Kilos = 0; Costo = 0; Flete = 0; Observaciones = null;
    }

    private async Task AnularAsync()
    {
        if (Seleccionado is null) return;
        if (!Ctx.Dialogos.Confirmar($"¿Anular el lote {Seleccionado.Codigo}? Esta acción no se puede deshacer.")) return;
        if (await EjecutarAsync(() => Ctx.Lotes.AnularAsync(Seleccionado.IdLote)))
            await CargarAsync();
    }
}
