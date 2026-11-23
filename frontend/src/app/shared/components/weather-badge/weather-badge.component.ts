import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WeatherData } from '../../../core/interfaces/weather.interface';
import { WeatherService } from '../../../core/services/weather.service';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'app-weather-badge',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './weather-badge.component.html',
  styleUrl: './weather-badge.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WeatherBadgeComponent {
  private readonly weatherService = inject(WeatherService);
  private readonly destroyRef = inject(DestroyRef);

  readonly weather = signal<WeatherData | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);

  constructor() {
    this.loadWeather();
  }

  loadWeather(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.failed.set(false);

    this.weatherService.getWeather()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.weather.set(data);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  iconFor(code: number, iconName?: string): string {
    const icon = iconName?.toLowerCase() ?? '';
    if (icon.includes('rain') || (code >= 51 && code <= 67) || (code >= 80 && code <= 82)) {
      return 'cloud-rain';
    }
    if (icon.includes('sun') || icon.includes('clear') || code === 0 || code === 1) {
      return 'sun';
    }
    if (icon.includes('wind')) {
      return 'wind';
    }
    return 'cloud';
  }
}
