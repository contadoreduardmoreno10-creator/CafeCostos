using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CafeCostos.App.Converters;

/// <summary>
/// Pesos colombianos: muestra «1.250.000» y acepta «1250000», «1.250.000», «$ 1.250.000» o «1250000,50».
/// El punto se interpreta como separador de miles (costumbre en Colombia) y la coma como decimal.
/// </summary>
public sealed class PesosConverter : IValueConverter
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => "",
        decimal d => d.ToString(d == Math.Truncate(d) ? "#,##0" : "#,##0.00", Co),
        _ => value.ToString()
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? "").Replace("$", "").Replace(" ", "").Trim();
        if (s.Length == 0)
            return Nullable.GetUnderlyingType(targetType) is not null ? null : 0m;
        return decimal.TryParse(s, NumberStyles.Number, Co, out var d) && d >= 0 ? d : DependencyProperty.UnsetValue;
    }
}

/// <summary>
/// Kilos, cantidades y porcentajes: acepta coma o punto como separador decimal («25,5» o «25.5»)
/// y no usa separador de miles para evitar ambigüedades. Sirve para decimal, int y sus anulables.
/// </summary>
public sealed class NumeroConverter : IValueConverter
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => "",
        decimal d => d.ToString("0.###", Co),
        int i => i.ToString(Co),
        _ => value.ToString()
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tipo = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var s = (value as string ?? "").Trim().Replace(" ", "").Replace('.', ',');
        if (s.Length == 0)
            return Nullable.GetUnderlyingType(targetType) is not null ? null : System.Convert.ChangeType(0, tipo);
        if (s.Count(ch => ch == ',') > 1 ||
            !decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Co, out var d) || d < 0)
            return DependencyProperty.UnsetValue;
        if (tipo == typeof(int))
            return d == Math.Truncate(d) && d <= int.MaxValue ? (int)d : DependencyProperty.UnsetValue;
        return d;
    }
}

public sealed class BoolVisibilidadConverter : IValueConverter
{
    public bool Invertir { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is true) ^ Invertir ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NuloVisibilidadConverter : IValueConverter
{
    /// <summary>Por defecto: visible cuando NO es nulo.</summary>
    public bool Invertir { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is not null) ^ Invertir ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible si el valor (p. ej. el paso del asistente) es igual al parámetro.</summary>
public sealed class IgualVisibilidadConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class TextoVacioVisibilidadConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NegarConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not true;
}
