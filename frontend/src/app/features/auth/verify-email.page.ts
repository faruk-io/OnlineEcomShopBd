import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, afterRenderEffect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { isApiError } from '../../core/interceptors/error.interceptor';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { VerifyEmailBannerComponent } from '../../shared/verify-email-banner.component';
import { readTokenFromFragment } from './token-fragment';

type VerifyState = 'verifying' | 'verified' | 'invalid' | 'failed';

@Component({
  selector: 'app-verify-email',
  imports: [RouterLink, VerifyEmailBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section narrow">
      <div class="panel">
        @switch (state()) {
          @case ('verifying') {
            <h1 #heading tabindex="-1">Verifying your email…</h1>
            <p class="muted" role="status">One moment, please.</p>
          }
          @case ('verified') {
            <h1 #heading tabindex="-1">Email verified</h1>
            <p class="alert alert-success" role="status">Thank you. Your email address is confirmed.</p>
            <p class="actions">
              <a class="btn btn-primary" routerLink="/shop">Continue shopping</a>
              @if (!auth.isAuthenticated()) { <a class="btn btn-outline" routerLink="/login">Sign in</a> }
            </p>
          }
          @case ('invalid') {
            <h1 #heading tabindex="-1">This link is invalid or has expired</h1>
            <p class="muted">Verification links work once and expire after a day. If you already used this link, your email may be verified already.</p>
            @if (auth.user()?.emailConfirmed === false) {
              <app-verify-email-banner buttonLabel="Send me a new link" />
            } @else if (!auth.isAuthenticated()) {
              <p>Sign in to request a new verification email.</p>
            }
            <p class="actions">
              @if (!auth.isAuthenticated()) { <a class="btn btn-primary" routerLink="/login" [queryParams]="{ returnUrl: '/account/profile' }">Sign in</a> }
              <a class="btn btn-outline" routerLink="/shop">Continue shopping</a>
            </p>
          }
          @case ('failed') {
            <h1 #heading tabindex="-1">We could not verify your email</h1>
            <p class="alert alert-error" role="alert">Something went wrong on our side or the connection dropped. Your link has not been used up.</p>
            <p class="actions"><button type="button" class="btn btn-primary" (click)="verify()">Try again</button></p>
          }
        }
      </div>
    </div>
  `,
  styles: `.narrow { max-width: 30rem; padding-block: 2rem; } h1 { margin-bottom: .5rem; } .actions { display: flex; gap: .5rem; flex-wrap: wrap; margin-top: 1rem; }`,
})
export class VerifyEmailPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  /** The one-time token lives in memory only: never in storage, never logged, never back in the address bar. */
  private token = readTokenFromFragment(this.route.snapshot.fragment);
  protected readonly state = signal<VerifyState>(this.token === null ? 'invalid' : 'verifying');

  constructor() {
    inject(SeoService).set({ title: 'Verify email', noindex: true });
    // Browser only (the route is client-rendered). Every fragment that arrives (the first load, or a second emailed link opened
    // in this same tab, which is only a hash change) is read, removed from the address bar and spent exactly once.
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.route.fragment.pipe(takeUntilDestroyed(destroyRef)).subscribe((fragment) => {
        if (fragment === null) return;   // our own cleanup navigation
        this.token = readTokenFromFragment(fragment);
        void this.router.navigate([], { relativeTo: this.route, fragment: undefined, queryParamsHandling: 'preserve', replaceUrl: true });
        if (this.token === null) this.state.set('invalid');
        else this.verify();
      });
    });
    let first = true;
    afterRenderEffect(() => {
      this.state();
      if (first) { first = false; return; }
      this.heading()?.nativeElement.focus();
    });
  }

  protected verify(): void {
    if (this.token === null) return;
    this.state.set('verifying');
    this.auth.verifyEmail(this.token).subscribe({
      next: () => {
        this.state.set('verified');
        void this.refreshProfile();
      },
      error: (e: unknown) => this.state.set(isApiError(e) && e.status === 400 ? 'invalid' : 'failed'),
    });
  }

  /** When this browser is signed in, pick up `emailConfirmed: true` so banners and checkout unlock without a reload. */
  private async refreshProfile(): Promise<void> {
    await this.auth.whenReady();
    if (this.auth.isAuthenticated()) this.auth.reloadUser().subscribe({ error: () => undefined });
  }
}
