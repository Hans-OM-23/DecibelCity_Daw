using DecibelCity.Api.Models.Dtos;

namespace DecibelCity.Api.Errors;

/// <summary>
/// Error esperado con código HTTP y mensaje seguro en español. El mensaje nunca contiene
/// datos de entrada, respuestas de proveedores, contraseñas ni hashes.
/// </summary>
public class ApiException : Exception
{
    /// <summary>Crea el error.</summary>
    /// <param name="statusCode">Código HTTP de la respuesta.</param>
    /// <param name="detail">Mensaje para la persona usuaria.</param>
    public ApiException(int statusCode, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
    }

    /// <summary>Código HTTP de la respuesta.</summary>
    public int StatusCode { get; }
}

/// <summary>Error de cuentas y sesiones (401, 403, 409, 422, 503).</summary>
/// <param name="statusCode">Código HTTP.</param>
/// <param name="detail">Mensaje en español.</param>
public sealed class AuthException(int statusCode, string detail) : ApiException(statusCode, detail);

/// <summary>Error al buscar o verificar lugares de OpenStreetMap (422, 502, 503).</summary>
/// <param name="detail">Mensaje en español.</param>
/// <param name="statusCode">Código HTTP (422 por defecto).</param>
public sealed class PlaceVerificationException(string detail, int statusCode = StatusCodes.Status422UnprocessableEntity)
    : ApiException(statusCode, detail);

/// <summary>Error al consultar el clima (502, 503).</summary>
/// <param name="detail">Mensaje en español.</param>
/// <param name="statusCode">Código HTTP.</param>
public sealed class WeatherUnavailableException(string detail, int statusCode) : ApiException(statusCode, detail);

/// <summary>Errores de validación de entrada (422) sin valores recibidos.</summary>
/// <param name="errors">Errores encontrados.</param>
public sealed class RequestValidationException(IReadOnlyList<ValidationErrorItem> errors)
    : Exception("La solicitud no superó la validación.")
{
    /// <summary>Errores encontrados.</summary>
    public IReadOnlyList<ValidationErrorItem> Errors { get; } = errors;
}

/// <summary>La base de datos no tiene una estructura reconocida; no se modifica.</summary>
/// <param name="message">Descripción.</param>
public sealed class DatabaseMigrationException(string message) : InvalidOperationException(message);

/// <summary>Mensajes compartidos.</summary>
public static class ErrorMessages
{
    /// <summary>Origen no permitido.</summary>
    public const string ForbiddenOrigin = "El origen de la solicitud no está permitido.";

    /// <summary>Petición sin sesión.</summary>
    public const string LoginRequired = "Debes iniciar sesión para continuar.";

    /// <summary>La cookie no corresponde a una cuenta existente.</summary>
    public const string InvalidSession = "La sesión no es válida. Inicia sesión de nuevo.";

    /// <summary>Cuenta o rol desactivados en una sesión existente.</summary>
    public const string AccountOrRoleDisabled = "La cuenta o su rol están desactivados.";

    /// <summary>Credenciales incorrectas (mismo mensaje para usuario inexistente).</summary>
    public const string InvalidCredentials = "Nombre de usuario o contraseña incorrectos.";

    /// <summary>Cuenta desactivada (solo tras demostrar la contraseña).</summary>
    public const string AccountDisabled = "La cuenta está desactivada. Comunícate con el equipo de DecibelCity.";

    /// <summary>Distrito no disponible.</summary>
    public const string DistrictUnavailable = "El distrito seleccionado no existe o no está disponible para el registro.";

    /// <summary>Nombre de usuario duplicado.</summary>
    public const string UsernameTaken = "El nombre de usuario ya está registrado.";

    /// <summary>Sin rol base activo.</summary>
    public const string RegistrationUnavailable = "El registro no está disponible en este momento.";

    /// <summary>Fallo de Turnstile.</summary>
    public const string TurnstileFailed = "La verificación de seguridad ha fallado o expiró. Inténtalo nuevamente.";

    /// <summary>Exceso de peticiones.</summary>
    public const string TooManyRequests = "Demasiadas solicitudes. Espera un momento e inténtalo nuevamente.";

    /// <summary>Error no controlado.</summary>
    public const string InternalError = "Ocurrió un error interno. Inténtalo nuevamente más tarde.";
}
