import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { NoiseApiService } from '../../core/services/noise-api.service';
import { Hotspot } from '../../core/interfaces/noise.interface';
import { MapPoint } from '../../core/interfaces/maps.interface';
import { MapComponent } from '../../shared/components/map/map.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { WeatherBadgeComponent } from '../../shared/components/weather-badge/weather-badge.component';

@Component({
  selector: 'app-hotspots',
  standalone: true,
  imports: [RouterLink, IconComponent, MapComponent, WeatherBadgeComponent],
  templateUrl: './hotspots.component.html',
  styleUrl: './hotspots.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HotspotsComponent {
  private readonly api = inject(NoiseApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly hotspots = signal<Hotspot[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly selectedId = signal<string | null>(null);
  readonly selectedHotspot = computed(() => this.hotspots().find((point) => point.id === this.selectedId()));
  readonly mapPoints = computed<readonly MapPoint[]>(() => this.hotspots().map((point) => ({
    id: point.id,
    title: point.name,
    label: `${point.decibels} dB · ejemplo`,
    position: point.latitude !== null && point.longitude !== null ? { lat: point.latitude, lng: point.longitude } : null,
  })));

  constructor() {
    this.loadHotspots();
  }

  loadHotspots(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.api.getHotspots()
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (points) => this.hotspots.set(points),
        error: () => this.error.set('No pudimos cargar los puntos críticos. Comprueba la conexión con el servicio e inténtalo de nuevo.'),
      });
  }

  selectPoint(point: Hotspot): void {
    this.selectedId.set(this.selectedId() === point.id ? null : point.id);
  }

  intensity(decibels: number): number {
    return Math.min(100, Math.max(0, ((decibels - 30) / 70) * 100));
  }

  difference(point: Hotspot): string {
    const difference = point.decibels - point.reference_limit;
    return `${difference > 0 ? '+' : ''}${difference} dB sobre referencia`;
  }
}
