# DecibelCity

Proyecto estudiantil para explorar el entorno acústico de Huancayo y registrar reportes de ruido con ubicaciones verificadas en OpenStreetMap.

Los niveles ilustrativos y las mediciones relativas del micrófono no son mediciones calibradas ni datos oficiales. La aplicación no recopila audio.

## Arquitectura vigente

| Capa | Tecnología | Carpeta |
| --- | --- | --- |
| Interfaz | Angular 22, Bootstrap 5.3 sin jQuery, Leaflet + OpenStreetMap | `frontend/` |
| API | ASP.NET Core 10 (minimal APIs, Npgsql, Argon2id con `Isopoh.Cryptography.Argon2`) | `backend/` |
| Pruebas de la API | xUnit v3 sobre Microsoft.Testing.Platform | `backend.Tests/` |
| Base de datos | PostgreSQL 18, esquema versión 5; bases `decibelcitydaw` (aplicación) y `decibelcitydaw_test` (pruebas) | `Database/` |
| Contenedores | `backend/Dockerfile` y `docker-compose.yml` (PostgreSQL 18 + API) | raíz |

El backend anterior (Python, FastAPI y uv) se retiró el 2026-11-20; `backend/` contiene ahora la API ASP.NET Core 10, que conserva su contrato HTTP. Queda un respaldo local en `Database/local/backups/` (excluido de Git).

## Integraciones externas

1. **Geolocation API + reverse OSM**: el navegador pide la posición con consentimiento; `GET /api/places/reverse` la resuelve con Nominatim desde el servidor (1 petición por segundo, User-Agent identificable). La ubicación guardada sigue siendo un `place_id` verificado.
2. **Cloudflare Turnstile**: solo en `POST /api/auth/login`, activo cuando hay clave pública y secreta.
3. **Telegram Bot API**: aviso saliente al recibir un reporte, sin datos de la cuenta; si falla, el reporte se guarda igual.
4. **Open-Meteo**: clima del mapa en `GET /api/weather`, sin clave y con caché de 15 minutos.

Búsqueda (Photon) y verificación (Nominatim) se hacen siempre desde la API; se mantiene visible la atribución «© colaboradores de OpenStreetMap».

## Ejecución local

```powershell
# API (http://127.0.0.1:8000); copiar y completar backend\.env antes
Copy-Item backend\.env.example backend\.env
& "C:\Program Files\dotnet\dotnet.exe" run --project backend\DecibelCity.Api.csproj

# Interfaz (http://127.0.0.1:4200, proxy /api → 127.0.0.1:8000)
cd frontend; npm install; npm start
```

Aprovisionamiento de las bases: `Database/migrar-a-decibelcitydaw.md`. Detalles de la API: `backend/README.md`.

### Con Docker

```powershell
# .env en la raíz (excluido de Git) con, como mínimo, DECIBELCITY_DB_PASSWORD=<contraseña local>
docker compose up --build -d
docker compose ps        # postgres y api deben quedar "healthy"
```

- `postgres` (`postgres:18-alpine`) crea `decibelcitydaw` con el rol del mismo nombre; vive solo en la red interna `datos` y no se publica en el anfitrión.
- `api` se publica en `127.0.0.1:8000` (cambiable con `DECIBELCITY_API_PORT`), inicializa el esquema v5 al arrancar y sale a Internet por la red `salida`.
- La contraseña viaja como `PGPASSWORD`, nunca dentro de la URL ni en archivos versionados.

## Despliegue detrás de Cloudflare

La imagen de `backend/Dockerfile` (Alpine, usuario sin privilegios, puerto `8080`, `HEALTHCHECK` en `/api/health`) sirve para Render, Koyeb, Google Cloud Run u OCI:

- Declarar `8080` como puerto del servicio en la plataforma (la API ignora `PORT`; Cloud Run usa 8080 por defecto, en Render y Koyeb hay que indicarlo).
- Configurar solo variables de entorno `DECIBELCITY_*` (lista completa en `backend/.env.example`); el archivo `.env` no entra en la imagen.
- `DECIBELCITY_CORS_ORIGINS` debe contener el origen público del frontend, sin barra final ni `*`.
- `DECIBELCITY_TRUSTED_PROXIES` lista **todos** los proxies entre el usuario y la API: los rangos publicados por Cloudflare (https://www.cloudflare.com/ips/) y, si la plataforma tiene balanceador, su red. La API recorre `X-Forwarded-For` mientras el salto sea de confianza y se detiene en el primero que no lo es, así que una entrada falsificada por el cliente no se acepta.
- Ejecutar **una sola instancia**: las sesiones viven en memoria del proceso y se pierden al reiniciar o escalar.
- La cookie de sesión lleva `Secure` cuando el proxy de confianza informa `X-Forwarded-Proto: https`.

## Verificación

- Frontend: `npm run typecheck`, `npm test`, `npm run build`.
- API: `dotnet test` en `backend.Tests/` con `DECIBELCITY_TEST_DATABASE_URL` apuntando a `decibelcitydaw_test`.
- Contenedor: `docker build -t decibelcity-api:local backend`.

## Organización del trabajo

- Rama fabricio: frontend.
- Rama hans: backend y Database.
- Los cambios se incorporan a main mediante pull requests.
