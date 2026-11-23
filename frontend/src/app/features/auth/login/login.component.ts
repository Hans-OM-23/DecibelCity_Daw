import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { ReportDraftService } from '../../../core/services/report-draft.service';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HttpErrorResponse } from '@angular/common/http';
import { textLength } from '../auth.validators';
import { LoginPayload } from '../../../core/interfaces/auth.interface';
import { catchError, of } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly reportDraftService = inject(ReportDraftService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly turnstileContainer = viewChild<ElementRef<HTMLDivElement>>('turnstileContainer');

  readonly returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') === '/reportar' || this.reportDraftService.hasDraft()
    ? '/reportar' : null;
  readonly hasPendingDraft = this.reportDraftService.hasDraftSignal.asReadonly();

  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  readonly turnstileEnabled = signal(false);
  readonly turnstileSiteKey = signal('');
  readonly turnstileToken = signal<string | null>(null);
  readonly turnstileError = signal<string | null>(null);
  readonly turnstileLoading = signal(false);
  private turnstileWidgetId: string | null = null;

  constructor() {
    this.loadAuthConfig();
    this.destroyRef.onDestroy(() => {
      this.cleanupTurnstile();
    });
  }

  private loadAuthConfig(): void {
    const config$ = this.authService.getAuthConfig ? this.authService.getAuthConfig() : of({ turnstile_enabled: false, turnstile_site_key: '' });
    config$
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        catchError(() => of({ turnstile_enabled: false, turnstile_site_key: '' })),
      )
      .subscribe((config) => {
        if (config?.turnstile_enabled && config.turnstile_site_key?.trim()) {
          this.turnstileEnabled.set(true);
          this.turnstileSiteKey.set(config.turnstile_site_key.trim());
          setTimeout(() => this.initTurnstile(), 0);
        } else {
          this.turnstileEnabled.set(false);
        }
      });
  }

  private async initTurnstile(): Promise<void> {
    if (!this.turnstileEnabled() || !this.turnstileSiteKey()) return;
    this.turnstileLoading.set(true);
    try {
      await this.loadTurnstileScript();
      const container = this.turnstileContainer()?.nativeElement;
      if (!container) return;
      const w = window as unknown as {
        turnstile?: {
          render: (container: HTMLElement, options: Record<string, unknown>) => string;
        };
      };
      if (w.turnstile) {
        container.innerHTML = '';
        this.turnstileWidgetId = w.turnstile.render(container, {
          sitekey: this.turnstileSiteKey(),
          theme: 'dark',
          language: 'es',
          callback: (token: string) => {
            this.turnstileToken.set(token);
            this.turnstileError.set(null);
          },
          'expired-callback': () => {
            this.turnstileToken.set(null);
          },
          'error-callback': () => {
            this.turnstileToken.set(null);
            this.turnstileError.set('Error en la verificación de seguridad. Inténtalo de nuevo.');
          },
        });
      }
    } catch {
      this.turnstileError.set('No se pudo cargar la verificación de seguridad.');
    } finally {
      this.turnstileLoading.set(false);
    }
  }

  private loadTurnstileScript(): Promise<void> {
    if (typeof window === 'undefined') return Promise.resolve();
    const w = window as unknown as { turnstile?: unknown };
    if (w.turnstile) return Promise.resolve();

    const scriptId = 'cf-turnstile-script';
    const existing = document.getElementById(scriptId) as HTMLScriptElement | null;
    if (existing) {
      return new Promise((resolve) => {
        if (w.turnstile) {
          resolve();
        } else {
          existing.addEventListener('load', () => resolve(), { once: true });
        }
      });
    }

    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.id = scriptId;
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
      script.async = true;
      script.defer = true;
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('Turnstile script failed to load'));
      document.head.appendChild(script);
    });
  }

  private resetTurnstile(): void {
    this.turnstileToken.set(null);
    const w = window as unknown as {
      turnstile?: {
        reset: (widgetId?: string) => void;
      };
    };
    if (w.turnstile && this.turnstileWidgetId) {
      try {
        w.turnstile.reset(this.turnstileWidgetId);
      } catch {
        // ignore
      }
    }
  }

  private cleanupTurnstile(): void {
    const w = window as unknown as {
      turnstile?: {
        remove: (widgetId: string) => void;
      };
    };
    if (w.turnstile && this.turnstileWidgetId) {
      try {
        w.turnstile.remove(this.turnstileWidgetId);
      } catch {
        // ignore
      }
    }
  }

  readonly loginForm = new FormGroup({
    nombre_usuario: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, textLength(1, 40, true)],
    }),
    contrasena: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, textLength(1, 128)],
    }),
  });

  get nombreUsuarioControl() {
    return this.loginForm.controls.nombre_usuario;
  }

  get contrasenaControl() {
    return this.loginForm.controls.contrasena;
  }

  onSubmit(): void {
    if (this.isSubmitting() || this.successMessage()) return;
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.errorMessage.set('Por favor, completa los campos obligatorios correctamente.');
      return;
    }

    if (this.turnstileEnabled() && !this.turnstileToken()) {
      this.errorMessage.set('Por favor, completa la verificación de seguridad antes de continuar.');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const { nombre_usuario, contrasena } = this.loginForm.getRawValue();
    const payload: LoginPayload = {
      nombre_usuario: nombre_usuario.trim(),
      contrasena,
      ...(this.turnstileEnabled() && this.turnstileToken() ? { turnstile_token: this.turnstileToken()! } : {}),
    };

    this.authService.login(payload).subscribe({
      next: (user) => {
        this.isSubmitting.set(false);
        this.successMessage.set(`¡Bienvenido/a, ${user.nombres || user.nombre_usuario}! Redirigiendo...`);
        const target = this.returnUrl || '/';
        setTimeout(() => {
          this.router.navigateByUrl(target);
        }, 600);
      },
      error: (err: HttpErrorResponse | Error) => {
        this.isSubmitting.set(false);
        if (this.turnstileEnabled()) {
          this.resetTurnstile();
        }
        if (err instanceof HttpErrorResponse) {
          const detail = typeof err.error?.detail === 'string' ? err.error.detail : null;
          if (err.status === 401 || err.status === 403) {
            this.errorMessage.set(detail ?? 'Credenciales incorrectas. Verifica tu usuario y contraseña.');
          } else if (err.status === 0) {
            this.errorMessage.set('No se pudo conectar con el servidor. Verifica tu conexión.');
          } else {
            this.errorMessage.set(detail ?? 'Los datos de acceso no son válidos. Revisa el usuario y la contraseña.');
          }
        } else {
          this.errorMessage.set('Error en la comunicación. Inténtalo de nuevo.');
        }
      },
    });
  }
}
