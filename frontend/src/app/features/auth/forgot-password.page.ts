import { ChangeDetectionStrategy, Component, ElementRef, afterRenderEffect, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { isApiError } from '../../core/interceptors/error.interceptor';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { fieldError } from './auth-forms';

/** Shown for every 202, whether or not the address has an account: the page must never reveal which. */
export const FORGOT_NEUTRAL_MESSAGE =
  'If an account exists for that email address, we have sent a link to reset the password. The link expires in about an hour.';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section narrow">
      <div class="panel">
        @if (sent()) {
          <h1 #heading tabindex="-1">Check your email</h1>
          <p class="alert alert-success" role="status">{{ neutral }}</p>
          <p class="muted">Nothing arrived? Check your spam folder, or <button type="button" class="linklike" (click)="sent.set(false)">try again</button>.</p>
          <p><a routerLink="/login">Back to sign in</a></p>
        } @else {
          <h1 #heading tabindex="-1">Forgot your password?</h1>
          <p class="muted">Enter the email address of your account and we will send you a link to choose a new password.</p>

          @if (formError()) { <div class="alert alert-error" role="alert" tabindex="-1" #errorBox>{{ formError() }}</div> }

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <div class="field">
              <label for="email">Email</label>
              <input id="email" class="input" type="email" formControlName="email" autocomplete="email" inputmode="email"
                [attr.aria-invalid]="!!err('email')" [attr.aria-describedby]="err('email') ? 'email-err' : null" />
              @if (err('email'); as m) { <span id="email-err" class="error-text">{{ m }}</span> }
            </div>
            <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Sending…' : 'Send reset link' }}</button>
          </form>
          <p class="back"><a routerLink="/login">Back to sign in</a></p>
        }
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 28rem; padding-block: 2rem; }
    h1 { margin-bottom: .25rem; }
    .back { margin-top: 1rem; }
    .linklike { background: none; border: 0; padding: 0; color: var(--primary); cursor: pointer; text-decoration: underline; font: inherit; }
  `,
})
export class ForgotPasswordPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly errorBox = viewChild<ElementRef<HTMLElement>>('errorBox');

  protected readonly neutral = FORGOT_NEUTRAL_MESSAGE;
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly formError = signal('');
  protected readonly form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]] });

  constructor() {
    inject(SeoService).set({ title: 'Forgot password', noindex: true });
    // Move focus to what changed so keyboard / screen-reader users hear the outcome (browser only).
    let first = true;
    afterRenderEffect(() => {
      const sent = this.sent();
      const error = this.formError();
      if (first) { first = false; return; }
      if (sent) this.heading()?.nativeElement.focus();
      else if (error) this.errorBox()?.nativeElement.focus();
    });
  }

  protected err(name: string): string | null {
    return fieldError(this.form, name, { email: 'Email' });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    this.auth.forgotPassword(this.form.getRawValue().email.trim()).subscribe({
      next: () => {
        this.busy.set(false);
        this.sent.set(true);
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.formError.set(
          isApiError(e) && e.status === 429
            ? 'Too many requests. Please wait a few minutes before asking for another link.'
            : 'We could not send the request right now. Please try again in a moment.',
        );
      },
    });
  }
}
