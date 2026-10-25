using System.Collections.ObjectModel;
using System.Globalization;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

/// <summary>Insumos de empaque: maestro, compras, ajustes por conteo y kardex.</summary>
public sealed class InsumosViewModel : ViewModelBase
{
    public InsumosViewModel(ContextoApp ctx) : base(ctx)
    {
        NuevoCommand = new RelayCommand(Nuevo);
        GuardarCommand = new AsyncCommand(GuardarAsync);
        ComprarCommand = new AsyncCommand(ComprarAsync, () => Seleccionado is not null);
        AjustarCommand = new AsyncCommand(AjustarAsync, () => Seleccionado is not null);
        Nuevo();
    }

    public override string Titulo => "Insumos de empaque";

    public ObservableCollection<Insumo> Insumos { get; } = new();
    public ObservableCollection<MovimientoInsumo> Kardex { get; } = new();
    public RelayCommand NuevoCommand { get; }
    public AsyncCommand GuardarCommand { get; }
    public AsyncCommand ComprarCommand { get; }
    public AsyncCommand AjustarCommand { get; }

    private Insumo? _seleccionado;
    public Insumo? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (!Set(ref _seleccionado, value)) return;
            ComprarCommand.Refrescar();
            AjustarCommand.Refrescar();
            if (value is not null)
            {
                Edicion = new Insumo
                {
                    IdInsumo = value.IdInsumo, Codigo = value.Codigo, Nombre = value.Nombre, Unidad = value.Unidad,
                    StockMinimo = value.StockMinimo, UltimoCosto = value.UltimoCosto, Activo = value.Activo, Stock = value.Stock
                };
                _ = CargarKardexAsync();
            }
        }
    }

    private Insumo _edicion = new();
    public Insumo Edicion { get => _edicion; private set => Set(ref _edicion, value); }

    // ---- Compra ----
    private DateTime _fechaCompra = DateTime.Today;
    public DateTime FechaCompra { get => _fechaCompra; set => Set(ref _fechaCompra, value); }
    private decimal _cantidadCompra;
    public decimal CantidadCompra { get => _cantidadCompra; set { if (Set(ref _cantidadCompra, value)) OnPropertyChanged(nameof(CostoUnitarioCompra)); } }
    private decimal _valorCompra;
    public decimal ValorCompra { get => _valorCompra; set { if (Set(ref _valorCompra, value)) OnPropertyChanged(nameof(CostoUnitarioCompra)); } }
    private string? _documentoCompra;
    public string? DocumentoCompra { get => _documentoCompra; set => Set(ref _documentoCompra, value); }
    private string? _proveedorCompra;
    public string? ProveedorCompra { get => _proveedorCompra; set => Set(ref _proveedorCompra, value); }
    public decimal CostoUnitarioCompra => CantidadCompra > 0 ? Math.Round(ValorCompra / CantidadCompra, 2) : 0;

    public decimal ValorTotalInventario => Insumos.Sum(i => i.ValorStock);

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        var id = Seleccionado?.IdInsumo;
        Insumos.Clear();
        foreach (var i in await Ctx.Insumos.ListarAsync()) Insumos.Add(i);
        OnPropertyChanged(nameof(ValorTotalInventario));
        Seleccionado = Insumos.FirstOrDefault(i => i.IdInsumo == id);
        if (Seleccionado is not null) await CargarKardexAsync();
    });

    private async Task CargarKardexAsync()
    {
        Kardex.Clear();
        if (Seleccionado is null) return;
        foreach (var m in await Ctx.Insumos.KardexAsync(Seleccionado.IdInsumo)) Kardex.Add(m);
    }

    private void Nuevo()
    {
        _seleccionado = null;
        OnPropertyChanged(nameof(Seleccionado));
        ComprarCommand.Refrescar();
        AjustarCommand.Refrescar();
        Kardex.Clear();
        Edicion = new Insumo { Unidad = "und", Activo = true };
    }

    private async Task GuardarAsync()
    {
        if (await EjecutarAsync(() => Ctx.Insumos.GuardarAsync(Edicion)))
        {
            Ctx.Dialogos.Informar("Insumo guardado.");
            await CargarAsync();
        }
    }

    private async Task ComprarAsync()
    {
        if (Seleccionado is null) return;
        var sel = Seleccionado;
        var ok = await EjecutarAsync(() => Ctx.Insumos.RegistrarCompraAsync(new CompraInsumo
        {
            IdInsumo = sel.IdInsumo, Fecha = FechaCompra, Cantidad = CantidadCompra, ValorTotal = ValorCompra,
            Documento = DocumentoCompra, Proveedor = ProveedorCompra
        }));
        if (!ok) return;
        Ctx.Dialogos.Informar($"Compra registrada: {CantidadCompra:N0} {sel.Unidad} de {sel.Nombre} a {CostoUnitarioCompra:C2} c/u.");
        CantidadCompra = 0; ValorCompra = 0; DocumentoCompra = null; ProveedorCompra = null; FechaCompra = DateTime.Today;
        await CargarAsync();
    }

    private async Task AjustarAsync()
    {
        if (Seleccionado is null) return;
        var sel = Seleccionado;
        var texto = Ctx.Dialogos.PedirTexto(
            $"{sel.Nombre}\nEl sistema registra {sel.Stock:N0} {sel.Unidad}.\n¿Cuántas unidades contó físicamente?",
            "Ajuste por conteo físico", sel.Stock.ToString("0.###", CultureInfo.CurrentCulture));
        if (texto is null) return;
        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.CurrentCulture, out var conteo) || conteo < 0)
        {
            Ctx.Dialogos.Advertir("Escriba un número válido.");
            return;
        }
        var motivo = Ctx.Dialogos.PedirTexto("Motivo del ajuste (ej.: bolsas dañadas, conteo mensual):", "Motivo", "Conteo físico");
        if (string.IsNullOrWhiteSpace(motivo)) return;
        if (await EjecutarAsync(() => Ctx.Insumos.AjustarConteoAsync(sel.IdInsumo, conteo, motivo)))
            await CargarAsync();
    }
}
