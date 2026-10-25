using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CafeCostos.Core;

namespace CafeCostos.App.ViewModels;

/// <summary>Base mínima de MVVM (sin dependencias externas para mantener el instalador liviano).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? nombre = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));

    protected bool Set<T>(ref T campo, T valor, [CallerMemberName] string? nombre = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor;
        OnPropertyChanged(nombre);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _ejecutar;
    private readonly Func<object?, bool>? _puede;

    public RelayCommand(Action<object?> ejecutar, Func<object?, bool>? puede = null)
    {
        _ejecutar = ejecutar;
        _puede = puede;
    }

    public RelayCommand(Action ejecutar, Func<bool>? puede = null)
        : this(_ => ejecutar(), puede is null ? null : _ => puede()) { }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _puede?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _ejecutar(parameter);
    public void Refrescar() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Comando asíncrono que no permite doble clic mientras se ejecuta.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _ejecutar;
    private readonly Func<object?, bool>? _puede;
    private bool _enCurso;

    public AsyncCommand(Func<object?, Task> ejecutar, Func<object?, bool>? puede = null)
    {
        _ejecutar = ejecutar;
        _puede = puede;
    }

    public AsyncCommand(Func<Task> ejecutar, Func<bool>? puede = null)
        : this(_ => ejecutar(), puede is null ? null : _ => puede()) { }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_enCurso && (_puede?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _enCurso = true;
        Refrescar();
        try { await _ejecutar(parameter); }
        finally
        {
            _enCurso = false;
            Refrescar();
        }
    }

    public void Refrescar() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Base de las pantallas: título, indicador de ocupado y manejo uniforme de errores.</summary>
public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase(ContextoApp ctx) => Ctx = ctx;

    protected ContextoApp Ctx { get; }

    public abstract string Titulo { get; }

    private bool _ocupado;
    public bool Ocupado { get => _ocupado; set => Set(ref _ocupado, value); }

    private string? _estado;
    /// <summary>Mensaje breve de estado en la parte inferior de la pantalla.</summary>
    public string? Estado { get => _estado; set => Set(ref _estado, value); }

    /// <summary>Se llama cada vez que el usuario entra a la pantalla.</summary>
    public virtual Task CargarAsync() => Task.CompletedTask;

    /// <summary>
    /// Ejecuta una acción mostrando el indicador de ocupado. Los errores de negocio se muestran
    /// como advertencia en lenguaje sencillo; los técnicos se registran en el log.
    /// </summary>
    protected async Task<bool> EjecutarAsync(Func<Task> accion)
    {
        Ocupado = true;
        try
        {
            await accion();
            return true;
        }
        catch (ReglaNegocioException ex)
        {
            Ctx.Dialogos.Advertir(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Ctx.Registro.Error(ex);
            Ctx.Dialogos.Error("Ocurrió un problema inesperado. Ya quedó registrado para soporte.\n\n" + ex.Message);
            return false;
        }
        finally
        {
            Ocupado = false;
        }
    }
}
