import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';

@Component({
  selector: 'app-account-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BreadcrumbComponent],
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
            <li><a routerLink="/wishlist">Wishlist</a></li>
            <li><button type="button" (click)="logout()">Logout</button></li>
          </ul>
        </nav>
        <div class="content"><router-outlet /></div>
      </div>
    </div>
  `,
  styles: `
    .grid { display: grid; gap: 1rem; margin-top: 1rem; }
    .who { display: grid; margin: 0 0 .75rem; word-break: break-all; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: .15rem; }
    a, button { display: block; width: 100%; text-align: left; padding: .55rem .75rem; border-radius: var(--radius); color: var(--text); background: none; border: 0; cursor: pointer; font-weight: 500; }
    a:hover, button:hover { background: var(--surface-2); text-decoration: none; }
    a.on { background: var(--primary-weak); color: var(--primary-strong); font-weight: 700; }
    @media (min-width: 800px) { .grid { grid-template-columns: 16rem 1fr; align-items: start; } }
  `,
})
export class AccountLayout {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/');
  }
}
