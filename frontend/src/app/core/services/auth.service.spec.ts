import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from './auth.service';
import {
  LoginPayload,
  RegisterPayload,
  TerritoriesResponse,
  UserProfile,
} from '../interfaces/auth.interface';

const mockUser: UserProfile = {
  id_usuario: 1,
  nombre_usuario: 'jperez',
  nombres: 'Juan',
  apellidos: 'Perez',
  telefono: '964123456',
  direccion: 'Calle Real 123',
  id_distrito: 120101,
  nombre_distrito: 'Huancayo',
  id_provincia: 1201,
  nombre_provincia: 'Huancayo',
  id_departamento: 12,
  nombre_departamento: 'Junín',
  rol: 'ciudadano',
  fecha_registro: '2026-11-19T00:00:00Z',
};

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
  ],
};

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('inicia con usuario nulo si no hay sesión guardada', () => {
    expect(service.currentUser()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('obtiene los territorios mediante GET /api/territories', () => {
    let result: TerritoriesResponse | undefined;
    service.getTerritories().subscribe((data) => (result = data));

    const req = httpMock.expectOne('/api/territories');
    expect(req.request.method).toBe('GET');
    req.flush(mockTerritories);

    expect(result).toEqual(mockTerritories);
    expect(result?.departamentos.length).toBe(2);
  });

  it('realiza login exitoso mediante POST /api/auth/login y actualiza signals', () => {
    const payload: LoginPayload = { nombre_usuario: 'jperez', contrasena: 'secreto123' };
    let loggedUser: UserProfile | undefined;

    service.login(payload).subscribe((res) => (loggedUser = res));

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    expect(req.request.withCredentials).toBe(true);
    req.flush(mockUser);

    expect(loggedUser).toEqual(mockUser);
    expect(service.currentUser()).toEqual(mockUser);
    expect(service.isAuthenticated()).toBe(true);
    expect(service.currentUserName()).toBe('jperez');
  });

  it('registra un usuario mediante POST /api/auth/register y actualiza sesión', () => {
    const payload: RegisterPayload = {
      nombres: 'Juan',
      apellidos: 'Perez',
      telefono: '964123456',
      direccion: 'Calle Real 123',
      id_departamento: 12,
      id_provincia: 1201,
      id_distrito: 120101,
      nombre_usuario: 'jperez',
      contrasena: 'secreto123',
    };
    let registeredUser: UserProfile | undefined;

    service.register(payload).subscribe((res) => (registeredUser = res));

    const req = httpMock.expectOne('/api/auth/register');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    expect(req.request.withCredentials).toBe(true);
    req.flush(mockUser, { status: 201, statusText: 'Created' });

    expect(registeredUser).toEqual(mockUser);
    expect(service.isAuthenticated()).toBe(true);
  });

  it('cierra sesión con logout y limpia el estado', () => {
    service.setUser(mockUser);
    expect(service.isAuthenticated()).toBe(true);

    service.logout().subscribe();
    const req = httpMock.expectOne('/api/auth/logout');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(service.currentUser()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('mantiene el acceso y el cierre de sesión cuando el navegador bloquea sessionStorage', () => {
    const storage = vi.spyOn(window, 'sessionStorage', 'get').mockImplementation(() => {
      throw new DOMException('Almacenamiento bloqueado', 'SecurityError');
    });
    try {
      expect(() => service.setUser(mockUser)).not.toThrow();
      expect(service.currentUser()).toEqual(mockUser);
      expect(() => service.logout().subscribe()).not.toThrow();
      httpMock.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
      expect(service.currentUser()).toBeNull();
      expect(() => TestBed.runInInjectionContext(() => new AuthService())).not.toThrow();
    } finally {
      storage.mockRestore();
    }
  });

  it('no considera autenticado un perfil falsificado en sessionStorage', () => {
    sessionStorage.setItem('decibelcity_user_profile', JSON.stringify(mockUser));
    expect(service.isAuthenticated()).toBe(false);
    service.checkSession().subscribe((profile) => expect(profile).toBeNull());
    const req = httpMock.expectOne('/api/auth/session');
    expect(req.request.withCredentials).toBe(true);
    req.flush({ detail: 'Inicia sesión para continuar.' }, { status: 401, statusText: 'Unauthorized' });
    expect(service.isAuthenticated()).toBe(false);
  });

  it('reconoce la sesión únicamente tras confirmación de FastAPI', () => {
    service.checkSession().subscribe();
    httpMock.expectOne('/api/auth/session').flush(mockUser);
    expect(service.currentUser()).toEqual(mockUser);
  });

  it('retira el perfil en memoria si FastAPI rechaza una sesión desactivada', () => {
    service.setUser(mockUser);
    service.checkSession().subscribe((profile) => expect(profile).toBeNull());
    httpMock.expectOne('/api/auth/session').flush({ detail: 'Cuenta desactivada.' }, { status: 403, statusText: 'Forbidden' });
    expect(service.currentUser()).toBeNull();
  });
});
