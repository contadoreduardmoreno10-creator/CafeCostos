using System.Collections.ObjectModel;
using CafeCostos.Core.Cartera;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

/// <summary>Panel de control: indicadores del negocio, alertas y accesos rápidos.</summary>
public sealed class PanelViewModel : ViewModelBase
{
    private readonly Action<string> _navegar;

    public PanelViewModel(ContextoApp ctx, Action<string> navegar) : base(ctx)
    {
        _navegar = navegar;
        IrCommand = new RelayCommand(p => _navegar(p as string ?? "panel"));
        ActualizarCommand = new AsyncCommand(CargarAsync);
    }

    public override string Titulo => "Panel de control";
    public RelayCommand IrCommand { get; }
    public AsyncCommand ActualizarCommand { get; }

    private ResumenPanel _resumen = new();
    public ResumenPanel Resumen { get => _resumen; private set => Set(ref _resumen, value); }

    private ResumenEdades _edades = new(0, 0, 0, 0, 0);
    public ResumenEdades Edades { get => _edades; private set => Set(ref _edades, value); }

    public ObservableCollection<string> Alertas { get; } = new();
    public string Saludo => DateTime.Now.Hour switch { < 12 => "Buenos días", < 19 => "Buenas tardes", _ => "Buenas noches" };
    public string Fecha => DateTime.Today.ToString("dddd, d 'de' MMMM 'de' yyyy");

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        Resumen = await Ctx.Reportes.ResumenPanelAsync();
        Edades = await Ctx.Cartera.ResumenEdadesAsync();

        Alertas.Clear();
        if (Resumen.InsumosBajoMinimo > 0)
        {
            var bajos = (await Ctx.Insumos.ListarAsync(soloActivos: true)).Where(i => i.BajoMinimo).Select(i => i.Nombre);
            Alertas.Add($"Insumos por agotarse: {string.Join(", ", bajos)}.");
        }
        if (Edades.Vencida > 0)
            Alertas.Add($"Hay {Edades.Vencida:C0} en cartera vencida. Revise el módulo Cartera.");

        var carpeta = await Ctx.CarpetaRespaldosAsync();
        var ultimo = Ctx.Respaldos.Listar(carpeta).FirstOrDefault();
        if (ultimo is null)
            Alertas.Add("Todavía no tiene copias de seguridad. Cree una en «Copias de seguridad».");
        else if ((DateTime.Now - ultimo.Fecha).TotalDays > 7)
            Alertas.Add($"La última copia de seguridad es del {ultimo.Fecha:dd/MM/yyyy}. Cree una nueva y guárdela en USB.");

        var porVencer = (await Ctx.Empaque.ListarAsync(soloDisponibles: true))
            .Count(l => l.FechaVencimiento is { } f && f <= DateTime.Today.AddDays(30));
        if (porVencer > 0)
            Alertas.Add($"{porVencer} lote(s) de producto terminado vencen en los próximos 30 días.");

        if (Alertas.Count == 0) Alertas.Add("Todo en orden. ¡Buen trabajo!");
        OnPropertyChanged(nameof(Saludo));
    });
}
