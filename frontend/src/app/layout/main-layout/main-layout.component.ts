import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { UpperCasePipe } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { IconComponent } from '../../shared/components/icon/icon.component';

@Component({
  selector: 'app-main-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, IconComponent, UpperCasePipe],
  templateUrl: './main-layout.component.html',
  styleUrl: './main-layout.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'profileOpen.set(false)' },
})
export class MainLayoutComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly profileOpen = signal(false);
  readonly logoutPending = signal(false);
  readonly logoutError = signal('');

  readonly currentUser = this.authService.currentUser;
  readonly isAuthenticated = this.authService.isAuthenticated;

  ngOnInit(): void {
    this.authService.checkSession().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => this.authService.clearLocalSession(),
    });
  }

  toggleProfile(): void {
    this.profileOpen.update((value) => !value);
  }

  logout(): void {
    if (this.logoutPending()) return;
    this.logoutPending.set(true);
    this.logoutError.set('');
    this.authService.logout().pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.logoutPending.set(false)),
    ).subscribe({
      next: () => {
        this.profileOpen.set(false);
        this.router.navigate(['/']);
      },
      error: () => this.logoutError.set('No se pudo cerrar la sesión. Reintenta.'),
    });
  }

  skipToContent(event: Event): void {
    event.preventDefault();
    const content = document.getElementById('main-content');
    content?.focus();
    content?.scrollIntoView({ block: 'start' });
  }
}
