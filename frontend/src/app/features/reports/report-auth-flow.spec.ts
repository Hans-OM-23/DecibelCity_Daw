import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TerritoriesResponse, UserProfile } from '../../core/interfaces/auth.interface';
import { MapPoint, SelectedPlace } from '../../core/interfaces/maps.interface';
import { NoiseReport } from '../../core/interfaces/noise.interface';
import { AuthService } from '../../core/services/auth.service';
import { MapsService } from '../../core/services/maps.service';
import { NoiseApiService } from '../../core/services/noise-api.service';
import { ReportDraftService } from '../../core/services/report-draft.service';
import { LoginComponent } from '../auth/login/login.component';
import { RegisterComponent } from '../auth/register/register.component';
import { LocationPickerComponent } from '../../shared/components/location-picker/location-picker.component';
import { MapComponent } from '../../shared/components/map/map.component';
import { ReportComponent } from './report.component';

@Component({ selector: 'app-map', standalone: true, template: '<span class="qa-marker">{{ points()[0]?.id }}</span>' })
class FlowMapStub {
  readonly points = input<readonly MapPoint[]>([]);
  readonly selectedId = input<string | null>(null);
}

const place: SelectedPlace = { placeId: 'W813046785', address: 'Avenida Giráldez, Huancayo',
  position: { lat: -12.0664834, lng: -75.2062612 } };
const account = { nombres: 'Ana', apellidos: 'Quispe', telefono: '123456789', direccion: 'Jr. Ica 123',
  id_departamento: 1, id_provincia: 1, id_distrito: 1, nombre_usuario: 'ana.quispe', contrasena: 'Clave segura 2026!' };
const user: UserProfile = { id_usuario: 1, nombres: 'Ana', apellidos: 'Quispe', telefono: '123456789',
  direccion: 'Jr. Ica 123', nombre_usuario: 'ana.quispe', id_departamento: 1, id_provincia: 1, id_distrito: 1,
  nombre_departamento: 'Junín', nombre_provincia: 'Huancayo', nombre_distrito: 'Huancayo',
  rol: 'ciudadano', fecha_registro: '2026-11-19T00:00:00Z' };
const territories: TerritoriesResponse = {
  departamentos: [{ id_departamento: 1, nombre_departamento: 'Junín', ubigeo: '12' }],
  provincias: [{ id_departamento: 1, id_provincia: 1, nombre_provincia: 'Huancayo', ubigeo: '1201' }],
  distritos: [{ id_provincia: 1, id_distrito: 1, nombre_distrito: 'Huancayo', ubigeo: '120101' }],
};

