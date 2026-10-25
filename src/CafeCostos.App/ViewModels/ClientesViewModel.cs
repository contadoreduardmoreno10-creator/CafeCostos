using System.Collections.ObjectModel;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.ViewModels;

public sealed class ClientesViewModel : ViewModelBase
{
    public ClientesViewModel(ContextoApp ctx) : base(ctx)
    {
        NuevoCommand = new RelayCommand(Nuevo);
        GuardarCommand = new AsyncCommand(GuardarAsync);
        BuscarCommand = new AsyncCommand(CargarAsync);
        Nuevo();
    }

    public override string Titulo => "Clientes";

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public RelayCommand NuevoCommand { get; }
    public AsyncCommand GuardarCommand { get; }
    public AsyncCommand BuscarCommand { get; }

    private string? _filtro;
    public string? Filtro { get => _filtro; set => Set(ref _filtro, value); }

    private Cliente? _seleccionado;
    public Cliente? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (!Set(ref _seleccionado, value) || value is null) return;
            // Se edita una copia para no alterar la grilla hasta guardar.
            Edicion = new Cliente
            {
                IdCliente = value.IdCliente, Documento = value.Documento, Nombre = value.Nombre, Telefono = value.Telefono,
                Direccion = value.Direccion, Ciudad = value.Ciudad, Email = value.Email, CupoCredito = value.CupoCredito,
                DiasCredito = value.DiasCredito, EsGenerico = value.EsGenerico, Activo = value.Activo
            };
            SinLimite = value.CupoCredito is null;
        }
    }

    private Cliente _edicion = new();
    public Cliente Edicion { get => _edicion; private set { if (Set(ref _edicion, value)) OnPropertyChanged(nameof(TituloFormulario)); } }
    public string TituloFormulario => Edicion.IdCliente == 0 ? "Nuevo cliente" : "Editar cliente";

    private bool _sinLimite;
    /// <summary>Si está marcado, el cliente tiene crédito sin tope (CupoCredito = null).</summary>
    public bool SinLimite { get => _sinLimite; set => Set(ref _sinLimite, value); }

    public override Task CargarAsync() => EjecutarAsync(async () =>
    {
        Clientes.Clear();
        foreach (var c in await Ctx.Clientes.ListarAsync(Filtro)) Clientes.Add(c);
    });

    private void Nuevo()
    {
        _seleccionado = null;
        OnPropertyChanged(nameof(Seleccionado));
        Edicion = new Cliente { DiasCredito = 30, CupoCredito = 0, Activo = true };
        SinLimite = false;
    }

    private async Task GuardarAsync()
    {
        Edicion.CupoCredito = SinLimite ? null : Edicion.CupoCredito ?? 0;
        var nuevo = Edicion.IdCliente == 0;
        if (await EjecutarAsync(() => Ctx.Clientes.GuardarAsync(Edicion)))
        {
            Ctx.Dialogos.Informar(nuevo ? $"Cliente «{Edicion.Nombre}» creado." : "Cambios guardados.");
            Nuevo();
            await CargarAsync();
        }
    }
}
