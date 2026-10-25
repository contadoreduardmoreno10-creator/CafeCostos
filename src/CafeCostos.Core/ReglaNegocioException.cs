namespace CafeCostos.Core;

/// <summary>
/// Error de negocio con un mensaje redactado para el usuario final (no técnico).
/// La interfaz lo muestra tal cual; cualquier otra excepción se considera error técnico.
/// </summary>
public sealed class ReglaNegocioException : Exception
{
    public ReglaNegocioException(string mensaje) : base(mensaje) { }
}

/// <summary>Utilidades de validación que lanzan <see cref="ReglaNegocioException"/>.</summary>
public static class Validar
{
    public static void Que(bool condicion, string mensaje)
    {
        if (!condicion) throw new ReglaNegocioException(mensaje);
    }

    public static void Positivo(decimal valor, string campo) =>
        Que(valor > 0, $"El campo «{campo}» debe ser mayor que cero.");

    public static void NoNegativo(decimal valor, string campo) =>
        Que(valor >= 0, $"El campo «{campo}» no puede ser negativo.");

    public static void Texto(string? valor, string campo) =>
        Que(!string.IsNullOrWhiteSpace(valor), $"El campo «{campo}» es obligatorio.");
}
