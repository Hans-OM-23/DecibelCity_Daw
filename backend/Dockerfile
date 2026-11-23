# syntax=docker/dockerfile:1
# DecibelCity API (ASP.NET Core 10) — imagen de producción.
# Construir desde la raíz del repositorio o desde esta carpeta (el contexto es backend/):
#   docker build -t decibelcity-api:local backend
# Configuración exclusivamente por variables de entorno DECIBELCITY_* (ver .env.example);
# el archivo .env nunca entra en la imagen.

# ============================================================
# Etapa 1: restauración y publicación con el SDK
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Solo el .csproj primero: la restauración se reutiliza mientras no cambien las dependencias.
COPY ["DecibelCity.Api.csproj", "./"]
RUN dotnet restore "DecibelCity.Api.csproj" --runtime linux-musl-x64

COPY . .
RUN dotnet publish "DecibelCity.Api.csproj" \
        --configuration "$BUILD_CONFIGURATION" \
        --runtime linux-musl-x64 \
        --self-contained false \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

# ============================================================
# Etapa 2: ejecución (Alpine, usuario sin privilegios)
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

# El proyecto declara InvariantGlobalization=false: se instalan ICU con datos completos (es-PE).
# krb5-libs evita el error «libgssapi_krb5.so.2» que Npgsql registra al sondear GSSAPI.
# Alpine ya incluye wget (BusyBox), que usa el HEALTHCHECK; no se añaden más paquetes.
RUN apk add --no-cache icu-libs icu-data-full krb5-libs

WORKDIR /app
COPY --from=build /app/publish .

# appsettings.json fija Urls=http://127.0.0.1:8000 para desarrollo. La clave sin prefijo URLS
# (variables de entorno sin prefijo) tiene prioridad sobre appsettings.json; ASPNETCORE_URLS
# sola NO bastaría porque las variables ASPNETCORE_* se cargan antes que appsettings.json.
# Render, Koyeb, Cloud Run y OCI enrutan al puerto 8080 del contenedor.
# ASPNETCORE_HTTP_PORTS (8080 en la imagen base) se vacía: URLS ya define el puerto y así se evita
# el aviso «Overriding HTTP_PORTS» en cada arranque.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=http://+:8080 \
    URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_NOLOGO=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/api/health || exit 1

# APP_UID (1654) lo define la imagen base de Microsoft: usuario sin privilegios.
USER $APP_UID

ENTRYPOINT ["dotnet", "DecibelCity.Api.dll"]
