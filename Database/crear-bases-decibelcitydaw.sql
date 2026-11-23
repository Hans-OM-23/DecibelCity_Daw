-- ============================================================================
-- DecibelCity: Creación de rol y bases de datos para decibelcitydaw (PostgreSQL 18)
-- ============================================================================
-- Este script es una plantilla reproducible y parametrizable.
-- NO almacena contraseñas en claro.
--
-- Forma 1: Ejecución interactiva/parametrizada con psql (recomendada):
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h 127.0.0.1 -p 5432 -U postgres -v app_password='TU_CONTRASENA_AQUI' -f Database/crear-bases-decibelcitydaw.sql
--
-- Forma 2: Reemplazar manualmente el texto 'TU_CONTRASENA_AQUI' y ejecutar desde pgAdmin 4
--          conectado a la base de mantenimiento 'postgres'.
-- ============================================================================

\set ON_ERROR_STOP on

-- Si se ejecuta por psql y no se definió app_password, psql detendrá la ejecución.
\if :{?app_password}
\else
    \set app_password 'TU_CONTRASENA_AQUI'
\endif

-- 1. Guardar temporalmente el parámetro de sesión y crear/actualizar el rol 'decibelcitydaw'
--    (psql no interpola variables dentro de bloques DO $$, por eso se usa set_config/current_setting)
SELECT set_config('decibelcity.app_password', :'app_password', false);

DO $$
DECLARE
    v_pass text := current_setting('decibelcity.app_password', true);
BEGIN
    IF v_pass IS NULL OR btrim(v_pass) = '' OR v_pass = 'TU_CONTRASENA_AQUI' THEN
        RAISE EXCEPTION 'Debes definir una contraseña válida pasando -v app_password=... al ejecutar psql.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'decibelcitydaw') THEN
        EXECUTE format('CREATE ROLE decibelcitydaw WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;', v_pass);
        RAISE NOTICE 'Rol decibelcitydaw creado con éxito.';
    ELSE
        EXECUTE format('ALTER ROLE decibelcitydaw WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;', v_pass);
        RAISE NOTICE 'Rol decibelcitydaw ya existía; contraseña y permisos actualizados.';
    END IF;
END
$$;

-- 2. Crear la base de datos de producción local 'decibelcitydaw' si no existe
SELECT 'CREATE DATABASE decibelcitydaw WITH OWNER decibelcitydaw ENCODING ''UTF8'''
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'decibelcitydaw')\gexec

-- 3. Crear la base de datos de pruebas 'decibelcitydaw_test' si no existe
SELECT 'CREATE DATABASE decibelcitydaw_test WITH OWNER decibelcitydaw ENCODING ''UTF8'''
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'decibelcitydaw_test')\gexec

-- 4. Conceder permisos de conexión y administración al rol sobre ambas bases
GRANT ALL PRIVILEGES ON DATABASE decibelcitydaw TO decibelcitydaw;
GRANT ALL PRIVILEGES ON DATABASE decibelcitydaw_test TO decibelcitydaw;

-- 5. Ajustar permisos en el esquema public de la base principal
\connect decibelcitydaw
GRANT ALL ON SCHEMA public TO decibelcitydaw;
ALTER SCHEMA public OWNER TO decibelcitydaw;

-- 6. Ajustar permisos en el esquema public de la base de pruebas
\connect decibelcitydaw_test
GRANT ALL ON SCHEMA public TO decibelcitydaw;
ALTER SCHEMA public OWNER TO decibelcitydaw;

\echo 'Bases decibelcitydaw y decibelcitydaw_test configuradas correctamente.'
