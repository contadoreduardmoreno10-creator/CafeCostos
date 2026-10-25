using System.Collections.ObjectModel;
using CafeCostos.Core.Modelos;
using CafeCostos.Data.Servicios;

namespace CafeCostos.App.ViewModels;

/// <summary>Rentabilidad por presentación y trazabilidad factura → lote empacado → tueste → lote seco.</summary>
public sealed class ReportesViewModel : ViewModelBase
{
    public ReportesViewModel(ContextoApp ctx) : base(ctx)
    {
        ConsultarCommand = new AsyncCommand(ConsultarVentasAsync);
        BuscarCommand = new AsyncCommand(BuscarTrazabilidadAsync);
    }

    public override string Titulo => "Reportes";

    public ObservableCollection<VentaPorPresentacion> VentasPresentacion { get; } = new();
    public ObservableCollection<LineaTrazabilidad> Trazabilidad { get; } = new();
    public AsyncCommand ConsultarCommand { get; }
    public AsyncCommand BuscarCommand { get; }

    private DateTime _desde = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime Desde { get => _desde; set => Set(ref _desde, value); }
    private DateTime _hasta = DateTime.Today;
    public DateTime Hasta { get => _hasta; set => Set(ref _hasta, value); }
    private string? _buscar;
    public string? Buscar { get => _buscar; set => Set(ref _buscar, value); }

    public decimal TotalVentas => VentasPresentacion.Sum(v => v.Ventas);
    public decimal TotalCosto => VentasPresentacion.Sum(v => v.Costo);
    public decimal TotalUtilidad => TotalVentas - TotalCosto;
    public decimal MargenTotal => TotalVentas > 0 ? Math.Round(TotalUtilidad / TotalVentas * 100m, 1) : 0;

    public override async Task CargarAsync()
    {
        await ConsultarVentasAsync();
        await BuscarTrazabilidadAsync();
    }

    private Task ConsultarVentasAsync() => EjecutarAsync(async () =>
    {
        VentasPresentacion.Clear();
        foreach (var v in await Ctx.Reportes.VentasPorPresentacionAsync(Desde, Hasta)) VentasPresentacion.Add(v);
        OnPropertyChanged(nameof(TotalVentas));
        OnPropertyChanged(nameof(TotalCosto));
        OnPropertyChanged(nameof(TotalUtilidad));
        OnPropertyChanged(nameof(MargenTotal));
    });

    private Task BuscarTrazabilidadAsync() => EjecutarAsync(async () =>
    {
        Trazabilidad.Clear();
        foreach (var t in await Ctx.Reportes.TrazabilidadAsync(Buscar)) Trazabilidad.Add(t);
    });
}
