import { HttpErrorResponse } from '@angular/common/http';
import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MapPoint, PlaceSuggestion, SelectedPlace } from '../../../core/interfaces/maps.interface';
import { MapsService } from '../../../core/services/maps.service';
import { MapComponent } from '../map/map.component';
import { LocationPickerComponent } from './location-picker.component';

@Component({ selector: 'app-map', standalone: true, template: '' })
class MapStubComponent {
  readonly points = input<readonly MapPoint[]>([]);
  readonly selectedId = input<string | null>(null);
}

const suggestions: PlaceSuggestion[] = [
  { place_id: 'W813046785', name: 'Avenida Giraldez', address: 'San Carlos, Huancayo', lat: -12.0664834, lng: -75.2062612 },
  { place_id: 'W515653867', name: 'Mercado Mayorista de Carne', address: 'Avenida Ferrocarril, Huancayo', lat: -12.0730445, lng: -75.2050162 },
];

describe('LocationPickerComponent: búsqueda de OpenStreetMap', () => {
  let fixture: ComponentFixture<LocationPickerComponent>;
  let search: ReturnType<typeof vi.fn<(query: string) => Observable<PlaceSuggestion[]>>>;
  let reverse: ReturnType<typeof vi.fn<(lat: number, lng: number) => Observable<PlaceSuggestion>>>;
  let emitted: (SelectedPlace | null)[];

  beforeEach(async () => {
    vi.useFakeTimers();
    search = vi.fn((_query: string) => of(suggestions));
    reverse = vi.fn((_lat: number, _lng: number) => of(suggestions[0]));
    const service = {
      searchPlaces: search,
      reverseGeocode: reverse,
      isValidPosition: (position: { lat: number; lng: number }) => Number.isFinite(position.lat) && Number.isFinite(position.lng),
      messageFor: (error: unknown) => error instanceof HttpErrorResponse && typeof error.error?.detail === 'string'
        ? error.error.detail : 'No pudimos conectar con el servicio de mapas.',
    };
    await TestBed.configureTestingModule({
      imports: [LocationPickerComponent],
      providers: [{ provide: MapsService, useValue: service }],
    })
      .overrideComponent(LocationPickerComponent, { remove: { imports: [MapComponent] }, add: { imports: [MapStubComponent] } })
      .compileComponents();
    fixture = TestBed.createComponent(LocationPickerComponent);
    emitted = [];
    fixture.componentInstance.locationSelected.subscribe(place => emitted.push(place));
    fixture.detectChanges();
  });

  afterEach(() => vi.useRealTimers());

  const input = (): HTMLInputElement => fixture.nativeElement.querySelector('#location-search');
  const key = (name: string): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { key: name, cancelable: true });
    input().dispatchEvent(event);
    fixture.detectChanges();
    return event;
  };
  const type = async (text: string): Promise<void> => {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    await vi.advanceTimersByTimeAsync(450);
    fixture.detectChanges();
  };

  it('espera 3 caracteres y agrupa las pulsaciones antes de buscar', async () => {
    await type('Gi');
    expect(search).not.toHaveBeenCalled();
    input().value = 'Gir';
    input().dispatchEvent(new Event('input'));
    await type('  Giraldez   ');
    expect(search).toHaveBeenCalledTimes(1);
    expect(search).toHaveBeenCalledWith('Giraldez');
    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelectorAll('[role="option"]').length).toBe(2);
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('2 sugerencias disponibles');
  });

  it('permite elegir con el teclado y Enter no envía texto sin confirmar', async () => {
    await type('Giraldez');
    expect(key('Enter').defaultPrevented).toBe(true);
    expect(emitted).toEqual([]);
    key('ArrowDown');
    key('ArrowDown');
    expect(input().getAttribute('aria-activedescendant')).toBe('location-option-1');
    key('Enter');
    expect(emitted).toEqual([{
      placeId: 'W515653867',
      address: 'Mercado Mayorista de Carne, Avenida Ferrocarril, Huancayo',
      position: { lat: -12.0730445, lng: -75.2050162 },
    }]);
    expect(fixture.nativeElement.textContent).toContain('Ubicación seleccionada');
    expect(fixture.nativeElement.querySelector('#location-search')).toBeNull();
  });

  it('Escape cierra la lista y cambiar ubicación anula la selección', async () => {
    await type('Giraldez');
    key('Escape');
    expect(input().getAttribute('aria-expanded')).toBe('false');
    fixture.nativeElement.querySelector('[role="option"]').click();
    fixture.detectChanges();
    expect(emitted.at(-1)?.placeId).toBe('W813046785');
    fixture.nativeElement.querySelector('.change-location').click();
    fixture.detectChanges();
    expect(emitted.at(-1)).toBeNull();
    expect(input().value).toBe('Giraldez');
  });

  it('informa cuando no hay resultados o el servicio falla', async () => {
    search.mockReturnValueOnce(of([]));
    await type('Lugar inexistente');
    expect(fixture.nativeElement.textContent).toContain('No encontramos lugares');
    search.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 503, error: { detail: 'La búsqueda de lugares no está disponible. Intenta más tarde.' },
    })));
    await type('Otra búsqueda');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('no está disponible');
    expect(emitted).toEqual([]);
  });

  it('restaura la ubicación seleccionada cuando se pasa selectedPlace por input y muestra el mapa', () => {
    const preselected: SelectedPlace = {
      placeId: 'W813046785',
      address: 'Avenida Giraldez, San Carlos, Huancayo',
      position: { lat: -12.0664834, lng: -75.2062612 },
    };

    fixture.componentRef.setInput('selectedPlace', preselected);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Ubicación seleccionada');
    expect(fixture.nativeElement.textContent).toContain('Avenida Giraldez');
    expect(fixture.nativeElement.querySelector('#location-search')).toBeNull();
    expect(fixture.componentInstance.mapPoints().length).toBe(1);
    expect(fixture.componentInstance.mapPoints()[0].id).toBe('W813046785');
  });

  describe('Geolocalización y Reverse-Geocoding', () => {
    it('obtiene la posición con el botón accesible, realiza reverse-geocoding y selecciona el lugar', () => {
      const getCurrentPositionMock = vi.fn((success: PositionCallback) => {
        success({
          coords: {
            latitude: -12.0664834,
            longitude: -75.2062612,
            accuracy: 10,
            altitude: null,
            altitudeAccuracy: null,
            heading: null,
            speed: null,
          },
          timestamp: Date.now(),
        } as GeolocationPosition);
      });

      vi.stubGlobal('navigator', {
        geolocation: { getCurrentPosition: getCurrentPositionMock },
      });

      const btn = fixture.nativeElement.querySelector('.geolocation-btn') as HTMLButtonElement;
      expect(btn).not.toBeNull();
      btn.click();
      fixture.detectChanges();

      expect(getCurrentPositionMock).toHaveBeenCalledWith(
        expect.any(Function),
        expect.any(Function),
        { enableHighAccuracy: true, timeout: 10000 },
      );
      expect(reverse).toHaveBeenCalledWith(-12.0664834, -75.2062612);
      expect(emitted.length).toBe(1);
      expect(emitted[0]?.placeId).toBe('W813046785');
      expect(fixture.nativeElement.textContent).toContain('Ubicación seleccionada');

      vi.unstubAllGlobals();
    });

    it('muestra error accesible cuando el usuario deniega el permiso de geolocalización', () => {
      const getCurrentPositionMock = vi.fn((_success: PositionCallback, error?: PositionErrorCallback) => {
        if (error) {
          error({
            code: 1, // PERMISSION_DENIED
            message: 'User denied geolocation',
            PERMISSION_DENIED: 1,
            POSITION_UNAVAILABLE: 2,
            TIMEOUT: 3,
          } as GeolocationPositionError);
        }
      });

      vi.stubGlobal('navigator', {
        geolocation: { getCurrentPosition: getCurrentPositionMock },
      });

      const btn = fixture.nativeElement.querySelector('.geolocation-btn') as HTMLButtonElement;
      btn.click();
      fixture.detectChanges();

      expect(reverse).not.toHaveBeenCalled();
      expect(emitted).toEqual([]);
      const errorAlert = fixture.nativeElement.querySelector('[role="alert"]');
      expect(errorAlert).not.toBeNull();
      expect(errorAlert.textContent).toContain('Permiso denegado para acceder a tu ubicación');

      vi.unstubAllGlobals();
    });

    it('muestra el mensaje de error cuando reverse-geocoding devuelve HTTP 422 (fuera de cobertura)', () => {
      const getCurrentPositionMock = vi.fn((success: PositionCallback) => {
        success({
          coords: {
            latitude: -11.5,
            longitude: -77.0,
            accuracy: 10,
            altitude: null,
            altitudeAccuracy: null,
            heading: null,
            speed: null,
          },
          timestamp: Date.now(),
        } as GeolocationPosition);
      });

      reverse.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 422,
        error: { detail: 'La ubicación se encuentra fuera del área de cobertura de Huancayo.' },
      })));

      vi.stubGlobal('navigator', {
        geolocation: { getCurrentPosition: getCurrentPositionMock },
      });

      const btn = fixture.nativeElement.querySelector('.geolocation-btn') as HTMLButtonElement;
      btn.click();
      fixture.detectChanges();

      expect(reverse).toHaveBeenCalledWith(-11.5, -77.0);
      expect(emitted).toEqual([]);
      const errorAlert = fixture.nativeElement.querySelector('[role="alert"]');
      expect(errorAlert).not.toBeNull();
      expect(errorAlert.textContent).toContain('fuera del área de cobertura');

      vi.unstubAllGlobals();
    });
  });
});
