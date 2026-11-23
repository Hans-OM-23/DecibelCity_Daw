import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { EMPTY, finalize, switchMap } from 'rxjs';
import { NoiseApiService } from '../../core/services/noise-api.service';
import { AuthService } from '../../core/services/auth.service';
import { ReportDraftService } from '../../core/services/report-draft.service';
import { NoiseReport, NoiseReportCreate, NoiseSource } from '../../core/interfaces/noise.interface';
import { SelectedPlace } from '../../core/interfaces/maps.interface';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { LocationPickerComponent } from '../../shared/components/location-picker/location-picker.component';

type ReportField = 'location' | 'source' | 'description' | 'decibels';

@Component({
  selector: 'app-report',
  standalone: true,
  imports: [DatePipe, RouterLink, IconComponent, LocationPickerComponent],
  templateUrl: './report.component.html',
  styleUrls: ['./report-controls.css', './report.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportComponent {
  private readonly api = inject(NoiseApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly reportDraftService = inject(ReportDraftService);

  readonly location = signal('');
  readonly selectedPlace = signal<SelectedPlace | null>(null);
  readonly source = signal('');
  readonly description = signal('');
  readonly decibels = signal<number | null>(70);
  readonly includeDecibels = signal(false);
  readonly sources = signal<NoiseSource[]>([]);
  readonly sourcesLoading = signal(false);
  readonly sourcesError = signal('');
  readonly submitting = signal(false);
  readonly attempted = signal(false);
  readonly touched = signal<Set<ReportField>>(new Set());
  readonly submitError = signal('');
  readonly receipt = signal<NoiseReport | null>(null);
  readonly levelPercentage = computed(() => (((this.decibels() ?? 70) - 30) / 90) * 100);
  readonly errors = computed<Record<ReportField, string>>(() => ({
    location: !this.selectedPlace()
      ? 'Selecciona una ubicación de las sugerencias para continuar.' : '',
    source: !this.sources().some((item) => item.value === this.source())
      ? 'Selecciona la fuente principal del ruido.' : '',
    description: this.description().trim().length < 10 || this.description().trim().length > 500
      ? 'Describe el problema con entre 10 y 500 caracteres.' : '',
    decibels: this.includeDecibels() && (this.decibels() === null || !Number.isFinite(this.decibels()) || this.decibels()! < 30 || this.decibels()! > 120)
      ? 'Introduce un nivel entre 30 y 120 dBA, o desactiva esta opción.' : '',
  }));

  constructor() {
    this.restoreDraftIfAny();
    this.loadSources();
    this.destroyRef.onDestroy(() => {
      if (!this.receipt() && (this.selectedPlace() || this.location() || this.source() || this.description()
        || this.includeDecibels() || this.decibels() !== 70)) {
        this.saveCurrentDraft();
      }
    });
  }

  private restoreDraftIfAny(): void {
    const draft = this.reportDraftService.getDraft();
    if (!draft) return;

    this.location.set(draft.location ?? '');
    this.selectedPlace.set(draft.selectedPlace ?? null);
    this.source.set(draft.source ?? '');
    this.includeDecibels.set(Boolean(draft.includeDecibels));
    this.decibels.set(draft.decibels);
    this.description.set(draft.description ?? '');

    if (draft.selectedPlace) {
      this.touch('location');
    }
    if (draft.source) {
      this.touch('source');
    }
  }

  loadSources(): void {
    if (this.sourcesLoading()) return;
    this.sourcesLoading.set(true);
    this.sourcesError.set('');
    this.api.getNoiseSources()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.sourcesLoading.set(false)))
      .subscribe({
        next: (sources) => {
          this.sources.set(sources);
          if (!sources.length) this.sourcesError.set('No hay fuentes de ruido disponibles. Intenta cargar las opciones de nuevo.');
        },
        error: () => this.sourcesError.set('No pudimos cargar las fuentes de ruido. Reintenta para poder enviar tu reporte.'),
      });
  }

  touch(field: ReportField): void {
    this.touched.update((fields) => new Set(fields).add(field));
  }

  fieldError(field: ReportField): string {
    return this.attempted() || this.touched().has(field) ? this.errors()[field] : '';
  }

  selectLocation(place: SelectedPlace | null): void {
    if (this.submitting()) return;
    const hadSelection = this.selectedPlace() !== null;
    this.selectedPlace.set(place);
    this.location.set(place?.address ?? '');
    if (place || hadSelection) this.touch('location');
    this.submitError.set('');
  }

  updateText(field: 'source' | 'description', event: Event): void {
    this[field].set((event.target as HTMLInputElement).value);
    this.submitError.set('');
  }

  updateDecibels(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.decibels.set(value === '' ? null : Number(value));
    this.submitError.set('');
  }

  toggleDecibels(event: Event): void {
    this.includeDecibels.set((event.target as HTMLInputElement).checked);
    this.submitError.set('');
  }

  submit(event: Event): void {
    event.preventDefault();
    if (this.submitting()) return;
    // Guardar también entradas incompletas y el valor del nivel aunque esté desactivado.
    this.saveCurrentDraft();
    this.submitting.set(true);
    this.submitError.set('');
    this.authService.checkSession()
      .pipe(
        switchMap((user) => {
          if (!user) {
            this.returnToLogin();
            return EMPTY;
          }
          this.attempted.set(true);
          if (this.sourcesLoading() || this.sourcesError() || Object.values(this.errors()).some(Boolean)) return EMPTY;
          const place = this.selectedPlace();
          if (!place) return EMPTY;
          const payload: NoiseReportCreate = {
            location: this.location().trim(),
            place_id: place.placeId,
            source: this.source() as NoiseReportCreate['source'],
            description: this.description().trim(),
            decibels: this.includeDecibels() ? this.decibels() : null,
          };
          return this.api.createReport(payload);
        }),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: (report) => {
          this.receipt.set(report);
          this.reportDraftService.clearDraft();
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
            this.authService.clearLocalSession();
            this.returnToLogin();
          } else {
            this.submitError.set(this.reportErrorMessage(error));
          }
        },
      });
  }

  private returnToLogin(): void {
    this.router.navigate(['/login'], { queryParams: { returnUrl: '/reportar' } });
  }

  private saveCurrentDraft(): void {
    this.reportDraftService.saveDraft({
      location: this.location(), selectedPlace: this.selectedPlace(), source: this.source(),
      includeDecibels: this.includeDecibels(), decibels: this.decibels(), description: this.description(),
    });
  }

  private reportErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const detail: unknown = error.error?.detail;
      if ([400, 422, 502, 503].includes(error.status) && typeof detail === 'string') {
        return `${detail} Tus datos siguen en este formulario.`;
      }
      if (error.status === 422) {
        return 'No se pudo validar el reporte. Revisa la ubicación seleccionada y los campos del formulario antes de volver a enviar.';
      }
    }
    return 'No recibimos confirmación del servicio. Tus datos siguen en este formulario para que puedas volver a intentar el envío.';
  }

  newReport(): void {
    this.receipt.set(null);
    this.location.set('');
    this.selectedPlace.set(null);
    this.source.set('');
    this.description.set('');
    this.decibels.set(70);
    this.includeDecibels.set(false);
    this.attempted.set(false);
    this.touched.set(new Set());
    this.submitError.set('');
    this.reportDraftService.clearDraft();
  }
}
