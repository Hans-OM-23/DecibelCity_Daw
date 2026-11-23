# syntax=docker/dockerfile:1
# DecibelCity Frontend (Angular 22) — imagen de producción Nginx.
# Construir desde la raíz: docker build -t decibelcity-frontend:local frontend
# o desde frontend/: docker build -t decibelcity-frontend:local .

# ============================================================
# Etapa 1: Compilación de la aplicación Angular
# ============================================================
FROM node:22-alpine AS build
WORKDIR /app

# Instalar dependencias reutilizando capas de Docker
COPY package*.json ./
RUN npm ci

# Compilar para producción (genera dist/decibelcity/browser)
COPY . .
RUN npm run build -- --configuration production

# ============================================================
# Etapa 2: Servidor estático Nginx en Alpine
# ============================================================
FROM nginx:alpine AS final

# Variable de puerto predeterminada (Railway inyecta $PORT en ejecución)
# Restringir envsubst únicamente a la variable PORT para no colisionar con directivas de Nginx
ENV PORT=80 \
    NGINX_ENVSUBST_FILTER="PORT"

# Copiar plantilla Nginx (el script 20-envsubst-on-templates.sh de nginx:alpine sustituye ${PORT})
COPY nginx.conf.template /etc/nginx/templates/default.conf.template

# Copiar script de inicialización para inyectar DECIBELCITY_API_URL en env.js
COPY 40-decibelcity-env.sh /docker-entrypoint.d/40-decibelcity-env.sh
RUN chmod +x /docker-entrypoint.d/40-decibelcity-env.sh

# Copiar artefactos compilados de Angular
COPY --from=build /app/dist/decibelcity/browser /usr/share/nginx/html

EXPOSE 80