describe('Navegación de reporte, login y registro con borrador', () => {
  let authenticated: boolean;
  let result: Subject<NoiseReport>;
  let createReport: ReturnType<typeof vi.fn>;
  let register: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    sessionStorage.clear();
    authenticated = false;
    result = new Subject<NoiseReport>();
    createReport = vi.fn(() => result.asObservable());
    register = vi.fn(() => { authenticated = true; return of(user); });
    await TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'reportar', component: ReportComponent },
          { path: 'login', component: LoginComponent },
          { path: 'registro', component: RegisterComponent },
        ]),
        { provide: NoiseApiService, useValue: {
          getNoiseSources: () => of([{ value: 'traffic', label: 'Tráfico' }]), createReport,
        } },
        { provide: MapsService, useValue: { searchPlaces: () => of([]), isValidPosition: () => true } },
        { provide: AuthService, useValue: {
          checkSession: () => of(authenticated ? user : null),
          clearLocalSession: () => { authenticated = false; },
          getTerritories: () => of(territories), register,
          login: () => { authenticated = true; return of(user); },
        } },
      ],
    }).overrideComponent(LocationPickerComponent, {
      remove: { imports: [MapComponent] }, add: { imports: [FlowMapStub] },
    }).compileComponents();
  });

  it.each(['login', 'registro'] as const)('recupera todos los campos y el marcador al volver desde %s', async (access) => {
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);
    const drafts = TestBed.inject(ReportDraftService);
    const report = await harness.navigateByUrl('/reportar', ReportComponent);
    report.selectLocation(place);
    report.source.set('traffic');
    report.description.set('  Bocinas constantes durante toda la tarde.  ');
    report.includeDecibels.set(false);
    report.decibels.set(83);
    harness.detectChanges();
    report.submit(new Event('submit', { cancelable: true }));
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(router.url).toBe('/login?returnUrl=%2Freportar');
    expect(createReport).not.toHaveBeenCalled();
    const saved = drafts.getDraft();
    expect(saved?.selectedPlace).toEqual(place);
    expect(saved?.decibels).toBe(83);
    expect(saved?.description).toBe('  Bocinas constantes durante toda la tarde.  ');

    if (access === 'registro') {
      const link = harness.routeNativeElement!.querySelector<HTMLAnchorElement>('a[href^="/registro"]')!;
      expect(link).not.toBeNull();
      link.click();
      await harness.fixture.whenStable();
      harness.detectChanges();
      expect(router.url).toBe('/registro?returnUrl=%2Freportar');
      expect(drafts.getDraft()).toEqual(saved);
      const registration = harness.routeDebugElement!.componentInstance as RegisterComponent;
      registration.registerForm.setValue(account);
      registration.onSubmit();
      expect(register).toHaveBeenCalledWith(account);
    } else {
      const login = harness.routeDebugElement!.componentInstance as LoginComponent;
      login.loginForm.setValue({ nombre_usuario: account.nombre_usuario, contrasena: account.contrasena });
      login.onSubmit();
    }
    await vi.waitFor(() => expect(router.url).toBe('/reportar'), { timeout: 3000 });
    await harness.fixture.whenStable();
    harness.detectChanges();
    const restored = harness.routeDebugElement!.componentInstance as ReportComponent;
    expect(restored.selectedPlace()).toEqual(place);
    expect(restored.location()).toBe(place.address);
    expect(restored.source()).toBe('traffic');
    expect(restored.description()).toBe(saved!.description);
    expect(restored.includeDecibels()).toBe(false);
    expect(restored.decibels()).toBe(83);
    const map = harness.routeDebugElement!.query(By.directive(FlowMapStub)).componentInstance as FlowMapStub;
    expect(map.points()[0].position).toEqual(place.position);
    expect(map.selectedId()).toBe(place.placeId);
    expect(harness.routeNativeElement!.querySelector('.qa-marker')?.textContent).toBe(place.placeId);
    expect(drafts.getDraft()).toEqual(saved);
    expect(createReport).not.toHaveBeenCalled();

    restored.submit(new Event('submit', { cancelable: true }));
    expect(createReport).toHaveBeenCalledExactlyOnceWith({ location: place.address, place_id: place.placeId,
      source: 'traffic', decibels: null, description: saved!.description.trim() });
    expect(drafts.hasDraft()).toBe(true);
    result.next({ id: 'qa-report', place_id: place.placeId, location: place.address, source: 'traffic', decibels: null,
      description: saved!.description.trim(), status: 'received', created_at: '2026-11-19T00:00:00Z',
      latitude: place.position.lat, longitude: place.position.lng });
    result.complete();
    expect(drafts.hasDraft()).toBe(false);
  });

  it.each(['login', 'registro'] as const)('guarda el formulario al entrar directamente a /%s sin pulsar enviar', async (access) => {
    const harness = await RouterTestingHarness.create();
    const report = await harness.navigateByUrl('/reportar', ReportComponent);
    report.selectLocation(place);
    report.source.set('traffic');
    report.description.set('Texto que todavía no se ha enviado.');
    report.decibels.set(87);
    const page = access === 'login'
      ? await harness.navigateByUrl('/login', LoginComponent)
      : await harness.navigateByUrl('/registro', RegisterComponent);
    expect(page.returnUrl).toBe('/reportar');
    expect(page.hasPendingDraft()).toBe(true);
    expect(TestBed.inject(ReportDraftService).getDraft()?.selectedPlace).toEqual(place);
    const restored = await harness.navigateByUrl('/reportar', ReportComponent);
    expect(restored.description()).toBe('Texto que todavía no se ha enviado.');
    expect(restored.decibels()).toBe(87);
    expect(createReport).not.toHaveBeenCalled();
    restored.newReport();
    await harness.navigateByUrl('/login', LoginComponent);
    expect(TestBed.inject(ReportDraftService).hasDraft()).toBe(false);
  });
});
