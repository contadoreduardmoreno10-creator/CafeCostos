using System.Collections.ObjectModel;
using CafeCostos.Core;
using CafeCostos.Core.Costeo;
using CafeCostos.Core.Modelos;
using CafeCostos.Core.Ventas;

namespace CafeCostos.App.ViewModels;

public sealed class LineaVentaVm : ObservableObject
{
    public required LoteEmpacado Lote { get; init; }
    public required int Cantidad { get; init; }
    public required decimal Precio { get; init; }
    public decimal Subtotal => Redondeo.Dinero(Cantidad * Precio);
    public decimal Costo => CalculadoraCostos.CostoSalida(Lote.UnidadesDisponibles, Lote.ValorDisponible, Cantidad);
    public decimal Utilidad => Subtotal - Costo;
}

/// <summary>Paso 5 del ciclo: venta de contado o crédito asociada al lote de producción.</summary>
public sealed class VentasViewModel : ViewModelBase
{
    public VentasViewModel(ContextoApp ctx) : base(ctx)
    {
        AgregarCommand = new RelayCommand(Agregar, () => LoteElegido is not null && Cantidad > 0);
        QuitarCommand = new RelayCommand(p => { if (p is LineaVentaVm l) { Lineas.Remove(l); Recalcular(); } });
        RegistrarCommand = new AsyncCommand(() => RegistrarAsync(imprimir: false), () => Lineas.Count > 0 && Cliente is not null);
        RegistrarImprimirCommand = new AsyncCommand(() => RegistrarAsync(imprimir: true), () => Lineas.Count > 0 && Cliente is not null);
        LimpiarCommand = new RelayCommand(Limpiar);
        BuscarCommand = new AsyncCommand(CargarHistorialAsync);
        ImprimirCommand = new AsyncCommand(ImprimirSeleccionadaAsync, () => Seleccionada is not null);
        AnularCommand = new AsyncCommand(AnularAsync, () => Seleccionada is not null && Seleccionada.Estado != Estados.Anulada);
    }

    public override string Titulo => "Ventas · Comprobante de venta interno";

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<LoteEmpacado> LotesDisponibles { get; } = new();
    public ObservableCollection<LineaVentaVm> Lineas { get; } = new();
    public ObservableCollection<Venta> Historial { get; } = new();
    public ObservableCollection<DetalleVenta> DetalleSeleccionada { get; } = new();
    public IReadOnlyList<string> MediosPago { get; } = new[] { "Efectivo", "Transferencia", "Nequi", "Daviplata", "Tarjeta", "Otro" };

    public RelayCommand AgregarCommand { get; }
    public RelayCommand QuitarCommand { get; }
    public AsyncCommand RegistrarCommand { get; }
    public AsyncCommand RegistrarImprimirCommand { get; }
    public RelayCommand LimpiarCommand { get; }
    public AsyncCommand BuscarCommand { get; }
    public AsyncCommand ImprimirCommand { get; }
    public AsyncCommand AnularCommand { get; }

    // ---- Encabezado ----
    private Cliente? _cliente;
    public Cliente? Cliente
    {
        get => _cliente;
        set
        {
            if (!Set(ref _cliente, value)) return;
            if (value is { EsGenerico: true }) EsCredito = false;
            DiasCredito = value?.DiasCredito ?? 30;
            OnPropertyChanged(nameof(InfoCliente));
            OnPropertyChanged(nameof(PuedeCredito));
            RefrescarComandos();
        }
    }
    /// <summary>El «Consumidor final» solo puede comprar de contado.</summary>
    public bool PuedeCredito => Cliente is { EsGenerico: false };

    public string InfoCliente => Cliente is null || Cliente.EsGenerico ? "" :
        $"Saldo pendiente: {Cliente.SaldoPendiente:C0}" +
        (Cliente.SaldoVencido > 0 ? $"  ·  Vencido: {Cliente.SaldoVencido:C0}" : "") +
        (Cliente.CupoCredito is { } c ? $"  ·  Cupo: {c:C0}" : "  ·  Cupo: sin límite");

    private DateTime _fecha = DateTime.Today;
    public DateTime Fecha { get => _fecha; set => Set(ref _fecha, value); }

