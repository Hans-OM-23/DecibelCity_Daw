# Usar la instalación de PostgreSQL con DecibelCity

> **Actualización 2026-11-20**: el backend Python (FastAPI/uv) se retiró y `backend/` contiene ahora la API ASP.NET Core 10 con el mismo contrato HTTP. Las órdenes `uv ...`, `scripts/postgres_local.py` y `pytest` de este documento son históricas; su código queda en el respaldo local `Database/local/backups/backend-python-2026-11-20.zip`. Equivalentes vigentes: `dotnet run --project backend\DecibelCity.Api.csproj`, `dotnet test` en `backend.Tests/` y `docker compose up --build`.

Comprobación del 8 de noviembre de 2026: PostgreSQL 18 está instalado como servicio de Windows y acepta conexiones en `127.0.0.1:5432`. La aplicación todavía apunta al PostgreSQL portátil del proyecto en `127.0.0.1:55432`. Son dos instancias independientes, aunque estén en la misma computadora.

Estos pasos trasladan la base a la instalación nueva conservando la instancia anterior. El modelo actual utiliza PostgreSQL estándar; pgAdmin permite administrar las tablas y ejecutar consultas. No requiere extensiones adicionales.

## 1. Registrar el servidor instalado en pgAdmin

Abrir pgAdmin 4. Si ya aparece un servidor PostgreSQL 18 con estos datos, utilizarlo. De lo contrario, en `Servers` elegir `Register > Server` y completar:

| Campo | Valor |
| --- | --- |
| General → Name | `PostgreSQL instalado` |
| Connection → Host name/address | `127.0.0.1` |
| Connection → Port | `5432` |
| Connection → Maintenance database | `postgres` |
| Connection → Username | `postgres` |
| Connection → Password | La contraseña elegida durante tu instalación de PostgreSQL |

Guardar para conectar. La contraseña de este `postgres` pertenece a la instalación nueva; es distinta de las contraseñas del servidor portátil.

## 2. Crear la cuenta y las bases

En el servidor instalado, abrir `Login/Group Roles > Create > Login/Group Role`:

- General → Name: `decibelcity`.
- Definition → Password: utilizar el valor `app_password` de `Database/local/postgresql-local.json`, abriendo ese archivo localmente. Es la contraseña ya utilizada por la aplicación, generada al preparar el proyecto. No copiarla en el chat.
- Privileges → Can login?: `Yes`. Conservar Superuser y Create databases en `No`.

Guardar. Así la aplicación podrá usar la misma cuenta y contraseña en el nuevo servidor.

En `Databases > Create > Database`, crear `decibelcity`, con Owner `decibelcity`. Repetir para `decibelcity_test`, también con Owner `decibelcity`. La segunda base se utiliza solo en las pruebas.

Si alguno de esos nombres ya existe, comprobar primero a qué proyecto pertenece; estas instrucciones no necesitan borrar ni reemplazar bases existentes.

## 3. Restaurar los datos antes de iniciar la aplicación

Se preparó este respaldo de la instancia anterior:

`Database/local/backups/decibelcity-20261108T053233301321Z.dump`

Contiene la estructura, los catálogos y los datos confirmados al crearlo. Si se añaden reportes posteriormente a la instancia anterior, crear un respaldo nuevo antes del traslado con `uv run scripts/postgres_local.py backup`, desde `backend/`.

Mantener detenida la aplicación durante el traslado. La base de destino `decibelcity` debe estar vacía. Desde PowerShell, situarse en `backend/` y ejecutar:

```powershell
& "C:/Program Files/PostgreSQL/18/bin/pg_restore.exe" --host 127.0.0.1 --port 5432 --username decibelcity --password --dbname decibelcity --no-owner --no-privileges --single-transaction --exit-on-error "../Database/local/backups/decibelcity-20261108T053233301321Z.dump"
```

Introducir la contraseña `app_password` cuando se solicite. El comando restaura las tablas y datos bajo la cuenta `decibelcity`, en una sola transacción. No ejecutar antes `schema.sql`: el respaldo ya contiene esa estructura. La base `decibelcity_test` puede permanecer vacía; las pruebas crean sus propias tablas en esquemas temporales.

Al terminar correctamente, volver a pgAdmin, actualizar `Databases` y expandir `decibelcity > Schemas > public > Tables`. Deben aparecer diez tablas del dominio y `decibelcity_schema`.

## 4. Conectar la aplicación a la instalación nueva

Abrir `backend/.env`. Como se reutilizó la contraseña actual de la aplicación, cambiar únicamente `:55432/` por `:5432/` en estas dos variables:

```dotenv
DECIBELCITY_DATABASE_URL=postgresql://decibelcity:CONTRASENA_ACTUAL@127.0.0.1:5432/decibelcity
DECIBELCITY_TEST_DATABASE_URL=postgresql://decibelcity:CONTRASENA_ACTUAL@127.0.0.1:5432/decibelcity_test
DECIBELCITY_DATABASE_SCHEMA=public
```

`CONTRASENA_ACTUAL` es explicativo: conservar el valor real que ya aparece en `.env`, sin sustituirlo por esa palabra. Conservar la variable de CORS.

Desde `backend/`:

```powershell
uv sync --locked
uv run --env-file .env uvicorn main:app --reload --host 127.0.0.1 --port 8000
```

Desde otra terminal, en `frontend/`:

```powershell
npm start
```

Abrir `http://127.0.0.1:4200`. La aplicación y pgAdmin deben utilizar la misma instancia `5432` y la base `decibelcity`. Los mapas usan OpenStreetMap y no necesitan claves. Si la base ya tenía el esquema versión 3, el backend la actualiza a la versión 4 al iniciar, sin borrar reportes.

La instancia instalada se inicia mediante el servicio `postgresql-x64-18` de Windows. Los comandos de `scripts/postgres_local.py` siguen administrando exclusivamente la instancia portátil del puerto `55432`; `init` vuelve a escribir las conexiones de esa instancia en `.env`. Para respaldar la nueva base, utilizar `Backup` sobre `decibelcity` dentro del servidor `5432` en pgAdmin.

Después de comprobar el traslado, se puede detener únicamente la instancia portátil con `uv run scripts/postgres_local.py stop`. Sus archivos y respaldos se conservan.

## 5. Hacer consultas

En pgAdmin, seleccionar la base `decibelcity` del servidor instalado y abrir `Tools > Query Tool`, o `Query Tool` desde su menú contextual. Abrir [consultas-postgresql.sql](consultas-postgresql.sql), seleccionar una consulta y usar `Execute script`. Los resultados se muestran en `Data Output`.

El archivo incluye consultas de conexión, estructura, recuentos, puntos con distrito y fuente, reportes, categorías, jerarquía territorial y promedio de niveles estimados. Todas son de lectura. Una tabla de roles vacía es correcta: el catálogo existe, pero la aplicación no implementa cuentas de usuarios.

Referencias: [conexión en pgAdmin](https://www.pgadmin.org/docs/pgadmin4/latest/server_dialog.html), [Query Tool](https://www.pgadmin.org/docs/pgadmin4/latest/query_tool.html) y [restauración PostgreSQL](https://www.postgresql.org/docs/current/app-pgrestore.html).
