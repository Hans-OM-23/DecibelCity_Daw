import { Injectable, signal } from '@angular/core';
import { ReportDraft } from '../interfaces/report-draft.interface';

const DRAFT_STORAGE_KEY = 'decibelcity_pending_report_draft';

@Injectable({ providedIn: 'root' })
export class ReportDraftService {
  private inMemoryDraft: ReportDraft | null = null;
  readonly hasDraftSignal = signal(false);

  constructor() {
    this.syncHasDraft();
  }

  saveDraft(draft: ReportDraft): void {
    this.inMemoryDraft = copyDraft(draft);
    try {
      if (typeof window !== 'undefined') window.sessionStorage.setItem(DRAFT_STORAGE_KEY, JSON.stringify(draft));
    } catch {
      // La copia en memoria sigue disponible si el navegador bloquea el almacenamiento.
    }
    this.hasDraftSignal.set(true);
  }

  getDraft(): ReportDraft | null {
    if (this.inMemoryDraft) {
      return copyDraft(this.inMemoryDraft);
    }
    try {
      if (typeof window !== 'undefined') {
        const raw = window.sessionStorage.getItem(DRAFT_STORAGE_KEY);
        const parsed: unknown = raw ? JSON.parse(raw) : null;
        if (isReportDraft(parsed)) {
          this.inMemoryDraft = copyDraft(parsed);
          this.hasDraftSignal.set(true);
          return copyDraft(parsed);
        }
      }
    } catch {
      // Los datos corruptos o inaccesibles no deben impedir abrir el formulario.
    }
    return null;
  }

  hasDraft(): boolean {
    return this.getDraft() !== null;
  }

  clearDraft(): void {
    this.inMemoryDraft = null;
    try {
      if (typeof window !== 'undefined') window.sessionStorage.removeItem(DRAFT_STORAGE_KEY);
    } catch {
      // La copia en memoria ya se eliminó.
    }
    this.hasDraftSignal.set(false);
  }

  private syncHasDraft(): void {
    this.hasDraftSignal.set(this.hasDraft());
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isReportDraft(value: unknown): value is ReportDraft {
  if (!isRecord(value) || typeof value['location'] !== 'string' || typeof value['source'] !== 'string'
    || typeof value['description'] !== 'string' || typeof value['includeDecibels'] !== 'boolean'
    || !(value['decibels'] === null || (typeof value['decibels'] === 'number' && Number.isFinite(value['decibels'])))) return false;
  const place = value['selectedPlace'];
  if (place === null) return true;
  if (!isRecord(place) || typeof place['placeId'] !== 'string' || !/^[NWR][0-9]{1,19}$/.test(place['placeId'])
    || typeof place['address'] !== 'string' || !isRecord(place['position'])) return false;
  const { lat, lng } = place['position'];
  return typeof lat === 'number' && Number.isFinite(lat) && lat >= -90 && lat <= 90
    && typeof lng === 'number' && Number.isFinite(lng) && lng >= -180 && lng <= 180;
}

function copyDraft(draft: ReportDraft): ReportDraft {
  return {
    ...draft,
    selectedPlace: draft.selectedPlace
      ? { ...draft.selectedPlace, position: { ...draft.selectedPlace.position } } : null,
  };
}
