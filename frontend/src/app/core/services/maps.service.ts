import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable, timeout } from 'rxjs';
import { MapBounds, MapPosition, MapsConfig, PlaceSuggestion } from '../interfaces/maps.interface';
import { API_BASE_URL } from './noise-api.service';

export class MapsUnavailableError extends Error {
  constructor(readonly code: string, message: string) {
    super(message);
    this.name = 'MapsUnavailableError';
  }
}

/** Leaflet module, loaded on demand so the map library stays out of the initial bundle. */
export type LeafletModule = typeof import('leaflet');

export interface LoadedMap {
  config: MapsConfig;
  leaflet: LeafletModule;
}

/**
 * OpenStreetMap integration: map tiles load in the browser; place search and
 * verification go through the backend (Photon and Nominatim). No API keys.
 */
@Injectable({ providedIn: 'root' })
export class MapsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private configPromise?: Promise<MapsConfig>;
  private leafletPromise?: Promise<LeafletModule>;

  getConfig(retry = false): Promise<MapsConfig> {
    if (retry) this.configPromise = undefined;
    if (!this.configPromise) {
      this.configPromise = firstValueFrom(this.http.get<MapsConfig>(`${this.baseUrl}/maps/config`).pipe(timeout(12000)))
        .then(config => {
          if (!config?.tile_url?.trim() || !this.isValidPosition(config.center) || !this.isValidBounds(config.bounds)
            || !this.isWithinBounds(config.center, config.bounds)) {
            throw new MapsUnavailableError('config', 'El mapa no está disponible en este momento. Inténtalo de nuevo más tarde.');
          }
          return config;
        })
        .catch((error: unknown) => {
          this.configPromise = undefined;
          if (error instanceof MapsUnavailableError) throw error;
          throw new MapsUnavailableError('config-network', 'No pudimos conectar con el servicio de mapas. Revisa tu conexión y vuelve a intentar.');
        });
    }
    return this.configPromise;
  }

  async loadMap(retry = false): Promise<LoadedMap> {
    const config = await this.getConfig(retry);
    if (!this.leafletPromise) {
      this.leafletPromise = import('leaflet')
        .then(module => ((module as { default?: LeafletModule }).default ?? module) as LeafletModule)
        .catch(() => {
          this.leafletPromise = undefined;
          throw new MapsUnavailableError('library', 'No pudimos cargar el mapa. Recarga la página para volver a intentarlo.');
        });
    }
    return { config, leaflet: await this.leafletPromise };
  }

  searchPlaces(query: string): Observable<PlaceSuggestion[]> {
    return this.http.get<PlaceSuggestion[]>(`${this.baseUrl}/places/search`, { params: { q: query } }).pipe(timeout(12000));
  }

  reverseGeocode(lat: number, lng: number): Observable<PlaceSuggestion> {
    return this.http.get<PlaceSuggestion>(`${this.baseUrl}/places/reverse`, {
      params: { lat: lat.toString(), lng: lng.toString() },
    }).pipe(timeout(12000));
  }

  isValidPosition(position: MapPosition | null | undefined): position is MapPosition {
    return !!position && Number.isFinite(position.lat) && Number.isFinite(position.lng)
      && position.lat >= -90 && position.lat <= 90 && position.lng >= -180 && position.lng <= 180;
  }

  isWithinBounds(position: MapPosition, bounds: MapBounds): boolean {
    return this.isValidPosition(position) && position.lat >= bounds.south && position.lat <= bounds.north
      && position.lng >= bounds.west && position.lng <= bounds.east;
  }

  messageFor(error: unknown): string {
    if (error instanceof MapsUnavailableError) return error.message;
    if (error instanceof HttpErrorResponse) {
      const detail: unknown = error.error?.detail;
      if (typeof detail === 'string' && detail.trim().length > 0) return detail;
      if ([502, 503].includes(error.status) && typeof detail === 'string') return detail;
      if (error.status === 422) return 'Escribe al menos 3 caracteres para buscar.';
    }
    if (error instanceof Error && error.name === 'TimeoutError') {
      return 'La búsqueda está tardando demasiado. Revisa tu conexión y vuelve a intentar.';
    }
    return 'No pudimos conectar con el servicio de mapas. Revisa tu conexión y vuelve a intentar.';
  }

  private isValidBounds(bounds: MapBounds | null | undefined): bounds is MapBounds {
    return !!bounds && this.isValidPosition({ lat: bounds.south, lng: bounds.west })
      && this.isValidPosition({ lat: bounds.north, lng: bounds.east })
      && bounds.south < bounds.north && bounds.west < bounds.east;
  }
}
