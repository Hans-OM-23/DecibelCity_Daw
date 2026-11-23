# Guía de Migración de Credenciales y Datos a PostgreSQL 18 (`decibelcitydaw`)

Esta guía documenta el procedimiento reproducible para aprovisionar las bases de datos y migrar los datos desde la base de datos actual `decibelcity` (con esquema versión 5) hacia la nueva base de datos `decibelcitydaw`, utilizando el rol de aplicación `decibelcitydaw` en PostgreSQL 18 (`127.0.0.1:5432`), sin hardcodear contraseñas y sin alterar la base original.

---

## 1. Contexto y Prerrequisitos

- **Instancia**: PostgreSQL 18 en `127.0.0.1:5432`.
- **Binarios de PostgreSQL**: `C:\Program Files\PostgreSQL\18\bin\`.
- **Rol actual de origen**: `decibelcity`.
- **Base actual de origen**: `decibelcity` (esquema versión 5, 11 tablas de dominio + `decibelcity_schema`).
- **Nuevo rol de destino**: `decibelcitydaw`.
- **Nuevas bases de destino**: `decibelcitydaw` (producción local) y `decibelcitydaw_test` (pruebas automatizadas).
- **Seguridad**: No se escriben contraseñas en scripts ni en el control de versiones. Se usan prompts interactivos o variables de entorno temporales de sesión.

---

## 2. Paso 1: Crear el Rol y las Bases de Datos

Ejecuta el script parametrizado [crear-bases-decibelcitydaw.sql](file:///C:/Users/HANS/Documents/Proyectos_Programacion/Desarrollo_de_Aplicaciones_Web/DecibelCity_DAW/Database/crear-bases-decibelcitydaw.sql) desde PowerShell con el usuario administrador `postgres`:

```powershell
# Solicita la contraseña para el nuevo rol 'decibelcitydaw' de forma segura en memoria
$securePass = Read-Host -Prompt "Ingresa la contrasena para el rol decibelcitydaw" -AsSecureString
$plainPass = [System.Net.NetworkCredential]::new('', $securePass).Password

# Ejecutar el script SQL pasando la variable app_password
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" `
    -h 127.0.0.1 `
    -p 5432 `
    -U postgres `
    -v app_password="$plainPass" `
    -f "Database/crear-bases-decibelcitydaw.sql"

# Limpiar la variable en texto plano de la sesión inmediatamente
$plainPass = $null
```

> [!NOTE]
> `psql` solicitará la contraseña de tu usuario administrador `postgres` para autenticar la conexión inicial.

---

## 3. Paso 2: Respaldo Consistente de `decibelcity` (Esquema v5) — YA GENERADO

Ya se generó un volcado actualizado en formato custom (`-F c --no-owner --no-privileges`) desde `127.0.0.1:5432/decibelcity` (versión de esquema `5`, 12 tablas, 4 usuarios, 124 distritos de Junín, 6 reportes y 3 puntos críticos) en:

`Database/local/backups/decibelcity-v5-20261120.dump`

Si necesitas regenerarlo más adelante, ejecuta:

```powershell
New-Item -ItemType Directory -Force -Path "Database/local/backups" | Out-Null
& "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" `
    --host 127.0.0.1 `
    --port 5432 `
    --username decibelcity `
    --format=c `
    --no-owner `
    --no-privileges `
    --file="Database/local/backups/decibelcity-v5-20261120.dump" `
    --dbname=decibelcity
```

---

## 4. Paso 3: Restaurar en la Nueva Base de Datos `decibelcitydaw`

Restaura el esquema y los datos en la base `decibelcitydaw` asignando los objetos al nuevo rol mediante `--no-owner --no-privileges`:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe" `
    --host 127.0.0.1 `
    --port 5432 `
    --username decibelcitydaw `
    --password `
    --dbname decibelcitydaw `
    --no-owner `
    --no-privileges `
    --single-transaction `
    --exit-on-error `
    --verbose `
    "Database/local/backups/decibelcity-v5-20261120.dump"
```

> [!TIP]
> Cuando `pg_restore` solicite la contraseña, ingresa la contraseña que definiste para `decibelcitydaw` en el Paso 1.

---

## 5. Paso 4: Verificación de Integridad

Ejecuta la siguiente consulta para verificar que las 11 tablas de dominio y `decibelcity_schema` están intactas con sus registros:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" `
    -h 127.0.0.1 `
    -p 5432 `
    -U decibelcitydaw `
    -d decibelcitydaw `
    -c "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;" `
    -c "SELECT version, applied_at FROM decibelcity_schema;" `
    -c "SELECT count(*) AS total_departamentos FROM departamentos;" `
    -c "SELECT count(*) AS total_provincias FROM provincias;" `
    -c "SELECT count(*) AS total_distritos FROM distritos;" `
    -c "SELECT count(*) AS total_hotspots FROM puntos_criticos;" `
    -c "SELECT count(*) AS total_usuarios FROM usuarios;"
```

Los resultados deben confirmar:
- 12 tablas: `categorias_fuente_ruido`, `decibelcity_schema`, `departamentos`, `distritos`, `estados_reporte`, `provincias`, `puntos_criticos`, `reportes_ciudadanos`, `roles_usuario`, `tipos_fuente_ruido`, `usuarios`, `zonificaciones`.
- Versión en `decibelcity_schema`: `5`.
- 1 departamento (Junín), 9 provincias, 124 distritos sembrados con UBIGEO INEI.
- Puntos críticos históricos y catálogo de fuentes presentes.

La base `decibelcitydaw_test` queda aprovisionada y vacía, lista para que la suite de pruebas xUnit genere y destruya sus esquemas temporales aislados (`test_<UUID>`).
