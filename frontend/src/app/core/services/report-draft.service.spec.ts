import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportDraftService } from './report-draft.service';
import { ReportDraft } from '../interfaces/report-draft.interface';
import { SelectedPlace } from '../interfaces/maps.interface';

const mockPlace: SelectedPlace = {
  placeId: 'W813046785',
  address: 'Calle Real 123, Huancayo',
  position: { lat: -12.067, lng: -75.21 },
};

const mockDraft: ReportDraft = {
  location: 'Calle Real 123, Huancayo',
  selectedPlace: mockPlace,
  source: 'traffic',
  includeDecibels: true,
  decibels: 75,
  description: 'Ruido excesivo por transporte público en hora punta.',
};

describe('ReportDraftService', () => {
  let service: ReportDraftService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [ReportDraftService],
    });
    service = TestBed.inject(ReportDraftService);
  });

  it('inicia sin borrador por defecto', () => {
    expect(service.hasDraft()).toBe(false);
    expect(service.getDraft()).toBeNull();
  });

  it('guarda el borrador en memoria y sessionStorage y actualiza hasDraftSignal', () => {
    service.saveDraft(mockDraft);

    expect(service.hasDraft()).toBe(true);
    expect(service.hasDraftSignal()).toBe(true);
    expect(service.getDraft()).toEqual(mockDraft);
  });

  it('limpia el borrador con clearDraft()', () => {
    service.saveDraft(mockDraft);
    expect(service.hasDraft()).toBe(true);

    service.clearDraft();
    expect(service.hasDraft()).toBe(false);
    expect(service.hasDraftSignal()).toBe(false);
    expect(service.getDraft()).toBeNull();
  });

  it('recupera el borrador desde sessionStorage si la instancia en memoria es nueva', () => {
    sessionStorage.setItem('decibelcity_pending_report_draft', JSON.stringify(mockDraft));

    const newServiceInstance = new ReportDraftService();
    expect(newServiceInstance.hasDraft()).toBe(true);
    expect(newServiceInstance.getDraft()).toEqual(mockDraft);
  });

  it('conserva y limpia el borrador en memoria si se bloquea el acceso a sessionStorage', () => {
    const storage = vi.spyOn(window, 'sessionStorage', 'get').mockImplementation(() => {
      throw new DOMException('Almacenamiento bloqueado', 'SecurityError');
    });
    try {
      const memoryOnly = new ReportDraftService();
      expect(() => memoryOnly.saveDraft(mockDraft)).not.toThrow();
      expect(memoryOnly.getDraft()).toEqual(mockDraft);
      expect(memoryOnly.hasDraftSignal()).toBe(true);
      expect(() => memoryOnly.clearDraft()).not.toThrow();
      expect(memoryOnly.getDraft()).toBeNull();
      expect(memoryOnly.hasDraftSignal()).toBe(false);
    } finally {
      storage.mockRestore();
    }
  });

  it('aísla la ubicación y las coordenadas de cambios ajenos al borrador guardado', () => {
    const original = structuredClone(mockDraft);
    service.saveDraft(original);
    original.selectedPlace!.position.lat = 0;
    const restored = service.getDraft()!;
    expect(restored).toEqual(mockDraft);
    restored.selectedPlace!.position.lng = 0;
    expect(service.getDraft()).toEqual(mockDraft);
  });

  it.each([
    '{', '{}', 'false', '[]',
    JSON.stringify({ ...mockDraft, selectedPlace: { ...mockPlace, position: { lat: 'texto', lng: 0 } } }),
  ])('ignora un borrador almacenado inválido sin romper el formulario', (raw) => {
    sessionStorage.setItem('decibelcity_pending_report_draft', raw);
    const restored = new ReportDraftService();
    expect(restored.getDraft()).toBeNull();
    expect(restored.hasDraftSignal()).toBe(false);
  });
});
