using DecibelCity.Api.Filters;
using DecibelCity.Api.Infrastructure;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Services;
using DecibelCity.Api.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DecibelCity.Api.Endpoints;

/// <summary>Recepción de reportes ciudadanos.</summary>
public static class ReportEndpoints
{
    /// <summary>Registra <c>POST /api/reports</c>.</summary>
    /// <param name="routes">Constructor de rutas.</param>
    /// <returns>El mismo constructor.</returns>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        // Orden de filtros: Origin → sesión vigente con cuenta y rol activos → cuerpo → Nominatim → guardado.
        routes.MapPost("/api/reports", CreateReportAsync)
            .AddEndpointFilter<OriginValidationFilter>()
            .AddEndpointFilter<ActiveUserFilter>()
            .RequireRateLimiting(RateLimitPolicies.Reports)
            .Accepts<NoiseReportCreate>("application/json")
            .WithTags("DecibelCity")
            .WithName("CreateReport")
            .WithSummary("Envía un reporte (exige sesión); verifica el place_id en Nominatim")
            .WithDescription("El reporte almacenado solo guarda id_lugar y proveedor_lugar='osm', sin relación con la cuenta. No se publica ningún endpoint de lectura.")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ValidationErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return routes;
    }

    // Sin cabecera Location: los reportes no tienen un recurso público de lectura (privacidad).
    private static async Task<Created<NoiseReport>> CreateReportAsync(
        HttpContext http, INoiseService service, CancellationToken cancellationToken)
    {
        var payload = RequestValidators.Report(await JsonBody.ReadAsync(http.Request, cancellationToken).ConfigureAwait(false));
        var report = await service.CreateReportAsync(payload, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created((string?)null, report);
    }
}
