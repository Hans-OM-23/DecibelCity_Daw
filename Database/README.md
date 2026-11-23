# Database

Todo lo relativo a la base de datos PostgreSQL 18 de DecibelCity (esquema versión 5: once tablas de dominio y `decibelcity_schema`).

| Base | Uso |
| --- | --- |
| `decibelcitydaw` | Aplicación (API ASP.NET Core 10 de `backend/`). Propietario: rol `decibelcitydaw`. |
| `decibelcitydaw_test` | Pruebas automatizadas (`backend.Tests/`). Solo se crean y eliminan esquemas `test_<guid>`. |
| `decibelcity` / `decibelcity_test` | Bases anteriores del backend Python (retirado el 2026-11-20); origen de la migración. |

| Ruta | Contenido |
| --- | --- |
| [migrar-a-decibelcitydaw.md](migrar-a-decibelcitydaw.md) | Aprovisionamiento del rol y las bases `decibelcitydaw*` y migración de datos desde `decibelcity`, sin contraseñas en archivos. |
| [crear-bases-decibelcitydaw.sql](crear-bases-decibelcitydaw.sql) | Script parametrizado (`-v app_password=...`) que crea el rol `decibelcitydaw` y sus dos bases. |
| [base-datos.md](base-datos.md) | Evaluación del modelo, PostgreSQL portátil, importación, respaldo y restauración. |
| [configurar-postgresql-instalado.md](configurar-postgresql-instalado.md) | Traslado al PostgreSQL instalado (puerto 5432) y uso de pgAdmin. |
| [esquema-actual.dbml](esquema-actual.dbml) | Diagrama DBML del modelo vigente (documentación; las pruebas de `backend.Tests/` no lo contrastan con la base real). |
| [consultas-postgresql.sql](consultas-postgresql.sql) | Consultas de lectura de ejemplo. |
| [referencias/](referencias/) | Esquemas y DDL históricos, solo como documentación. |
| `local/` | Datos de esta computadora, **excluidos de Git**: binarios y clúster portátil, `postgresql-local.json` (credenciales), registro, respaldos `.dump`, exportación v2 y bases SQLite históricas (`qa/` contiene las de QA). |

## SQL que ejecuta la API

- `backend/Repositories/Sql/` (`schema.sql`, `upgrade_v4.sql`, `upgrade_v5.sql`), embebido en el ensamblado. Con `DECIBELCITY_DATABASE_INITIALIZE=true` (valor predeterminado) la API crea o verifica el esquema v5 al arrancar dentro de una transacción con cerradura consultiva, actualiza v3/v4 conservando datos y siembra los catálogos INEI con `ON CONFLICT DO NOTHING`.
- El asistente Python del clúster portátil (`scripts/postgres_local.py`) y la importación v2 (`data_import.py`) se retiraron con el backend Python; quedan en el respaldo `Database/local/backups/backend-python-2026-11-20.zip`. Para respaldos usar `pg_dump`/`pg_restore` o pgAdmin.

## PostgreSQL en Docker

`docker-compose.yml` (raíz) levanta `postgres:18-alpine` con la base y el rol `decibelcitydaw`, en una red interna sin puertos publicados y con un volumen propio (`postgres-data`, montado en `/var/lib/postgresql` como exige la imagen 18). La contraseña se lee de `DECIBELCITY_DB_PASSWORD` en el `.env` raíz (excluido de Git). Ese contenedor es independiente del PostgreSQL instalado en `127.0.0.1:5432`; los datos no se comparten.

> No compartir ni publicar `local/`: contiene credenciales y datos de reportes.
