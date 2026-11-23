using DecibelCity.Api.Errors;
using DecibelCity.Api.Infrastructure;
using DecibelCity.Api.Models.Dtos;
using Microsoft.AspNetCore.Diagnostics;

namespace DecibelCity.Api.Middleware;

/// <summary>
/// Traduce los errores esperados al contrato HTTP del proyecto: <c>{ "detail": "..." }</c> en español
/// o <c>{ "detail": [{ "type", "loc", "msg" }] }</c> para 422, sin repetir valores recibidos.
/// </summary>
/// <param name="logger">Registro (solo tipo y código, nunca datos de entrada).</param>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        switch (exception)
        {
            case RequestValidationException validation:
                httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await httpContext.Response.WriteAsJsonAsync(
                    new ValidationErrorResponse(validation.Errors), ApiJson.Options, cancellationToken).ConfigureAwait(false);
                return true;

            case ApiException api:
                LogHandled(logger, api.StatusCode);
                httpContext.Response.StatusCode = api.StatusCode;
                await WriteDetailAsync(httpContext, api.Message, cancellationToken).ConfigureAwait(false);
                return true;

            case BadHttpRequestException badRequest:
                httpContext.Response.StatusCode = badRequest.StatusCode;
                await WriteDetailAsync(httpContext, "La solicitud no es válida.", cancellationToken).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Respuesta para errores no controlados (500) sin detalles internos.</summary>
    /// <param name="httpContext">Contexto de la petición.</param>
    /// <returns>Tarea asíncrona.</returns>
    public static Task WriteUnhandledAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return WriteDetailAsync(httpContext, ErrorMessages.InternalError, httpContext.RequestAborted);
    }

    /// <summary>Escribe <c>{ "detail": mensaje }</c>.</summary>
    /// <param name="httpContext">Contexto.</param>
    /// <param name="detail">Mensaje en español.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Tarea asíncrona.</returns>
    public static Task WriteDetailAsync(HttpContext httpContext, string detail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Response.WriteAsJsonAsync(new ErrorResponse(detail), ApiJson.Options, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Respuesta de error controlada ({StatusCode}).")]
    private static partial void LogHandled(ILogger logger, int statusCode);
}
