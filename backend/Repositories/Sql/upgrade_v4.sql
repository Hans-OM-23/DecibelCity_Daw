-- Migración v3 → v4: de Google Places a OpenStreetMap. Se ejecuta en la misma transacción
-- del arranque, bajo la cerradura de migraciones. No borra reportes.
ALTER TABLE reportes_ciudadanos RENAME COLUMN google_place_id TO id_lugar;
ALTER TABLE reportes_ciudadanos ADD COLUMN proveedor_lugar TEXT CHECK (proveedor_lugar IN ('google', 'osm'));
UPDATE reportes_ciudadanos SET proveedor_lugar = 'google' WHERE id_lugar IS NOT NULL;
ALTER TABLE reportes_ciudadanos
    ADD CONSTRAINT reportes_lugar_con_proveedor CHECK ((id_lugar IS NULL) = (proveedor_lugar IS NULL)),
    ADD CONSTRAINT reportes_lugar_osm_valido CHECK (proveedor_lugar IS DISTINCT FROM 'osm' OR id_lugar ~ '^[NWR][0-9]{1,19}$');

-- La consulta de texto solo servía para geocodificar con Google en el navegador.
ALTER TABLE puntos_criticos DROP COLUMN consulta_google;
ALTER TABLE puntos_criticos
    ADD COLUMN latitud DOUBLE PRECISION CHECK (latitud BETWEEN -90 AND 90),
    ADD COLUMN longitud DOUBLE PRECISION CHECK (longitud BETWEEN -180 AND 180),
    ADD CONSTRAINT puntos_coordenadas_completas CHECK ((latitud IS NULL) = (longitud IS NULL));
-- Posiciones aproximadas de OpenStreetMap para los tres puntos ilustrativos de ejemplo.
UPDATE puntos_criticos SET latitud = -12.06865, longitud = -75.21025 WHERE id_punto = 'sector-01';
UPDATE puntos_criticos SET latitud = -12.07304, longitud = -75.20502 WHERE id_punto = 'sector-02';
UPDATE puntos_criticos SET latitud = -12.07323, longitud = -75.21301 WHERE id_punto = 'sector-03';

UPDATE decibelcity_schema SET version = 4, applied_at = CURRENT_TIMESTAMP;
