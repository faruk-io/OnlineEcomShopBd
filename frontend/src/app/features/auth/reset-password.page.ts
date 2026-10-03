import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, afterRenderEffect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { isApiError } from '../../core/interceptors/error.interceptor';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { applyServerErrors, fieldError, matchValidator, passwordValidator } from './auth-forms';
import { readTokenFromFragment } from './token-fragment';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section narrow">
      <div class="panel">
        @if (invalid()) {
          <h1 #heading tabindex="-1">This link is invalid or has expired</h1>
          <p class="muted">Reset links work once and expire after about an hour. Request a new one and use the newest email.</p>
          <a class="btn btn-primary" routerLink="/forgot-password">Request a new link</a>
          <p class="back"><a routerLink="/login">Back to sign in</a></p>
        } @else {
          <h1 #heading tabindex="-1">Choose a new password</h1>
          <p class="muted">For your security you will be signed out on all devices.</p>

          @if (formError()) { <div class="alert alert-error" role="alert" tabindex="-1" #errorBox>{{ formError() }}</div> }

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <div class="field">
              <label for="newPassword">New password</label>
              <input id="newPassword" class="input" type="password" formControlName="newPassword" autocomplete="new-password"
                [attr.aria-invalid]="!!err('newPassword')" [attr.aria-describedby]="err('newPassword') ? 'np-err' : 'np-hint'" />
              @if (err('newPassword'); as m) { <span id="np-err" class="error-text">{{ m }}</span> }
              @else { <span id="np-hint" class="hint">At least 8 characters with upper-case, lower-case and a number.</span> }
            </div>
            <div class="field">
              <label for="confirm">Confirm new password</label>
              <input id="confirm" class="input" type="password" formControlName="confirm" autocomplete="new-password"
                [attr.aria-invalid]="!!err('confirm')" [attr.aria-describedby]="err('confirm') ? 'cf-err' : null" />
              @if (err('confirm'); as m) { <span id="cf-err" class="error-text">{{ m }}</span> }
            </div>
            @if (form.touched && form.hasError('mismatch')) { <p class="error-text" role="alert">Passwords do not match.</p> }
            <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Set new password' }}</button>
          </form>
        }
      </div>
    </div>
  `,
  styles: `.narrow { max-width: 28rem; padding-block: 2rem; } h1 { margin-bottom: .25rem; } .back { margin-top: 1rem; }`,
})
export class ResetPasswordPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly errorBox = viewChild<ElementRef<HTMLElement>>('errorBox');

  /** The one-time token lives in memory only: never in storage, never logged, never back in the address bar. */
  private readonly token = signal<string | null>(readTokenFromFragment(this.route.snapshot.fragment));
  protected readonly invalid = signal(this.token() === null);
  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly form = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, passwordValidator]],
      confirm: ['', [Validators.required]],
    },
    { validators: [matchValidator('newPassword', 'confirm')] },
  );

  constructor() {
    inject(SeoService).set({ title: 'Reset password', noindex: true });
    // Take the secret out of the address bar / history entry as soon as it has been read. A newer link opened in this same tab is
    // only a hash change, so every fragment that arrives replaces the token held in memory.
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.route.fragment.pipe(takeUntilDestroyed(destroyRef)).subscribe((fragment) => {
        if (fragment === null) return;   // our own cleanup navigation
        const token = readTokenFromFragment(fragment);
        this.token.set(token);
        this.invalid.set(token === null);
        this.formError.set('');
        void this.router.navigate([], { relativeTo: this.route, fragment: undefined, queryParamsHandling: 'preserve', replaceUrl: true });
      });
    });
    let first = true;
    afterRenderEffect(() => {
      const invalid = this.invalid();
      const error = this.formError();
      if (first) { first = false; return; }
      if (invalid) this.heading()?.nativeElement.focus();
      else if (error) this.errorBox()?.nativeElement.focus();
    });
  }

  protected err(name: string): string | null {
    return fieldError(this.form, name, { newPassword: 'New password', confirm: 'Password confirmation' });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    const token = this.token();
    if (this.form.invalid || this.busy() || !token) return;
    this.busy.set(true);
    this.formError.set('');
    this.auth.resetPassword(token, this.form.getRawValue().newPassword).subscribe({
      next: () => {
        this.token.set(null);
        void this.router.navigate(['/login'], { queryParams: { reset: 1 }, replaceUrl: true });
      },
      error: (e: unknown) => {
        this.busy.set(false);
        if (isApiError(e) && e.status === 400 && !e.errors?.['newPassword']) {
          // unknown / expired / used link (the server never says which)
          this.token.set(null);
          this.invalid.set(true);
          return;
        }
        // Weak password (token NOT spent), throttling or an outage: keep the form and the token so the user can retry.
        this.formError.set(applyServerErrors(this.form, e));
      },
    });
  }
}
