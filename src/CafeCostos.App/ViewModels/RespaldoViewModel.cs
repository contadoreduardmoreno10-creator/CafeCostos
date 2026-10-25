using System.Collections.ObjectModel;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Servicios;

namespace CafeCostos.App.ViewModels;

/// <summary>Copias de seguridad con un clic y asistente de restauración en 3 pasos.</summary>
public sealed class RespaldoViewModel : ViewModelBase
{
    public RespaldoViewModel(ContextoApp ctx) : base(ctx)
    {
        CrearCommand = new AsyncCommand(CrearAsync);
        CrearEnCommand = new AsyncCommand(CrearEnOtraCarpetaAsync);
        CambiarCarpetaCommand = new AsyncCommand(CambiarCarpetaAsync);
        ElegirArchivoCommand = new AsyncCommand(ElegirArchivoAsync);
        RestaurarSeleccionadoCommand = new AsyncCommand(() => InspeccionarAsync(Seleccionado?.Ruta), () => Seleccionado is not null);
        ConfirmarRestauracionCommand = new AsyncCommand(RestaurarAsync, () => PasoRestauracion == 2);
        CancelarRestauracionCommand = new RelayCommand(() => { PasoRestauracion = 0; ArchivoRestaurar = null; InfoArchivo = null; });
        MantenimientoCommand = new AsyncCommand(MantenimientoAsync);
    }

    public override string Titulo => "Copias de seguridad";

    public ObservableCollection<InfoRespaldo> Respaldos { get; } = new();
    public ObservableCollection<FilaAlmacenamiento> Almacenamiento { get; } = new();

    public AsyncCommand CrearCommand { get; }
    public AsyncCommand CrearEnCommand { get; }
    public AsyncCommand CambiarCarpetaCommand { get; }
    public AsyncCommand ElegirArchivoCommand { get; }
    public AsyncCommand RestaurarSeleccionadoCommand { get; }
    public AsyncCommand ConfirmarRestauracionCommand { get; }
    public RelayCommand CancelarRestauracionCommand { get; }
    public AsyncCommand MantenimientoCommand { get; }

    private string _carpeta = "";
    public string Carpeta { get => _carpeta; private set => Set(ref _carpeta, value); }

    private bool _automatico = true;
    public bool Automatico
    {
        get => _automatico;
        set { if (Set(ref _automatico, value)) _ = Ctx.Parametros.GuardarAsync("Respaldo.AutomaticoAlCerrar", value); }
    }

    private string? _progreso;
    public string? Progreso { get => _progreso; private set => Set(ref _progreso, value); }

    private InfoRespaldo? _seleccionado;
    public InfoRespaldo? Seleccionado { get => _seleccionado; set { if (Set(ref _seleccionado, value)) RestaurarSeleccionadoCommand.Refrescar(); } }

    public string MotorInfo { get; private set; } = "";
    public decimal TamanoTotalMB => Almacenamiento.Sum(a => a.TamanoMB);

    // ---- Asistente de restauración: 0 = inicio, 1 = elegir archivo, 2 = confirmar, 3 = terminado ----
    private int _pasoRestauracion;
    public int PasoRestauracion
    {
        get => _pasoRestauracion;
        private set
        {
            if (!Set(ref _pasoRestauracion, value)) return;
            ConfirmarRestauracionCommand.Refrescar();
            OnPropertyChanged(nameof(EnAsistente));
        }
    }
    public bool EnAsistente => PasoRestauracion > 0;

