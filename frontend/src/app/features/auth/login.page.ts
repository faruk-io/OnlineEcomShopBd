import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { applyServerErrors, fieldError, safeReturnUrl } from './auth-forms';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section narrow">
      <div class="panel">
        <h1>Sign in</h1>
        <p class="muted">New here? <a routerLink="/register" [queryParams]="route.snapshot.queryParams">Create an account</a></p>

        @if (formError()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }

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
          </div>
          <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        </form>
      </div>
    </div>
  `,
  styles: `.narrow { max-width: 28rem; padding-block: 2rem; } h1 { margin-bottom: .25rem; }`,
})
export class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  constructor() {
    inject(SeoService).set({ title: 'Sign in', noindex: true });
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
      next: (user) => {
        this.toast.success(`Welcome back, ${user.fullName.split(' ')[0]}!`);
        void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(applyServerErrors(this.form, e));
      },
    });
  }
}
