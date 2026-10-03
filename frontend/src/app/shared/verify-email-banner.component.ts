import { isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, inject, input, signal } from '@angular/core';
import { isApiError } from '../core/interceptors/error.interceptor';
import { AuthService } from '../core/services/auth.service';

/** Client-side pause between resend clicks (the server throttles too; this just stops accidental double sends). */
export const RESEND_COOLDOWN_SECONDS = 60;

/**
 * "Please verify your email" notice with a resend button. Renders only for a signed-in user whose `emailConfirmed` is false.
 * `blocking` is the prominent variant used where an unverified address stops the user from doing something (checkout).
 */
@Component({
  selector: 'app-verify-email-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <section class="banner" [class.blocking]="blocking()" aria-label="Email verification">
        <p class="msg">
          <strong>{{ blocking() ? 'Verify your email to place orders.' : 'Please verify your email address.' }}</strong>
          We sent a link to {{ auth.user()?.email }}. Open it to confirm that this address is yours.
        </p>
        <button type="button" class="btn btn-outline btn-sm" [disabled]="busy() || cooldown() > 0" (click)="resend()">
          {{ busy() ? 'Sending…' : cooldown() > 0 ? buttonLabel() + ' (' + cooldown() + 's)' : buttonLabel() }}
        </button>
        <p class="status" role="status">{{ note() }}</p>
        @if (problem()) { <p class="error-text" role="alert">{{ problem() }}</p> }
      </section>
    }
  `,
  styles: `
    .banner { display: grid; gap: .5rem; justify-items: start; padding: .75rem 1rem; margin-bottom: 1rem; border-radius: var(--radius); background: var(--warn-bg); color: var(--text); border: 1px solid var(--warn); font-size: .92rem; }
    .banner.blocking { border-width: 2px; padding: 1rem 1.25rem; }
    .msg, .status { margin: 0; }
    .status:empty { display: none; }
    .error-text { margin: 0; }
  `,
})
export class VerifyEmailBannerComponent {
  protected readonly auth = inject(AuthService);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly blocking = input(false);
  readonly buttonLabel = input('Resend verification email');

  protected readonly visible = computed(() => this.auth.user()?.emailConfirmed === false);
  protected readonly busy = signal(false);
  protected readonly cooldown = signal(0);
  protected readonly note = signal('');
  protected readonly problem = signal('');
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopTimer());
  }

  protected resend(): void {
    if (this.busy() || this.cooldown() > 0) return;
    this.busy.set(true);
    this.note.set('');
    this.problem.set('');
    this.auth.resendVerification().subscribe({
      next: (res) => {
        this.busy.set(false);
        this.note.set(res.message || 'If your address still needs verifying, a new link is on its way.');
        this.startCooldown();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        if (isApiError(e) && e.status === 429) {
          this.problem.set('You have asked for several emails recently. Please wait a few minutes, then try again.');
          this.startCooldown();
        } else {
          this.problem.set('We could not send the email right now. Please try again in a moment.');
        }
      },
    });
  }

  private startCooldown(): void {
    this.stopTimer();
    this.cooldown.set(RESEND_COOLDOWN_SECONDS);
    if (!this.browser) return;
    this.timer = setInterval(() => {
      this.cooldown.update((s) => Math.max(0, s - 1));
      if (this.cooldown() === 0) this.stopTimer();
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
  }
}
