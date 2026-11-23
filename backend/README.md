# DecibelCity API — ASP.NET Core 10

API vigente de DecibelCity. Conserva el contrato HTTP del backend FastAPI anterior (JSON `snake_case`,
errores `{ "detail": ... }` en español) y añade las integraciones de la Fase 2. El backend Python se
retiró el 2026-11-20; su respaldo local está en `Database/local/backups/` (excluido de Git).

## Ejecutar

```powershell
Copy-Item backend\.env.example backend\.env   # y completar DECIBELCITY_DATABASE_URL
& "C:\Program Files\dotnet\dotnet.exe" run --project backend\DecibelCity.Api.csproj
```

Escucha en `http://127.0.0.1:8000`, compatible con `frontend/proxy.conf.json`. Al arrancar crea o
verifica el esquema v5 (transacción con cerradura consultiva; actualiza v3/v4 en el sitio) y siembra
los catálogos con `ON CONFLICT DO NOTHING`. OpenAPI: `GET /openapi/v1.json`. Ejemplos: `DecibelCity.http`.

Las sesiones viven en memoria de **un único proceso**: reiniciar invalida las cookies y no deben
ejecutarse varias instancias con este almacenamiento.

## Endpoints

| Método | Ruta | Éxito | Errores | Notas |
| :-- | :-- | :-- | :-- | :-- |
| GET | `/api/health` | 200 | — | `{"status":"ok","service":"DecibelCity API"}` |
| GET | `/api/hotspots` | 200 | — | Datos ilustrativos, no mediciones calibradas |
| GET | `/api/maps/config` | 200 | — | Centro `-12.067, -75.210`; límites `S -12.16, W -75.30, N -11.95, E -75.12` |
| GET | `/api/places/search?q=` | 200 | 422, 502, 503 | Photon limitado al área; 3–120 caracteres; no se guarda |
| GET | `/api/places/reverse?lat=&lng=` | 200 | 422, 502, 503 | Nominatim `/reverse`, mismas reglas que la verificación |
| GET | `/api/noise-sources` | 200 | — | |
| GET | `/api/weather[?lat=&lng=]` | 200 | 422, 502, 503 | Open-Meteo con caché de 15 min por coordenada a 2 decimales |
| POST | `/api/reports` | 201 | 401, 403, 422, 429, 502, 503 | Origin → sesión → cuerpo → Nominatim `/lookup` → guardado → Telegram |
| GET | `/api/territories` | 200 | — | Solo jerarquía activa completa |
| GET | `/api/auth/config` | 200 | — | `{ turnstile_enabled, turnstile_site_key }` |
| POST | `/api/auth/register` | 201 | 403, 409, 422, 429, 503 | Nueve campos; guarda solo `id_distrito`; emite cookie |
| POST | `/api/auth/login` | 200 | 401, 403, 422, 429 | Argon2id; `turnstile_token` si Turnstile está activo; emite cookie |
| GET | `/api/auth/session` | 200 | 401, 403 | Relee cuenta y rol en cada petición |
| POST | `/api/auth/logout` | 204 | 403 | Revoca la sesión y borra la cookie (`Max-Age=0`) |

Todas las respuestas de `/api/auth/*` llevan `Cache-Control: no-store`.

## Seguridad

- **Contraseñas**: Argon2id compatible con `argon2-cffi` (PHC `$argon2id$v=19$m=65536,t=3,p=4$...`),
  comparación en tiempo constante, hash de referencia para usuarios inexistentes y rehash al iniciar
  sesión si los parámetros difieren. Nunca se devuelven ni se registran contraseñas ni hashes.
- **Sesiones**: token opaco de 32 bytes en la cookie `decibelcity_session` (`HttpOnly`, `SameSite=Lax`,
  `Path=/api`, 8 h, `Secure` en HTTPS tras `UseForwardedHeaders`). El almacén guarda solo el SHA-256 del
  token; máximo 10 000 sesiones, expulsando la creada hace más tiempo tras purgar las caducadas.
