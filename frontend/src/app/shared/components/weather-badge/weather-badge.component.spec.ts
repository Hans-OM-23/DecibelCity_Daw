import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { WeatherData } from '../../../core/interfaces/weather.interface';
import { WeatherService } from '../../../core/services/weather.service';
import { WeatherBadgeComponent } from './weather-badge.component';

const mockWeather: WeatherData = {
  temperature_c: 16.5,
  apparent_temperature_c: 15.8,
  humidity_percent: 68,
  precipitation_mm: 0.2,
  wind_speed_kmh: 12.4,
  weather_code: 3,
  condition: 'Parcialmente nublado',
  icon: 'cloud',
  updated_at: '2026-11-20T08:00:00Z',
};

describe('WeatherBadgeComponent: widget de clima Open-Meteo', () => {
  let fixture: ComponentFixture<WeatherBadgeComponent>;
  let weatherServiceMock: { getWeather: ReturnType<typeof vi.fn> };

  it('muestra la temperatura, condición, humedad, viento y atribución ante respuesta exitosa', () => {
    weatherServiceMock = {
      getWeather: vi.fn(() => of(mockWeather)),
    };

    TestBed.configureTestingModule({
      imports: [WeatherBadgeComponent],
      providers: [{ provide: WeatherService, useValue: weatherServiceMock }],
    }).compileComponents();

    fixture = TestBed.createComponent(WeatherBadgeComponent);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const badge = el.querySelector('.weather-badge');
    expect(badge).not.toBeNull();
    expect(el.textContent).toContain('16.5°C');
    expect(el.textContent).toContain('Parcialmente nublado');
    expect(el.textContent).toContain('68%');
    expect(el.textContent).toContain('12.4 km/h');
    expect(el.textContent).toContain('Open-Meteo');
    expect(el.textContent).toContain('Dato ambiental real');
    expect(el.textContent).toContain('No asociado al nivel de ruido');
  });

  it('degrada elegantemente sin romper cuando el servicio falla', () => {
    weatherServiceMock = {
      getWeather: vi.fn(() => throwError(() => new Error('Error al conectar con Open-Meteo'))),
    };

    TestBed.configureTestingModule({
      imports: [WeatherBadgeComponent],
      providers: [{ provide: WeatherService, useValue: weatherServiceMock }],
    }).compileComponents();

    fixture = TestBed.createComponent(WeatherBadgeComponent);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.failed()).toBe(true);
    expect(fixture.componentInstance.weather()).toBeNull();
    // No debe renderizar el badge ni arrojar excepción
    expect(el.querySelector('.weather-badge')).toBeNull();
  });
});
