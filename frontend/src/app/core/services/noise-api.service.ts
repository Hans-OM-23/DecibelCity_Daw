import { inject, Injectable, InjectionToken } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timeout } from 'rxjs';
import { Hotspot, NoiseReport, NoiseReportCreate, NoiseSource } from '../interfaces/noise.interface';
import { environment } from '../../../environments/environment';

export interface DecibelCityWindowConfig {
  apiBaseUrl?: string;
}

function normalizeApiBaseUrl(url: string | undefined): string | null {
  if (!url) return null;
  const trimmed = url.trim().replace(/\/+$/, '');
  if (!trimmed) return null;
  return trimmed.endsWith('/api') ? trimmed : `${trimmed}/api`;
}

export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => {
    // 1. Configuración dinámica en runtime inyectada por el contenedor vía env.js
    if (typeof window !== 'undefined') {
      const runtimeConfig = (window as Window & { __DECIBELCITY_CONFIG__?: DecibelCityWindowConfig }).__DECIBELCITY_CONFIG__;
      const normalized = normalizeApiBaseUrl(runtimeConfig?.apiBaseUrl);
      if (normalized) return normalized;
    }

    // 2. Configuración en tiempo de compilación (environment.ts / environment.production.ts)
    const envNormalized = normalizeApiBaseUrl(environment.apiBaseUrl);
    if (envNormalized) return envNormalized;

    // 3. Fallback para desarrollo local con proxy.conf.json
    return '/api';
  },
});

@Injectable({ providedIn: 'root' })
export class NoiseApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getHotspots(): Observable<Hotspot[]> {
    return this.http.get<Hotspot[]>(`${this.baseUrl}/hotspots`).pipe(timeout(12000));
  }

  getNoiseSources(): Observable<NoiseSource[]> {
    return this.http.get<NoiseSource[]>(`${this.baseUrl}/noise-sources`).pipe(timeout(12000));
  }

  createReport(payload: NoiseReportCreate): Observable<NoiseReport> {
    return this.http.post<NoiseReport>(`${this.baseUrl}/reports`, payload, { withCredentials: true }).pipe(timeout(20000));
  }
}
