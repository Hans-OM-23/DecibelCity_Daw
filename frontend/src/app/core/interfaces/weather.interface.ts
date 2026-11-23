export interface WeatherData {
  temperature_c: number;
  apparent_temperature_c: number;
  humidity_percent: number;
  precipitation_mm: number;
  wind_speed_kmh: number;
  weather_code: number;
  condition: string;
  icon: string;
  updated_at: string;
}
