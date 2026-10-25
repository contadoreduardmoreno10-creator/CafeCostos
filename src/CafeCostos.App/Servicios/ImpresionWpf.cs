using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CafeCostos.App.ViewModels;
using CafeCostos.Core.Modelos;

namespace CafeCostos.App.Servicios;

/// <summary>
/// Comprobante de venta interno y recibo de caja. Se muestra una vista previa con botón «Imprimir»;
/// desde la impresora «Microsoft Print to PDF» también se puede guardar como PDF o enviar por WhatsApp.
/// </summary>
public sealed class ImpresionWpf : IImpresion
{
    private readonly ContextoApp _ctx;
    public ImpresionWpf(ContextoApp ctx) => _ctx = ctx;

    private sealed record Empresa(string Nombre, string Nit, string Direccion, string Telefono, string Ciudad, string Pie);

    private async Task<Empresa> EmpresaAsync()
    {
        var p = await _ctx.Parametros.TodosAsync();
        string V(string k) => p.TryGetValue(k, out var v) ? v : "";
        return new Empresa(V("Empresa.Nombre"), V("Empresa.NIT"), V("Empresa.Direccion"), V("Empresa.Telefono"),
                           V("Empresa.Ciudad"), V("Factura.PiePagina"));
    }

    // ==================================================================================
    public async Task ImprimirVentaAsync(int idVenta)
    {
        var venta = await _ctx.Ventas.ObtenerAsync(idVenta) ?? throw new InvalidOperationException("Venta no encontrada.");
        var detalle = await _ctx.Ventas.DetalleAsync(idVenta);
        var empresa = await EmpresaAsync();
        MostrarVistaPrevia($"Venta {venta.NumeroFactura}", () => DocumentoVenta(empresa, venta, detalle));
    }

    public async Task ImprimirReciboAsync(int idAbono)
    {
        var abono = (await _ctx.Cartera.AbonosAsync()).FirstOrDefault(a => a.IdAbono == idAbono)
                    ?? throw new InvalidOperationException("Recibo no encontrado.");
        var venta = await _ctx.Ventas.ObtenerAsync(abono.IdVenta) ?? throw new InvalidOperationException("Venta no encontrada.");
        var empresa = await EmpresaAsync();
        MostrarVistaPrevia($"Recibo {abono.NumeroRecibo}", () => DocumentoRecibo(empresa, abono, venta));
    }

