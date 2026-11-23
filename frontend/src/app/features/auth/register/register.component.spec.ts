import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RegisterComponent } from './register.component';
import { AuthService } from '../../../core/services/auth.service';
import { ReportDraftService } from '../../../core/services/report-draft.service';
import { TerritoriesResponse, UserProfile } from '../../../core/interfaces/auth.interface';

const mockTerritories: TerritoriesResponse = {
  departamentos: [
    { id_departamento: 12, ubigeo: '120000', nombre_departamento: 'Junín' },
    { id_departamento: 15, ubigeo: '150000', nombre_departamento: 'Lima' },
  ],
  provincias: [
    { id_provincia: 1201, id_departamento: 12, ubigeo: '120100', nombre_provincia: 'Huancayo' },
    { id_provincia: 1202, id_departamento: 12, ubigeo: '120200', nombre_provincia: 'Concepción' },
    { id_provincia: 1501, id_departamento: 15, ubigeo: '150100', nombre_provincia: 'Lima' },
  ],
  distritos: [
    { id_distrito: 120101, id_provincia: 1201, ubigeo: '120101', nombre_distrito: 'Huancayo' },
    { id_distrito: 120114, id_provincia: 1201, ubigeo: '120114', nombre_distrito: 'El Tambo' },
    { id_distrito: 120201, id_provincia: 1202, ubigeo: '120201', nombre_distrito: 'Concepción' },
    { id_distrito: 150101, id_provincia: 1501, ubigeo: '150101', nombre_distrito: 'Lima Cercado' },
  ],
};

const mockCreatedUser: UserProfile = {
  id_usuario: 5,
  nombre_usuario: 'mariag',
  nombres: 'Maria',
  apellidos: 'Garcia',
  telefono: '987654321',
  direccion: 'Av. Giraldez 300',
  id_distrito: 120114,
  nombre_distrito: 'El Tambo',
  id_provincia: 1201,
  nombre_provincia: 'Huancayo',
  id_departamento: 12,
  nombre_departamento: 'Junín',
  rol: 'ciudadano',
  fecha_registro: '2026-11-19T00:00:00Z',
};

