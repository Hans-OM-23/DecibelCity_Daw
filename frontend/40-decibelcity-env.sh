#!/bin/sh
set -e

TARGET="/usr/share/nginx/html/env.js"
API_URL="${DECIBELCITY_API_URL:-${BACKEND_URL:-}}"

cat <<EOF > "$TARGET"
window.__DECIBELCITY_CONFIG__ = {
  apiBaseUrl: "${API_URL}"
};
EOF

chmod 644 "$TARGET"
echo "[DecibelCity] env.js generado con apiBaseUrl='${API_URL}'"