    // ==================================================================================
    private static void MostrarVistaPrevia(string titulo, Func<FlowDocument> crear)
    {
        var visor = new FlowDocumentScrollViewer { Document = crear(), Background = Brushes.White };
        var imprimir = new Button { Content = "Imprimir", MinWidth = 140, Style = (Style)Application.Current.FindResource("BotonPrimario") };
        var cerrar = new Button { Content = "Cerrar", MinWidth = 120, IsCancel = true };
        var barra = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        barra.Children.Add(cerrar);
        barra.Children.Add(imprimir);
        var raiz = new DockPanel();
        DockPanel.SetDock(barra, Dock.Bottom);
        raiz.Children.Add(barra);
        raiz.Children.Add(visor);

        var ventana = new Window
        {
            Title = titulo, Content = raiz, Width = 760, Height = 820,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow,
            Background = (Brush)Application.Current.FindResource("Fondo")
        };
        imprimir.Click += (_, _) =>
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;
            var doc = crear();                     // documento nuevo: el del visor está paginado para pantalla
            doc.PageWidth = dlg.PrintableAreaWidth;
            doc.PageHeight = dlg.PrintableAreaHeight;
            doc.ColumnWidth = double.PositiveInfinity;
            doc.PagePadding = new Thickness(48);
            dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, titulo);
            ventana.Close();
        };
        ventana.ShowDialog();
    }

    private static FlowDocument Base() => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12.5,
        PagePadding = new Thickness(40),
        ColumnWidth = double.PositiveInfinity,
        Foreground = Brushes.Black
    };

    private static Block Encabezado(Empresa e, string documento, string numero)
    {
        var t = new Table { CellSpacing = 0 };
        t.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        t.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var g = new TableRowGroup();
        var fila = new TableRow();
        var izq = new Paragraph { Margin = new Thickness(0) };
        izq.Inlines.Add(new Run(e.Nombre) { FontSize = 20, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrWhiteSpace(e.Nit)) izq.Inlines.Add(new LineBreak());
        if (!string.IsNullOrWhiteSpace(e.Nit)) izq.Inlines.Add(new Run("NIT / C.C.: " + e.Nit));
        var linea = string.Join(" · ", new[] { e.Direccion, e.Ciudad, e.Telefono }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (linea.Length > 0) { izq.Inlines.Add(new LineBreak()); izq.Inlines.Add(new Run(linea)); }
        var der = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(0) };
        der.Inlines.Add(new Run(documento) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray });
        der.Inlines.Add(new LineBreak());
        der.Inlines.Add(new Run(numero) { FontSize = 18, FontWeight = FontWeights.Bold });
        fila.Cells.Add(new TableCell(izq));
        fila.Cells.Add(new TableCell(der));
        g.Rows.Add(fila);
        t.RowGroups.Add(g);
        return t;
    }

    private static Paragraph Linea(string etiqueta, string valor, bool negrilla = false)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        p.Inlines.Add(new Run(etiqueta + " ") { Foreground = Brushes.DimGray });
        p.Inlines.Add(new Run(valor) { FontWeight = negrilla ? FontWeights.Bold : FontWeights.Normal });
        return p;
    }

    private static TableCell Celda(string texto, bool derecha = false, bool negrilla = false, Brush? fondo = null) =>
        new(new Paragraph(new Run(texto)) { Margin = new Thickness(4), TextAlignment = derecha ? TextAlignment.Right : TextAlignment.Left })
        {
            FontWeight = negrilla ? FontWeights.Bold : FontWeights.Normal,
            Background = fondo,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

    private static Paragraph Pie(string texto) => new(new Run(texto))
    {
        FontSize = 10.5, Foreground = Brushes.DimGray, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 24, 0, 0)
    };

    // ----------------------------------------------------------------------------------
    private static FlowDocument DocumentoVenta(Empresa e, Venta v, IReadOnlyList<DetalleVenta> detalle)
    {
        var doc = Base();
        doc.Blocks.Add(Encabezado(e, "COMPROBANTE DE VENTA", v.NumeroFactura));
        if (v.Estado == Estados.Anulada)
            doc.Blocks.Add(new Paragraph(new Run("*** DOCUMENTO ANULADO ***")) { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkRed, TextAlignment = TextAlignment.Center });

        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 10, 0, 0) });
        doc.Blocks.Add(Linea("Fecha:", v.Fecha.ToString("dd/MM/yyyy")));
        doc.Blocks.Add(Linea("Cliente:", v.Cliente + (string.IsNullOrWhiteSpace(v.DocumentoCliente) ? "" : $"  ·  {v.DocumentoCliente}"), true));
        doc.Blocks.Add(Linea("Forma de pago:", v.TipoPago == "CREDITO"
            ? $"CRÉDITO · vence {v.FechaVencimiento:dd/MM/yyyy}"
            : $"CONTADO · {v.MedioPago}"));

        var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var w in new[] { 1.4, 3.2, 1.0, 1.6, 1.8 }) t.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        var g = new TableRowGroup();
        var enc = new TableRow();
        var gris = new SolidColorBrush(Color.FromRgb(0xF1, 0xEC, 0xE6));
        foreach (var (txt, der) in new[] { ("Lote", false), ("Producto", false), ("Cant.", true), ("Precio", true), ("Total", true) })
            enc.Cells.Add(Celda(txt, der, true, gris));
        g.Rows.Add(enc);
        foreach (var d in detalle)
        {
            var r = new TableRow();
            r.Cells.Add(Celda(d.CodigoLote));
            r.Cells.Add(Celda(d.Presentacion));
            r.Cells.Add(Celda(d.Cantidad.ToString("N0"), true));
            r.Cells.Add(Celda(d.PrecioUnitario.ToString("C0"), true));
            r.Cells.Add(Celda(d.Subtotal.ToString("C0"), true));
            g.Rows.Add(r);
        }
        void Total(string etiqueta, decimal valor, bool negrilla = false)
        {
            var r = new TableRow();
            r.Cells.Add(new TableCell(new Paragraph(new Run(etiqueta)) { TextAlignment = TextAlignment.Right, Margin = new Thickness(4) })
                { ColumnSpan = 4, FontWeight = negrilla ? FontWeights.Bold : FontWeights.Normal });
            r.Cells.Add(Celda(valor.ToString("C0"), true, negrilla));
            g.Rows.Add(r);
        }
        Total("Subtotal", v.Subtotal);
        if (v.Descuento > 0) Total("Descuento", -v.Descuento);
        Total("TOTAL", v.Total, true);
        if (v.TipoPago == "CREDITO" && v.Estado != Estados.Anulada) Total("Saldo pendiente", v.Saldo, true);
        t.RowGroups.Add(g);
        doc.Blocks.Add(t);

        if (!string.IsNullOrWhiteSpace(v.Observaciones)) doc.Blocks.Add(Linea("Observaciones:", v.Observaciones));
        if (v.TipoPago == "CREDITO")
            doc.Blocks.Add(new Paragraph(new Run("\n\n_______________________________\nRecibido por (firma y cédula)")) { Margin = new Thickness(0, 24, 0, 0) });
        doc.Blocks.Add(Pie(e.Pie));
        return doc;
    }

    private static FlowDocument DocumentoRecibo(Empresa e, Abono a, Venta v)
    {
        var doc = Base();
        doc.Blocks.Add(Encabezado(e, "RECIBO DE CAJA", a.NumeroRecibo));
        if (a.Anulado)
            doc.Blocks.Add(new Paragraph(new Run("*** RECIBO ANULADO ***")) { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkRed, TextAlignment = TextAlignment.Center });
        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 10, 0, 0) });
        doc.Blocks.Add(Linea("Fecha:", a.Fecha.ToString("dd/MM/yyyy")));
        doc.Blocks.Add(Linea("Recibimos de:", v.Cliente, true));
        doc.Blocks.Add(Linea("La suma de:", a.Valor.ToString("C0"), true));
        doc.Blocks.Add(Linea("Por concepto de:", $"Abono a la venta {v.NumeroFactura} del {v.Fecha:dd/MM/yyyy}"));
        doc.Blocks.Add(Linea("Medio de pago:", a.MedioPago + (string.IsNullOrWhiteSpace(a.Referencia) ? "" : $" · Ref. {a.Referencia}")));
        doc.Blocks.Add(Linea("Saldo actual de la venta:", v.Saldo.ToString("C0"), true));
        doc.Blocks.Add(new Paragraph(new Run("\n\n_______________________________\nFirma")) { Margin = new Thickness(0, 24, 0, 0) });
        doc.Blocks.Add(Pie(e.Pie));
        return doc;
    }
}
