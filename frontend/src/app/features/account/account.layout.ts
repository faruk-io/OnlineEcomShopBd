import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { VerifyEmailBannerComponent } from '../../shared/verify-email-banner.component';

@Component({
  selector: 'app-account-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BreadcrumbComponent, VerifyEmailBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section">
      <app-breadcrumb [items]="[{ label: 'My account' }]" />
      <div class="grid">
        <nav class="side panel" aria-label="Account">
          <p class="who"><strong>{{ auth.user()?.fullName }}</strong><span class="muted">{{ auth.user()?.email }}</span></p>
          <ul>
            <li><a routerLink="profile" routerLinkActive="on" ariaCurrentWhenActive="page">Profile</a></li>
            <li><a routerLink="orders" routerLinkActive="on" ariaCurrentWhenActive="page">Order history</a></li>
            <li><a routerLink="addresses" routerLinkActive="on" ariaCurrentWhenActive="page">Address book</a></li>
            @if (auth.isAdmin()) { <li><a routerLink="/admin">Admin panel</a></li> }
            <li><a routerLink="/wishlist">Wishlist</a></li>
            <li><button type="button" (click)="logout()">Logout</button></li>
            <li><button type="button" class="all" (click)="logoutEverywhere()">Sign out on all devices</button></li>
          </ul>
        </nav>
        <div class="content"><app-verify-email-banner /><router-outlet /></div>
      </div>
    </div>
  `,
  styles: `
    .grid { display: grid; gap: 1rem; margin-top: 1rem; }
    .who { display: grid; margin: 0 0 .75rem; word-break: break-all; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: .15rem; }
    a, button { display: block; width: 100%; text-align: left; padding: .55rem .75rem; border-radius: var(--radius); color: var(--text); background: none; border: 0; cursor: pointer; font-weight: 500; }
    a:hover, button:hover { background: var(--surface-2); text-decoration: none; }
    .all { color: var(--muted); font-size: .85rem; }
    a.on { background: var(--primary-weak); color: var(--primary-strong); font-weight: 700; }
    @media (min-width: 800px) { .grid { grid-template-columns: 16rem 1fr; align-items: start; } }
  `,
})
export class AccountLayout {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  /** Revokes every refresh token of the account (lost phone, shared computer, suspected compromise). */
  protected logoutEverywhere(): void {
    this.auth.logoutAll().subscribe({
      next: () => {
        this.toast.success('Signed out on all devices');
        void this.router.navigateByUrl('/login');
      },
      error: () => this.toast.error('Could not sign out on all devices. Please try again.'),
    });
  }

  protected logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/');
  }
}
