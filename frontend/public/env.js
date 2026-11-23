// Configuración de entorno en tiempo de ejecución para DecibelCity.
// En producción, el script 40-decibelcity-env.sh en el contenedor Nginx sobrescribe este archivo
// antes de iniciar el servidor con el valor de la variable DECIBELCITY_API_URL (o BACKEND_URL).
window.__DECIBELCITY_CONFIG__ = {
  apiBaseUrl: ''
};
