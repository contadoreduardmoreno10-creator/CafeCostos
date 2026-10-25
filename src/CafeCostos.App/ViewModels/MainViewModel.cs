using System.Collections.ObjectModel;

namespace CafeCostos.App.ViewModels;

public sealed class ItemMenu : ObservableObject
{
    public required string Clave { get; init; }
    public required string Titulo { get; init; }
    /// <summary>Glifo de la fuente «Segoe MDL2 Assets» (incluida en Windows 10/11).</summary>
    public required string Icono { get; init; }
    public required Func<ViewModelBase> Crear { get; init; }
    public bool SeparadorAntes { get; init; }

    private bool _activo;
    public bool Activo { get => _activo; set => Set(ref _activo, value); }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly ContextoApp _ctx;
    private readonly Dictionary<string, ViewModelBase> _cache = new();

    public MainViewModel(ContextoApp ctx)
    {
        _ctx = ctx;
        Menu = new ObservableCollection<ItemMenu>
        {
            new() { Clave = "panel",     Titulo = "Inicio",            Icono = "\uE80F", Crear = () => new PanelViewModel(ctx, Navegar) },
            new() { Clave = "lotes",     Titulo = "Café seco",         Icono = "\uE8F1", Crear = () => new LotesViewModel(ctx), SeparadorAntes = true },
            new() { Clave = "tueste",    Titulo = "Tostión",           Icono = "\uE706", Crear = () => new TuesteViewModel(ctx) },
            new() { Clave = "empaque",   Titulo = "Molienda y empaque", Icono = "\uE7B8", Crear = () => new EmpaqueViewModel(ctx) },
            new() { Clave = "ventas",    Titulo = "Ventas",            Icono = "\uE719", Crear = () => new VentasViewModel(ctx), SeparadorAntes = true },
            new() { Clave = "cartera",   Titulo = "Cartera",           Icono = "\uE8C7", Crear = () => new CarteraViewModel(ctx) },
            new() { Clave = "clientes",  Titulo = "Clientes",          Icono = "\uE716", Crear = () => new ClientesViewModel(ctx) },
            new() { Clave = "insumos",   Titulo = "Insumos",           Icono = "\uE7BF", Crear = () => new InsumosViewModel(ctx), SeparadorAntes = true },
            new() { Clave = "presentaciones", Titulo = "Presentaciones y precios", Icono = "\uE8EC", Crear = () => new PresentacionesViewModel(ctx) },
            new() { Clave = "reportes",  Titulo = "Reportes",          Icono = "\uE9D2", Crear = () => new ReportesViewModel(ctx) },
            new() { Clave = "respaldo",  Titulo = "Copias de seguridad", Icono = "\uE74E", Crear = () => new RespaldoViewModel(ctx), SeparadorAntes = true },
            new() { Clave = "config",    Titulo = "Configuración",     Icono = "\uE713", Crear = () => new ConfiguracionViewModel(ctx) },
        };
        NavegarCommand = new RelayCommand(p => Navegar(p as string ?? "panel"));
    }

    public ObservableCollection<ItemMenu> Menu { get; }
    public RelayCommand NavegarCommand { get; }

    private ViewModelBase? _vistaActual;
    public ViewModelBase? VistaActual { get => _vistaActual; private set => Set(ref _vistaActual, value); }

    private string _nombreEmpresa = "CaféCostos";
    public string NombreEmpresa { get => _nombreEmpresa; set => Set(ref _nombreEmpresa, value); }

    public async Task IniciarAsync()
    {
        NombreEmpresa = await _ctx.Parametros.TextoAsync("Empresa.Nombre", "CaféCostos");
        Navegar("panel");
    }

    public async void Navegar(string clave)
    {
        var item = Menu.FirstOrDefault(m => m.Clave == clave) ?? Menu[0];
        foreach (var m in Menu) m.Activo = m == item;
        if (!_cache.TryGetValue(item.Clave, out var vm))
            _cache[item.Clave] = vm = item.Crear();
        VistaActual = vm;
        try { await vm.CargarAsync(); }
        catch (Exception ex)
        {
            _ctx.Registro.Error(ex);
            _ctx.Dialogos.Error("No se pudo cargar la pantalla: " + ex.Message);
        }
        if (clave == "config") NombreEmpresa = await _ctx.Parametros.TextoAsync("Empresa.Nombre", "CaféCostos");
    }
}