- **Origin**: los POST de `/api/auth/*` y `/api/reports` rechazan con 403 un `Origin` distinto del
  propio de la API y de `DECIBELCITY_CORS_ORIGINS`, antes de leer el cuerpo o producir efectos.
- **422 sanitizado**: `{ "detail": [{ "type", "loc", "msg" }] }` sin valores ni claves recibidas
  (las claves desconocidas se informan solo como `["body"]`). Cuerpos con UTF-8 inválido → 422.
- **Rate limiting** nativo por IP: login 5/min, registro 3/min, reportes 10/min (configurable).
- **Proxies**: `DECIBELCITY_TRUSTED_PROXIES` define qué redes pueden fijar `X-Forwarded-For/Proto`. Se
  recorren todos los saltos de confianza (`ForwardLimit = null`) y el recorrido se detiene en el primero
  que no lo es, así que detrás de Cloudflare + balanceador hay que listar ambas redes.

## Integraciones

- **Nominatim**: User-Agent identificable y peticiones serializadas con al menos 1 s entre el final
  de una y el inicio de la siguiente (compartido por `/lookup` y `/reverse`). Nunca autocompleta.
- **Turnstile**: activo con clave pública y secreta configuradas (y `DECIBELCITY_TURNSTILE_ENABLED`
  distinto de `false`). Token ausente, inválido o fallo de Cloudflare → 422 con mensaje en español.
- **Telegram**: no-op sin token/chat. Tras guardar, envía lugar verificado, fuente, nivel (o
  «No especificado»), descripción, fecha/hora e ID, con 2 s máximo; cualquier fallo se registra como
  advertencia (sin token ni mensaje de la excepción) y nunca altera el 201.
- **Open-Meteo**: sin clave; los fallos no se guardan en caché.

## Pruebas

```powershell
cd backend.Tests
$env:DECIBELCITY_TEST_DATABASE_URL = "postgresql://decibelcitydaw:CAMBIAR@127.0.0.1:5432/decibelcitydaw_test"
& "C:\Program Files\dotnet\dotnet.exe" test
```

`backend.Tests/global.json` (y `global.json` en la raíz, para ejecutar desde allí) activan Microsoft.Testing.Platform (obligatorio con el SDK 10).
Sin `DECIBELCITY_TEST_DATABASE_URL`, o si la base no responde, las pruebas de PostgreSQL se omiten; si
la base no termina en `_test`, fallan. Cada prueba usa un esquema `test_<guid>` que se elimina al final.

## Contenedor

```powershell
docker build -t decibelcity-api:local backend
docker compose up --build -d     # desde la raíz; requiere DECIBELCITY_DB_PASSWORD en el .env raíz
```

Imagen `aspnet:10.0-alpine`, usuario sin privilegios (`$APP_UID`), puerto `8080` (`URLS`, porque
`appsettings.json` fija `127.0.0.1:8000` para desarrollo), `HEALTHCHECK` con `wget` a `/api/health`,
ICU completo y `krb5-libs` para Npgsql. `.env` nunca entra en la imagen: en producción solo variables
`DECIBELCITY_*`.

### Detrás de Cloudflare

- `DECIBELCITY_TRUSTED_PROXIES`: rangos IPv4/IPv6 publicados por Cloudflare (https://www.cloudflare.com/ips/)
  más la red del balanceador de la plataforma si existe (Render, Koyeb, Cloud Run, OCI). Sin el salto
  intermedio en la lista, la IP que ven la limitación por IP y Turnstile será la del proxy.
- `DECIBELCITY_CORS_ORIGINS`: origen público del frontend (sin `*` ni barra final).
- Cloudflare con SSL «Full (strict)»; `X-Forwarded-Proto: https` de un proxy de confianza activa `Secure`
  en la cookie.
- Puerto del servicio: `8080` (la API no lee `PORT`). Una sola instancia: sesiones en memoria.
