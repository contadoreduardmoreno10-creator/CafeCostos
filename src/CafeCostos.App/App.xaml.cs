using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using CafeCostos.App.Servicios;
using CafeCostos.App.ViewModels;
using CafeCostos.Core;
using CafeCostos.Data.Infraestructura;

namespace CafeCostos.App;

public partial class App : Application
{
    private static Mutex? _instanciaUnica;
    private RegistroArchivo? _registro;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigurarCultura();

        var cfg = new ConfiguracionApp();
        _registro = new RegistroArchivo(cfg.CarpetaRegistros);
        DispatcherUnhandledException += ErrorNoControlado;

        // ---- Modo instalador: «CafeCostos.exe --inicializar» prepara la base de datos sin interfaz ----
        if (e.Args.Any(a => a.Equals("--inicializar", StringComparison.OrdinalIgnoreCase)))
        {
            var codigo = 0;
            try { await new InicializadorBaseDatos(cfg).AsegurarAsync(); }
            catch (Exception ex) { _registro.Error(ex); codigo = 1; }
            Shutdown(codigo);
            return;
        }

        // ---- Monousuario estricto: una sola ventana abierta a la vez ----
        _instanciaUnica = new Mutex(true, @"Local\CafeCostos_InstanciaUnica", out var esPrimera);
        if (!esPrimera)
        {
            MessageBox.Show("CaféCostos ya está abierto en este computador.", "CaféCostos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        RegistrarComportamientosGlobales();

        // ---- Arranque: preparar base de datos con ventana de bienvenida ----
        var bienvenida = new VentanaInicio();
        bienvenida.Show();
        try
        {
            await new InicializadorBaseDatos(cfg).AsegurarAsync(new Progress<string>(m => bienvenida.Mensaje = m));
        }
        catch (Exception ex)
        {
            _registro.Error(ex);
            bienvenida.Close();
            MessageBox.Show(
                (ex is ReglaNegocioException ? ex.Message : "No se pudo abrir la base de datos.\n\n" + ex.Message) +
                "\n\nSi el problema continúa, reinicie el computador o reinstale CaféCostos (su información no se borra).",
                "CaféCostos", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var dialogos = new DialogosWpf();
        var ctx = ContextoApp.Crear(cfg, dialogos, null, _registro, c => new ImpresionWpf(c));
        var principal = new MainViewModel(ctx);
        var ventana = new MainWindow(ctx) { DataContext = principal };
        MainWindow = ventana;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        ventana.Show();
        bienvenida.Close();
        await principal.IniciarAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanciaUnica?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Formatos colombianos: $ 1.250.000 · 24/10/2026 · 25,5 kg.</summary>
    private static void ConfigurarCultura()
    {
        var co = new CultureInfo("es-CO");
        CultureInfo.DefaultThreadCurrentCulture = co;
        CultureInfo.DefaultThreadCurrentUICulture = co;
        Thread.CurrentThread.CurrentCulture = co;
        Thread.CurrentThread.CurrentUICulture = co;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(co.IetfLanguageTag)));
    }

    /// <summary>
    /// Comodidades para usuarios no técnicos:
    ///   • Al entrar a una caja de texto se selecciona todo (para reemplazar el número fácilmente).
    ///   • Enter confirma el valor y pasa al siguiente campo (como en una calculadora).
    /// </summary>
    private static void RegistrarComportamientosGlobales()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((s, _) => ((TextBox)s).SelectAll()));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((s, e) =>
            {
                var tb = (TextBox)s;
                if (tb.IsKeyboardFocusWithin) return;
                e.Handled = true;
                tb.Focus();
            }));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.KeyDownEvent,
            new KeyEventHandler((s, e) =>
            {
                var tb = (TextBox)s;
                if (e.Key != Key.Enter || tb.AcceptsReturn) return;
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }));
    }

    private void ErrorNoControlado(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _registro?.Error(e.Exception);
        MessageBox.Show(
            e.Exception is ReglaNegocioException ? e.Exception.Message
                : "Ocurrió un problema inesperado. Ya quedó registrado.\n\n" + e.Exception.Message,
            "CaféCostos", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
