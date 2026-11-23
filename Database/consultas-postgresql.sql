-- DecibelCity: ejemplos de solo lectura para pgAdmin / Query Tool.
-- Seleccionar una consulta y ejecutarla dentro de la base decibelcity.
-- Los puntos y sus niveles son ilustrativos; los reportes no son mediciones calibradas.

-- 1. Confirmar la base, usuario y puerto al que estás conectado.
SELECT current_database() AS base_de_datos,
       current_user AS usuario,
       inet_server_port() AS puerto,
       current_schema() AS esquema;

-- 2. Versión del esquema implementado: debe ser 3.
SELECT version, applied_at FROM public.decibelcity_schema;

-- 3. Tablas implementadas: diez de dominio y una técnica de versión.
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;

-- 4. Recuento de puntos y reportes. El respaldo inicial tiene 3 puntos y 2 reportes.
SELECT 'puntos_criticos' AS tabla, COUNT(*) AS registros FROM public.puntos_criticos
UNION ALL
SELECT 'reportes_ciudadanos', COUNT(*) FROM public.reportes_ciudadanos;

-- 5. Puntos con su distrito y fuente. LEFT JOIN conserva etiquetas territoriales heredadas.
SELECT p.id_punto, p.nombre,
       COALESCE(d.nombre_distrito, p.distrito_heredado) AS distrito,
       f.nombre_fuente AS fuente, p.nivel_db, p.ilustrativo
FROM public.puntos_criticos p
JOIN public.tipos_fuente_ruido f USING (id_tipo_fuente)
LEFT JOIN public.distritos d USING (id_distrito)
ORDER BY p.nombre;

-- 6. Reportes con fuente, estado y nivel estimado opcional.
SELECT r.id_reporte, f.nombre_fuente AS fuente, e.codigo_estado AS estado,
       r.descripcion, r.nivel_estimado_db, r.fecha_hora,
       r.id_lugar, r.proveedor_lugar, r.es_historico
FROM public.reportes_ciudadanos r
JOIN public.tipos_fuente_ruido f USING (id_tipo_fuente)
JOIN public.estados_reporte e USING (id_estado)
ORDER BY r.fecha_hora DESC, r.id_reporte
LIMIT 100;

-- 7. Categoría de cada opción del formulario.
SELECT f.codigo, f.nombre_fuente, c.nombre AS categoria
FROM public.tipos_fuente_ruido f
JOIN public.categorias_fuente_ruido c USING (id_categoria)
ORDER BY f.id_tipo_fuente;

-- 8. Jerarquía territorial y sus banderas de disponibilidad independientes.
SELECT dep.nombre_departamento, dep.activo AS departamento_activo,
       pr.nombre_provincia, pr.activo AS provincia_activa,
       d.nombre_distrito, d.ubigeo, d.activo AS distrito_activo
FROM public.distritos d
JOIN public.provincias pr USING (id_provincia)
JOIN public.departamentos dep USING (id_departamento)
ORDER BY d.ubigeo;

-- 9. Resumen por fuente, incluyendo las que todavía no tienen reportes.
-- AVG omite niveles NULL; el promedio sigue siendo una estimación ciudadana.
SELECT f.codigo, COUNT(r.id_reporte) AS total_reportes,
       COUNT(r.nivel_estimado_db) AS reportes_con_nivel,
       ROUND(AVG(r.nivel_estimado_db)::numeric, 2) AS promedio_estimado_db
FROM public.tipos_fuente_ruido f
LEFT JOIN public.reportes_ciudadanos r USING (id_tipo_fuente)
GROUP BY f.id_tipo_fuente, f.codigo
ORDER BY total_reportes DESC, f.codigo;
