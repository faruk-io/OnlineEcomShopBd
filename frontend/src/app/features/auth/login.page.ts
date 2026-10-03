import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { errorMessage, isApiError } from '../../core/interceptors/error.interceptor';
import { AuthService, MfaProof } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { applyServerErrors, fieldError, normaliseRecoveryCode, safeReturnUrl, totpDigits } from './auth-forms';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section narrow">
      <div class="panel">
        <h1>Sign in</h1>
        @if (!auth.mfaPending()) { <p class="muted">New here? <a routerLink="/register" [queryParams]="route.snapshot.queryParams">Create an account</a></p> }

        @if (resetDone) { <div class="alert alert-success" role="status">Your password has been changed. Sign in with the new password.</div> }
        @if (formError() && !auth.mfaPending()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }

        @if (!auth.mfaPending()) {
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <div class="field">
              <label for="email">Email</label>
              <input id="email" class="input" type="email" formControlName="email" autocomplete="username" inputmode="email"
                [attr.aria-invalid]="!!err('email')" [attr.aria-describedby]="err('email') ? 'email-err' : null" />
              @if (err('email'); as m) { <span id="email-err" class="error-text">{{ m }}</span> }
            </div>
            <div class="field">
              <label for="password">Password</label>
              <input id="password" class="input" type="password" formControlName="password" autocomplete="current-password"
                [attr.aria-invalid]="!!err('password')" [attr.aria-describedby]="err('password') ? 'pw-err' : null" />
              @if (err('password'); as m) { <span id="pw-err" class="error-text">{{ m }}</span> }
              <a class="forgot" routerLink="/forgot-password">Forgot your password?</a>
            </div>
            <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
          </form>
        } @else {
          <form [formGroup]="mfaForm" (ngSubmit)="submitMfa()" novalidate>
            <h2 class="step">Two-step verification</h2>
            @if (!useRecovery()) {
              <p class="muted" id="mfa-help">Enter the 6-digit code from your authenticator app.</p>
              <div class="field">
                <label for="mfa-code">Authentication code</label>
                <input id="mfa-code" data-testid="mfa-code" #codeInput class="input code" type="text" formControlName="code" inputmode="numeric" autocomplete="one-time-code"
                  maxlength="7" spellcheck="false" aria-describedby="mfa-help" [attr.aria-invalid]="!!mfaError()" />
              </div>
            } @else {
              <p class="muted" id="mfa-help">Enter one of the recovery codes you saved. Each code works once.</p>
              <div class="field">
                <label for="mfa-recovery">Recovery code</label>
                <input id="mfa-recovery" data-testid="mfa-recovery" #codeInput class="input code" type="text" formControlName="recovery" autocomplete="off"
                  autocapitalize="characters" maxlength="24" spellcheck="false" aria-describedby="mfa-help" [attr.aria-invalid]="!!mfaError()" />
              </div>
            }
            <div class="live" aria-live="polite" role="status">
              @if (mfaError(); as m) { <div class="alert alert-error" id="mfa-error" data-testid="mfa-error">{{ m }}</div> }
            </div>
            <button id="mfa-submit" data-testid="mfa-submit" class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Verifying…' : 'Verify' }}</button>
            <div class="row">
              <button id="mfa-toggle" type="button" class="btn btn-ghost btn-sm" (click)="toggleRecovery()">
                {{ useRecovery() ? 'Use an authenticator code instead' : 'Use a recovery code instead' }}
              </button>
              <button id="mfa-back" type="button" class="btn btn-ghost btn-sm" (click)="back()">Back</button>
            </div>
          </form>
        }
      </div>
    </div>
  `,
  styles: `.step { font-size: 1.1rem; } .code { letter-spacing: .15em; font-size: 1.2rem; font-family: ui-monospace, monospace; } .row { display: flex; justify-content: space-between; margin-top: .5rem; } .live:empty { display: none; } .narrow { max-width: 28rem; padding-block: 2rem; } h1 { margin-bottom: .25rem; } .forgot { font-size: .85rem; }`,
})
export class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly route = inject(ActivatedRoute);
  protected readonly auth = inject(AuthService);

  protected readonly resetDone = this.route.snapshot.queryParamMap.get('reset') === '1';
  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected readonly useRecovery = signal(false);
  protected readonly mfaError = signal('');
  protected readonly mfaForm = this.fb.nonNullable.group({ code: [''], recovery: [''] });
  private readonly codeInput = viewChild<ElementRef<HTMLInputElement>>('codeInput');

  constructor() {
    inject(SeoService).set({ title: 'Sign in', noindex: true });
    // Move focus to the code field whenever the second step (or the recovery variant) appears.
    effect(() => this.codeInput()?.nativeElement.focus());
  }

  protected err(name: string): string | null {
    return fieldError(this.form, name, { email: 'Email', password: 'Password' });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: (result) => {
        if (result.kind === 'mfa') {
          this.busy.set(false);
          this.mfaError.set('');
          this.useRecovery.set(false);
          this.mfaForm.reset();
          return;
        }
        this.finish(result.user.fullName);
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(applyServerErrors(this.form, e));
      },
    });
  }

  protected submitMfa(): void {
    if (this.busy()) return;
    const { code, recovery } = this.mfaForm.getRawValue();
    let proof: MfaProof;
    if (this.useRecovery()) {
      const rc = normaliseRecoveryCode(recovery);
      if (!rc) return void this.mfaError.set('Enter a recovery code.');
      proof = { recoveryCode: rc };
    } else {
      const digits = totpDigits(code);
      if (digits.length !== 6) return void this.mfaError.set('Enter the 6-digit code from your authenticator app.');
      proof = { code: digits };
    }
    this.busy.set(true);
    this.mfaError.set('');
    this.auth.verifyMfa(proof).subscribe({
      next: (user) => this.finish(user.fullName),
      error: (e) => {
        this.busy.set(false);
        const message = isApiError(e) && e.status === 429 ? 'Too many attempts. Please wait a minute and try again.' : errorMessage(e, 'Could not verify the code. Please try again.');
        if (this.auth.mfaPending()) this.mfaError.set(message);   // wrong code: stay on the step
        else this.formError.set(message);                          // expired / locked: back to the password step with the reason
      },
    });
  }

  protected toggleRecovery(): void {
    this.useRecovery.update((v) => !v);
    this.mfaError.set('');
    this.mfaForm.reset();
  }

  protected back(): void {
    this.auth.cancelMfa();
    this.mfaForm.reset();
    this.mfaError.set('');
    this.useRecovery.set(false);
    this.form.controls.password.reset('');
  }

  private finish(fullName: string): void {
    this.toast.success(`Welcome back, ${fullName.split(' ')[0]}!`);
    void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
  }
}
