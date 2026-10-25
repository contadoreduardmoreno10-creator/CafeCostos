using System.Collections.ObjectModel;
using CafeCostos.Core.Cartera;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

/// <summary>Paso 6 del ciclo: cartera, abonos y vencimientos.</summary>
public sealed class CarteraViewModel : ViewModelBase
{
    public CarteraViewModel(ContextoApp ctx) : base(ctx)
    {
        RegistrarAbonoCommand = new AsyncCommand(() => RegistrarAbonoAsync(false), () => Factura is not null && ValorAbono > 0);
        RegistrarImprimirCommand = new AsyncCommand(() => RegistrarAbonoAsync(true), () => Factura is not null && ValorAbono > 0);
        PagarTodoCommand = new RelayCommand(() => ValorAbono = Factura?.Saldo ?? 0, () => Factura is not null);
        AnularAbonoCommand = new AsyncCommand(AnularAbonoAsync, () => AbonoSeleccionado is { Anulado: false });
        ImprimirReciboCommand = new AsyncCommand(ImprimirReciboAsync, () => AbonoSeleccionado is { Anulado: false });
        ActualizarCommand = new AsyncCommand(CargarAsync);
        LimpiarFiltroCommand = new RelayCommand(() => FiltroCliente = null);
    }

    public override string Titulo => "Cartera · Cuentas por cobrar";

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<Venta> Pendientes { get; } = new();
    public ObservableCollection<Abono> Abonos { get; } = new();
    public IReadOnlyList<string> MediosPago { get; } = new[] { "Efectivo", "Transferencia", "Nequi", "Daviplata", "Tarjeta", "Otro" };

    public AsyncCommand RegistrarAbonoCommand { get; }
    public AsyncCommand RegistrarImprimirCommand { get; }
    public RelayCommand PagarTodoCommand { get; }
    public AsyncCommand AnularAbonoCommand { get; }
    public AsyncCommand ImprimirReciboCommand { get; }
    public AsyncCommand ActualizarCommand { get; }
    public RelayCommand LimpiarFiltroCommand { get; }

    private ResumenEdades _edades = new(0, 0, 0, 0, 0);
    public ResumenEdades Edades { get => _edades; private set => Set(ref _edades, value); }

    private Cliente? _filtroCliente;
    /// <summary>null = todos los clientes.</summary>
    public Cliente? FiltroCliente
    {
        get => _filtroCliente;
        set { if (Set(ref _filtroCliente, value)) _ = CargarPendientesAsync(); }
    }

    private Venta? _factura;
    public Venta? Factura
    {
        get => _factura;
        set
        {
            if (!Set(ref _factura, value)) return;
            ValorAbono = 0;
            PagarTodoCommand.Refrescar();
            RefrescarComandos();
            _ = CargarAbonosAsync();
        }
    }

    private DateTime _fechaAbono = DateTime.Today;
    public DateTime FechaAbono { get => _fechaAbono; set => Set(ref _fechaAbono, value); }
    private decimal _valorAbono;
    public decimal ValorAbono { get => _valorAbono; set { if (Set(ref _valorAbono, value)) RefrescarComandos(); } }
    private string? _medioPago = "Efectivo";
    public string? MedioPago { get => _medioPago; set => Set(ref _medioPago, value); }
    private string? _referencia;
    public string? Referencia { get => _referencia; set => Set(ref _referencia, value); }

    private Abono? _abonoSeleccionado;
    public Abono? AbonoSeleccionado
    {
        get => _abonoSeleccionado;
        set { if (Set(ref _abonoSeleccionado, value)) { AnularAbonoCommand.Refrescar(); ImprimirReciboCommand.Refrescar(); } }
    }

    public decimal TotalFiltrado => Pendientes.Sum(p => p.Saldo);

    private void RefrescarComandos()
    {
        RegistrarAbonoCommand.Refrescar();
        RegistrarImprimirCommand.Refrescar();
    }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var idFiltro = FiltroCliente?.IdCliente;
        Clientes.Clear();
        foreach (var c in (await Ctx.Clientes.ListarAsync()).Where(c => !c.EsGenerico)) Clientes.Add(c);
        _filtroCliente = Clientes.FirstOrDefault(c => c.IdCliente == idFiltro);
        OnPropertyChanged(nameof(FiltroCliente));
        Edades = await Ctx.Cartera.ResumenEdadesAsync();
        await CargarPendientesAsync();
    });

    private async Task CargarPendientesAsync()
    {
        var idFactura = Factura?.IdVenta;
        Pendientes.Clear();
        foreach (var p in await Ctx.Cartera.PendientesAsync(FiltroCliente?.IdCliente)) Pendientes.Add(p);
        Factura = Pendientes.FirstOrDefault(p => p.IdVenta == idFactura);
        OnPropertyChanged(nameof(TotalFiltrado));
        if (Factura is null) await CargarAbonosAsync();
    }

    private async Task CargarAbonosAsync()
    {
        Abonos.Clear();
        // Con factura seleccionada: sus abonos. Sin selección: los recibos de los últimos 30 días.
        var lista = Factura is not null
            ? await Ctx.Cartera.AbonosAsync(idVenta: Factura.IdVenta)
            : await Ctx.Cartera.AbonosAsync(desde: DateTime.Today.AddDays(-30));
        foreach (var a in lista) Abonos.Add(a);
    }

    private async Task RegistrarAbonoAsync(bool imprimir)
    {
        if (Factura is null) return;
        if (!Ctx.Dialogos.Confirmar(
                $"Registrar abono de {ValorAbono:C0} a la factura {Factura.NumeroFactura} de {Factura.Cliente}?\n" +
                $"Saldo después del abono: {Factura.Saldo - ValorAbono:C0}", "Confirmar abono")) return;

        string? recibo = null;
        var ok = await EjecutarAsync(async () =>
        {
            recibo = await Ctx.Cartera.RegistrarAbonoAsync(new NuevoAbono
            {
                IdVenta = Factura.IdVenta, Fecha = FechaAbono, Valor = ValorAbono, MedioPago = MedioPago, Referencia = Referencia
            });
        });
        if (!ok) return;

        if (imprimir)
        {
            var abono = (await Ctx.Cartera.AbonosAsync(idVenta: Factura.IdVenta)).FirstOrDefault(a => a.NumeroRecibo == recibo);
            if (abono is not null) await EjecutarAsync(() => Ctx.Impresion.ImprimirReciboAsync(abono.IdAbono));
        }
        else Ctx.Dialogos.Informar($"Recibo {recibo} registrado.");

        ValorAbono = 0; Referencia = null; FechaAbono = DateTime.Today;
        await CargarAsync();
    }

    private async Task AnularAbonoAsync()
    {
        if (AbonoSeleccionado is null) return;
        if (!Ctx.Dialogos.Confirmar($"¿Anular el recibo {AbonoSeleccionado.NumeroRecibo} por {AbonoSeleccionado.Valor:C0}? El saldo de la factura aumentará.")) return;
        if (await EjecutarAsync(() => Ctx.Cartera.AnularAbonoAsync(AbonoSeleccionado.IdAbono)))
            await CargarAsync();
    }

    private Task ImprimirReciboAsync() =>
        AbonoSeleccionado is null ? Task.CompletedTask : EjecutarAsync(() => Ctx.Impresion.ImprimirReciboAsync(AbonoSeleccionado.IdAbono));
}
