import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { ReportDraftService } from '../../../core/services/report-draft.service';
import { District, Province, RegisterPayload, TerritoriesResponse } from '../../../core/interfaces/auth.interface';
import { textLength, trimmedPattern } from '../auth.validators';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HttpErrorResponse } from '@angular/common/http';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './register.component.html',
  styleUrl: './register.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly reportDraftService = inject(ReportDraftService);

  readonly returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') === '/reportar' || this.reportDraftService.hasDraft()
    ? '/reportar' : null;
  readonly hasPendingDraft = this.reportDraftService.hasDraftSignal.asReadonly();

  readonly isSubmitting = signal(false);
  readonly loadingTerritories = signal(true);
  readonly territoriesError = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  readonly territories = signal<TerritoriesResponse | null>(null);
  readonly selectedDepartamentoId = signal<number | null>(null);
  readonly selectedProvinciaId = signal<number | null>(null);

  readonly availableDepartamentos = computed(() => this.territories()?.departamentos ?? []);

  readonly availableProvincias = computed<Province[]>(() => {
    const deptId = this.selectedDepartamentoId();
    const allProvincias = this.territories()?.provincias ?? [];
    if (!deptId) return [];
    return allProvincias.filter((p) => p.id_departamento === deptId);
  });

  readonly availableDistritos = computed<District[]>(() => {
    const provId = this.selectedProvinciaId();
    const allDistritos = this.territories()?.distritos ?? [];
    if (!provId) return [];
    return allDistritos.filter((d) => d.id_provincia === provId);
  });

  readonly registerForm = new FormGroup({
    nombres: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, textLength(2, 80, true)],
    }),
    apellidos: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, textLength(2, 80, true)],
    }),
    telefono: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        trimmedPattern(/^[0-9+() -]{6,20}$/),
      ],
    }),
    direccion: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, textLength(5, 200, true)],
    }),
    id_departamento: new FormControl<number | null>(null, {
      validators: [Validators.required],
    }),
    id_provincia: new FormControl<number | null>(null, {
      validators: [Validators.required],
    }),
    id_distrito: new FormControl<number | null>(null, {
      validators: [Validators.required],
    }),
    nombre_usuario: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        trimmedPattern(/^[a-zA-Z0-9_.-]{3,40}$/),
      ],
    }),
    contrasena: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, textLength(8, 128), Validators.pattern(/\S/)],
    }),
  });

  ngOnInit(): void {
    this.loadTerritories();
  }

  loadTerritories(): void {
    this.loadingTerritories.set(true);
    this.territoriesError.set(null);

    this.authService.getTerritories().subscribe({
      next: (data) => {
        this.territories.set(data);
        this.loadingTerritories.set(false);
      },
      error: () => {
        this.loadingTerritories.set(false);
        this.territoriesError.set(
          'No se pudieron cargar los datos de ubicación territorial. Por favor, reintenta.'
        );
      },
    });
  }

  onDepartamentoChange(value: string | number | null): void {
    const numId = value ? Number(value) : null;
    this.selectedDepartamentoId.set(numId);
    this.selectedProvinciaId.set(null);

    this.registerForm.controls.id_departamento.setValue(numId);
    this.registerForm.controls.id_provincia.setValue(null);
    this.registerForm.controls.id_distrito.setValue(null);
  }

  onProvinciaChange(value: string | number | null): void {
    const numId = value ? Number(value) : null;
    this.selectedProvinciaId.set(numId);

    this.registerForm.controls.id_provincia.setValue(numId);
    this.registerForm.controls.id_distrito.setValue(null);
  }

  onDistritoChange(value: string | number | null): void {
    const numId = value ? Number(value) : null;
    this.registerForm.controls.id_distrito.setValue(numId);
  }

  onSubmit(): void {
    if (this.isSubmitting() || this.successMessage()) return;
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      this.errorMessage.set('Por favor, completa correctamente todos los campos obligatorios.');
      return;
    }

    const raw = this.registerForm.getRawValue();
    if (!raw.id_departamento || !raw.id_provincia || !raw.id_distrito) {
      this.errorMessage.set('Debes seleccionar un distrito válido.');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const payload: RegisterPayload = {
      nombres: raw.nombres.trim(),
      apellidos: raw.apellidos.trim(),
      telefono: raw.telefono.trim(),
      direccion: raw.direccion.trim(),
      id_departamento: raw.id_departamento,
      id_provincia: raw.id_provincia,
      id_distrito: Number(raw.id_distrito),
      nombre_usuario: raw.nombre_usuario.trim(),
      contrasena: raw.contrasena,
    };

    this.authService.register(payload).subscribe({
      next: (user) => {
        this.isSubmitting.set(false);
        this.successMessage.set(
          `¡Registro exitoso! Cuenta creada para ${user.nombres} ${user.apellidos}. Redirigiendo...`
        );
        const target = this.returnUrl || '/';
        setTimeout(() => {
          this.router.navigateByUrl(target);
        }, 1000);
      },
      error: (err: HttpErrorResponse | Error) => {
        this.isSubmitting.set(false);
        if (err instanceof HttpErrorResponse) {
          const detail = err.error?.detail;
          if (typeof detail === 'string') {
            this.errorMessage.set(detail);
          } else if (Array.isArray(detail)) {
            const firstMsg = detail[0]?.msg ?? 'Error de validación en los datos ingresados.';
            this.errorMessage.set(firstMsg);
          } else if (err.status === 409) {
            this.errorMessage.set('El nombre de usuario ya está registrado.');
          } else if (err.status === 0) {
            this.errorMessage.set('No se pudo conectar con el servidor.');
          } else {
            this.errorMessage.set('Ocurrió un error al registrar la cuenta. Inténtalo de nuevo.');
          }
        } else {
          this.errorMessage.set('Error en la comunicación con el servidor.');
        }
      },
    });
  }
}
