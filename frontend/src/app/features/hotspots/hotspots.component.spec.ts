import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { HotspotsComponent } from './hotspots.component';
import { NoiseApiService } from '../../core/services/noise-api.service';
import { Hotspot } from '../../core/interfaces/noise.interface';

const mockHotspots: Hotspot[] = [
  {
    id: 'sector-01',
    name: 'Zona Comercial Real',
    area: 'Centro de Huancayo',
    district: 'Huancayo',
    sector: 'Sector 01',
    decibels: 74,
    source: 'traffic',
    source_label: 'Tráfico vehicular',
    description: 'Congestión vehicular constante.',
    time_label: 'Hora punta mañana',
    x: 45,
    y: 52,
    reference_limit: 70,
    illustrative: true,
    latitude: -12.06865,
    longitude: -75.21025,
  },
  {
    id: 'report-abc',
    name: 'Plaza Constitución',
    area: 'Huancayo',
    district: 'Huancayo',
    sector: 'Reporte ciudadano',
    decibels: 82,
    source: 'commerce',
    source_label: 'Comercio ambulatorio',
    description: 'Megáfonos y música a alto volumen.',
    time_label: 'Reporte registrado',
    x: 50,
    y: 50,
    reference_limit: 70,
    illustrative: false,
    latitude: -12.067,
    longitude: -75.21,
  },
];

describe('HotspotsComponent', () => {
  let fixture: ComponentFixture<HotspotsComponent>;
  let component: HotspotsComponent;
  let apiMock: { getHotspots: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    apiMock = {
      getHotspots: vi.fn(() => of(mockHotspots)),
    };

    await TestBed.configureTestingModule({
      imports: [HotspotsComponent],
      providers: [
        provideRouter([]),
        { provide: NoiseApiService, useValue: apiMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(HotspotsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('carga los puntos críticos y los almacena en la señal', () => {
    expect(component.hotspots().length).toBe(2);
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe('');
  });

  it('genera mapPoints diferenciando ejemplos de reportes ciudadanos', () => {
    const points = component.mapPoints();
    expect(points.length).toBe(2);
    expect(points[0].label).toContain('ejemplo');
    expect(points[1].label).toContain('reporte');
  });

  it('asigna correctamente los iconos según la fuente', () => {
    expect(component.sourceIcon('traffic')).toBe('bus');
    expect(component.sourceIcon('commerce')).toBe('megaphone');
    expect(component.sourceIcon('construction')).toBe('truck');
    expect(component.sourceIcon('entertainment')).toBe('radio');
    expect(component.sourceIcon('other')).toBe('volume');
  });

  it('permite seleccionar y deseleccionar un punto', () => {
    expect(component.selectedId()).toBeNull();
    component.selectPoint(mockHotspots[0]);
    expect(component.selectedId()).toBe('sector-01');
    expect(component.selectedHotspot()?.name).toBe('Zona Comercial Real');

    // Deseleccionar al hacer clic de nuevo
    component.selectPoint(mockHotspots[0]);
    expect(component.selectedId()).toBeNull();
  });

  it('muestra mensaje de error cuando la API falla', () => {
    apiMock.getHotspots.mockReturnValue(throwError(() => new Error('Error de red')));
    component.loadHotspots();
    expect(component.error()).toContain('No pudimos cargar los puntos críticos');
  });
});
