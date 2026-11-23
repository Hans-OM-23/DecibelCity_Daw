import { HttpErrorResponse } from '@angular/common/http';
import { Component, input, output, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SelectedPlace } from '../../core/interfaces/maps.interface';
import { NoiseReport, NoiseReportCreate, NoiseSource } from '../../core/interfaces/noise.interface';
import { NoiseApiService } from '../../core/services/noise-api.service';
import { AuthService } from '../../core/services/auth.service';
import { ReportDraftService } from '../../core/services/report-draft.service';
import { LocationPickerComponent } from '../../shared/components/location-picker/location-picker.component';
import { ReportComponent } from './report.component';

@Component({
  selector: 'app-location-picker',
  standalone: true,
  template: '<input aria-label="Buscar ubicación" [disabled]="disabled()" (input)="locationSelected.emit(null)">',
})
class LocationPickerStubComponent {
  readonly disabled = input(false);
  readonly selectedPlace = input<SelectedPlace | null>(null);
  readonly locationSelected = output<SelectedPlace | null>();
}

const place: SelectedPlace = {
  placeId: 'W813046785',
  address: 'Calle Real 123, Huancayo, Perú',
  position: { lat: -12.067, lng: -75.21 },
};
const description = 'Bocinas constantes junto al paradero durante la tarde.';
const sources: NoiseSource[] = [{ value: 'traffic', label: 'Tráfico vehicular y transporte público' }];

function createApiMock(response: Subject<NoiseReport>) {
  return {
    getNoiseSources: vi.fn(() => of(sources)),
    createReport: vi.fn((_payload: NoiseReportCreate) => response.asObservable()),
  };
}