    private bool _esCredito;
    public bool EsCredito
    {
        get => _esCredito;
        set
        {
            if (value && Cliente is { EsGenerico: true })
            {
                Ctx.Dialogos.Advertir("Para vender a crédito seleccione un cliente registrado (no «Consumidor final»).");
                value = false;
            }
            if (Set(ref _esCredito, value)) { OnPropertyChanged(nameof(EsContado)); Recalcular(); }
        }
    }
    public bool EsContado { get => !EsCredito; set => EsCredito = !value; }

    private string? _medioPago = "Efectivo";
    public string? MedioPago { get => _medioPago; set => Set(ref _medioPago, value); }
    private int _diasCredito = 30;
    public int DiasCredito { get => _diasCredito; set => Set(ref _diasCredito, value); }
    private decimal _descuento;
    public decimal Descuento { get => _descuento; set { if (Set(ref _descuento, value)) Recalcular(); } }
    private string? _observaciones;
    public string? Observaciones { get => _observaciones; set => Set(ref _observaciones, value); }

    // ---- Captura de líneas ----
    private LoteEmpacado? _loteElegido;
    public LoteEmpacado? LoteElegido
    {
        get => _loteElegido;
        set
        {
            if (!Set(ref _loteElegido, value)) return;
            Precio = value?.PrecioVenta ?? 0;
            Cantidad = value is null ? 0 : 1;
            AgregarCommand.Refrescar();
        }
    }
    private int _cantidad;
    public int Cantidad { get => _cantidad; set { if (Set(ref _cantidad, value)) AgregarCommand.Refrescar(); } }
    private decimal _precio;
    public decimal Precio { get => _precio; set => Set(ref _precio, value); }

    // ---- Totales ----
    private TotalesVenta? _totales;
    public TotalesVenta? Totales { get => _totales; private set => Set(ref _totales, value); }
    private string? _mensajeTotales;
    public string? MensajeTotales { get => _mensajeTotales; private set => Set(ref _mensajeTotales, value); }

    private void Recalcular()
    {
        try
        {
            Totales = Lineas.Count == 0 ? null : CalculadoraVenta.Totales(
                Lineas.Select(l => new LineaVentaCosteada(l.Lote.IdInventarioEmpacado, l.Cantidad, l.Precio, l.Costo)).ToList(),
                Descuento, EsCredito ? TipoPago.Credito : TipoPago.Contado);
            MensajeTotales = null;
        }
        catch (ReglaNegocioException ex)
        {
            Totales = null;
            MensajeTotales = ex.Message;
        }
        RefrescarComandos();
    }

    private void RefrescarComandos()
    {
        RegistrarCommand.Refrescar();
        RegistrarImprimirCommand.Refrescar();
    }

    private void Agregar()
    {
        if (LoteElegido is null) return;
        var ya = Lineas.Where(l => l.Lote.IdInventarioEmpacado == LoteElegido.IdInventarioEmpacado).Sum(l => l.Cantidad);
        if (Cantidad <= 0 || ya + Cantidad > LoteElegido.UnidadesDisponibles)
        {
            Ctx.Dialogos.Advertir($"El lote {LoteElegido.CodigoLote} tiene {LoteElegido.UnidadesDisponibles - ya} unidades disponibles.");
            return;
        }
        if (Precio <= 0 && !Ctx.Dialogos.Confirmar("El precio es cero. ¿Desea agregarlo como obsequio?")) return;

        var existente = Lineas.FirstOrDefault(l => l.Lote.IdInventarioEmpacado == LoteElegido.IdInventarioEmpacado);
        if (existente is not null)
        {
            if (existente.Precio != Precio) { Ctx.Dialogos.Advertir("Ese lote ya está en la venta con otro precio."); return; }
            Lineas[Lineas.IndexOf(existente)] = new LineaVentaVm { Lote = existente.Lote, Cantidad = existente.Cantidad + Cantidad, Precio = Precio };
        }
        else Lineas.Add(new LineaVentaVm { Lote = LoteElegido, Cantidad = Cantidad, Precio = Precio });

        LoteElegido = null;
        Recalcular();
    }

    // ---- Historial ----
    private DateTime _desde = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime Desde { get => _desde; set => Set(ref _desde, value); }
    private DateTime _hasta = DateTime.Today;
    public DateTime Hasta { get => _hasta; set => Set(ref _hasta, value); }

