// DecibelCity API (ASP.NET Core 10). Conserva el contrato HTTP del backend FastAPI retirado el 2026-11-20.
// Ejecutar con: dotnet run --project backend (http://127.0.0.1:8000).
// Las sesiones viven en memoria de un único proceso: no habilitar varias instancias con este almacenamiento.
using DecibelCity.Api.Configuration;
using DecibelCity.Api.Endpoints;
using DecibelCity.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddDotEnvFile(Path.Combine(builder.Environment.ContentRootPath, ".env"));

// En plataformas en la nube como Railway, Koyeb o Render, el puerto se inyecta en la variable PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var p))
{
    builder.WebHost.UseUrls($"http://+:{p}");
}

builder.Services.AddDecibelCity();

var app = builder.Build();
app.UseDecibelCity();
app.MapPublicEndpoints();
app.MapAuthEndpoints();
app.MapReportEndpoints();
app.Run();