describe('ReportComponent: reportes con ubicación seleccionada y autenticación', () => {
  let fixture: ComponentFixture<ReportComponent>;
  let component: ReportComponent;
  let response: Subject<NoiseReport>;
  let api: ReturnType<typeof createApiMock>;
  let authServiceMock: {
    isAuthenticated: ReturnType<typeof vi.fn>;
    checkSession: ReturnType<typeof vi.fn>;
    clearLocalSession: ReturnType<typeof vi.fn>;
  };
  let reportDraftService: ReportDraftService;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    response = new Subject<NoiseReport>();
    api = createApiMock(response);
    authServiceMock = {
      isAuthenticated: vi.fn(() => true),
      checkSession: vi.fn(() => of({ id_usuario: 1 })),
      clearLocalSession: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [ReportComponent],
      providers: [
        provideRouter([]),
        { provide: NoiseApiService, useValue: api },
        { provide: AuthService, useValue: authServiceMock },
        ReportDraftService,
      ],
    })
      .overrideComponent(ReportComponent, {
        remove: { imports: [LocationPickerComponent] },
        add: { imports: [LocationPickerStubComponent] },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate');
    reportDraftService = TestBed.inject(ReportDraftService);
    reportDraftService.clearDraft();

    fixture = TestBed.createComponent(ReportComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function element<T extends HTMLElement>(selector: string): T {
    const found = (fixture.nativeElement as HTMLElement).querySelector<T>(selector);
    if (!found) throw new Error(`No se encontró el elemento: ${selector}`);
    return found;
  }

  function fillDetails(): void {
    const source = element<HTMLSelectElement>('#report-source');
    source.value = 'traffic';
    source.dispatchEvent(new Event('change', { bubbles: true }));
    const textarea = element<HTMLTextAreaElement>('#report-description');
    textarea.value = description;
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function selectPlace(): void {
    const picker = fixture.debugElement.query(By.directive(LocationPickerStubComponent))
      .componentInstance as LocationPickerStubComponent;
    picker.locationSelected.emit(place);
    fixture.detectChanges();
  }

  function submitForm(): void {
    element<HTMLFormElement>('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  it('no envía texto de búsqueda sin seleccionar un lugar, incluso si se dispara submit', () => {
    fillDetails();
    const search = element<HTMLInputElement>('app-location-picker input');
    search.value = 'Una dirección escrita sin verificar';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    expect(element<HTMLButtonElement>('.submit-button').disabled).toBe(true);
    submitForm();

    expect(api.createReport).not.toHaveBeenCalled();
    expect(component.selectedPlace()).toBeNull();
    expect(element<HTMLElement>('#location-error').textContent).toContain('Selecciona una ubicación');
    expect(fixture.nativeElement.querySelector('.receipt-card')).toBeNull();
  });

  it('envía el place_id seleccionado y omite el nivel cuando el usuario está autenticado', () => {
    fillDetails();
    selectPlace();
    expect(element<HTMLButtonElement>('.submit-button').disabled).toBe(false);

    submitForm();

    expect(api.createReport).toHaveBeenCalledExactlyOnceWith({
      location: place.address,
      place_id: place.placeId,
      source: 'traffic',
      description,
      decibels: null,
    });
    expect(component.submitting()).toBe(true);
    expect(fixture.nativeElement.querySelector('.receipt-card')).toBeNull();
  });

  it('bloquea el envío si el usuario no tiene sesión, guarda el borrador y redirige a /login con returnUrl', () => {
    authServiceMock.isAuthenticated.mockReturnValue(false);
    authServiceMock.checkSession.mockReturnValue(of(null));

    fillDetails();
    selectPlace();
    component.decibels.set(80);
    component.includeDecibels.set(true);
    fixture.detectChanges();

    submitForm();

    expect(api.createReport).not.toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/reportar' },
    });

    const savedDraft = reportDraftService.getDraft();
    expect(savedDraft).not.toBeNull();
    expect(savedDraft?.location).toBe(place.address);
    expect(savedDraft?.selectedPlace).toEqual(place);
    expect(savedDraft?.source).toBe('traffic');
    expect(savedDraft?.description).toBe(description);
    expect(savedDraft?.includeDecibels).toBe(true);
    expect(savedDraft?.decibels).toBe(80);
  });

  it('conserva los campos incompletos y el nivel desactivado antes de redirigir al login', () => {
    authServiceMock.checkSession.mockReturnValue(of(null));
    selectPlace();
    component.description.set('  Pendiente  ');
    component.decibels.set(84);
    component.includeDecibels.set(false);
    submitForm();
    expect(api.createReport).not.toHaveBeenCalled();
    expect(reportDraftService.getDraft()).toMatchObject({ description: '  Pendiente  ', decibels: 84, includeDecibels: false });
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/reportar' } });
  });

  it('no envía el reporte si la consulta de sesión falla, y conserva el borrador', () => {
    fillDetails();
    selectPlace();
    authServiceMock.checkSession.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    submitForm();
    expect(api.createReport).not.toHaveBeenCalled();
    expect(reportDraftService.getDraft()?.selectedPlace).toEqual(place);
    expect(component.submitError()).toContain('Tus datos siguen');
  });

  it.each([401, 403])('conserva el último borrador si la sesión deja de ser válida durante el POST (%s)', (status) => {
    fillDetails();
    selectPlace();
    component.description.set('Descripción editada antes de enviar.');
    submitForm();
    response.error(new HttpErrorResponse({ status }));
    fixture.detectChanges();
    expect(reportDraftService.getDraft()?.description).toBe('Descripción editada antes de enviar.');
    expect(reportDraftService.getDraft()?.selectedPlace).toEqual(place);
    expect(authServiceMock.clearLocalSession).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/reportar' } });
    expect(component.receipt()).toBeNull();
  });

  it('recupera íntegramente el borrador guardado al inicializarse el componente', () => {
    reportDraftService.saveDraft({
      location: place.address,
      selectedPlace: place,
      source: 'traffic',
      includeDecibels: true,
      decibels: 85,
      description: 'Ruido de bocinas persistente.',
    });

    const newFixture = TestBed.createComponent(ReportComponent);
    const newComponent = newFixture.componentInstance;
    newFixture.detectChanges();

    expect(newComponent.location()).toBe(place.address);
    expect(newComponent.selectedPlace()).toEqual(place);
    expect(newComponent.source()).toBe('traffic');
    expect(newComponent.includeDecibels()).toBe(true);
    expect(newComponent.decibels()).toBe(85);
    expect(newComponent.description()).toBe('Ruido de bocinas persistente.');

    const picker = newFixture.debugElement.query(By.directive(LocationPickerStubComponent))
      .componentInstance as LocationPickerStubComponent;
    expect(picker.selectedPlace()).toEqual(place);
  });

  it('restaura un nivel vacío sin convertirlo en 70', () => {
    reportDraftService.saveDraft({ location: place.address, selectedPlace: place, source: 'traffic',
      includeDecibels: true, decibels: null, description });
    const restored = TestBed.createComponent(ReportComponent);
    restored.detectChanges();
    expect(restored.componentInstance.decibels()).toBeNull();
    expect(restored.componentInstance.fieldError('decibels')).toBe('');
    expect(reportDraftService.hasDraft()).toBe(true);
  });

  it('invalida la selección anterior cuando se vuelve a editar la búsqueda', () => {
    fillDetails();
    selectPlace();
    const search = element<HTMLInputElement>('app-location-picker input');
    search.value = 'Otra calle sin seleccionar';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    expect(component.selectedPlace()).toBeNull();
    expect(component.location()).toBe('');
    expect(element<HTMLButtonElement>('.submit-button').disabled).toBe(true);
    expect(element<HTMLElement>('#submit-location-hint').textContent).toContain('Selecciona una ubicación');
    submitForm();
    expect(api.createReport).not.toHaveBeenCalled();
  });

  it('bloquea envíos duplicados y la edición del selector mientras espera la respuesta', () => {
    fillDetails();
    selectPlace();
    submitForm();
    submitForm();

    expect(api.createReport).toHaveBeenCalledTimes(1);
    expect(element<HTMLButtonElement>('.submit-button').disabled).toBe(true);
    expect(element<HTMLInputElement>('app-location-picker input').disabled).toBe(true);
    expect(element<HTMLTextAreaElement>('#report-description').closest('fieldset')?.disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.receipt-card')).toBeNull();
  });

  it('conserva el lugar y la descripción y muestra el motivo seguro de un rechazo del backend', () => {
    fillDetails();
    selectPlace();
    submitForm();
    const detail = 'El lugar debe estar dentro del área de cobertura de Huancayo, Perú.';
    response.error(new HttpErrorResponse({ status: 422, error: { detail } }));
    fixture.detectChanges();

    expect(component.selectedPlace()).toEqual(place);
    expect(component.location()).toBe(place.address);
    expect(element<HTMLTextAreaElement>('#report-description').value).toBe(description);
    expect(element<HTMLElement>('.submit-error').textContent).toContain(detail);
    expect(element<HTMLButtonElement>('.submit-button').disabled).toBe(false);
    expect(element<HTMLButtonElement>('.submit-button').textContent).toContain('Reintentar envío');
    expect(component.receipt()).toBeNull();
    expect(reportDraftService.getDraft()?.selectedPlace).toEqual(place);
    expect(reportDraftService.getDraft()?.description).toBe(description);
    expect(fixture.nativeElement.querySelector('.receipt-card')).toBeNull();
  });

  it('muestra el recibo solo tras confirmación y limpia el borrador', () => {
    reportDraftService.saveDraft({
      location: place.address,
      selectedPlace: place,
      source: 'traffic',
      includeDecibels: false,
      decibels: null,
      description,
    });
    expect(reportDraftService.hasDraft()).toBe(true);

    fillDetails();
    selectPlace();
    submitForm();

    const receipt: NoiseReport = {
      location: 'Calle Real 123, Huancayo 12001, Perú',
      place_id: place.placeId,
      source: 'traffic',
      description,
      decibels: null,
      id: 'report-verified-001',
      created_at: '2026-10-28T15:30:00Z',
      status: 'received',
      latitude: place.position.lat,
      longitude: place.position.lng,
    };
    response.next(receipt);
    response.complete();
    fixture.detectChanges();

    expect(element<HTMLElement>('.receipt-card').textContent).toContain(receipt.id);
    expect(element<HTMLElement>('.receipt-card').textContent).toContain(receipt.location);
    expect(fixture.nativeElement.querySelector('.report-form')).toBeNull();
    expect(reportDraftService.hasDraft()).toBe(false);

    element<HTMLButtonElement>('.receipt-actions button').click();
    fixture.detectChanges();

    expect(component.selectedPlace()).toBeNull();
    expect(element<HTMLTextAreaElement>('#report-description').value).toBe('');
    expect(reportDraftService.hasDraft()).toBe(false);
    expect(fixture.nativeElement.querySelector('.receipt-card')).toBeNull();
    expect(element<HTMLButtonElement>('.submit-button').disabled).toBe(true);
    expect(api.createReport).toHaveBeenCalledTimes(1);
  });

  it('newReport elimina también un borrador pendiente sin recibo', () => {
    fillDetails();
    selectPlace();
    reportDraftService.saveDraft({ location: place.address, selectedPlace: place, source: 'traffic',
      includeDecibels: true, decibels: 89, description });
    component.newReport();
    fixture.detectChanges();
    expect(reportDraftService.getDraft()).toBeNull();
    expect(component.selectedPlace()).toBeNull();
    expect(component.description()).toBe('');
  });
});
