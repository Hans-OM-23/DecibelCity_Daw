import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, timeout } from 'rxjs';
import { WeatherData } from '../interfaces/weather.interface';
import { API_BASE_URL } from './noise-api.service';

@Injectable({ providedIn: 'root' })
export class WeatherService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getWeather(): Observable<WeatherData> {
    return this.http.get<WeatherData>(`${this.baseUrl}/weather`).pipe(timeout(10000));
  }
}
