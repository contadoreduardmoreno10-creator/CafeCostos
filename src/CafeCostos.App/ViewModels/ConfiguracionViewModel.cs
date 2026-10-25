namespace CafeCostos.App.ViewModels;

/// <summary>Datos de la empresa (para el comprobante) y tarifas de costeo de conversión.</summary>
public sealed class ConfiguracionViewModel : ViewModelBase
{
    public ConfiguracionViewModel(ContextoApp ctx) : base(ctx)
    {
        GuardarCommand = new AsyncCommand(GuardarAsync);
    }

    public override string Titulo => "Configuración";
    public AsyncCommand GuardarCommand { get; }

    private string _nombre = "", _nit = "", _direccion = "", _telefono = "", _ciudad = "", _pie = "";
    public string Nombre { get => _nombre; set => Set(ref _nombre, value); }
    public string Nit { get => _nit; set => Set(ref _nit, value); }
    public string Direccion { get => _direccion; set => Set(ref _direccion, value); }
    public string Telefono { get => _telefono; set => Set(ref _telefono, value); }
    public string Ciudad { get => _ciudad; set => Set(ref _ciudad, value); }
    public string PiePagina { get => _pie; set => Set(ref _pie, value); }

    private decimal _energiaKg, _manoObraKg, _moliendaKg, _manoObraUnidad, _mermaMolienda, _margen;
    private int _diasCredito;
    public decimal EnergiaPorKg { get => _energiaKg; set => Set(ref _energiaKg, value); }
    public decimal ManoObraTuestePorKg { get => _manoObraKg; set => Set(ref _manoObraKg, value); }
    public decimal MoliendaPorKg { get => _moliendaKg; set => Set(ref _moliendaKg, value); }
    public decimal ManoObraEmpaquePorUnidad { get => _manoObraUnidad; set => Set(ref _manoObraUnidad, value); }
    public decimal MermaMoliendaEsperada { get => _mermaMolienda; set => Set(ref _mermaMolienda, value); }
    public decimal MargenObjetivo { get => _margen; set => Set(ref _margen, value); }
    public int DiasCreditoDefecto { get => _diasCredito; set => Set(ref _diasCredito, value); }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var p = Ctx.Parametros;
        Nombre = await p.TextoAsync("Empresa.Nombre");
        Nit = await p.TextoAsync("Empresa.NIT");
        Direccion = await p.TextoAsync("Empresa.Direccion");
        Telefono = await p.TextoAsync("Empresa.Telefono");
        Ciudad = await p.TextoAsync("Empresa.Ciudad");
        PiePagina = await p.TextoAsync("Factura.PiePagina");
        EnergiaPorKg = await p.DecimalAsync("Costeo.EnergiaPorKgSeco");
        ManoObraTuestePorKg = await p.DecimalAsync("Costeo.ManoObraTuestePorKgSeco");
        MoliendaPorKg = await p.DecimalAsync("Costeo.MoliendaPorKg");
        ManoObraEmpaquePorUnidad = await p.DecimalAsync("Costeo.ManoObraEmpaquePorUnidad");
        MermaMoliendaEsperada = await p.DecimalAsync("Costeo.MermaMoliendaEsperada", 1);
        MargenObjetivo = await p.DecimalAsync("Costeo.MargenObjetivo", 40);
        DiasCreditoDefecto = (int)await p.DecimalAsync("Ventas.DiasCreditoDefecto", 30);
    });

    private async Task GuardarAsync()
    {
        var ok = await EjecutarAsync(async () =>
        {
            Core.Validar.Texto(Nombre, "Nombre del negocio");
            Core.Validar.Que(MermaMoliendaEsperada is >= 0 and < 20, "La merma de molienda esperada debe estar entre 0 y 20 %.");
            Core.Validar.Que(MargenObjetivo is >= 0 and < 100, "El margen objetivo debe estar entre 0 y 99 %.");
            Core.Validar.Que(DiasCreditoDefecto is >= 0 and <= 365, "Los días de crédito deben estar entre 0 y 365.");
            var p = Ctx.Parametros;
            await p.GuardarAsync("Empresa.Nombre", Nombre.Trim());
            await p.GuardarAsync("Empresa.NIT", Nit.Trim());
            await p.GuardarAsync("Empresa.Direccion", Direccion.Trim());
            await p.GuardarAsync("Empresa.Telefono", Telefono.Trim());
            await p.GuardarAsync("Empresa.Ciudad", Ciudad.Trim());
            await p.GuardarAsync("Factura.PiePagina", PiePagina.Trim());
            await p.GuardarAsync("Costeo.EnergiaPorKgSeco", EnergiaPorKg);
            await p.GuardarAsync("Costeo.ManoObraTuestePorKgSeco", ManoObraTuestePorKg);
            await p.GuardarAsync("Costeo.MoliendaPorKg", MoliendaPorKg);
            await p.GuardarAsync("Costeo.ManoObraEmpaquePorUnidad", ManoObraEmpaquePorUnidad);
            await p.GuardarAsync("Costeo.MermaMoliendaEsperada", MermaMoliendaEsperada);
            await p.GuardarAsync("Costeo.MargenObjetivo", MargenObjetivo);
            await p.GuardarAsync("Ventas.DiasCreditoDefecto", DiasCreditoDefecto);
        });
        if (ok) Ctx.Dialogos.Informar("Configuración guardada.");
    }
}
