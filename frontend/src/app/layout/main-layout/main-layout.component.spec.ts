import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, tap, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/services/auth.service';
import { MainLayoutComponent } from './main-layout.component';

describe('MainLayoutComponent: sesión confirmada y cierre en servidor', () => {
  const profile = { nombre_usuario: 'ana', nombres: 'Ana', apellidos: 'Quispe', rol: 'ciudadano',
    nombre_distrito: 'Huancayo', nombre_provincia: 'Huancayo' };
  let currentUser: ReturnType<typeof signal<typeof profile | null>>;
  let response: Subject<void>;
  let auth: { currentUser: typeof currentUser; isAuthenticated: ReturnType<typeof computed<boolean>>;
    checkSession: ReturnType<typeof vi.fn>; clearLocalSession: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    currentUser = signal<typeof profile | null>(null);
    response = new Subject<void>();
    auth = { currentUser, isAuthenticated: computed(() => currentUser() !== null),
      checkSession: vi.fn(() => of(profile).pipe(tap((value) => currentUser.set(value)))),
      clearLocalSession: vi.fn(() => currentUser.set(null)),
      logout: vi.fn(() => response.pipe(tap(() => currentUser.set(null)))),
    };
    await TestBed.configureTestingModule({ imports: [MainLayoutComponent], providers: [provideRouter([]),
      { provide: AuthService, useValue: auth }] }).compileComponents();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  it('consulta la sesión al montar la navegación y muestra el perfil confirmado', () => {
    const fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
    expect(auth.checkSession).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.isAuthenticated()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('ana');
  });

  it('espera la revocación del servidor y evita solicitudes de logout duplicadas', () => {
    const fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.profileOpen.set(true);
    component.logout();
    component.logout();
    expect(auth.logout).toHaveBeenCalledTimes(1);
    expect(component.logoutPending()).toBe(true);
    expect(component.isAuthenticated()).toBe(true);
    response.next();
    response.complete();
    expect(component.isAuthenticated()).toBe(false);
    expect(component.profileOpen()).toBe(false);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/']);
  });

  it('mantiene la sesión y muestra un error si el servidor no confirma el cierre', () => {
    auth.logout.mockReturnValue(throwError(() => new Error('Sin conexión')));
    const fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
    fixture.componentInstance.profileOpen.set(true);
    fixture.componentInstance.logout();
    fixture.detectChanges();
    expect(fixture.componentInstance.isAuthenticated()).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('No se pudo cerrar');
    expect(fixture.componentInstance.logoutPending()).toBe(false);
  });
});