    private string? _archivoRestaurar;
    public string? ArchivoRestaurar { get => _archivoRestaurar; private set => Set(ref _archivoRestaurar, value); }
    private RespaldoService.InfoArchivoRespaldo? _infoArchivo;
    public RespaldoService.InfoArchivoRespaldo? InfoArchivo { get => _infoArchivo; private set => Set(ref _infoArchivo, value); }
    private string? _resultadoRestauracion;
    public string? ResultadoRestauracion { get => _resultadoRestauracion; private set => Set(ref _resultadoRestauracion, value); }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        Carpeta = await Ctx.CarpetaRespaldosAsync();
        _automatico = await Ctx.Parametros.BoolAsync("Respaldo.AutomaticoAlCerrar", true);
        OnPropertyChanged(nameof(Automatico));
        MotorInfo = await Ctx.Respaldos.SoportaCompresionNativaAsync()
            ? "El motor permite compresión nativa: las copias se guardan como .bak comprimido."
            : "Motor LocalDB/Express: las copias se guardan como .zip (el .bak verificado y comprimido al máximo).";
        OnPropertyChanged(nameof(MotorInfo));
        ListarRespaldos();
        Almacenamiento.Clear();
        foreach (var a in await Ctx.Reportes.EstadoAlmacenamientoAsync()) Almacenamiento.Add(a);
        OnPropertyChanged(nameof(TamanoTotalMB));
    });

    private void ListarRespaldos()
    {
        Respaldos.Clear();
        foreach (var r in Ctx.Respaldos.Listar(Carpeta)) Respaldos.Add(r);
    }

    private Task CrearAsync() => CrearEnAsync(Carpeta);

    private async Task CrearEnOtraCarpetaAsync()
    {
        var destino = Ctx.Dialogos.ElegirCarpeta("Elija la carpeta o memoria USB donde guardar la copia");
        if (destino is not null) await CrearEnAsync(destino);
    }

    private async Task CrearEnAsync(string destino)
    {
        InfoRespaldo? r = null;
        var ok = await EjecutarAsync(async () =>
        {
            r = await Ctx.Respaldos.CrearAsync(destino, new Progress<string>(m => Progreso = m));
        });
        Progreso = null;
        if (!ok) return;
        Ctx.Dialogos.Informar($"Copia de seguridad creada correctamente.\n\n{r!.Ruta}\nTamaño: {r.Tamano}");
        ListarRespaldos();
    }

    private async Task CambiarCarpetaAsync()
    {
        var nueva = Ctx.Dialogos.ElegirCarpeta("Carpeta para las copias de seguridad", Carpeta);
        if (nueva is null) return;
        await Ctx.Parametros.GuardarAsync("Respaldo.Carpeta", nueva);
        Carpeta = nueva;
        ListarRespaldos();
    }

    private async Task ElegirArchivoAsync()
    {
        var archivo = Ctx.Dialogos.ElegirArchivoRespaldo(Carpeta);
        if (archivo is not null) await InspeccionarAsync(archivo);
    }

    /// <summary>Paso 1 → 2: valida el archivo y muestra de qué fecha es la información.</summary>
    private async Task InspeccionarAsync(string? archivo)
    {
        if (archivo is null) return;
        PasoRestauracion = 1;
        ArchivoRestaurar = archivo;
        var ok = await EjecutarAsync(async () => InfoArchivo = await Ctx.Respaldos.InspeccionarAsync(archivo));
        PasoRestauracion = ok ? 2 : 0;
    }

    /// <summary>Paso 2 → 3: restaura (con copia previa automática de la información actual).</summary>
    private async Task RestaurarAsync()
    {
        if (ArchivoRestaurar is null || InfoArchivo is null) return;
        if (!Ctx.Dialogos.Confirmar(
                $"La información actual será REEMPLAZADA por la de la copia del {InfoArchivo.FechaCopia:dd/MM/yyyy HH:mm}.\n\n" +
                "Antes de continuar se guardará automáticamente una copia de la información actual.\n\n¿Desea continuar?",
                "Confirmar restauración")) return;

        InfoRespaldo? previa = null;
        var ok = await EjecutarAsync(async () =>
        {
            previa = await Ctx.Respaldos.RestaurarAsync(ArchivoRestaurar, Carpeta, new Progress<string>(m => Progreso = m));
        });
        Progreso = null;
        if (!ok) { PasoRestauracion = 0; return; }

        ResultadoRestauracion =
            $"La información se restauró con la copia del {InfoArchivo.FechaCopia:dd/MM/yyyy HH:mm}.\n" +
            $"Su información anterior quedó guardada en:\n{previa!.Ruta}";
        PasoRestauracion = 3;
        ListarRespaldos();
    }

    private async Task MantenimientoAsync()
    {
        ResultadoMantenimiento? r = null;
        var ok = await EjecutarAsync(async () =>
        {
            Progreso = "Optimizando la base de datos…";
            r = await Ctx.Mantenimiento.EjecutarAsync();
        });
        Progreso = null;
        if (!ok) return;
        Ctx.Dialogos.Informar(
            $"Mantenimiento terminado.\nÍndices optimizados: {r!.Reorganizados + r.Reconstruidos}\n" +
            $"Archivos temporales eliminados: {r.TemporalesBorrados}" + (r.LogReducido ? "\nArchivo de registro reducido." : ""));
        await CargarAsync();
    }
}
