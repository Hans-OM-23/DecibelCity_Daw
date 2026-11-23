import { inject, Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, of, tap, throwError, timeout } from 'rxjs';
import {
  AuthConfig,
  LoginPayload,
  RegisterPayload,
  TerritoriesResponse,
  UserProfile,
} from '../interfaces/auth.interface';
import { API_BASE_URL } from './noise-api.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL, { optional: true }) ?? '/api';

  private readonly _currentUser = signal<UserProfile | null>(null);

  readonly currentUser = this._currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this._currentUser() !== null);
  readonly currentUserName = computed(() => this._currentUser()?.nombre_usuario ?? '');

  getTerritories(): Observable<TerritoriesResponse> {
    return this.http.get<TerritoriesResponse>(`${this.baseUrl}/territories`).pipe(timeout(12000));
  }

  getAuthConfig(): Observable<AuthConfig> {
    return this.http.get<AuthConfig>(`${this.baseUrl}/auth/config`).pipe(timeout(12000));
  }

  checkSession(): Observable<UserProfile | null> {
    return this.http.get<UserProfile>(`${this.baseUrl}/auth/session`, { withCredentials: true }).pipe(
      timeout(12000),
      tap((user) => this.setUser(user)),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
          this.clearLocalSession();
          return of(null);
        }
        return throwError(() => error);
      }),
    );
  }

  login(payload: LoginPayload): Observable<UserProfile> {
    return this.http
      .post<UserProfile>(`${this.baseUrl}/auth/login`, payload, { withCredentials: true })
      .pipe(
        timeout(12000),
        tap((user) => this.setUser(user))
      );
  }

  register(payload: RegisterPayload): Observable<UserProfile> {
    return this.http
      .post<UserProfile>(`${this.baseUrl}/auth/register`, payload, { withCredentials: true })
      .pipe(
        timeout(15000),
        tap((user) => this.setUser(user))
      );
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/auth/logout`, {}, { withCredentials: true }).pipe(
      timeout(12000),
      tap(() => this.clearLocalSession()),
    );
  }

  clearLocalSession(): void {
    this._currentUser.set(null);
  }

  setUser(user: UserProfile): void {
    this._currentUser.set(user);
  }
}