    private Venta? _seleccionada;
    public Venta? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (!Set(ref _seleccionada, value)) return;
            ImprimirCommand.Refrescar();
            AnularCommand.Refrescar();
            _ = CargarDetalleAsync();
        }
    }

    public decimal TotalPeriodo => Historial.Where(v => v.Estado != Estados.Anulada).Sum(v => v.Total);
    public decimal UtilidadPeriodo => Historial.Where(v => v.Estado != Estados.Anulada).Sum(v => v.Utilidad);

    private async Task CargarDetalleAsync()
    {
        DetalleSeleccionada.Clear();
        if (Seleccionada is null) return;
        foreach (var d in await Ctx.Ventas.DetalleAsync(Seleccionada.IdVenta)) DetalleSeleccionada.Add(d);
    }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var idCliente = Cliente?.IdCliente;
        Clientes.Clear();
        foreach (var c in await Ctx.Clientes.ListarAsync(soloActivos: true)) Clientes.Add(c);
        Cliente = Clientes.FirstOrDefault(c => c.IdCliente == idCliente) ?? Clientes.FirstOrDefault(c => c.EsGenerico);

        LotesDisponibles.Clear();
        foreach (var l in await Ctx.Empaque.ListarAsync(soloDisponibles: true)) LotesDisponibles.Add(l);
        await CargarHistorialAsync();
    });

    private async Task CargarHistorialAsync()
    {
        Historial.Clear();
        foreach (var v in await Ctx.Ventas.ListarAsync(Desde, Hasta)) Historial.Add(v);
        OnPropertyChanged(nameof(TotalPeriodo));
        OnPropertyChanged(nameof(UtilidadPeriodo));
    }

    private async Task RegistrarAsync(bool imprimir)
    {
        if (Cliente is null || Totales is null) return;
        var t = Totales;
        var msg = $"Cliente: {Cliente.Nombre}\nForma de pago: {(EsCredito ? $"CRÉDITO a {DiasCredito} días" : $"CONTADO ({MedioPago})")}\n\n" +
                  $"Total a cobrar: {t.Total:C0}\nCosto de lo vendido: {t.CostoTotal:C0}\nUtilidad bruta: {t.UtilidadBruta:C0} ({t.MargenPct:N1} %)" +
                  (EsCredito && Cliente.SaldoVencido > 0 ? $"\n\n⚠ El cliente tiene {Cliente.SaldoVencido:C0} vencidos." : "") +
                  "\n\n¿Registrar la venta?";
        if (!Ctx.Dialogos.Confirmar(msg, "Confirmar venta")) return;

        int idVenta = 0;
        string? numero = null;
        var ok = await EjecutarAsync(async () =>
        {
            (idVenta, numero, _) = await Ctx.Ventas.RegistrarAsync(new NuevaVenta
            {
                Fecha = Fecha, IdCliente = Cliente.IdCliente, TipoPago = EsCredito ? TipoPago.Credito : TipoPago.Contado,
                MedioPago = MedioPago, DiasCredito = EsCredito ? DiasCredito : null, Descuento = Descuento,
                Observaciones = Observaciones,
                Lineas = Lineas.Select(l => new LineaVentaSolicitada(l.Lote.IdInventarioEmpacado, l.Cantidad, l.Precio)).ToList()
            });
        });
        if (!ok) return;

        if (imprimir) await EjecutarAsync(() => Ctx.Impresion.ImprimirVentaAsync(idVenta));
        else Ctx.Dialogos.Informar($"Venta {numero} registrada por {t.Total:C0}.");
        Limpiar();
        await CargarAsync();
    }

    private void Limpiar()
    {
        Lineas.Clear();
        Descuento = 0; Observaciones = null; EsCredito = false; MedioPago = "Efectivo";
        Fecha = DateTime.Today;
        Cliente = Clientes.FirstOrDefault(c => c.EsGenerico);
        LoteElegido = null;
        Recalcular();
    }

    private Task ImprimirSeleccionadaAsync() =>
        Seleccionada is null ? Task.CompletedTask : EjecutarAsync(() => Ctx.Impresion.ImprimirVentaAsync(Seleccionada.IdVenta));

    private async Task AnularAsync()
    {
        if (Seleccionada is null) return;
        var motivo = Ctx.Dialogos.PedirTexto(
            $"Va a anular la venta {Seleccionada.NumeroFactura}. Las unidades regresarán al inventario.\nEscriba el motivo:",
            "Anular venta");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        if (await EjecutarAsync(() => Ctx.Ventas.AnularAsync(Seleccionada.IdVenta, motivo)))
            await CargarAsync();
    }
}