describe('RegisterComponent: cascada territorial y registro', () => {
  let component: RegisterComponent;
  let fixture: ComponentFixture<RegisterComponent>;
  let authServiceMock: {
    getTerritories: ReturnType<typeof vi.fn>;
    register: ReturnType<typeof vi.fn>;
  };
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    authServiceMock = {
      getTerritories: vi.fn(() => of(mockTerritories)),
      register: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        ReportDraftService,
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: {
                get: (key: string) => (key === 'returnUrl' ? '/reportar' : null),
              },
            },
          },
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl');

    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('muestra aviso accesible cuando returnUrl es /reportar', () => {
    expect(component.hasPendingDraft()).toBe(false);
    TestBed.inject(ReportDraftService).saveDraft({ location: '', selectedPlace: null, source: '', includeDecibels: false,
      decibels: 70, description: 'Borrador pendiente' });
    fixture.detectChanges();
    expect(component.hasPendingDraft()).toBe(true);
    const alertInfo = fixture.nativeElement.querySelector('.alert-info');
    expect(alertInfo).not.toBeNull();
    expect(alertInfo.textContent).toContain('reporte de ruido');
  });

  it('carga territorios al iniciar y puebla departamentos', () => {
    expect(authServiceMock.getTerritories).toHaveBeenCalled();
    expect(component.loadingTerritories()).toBe(false);
    expect(component.availableDepartamentos().length).toBe(2);
    expect(component.availableProvincias().length).toBe(0);
    expect(component.availableDistritos().length).toBe(0);
  });

  it('filtra provincias en cascada al seleccionar departamento', () => {
    component.onDepartamentoChange(12); // Junín
    fixture.detectChanges();

    const provs = component.availableProvincias();
    expect(provs.length).toBe(2);
    expect(provs.map((p) => p.nombre_provincia)).toEqual(['Huancayo', 'Concepción']);
    expect(component.availableDistritos().length).toBe(0);
  });

  it('mantiene selectores etiquetados y limpia sus valores al cambiar el departamento en el DOM', () => {
    const root: HTMLElement = fixture.nativeElement;
    const department = root.querySelector<HTMLSelectElement>('#reg-departamento')!;
    const province = root.querySelector<HTMLSelectElement>('#reg-provincia')!;
    const district = root.querySelector<HTMLSelectElement>('#reg-distrito')!;
    for (const select of [department, province, district]) {
      expect(root.querySelector(`label[for="${select.id}"]`)).not.toBeNull();
      expect(select.required).toBe(true);
      expect(select.closest('.select-wrapper')).not.toBeNull();
    }
    expect(province.disabled).toBe(true);
    expect(district.disabled).toBe(true);
    department.value = '12';
    department.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(province.disabled).toBe(false);
    province.value = '1201';
    province.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(district.disabled).toBe(false);
    district.value = '120114';
    district.dispatchEvent(new Event('change'));
    department.value = '15';
    department.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(component.registerForm.controls.id_provincia.value).toBeNull();
    expect(component.registerForm.controls.id_distrito.value).toBeNull();
    expect(district.disabled).toBe(true);
  });

  it('filtra distritos en cascada al seleccionar provincia', () => {
    component.onDepartamentoChange(12); // Junín
    component.onProvinciaChange(1201); // Huancayo
    fixture.detectChanges();

    const dists = component.availableDistritos();
    expect(dists.length).toBe(2);
    expect(dists.map((d) => d.nombre_distrito)).toEqual(['Huancayo', 'El Tambo']);
  });

  it('limpia provincia y distrito seleccionados si se cambia de departamento', () => {
    component.onDepartamentoChange(12);
    component.onProvinciaChange(1201);
    component.onDistritoChange(120101);

    expect(component.selectedProvinciaId()).toBe(1201);
    expect(component.registerForm.controls.id_distrito.value).toBe(120101);

    // Cambia de departamento a Lima (15)
    component.onDepartamentoChange(15);
    fixture.detectChanges();

    expect(component.selectedProvinciaId()).toBeNull();
    expect(component.registerForm.controls.id_provincia.value).toBeNull();
    expect(component.registerForm.controls.id_distrito.value).toBeNull();
    expect(component.availableDistritos().length).toBe(0);

    const limaProvincias = component.availableProvincias();
    expect(limaProvincias.length).toBe(1);
    expect(limaProvincias[0].nombre_provincia).toBe('Lima');
  });

  it('valida que los 9 campos obligatorios cumplan con las restricciones', () => {
    expect(component.registerForm.valid).toBe(false);

    component.registerForm.setValue({
      nombres: 'Maria',
      apellidos: 'Garcia',
      telefono: '987654321',
      direccion: 'Av. Giraldez 300',
      id_departamento: 12,
      id_provincia: 1201,
      id_distrito: 120114,
      nombre_usuario: 'mariag',
      contrasena: 'claveSecreta123',
    });

    expect(component.registerForm.valid).toBe(true);
  });

  it('rechaza teléfono inválido con letras o caracteres no permitidos', () => {
    component.registerForm.controls.telefono.setValue('123');
    expect(component.registerForm.controls.telefono.invalid).toBe(true);

    component.registerForm.controls.telefono.setValue('abc999');
    expect(component.registerForm.controls.telefono.invalid).toBe(true);

    component.registerForm.controls.telefono.setValue('987654321');
    expect(component.registerForm.controls.telefono.valid).toBe(true);
  });

  it.each([
    ['nombres', 'a'.repeat(81)],
    ['apellidos', 'a'.repeat(81)],
    ['nombres', '  A  '],
    ['direccion', 'Jr 1'],
    ['direccion', '     '],
    ['nombre_usuario', 'u'.repeat(41)],
    ['contrasena', '1234567'],
    ['contrasena', ' '.repeat(8)],
    ['contrasena', 'a'.repeat(129)],
    ['telefono', '123\t456'],
  ] as const)('rechaza %s fuera del contrato HTTP', (field, value) => {
    component.registerForm.controls[field].setValue(value);
    expect(component.registerForm.controls[field].invalid).toBe(true);
  });

  it('acepta el teléfono con paréntesis y la contraseña de 128 caracteres admitidos por FastAPI', () => {
    component.registerForm.controls.telefono.setValue('+51 (64) 123-456');
    component.registerForm.controls.contrasena.setValue('a'.repeat(128));
    expect(component.registerForm.controls.telefono.valid).toBe(true);
    expect(component.registerForm.controls.contrasena.valid).toBe(true);
  });

  it('envía el payload con id_distrito numérico y redirige a returnUrl tras registro exitoso', async () => {
    vi.useFakeTimers();
    authServiceMock.register.mockReturnValue(of(mockCreatedUser));

    component.registerForm.setValue({
      nombres: 'Maria',
      apellidos: 'Garcia',
      telefono: '987654321',
      direccion: 'Av. Giraldez 300',
      id_departamento: 12,
      id_provincia: 1201,
      id_distrito: 120114,
      nombre_usuario: 'mariag',
      contrasena: 'claveSecreta123',
    });

    component.onSubmit();
    fixture.detectChanges();

    expect(authServiceMock.register).toHaveBeenCalledWith({
      nombres: 'Maria',
      apellidos: 'Garcia',
      telefono: '987654321',
      direccion: 'Av. Giraldez 300',
      id_departamento: 12,
      id_provincia: 1201,
      id_distrito: 120114,
      nombre_usuario: 'mariag',
      contrasena: 'claveSecreta123',
    });
    expect(component.isSubmitting()).toBe(false);
    expect(component.successMessage()).toContain('Maria Garcia');

    await vi.advanceTimersByTimeAsync(1100);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/reportar');
    vi.useRealTimers();
  });

  it('muestra mensaje de error cuando el backend devuelve un conflicto (409)', () => {
    const errorResponse = new HttpErrorResponse({
      status: 409,
      statusText: 'Conflict',
      error: { detail: 'El nombre de usuario ya está registrado.' },
    });
    authServiceMock.register.mockReturnValue(throwError(() => errorResponse));

    component.registerForm.setValue({
      nombres: 'Maria',
      apellidos: 'Garcia',
      telefono: '987654321',
      direccion: 'Av. Giraldez 300',
      id_departamento: 12,
      id_provincia: 1201,
      id_distrito: 120114,
      nombre_usuario: 'mariag',
      contrasena: 'claveSecreta123',
    });

    component.onSubmit();
    fixture.detectChanges();

    expect(component.isSubmitting()).toBe(false);
    expect(component.errorMessage()).toBe('El nombre de usuario ya está registrado.');
  });
});
