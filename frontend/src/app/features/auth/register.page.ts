import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { applyServerErrors, fieldError, matchValidator, passwordValidator, phoneValidator, safeReturnUrl } from './auth-forms';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container section narrow">
      <div class="panel">
        <h1>Create your account</h1>
        <p class="muted">Already registered? <a routerLink="/login" [queryParams]="route.snapshot.queryParams">Sign in</a></p>

        @if (formError()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @for (f of fields; track f.name) {
            <div class="field">
              <label [for]="f.name">{{ f.label }}@if (f.optional) { <span class="muted"> (optional)</span> }</label>
              <input [id]="f.name" class="input" [type]="f.type" [formControlName]="f.name" [attr.autocomplete]="f.autocomplete" [attr.inputmode]="f.inputmode"
                [attr.aria-invalid]="!!err(f.name)" [attr.aria-describedby]="err(f.name) ? f.name + '-err' : (f.hint ? f.name + '-hint' : null)" />
              @if (err(f.name); as m) { <span [id]="f.name + '-err'" class="error-text">{{ m }}</span> }
              @else if (f.hint) { <span [id]="f.name + '-hint'" class="hint">{{ f.hint }}</span> }
            </div>
          }
          @if (form.touched && form.hasError('mismatch')) { <p class="error-text" role="alert">Passwords do not match.</p> }
          <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Creating account…' : 'Create account' }}</button>
        </form>
      </div>
    </div>
  `,
  styles: `.narrow { max-width: 30rem; padding-block: 2rem; } h1 { margin-bottom: .25rem; }`,
})
export class RegisterPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly fields = [
    { name: 'fullName', label: 'Full name', type: 'text', autocomplete: 'name', inputmode: null, hint: '', optional: false },
    { name: 'email', label: 'Email', type: 'email', autocomplete: 'email', inputmode: 'email', hint: '', optional: false },
    { name: 'phone', label: 'Mobile number', type: 'tel', autocomplete: 'tel', inputmode: 'tel', hint: 'e.g. 01712345678', optional: true },
    { name: 'password', label: 'Password', type: 'password', autocomplete: 'new-password', inputmode: null, hint: 'At least 8 characters with upper-case, lower-case and a number.', optional: false },
    { name: 'confirm', label: 'Confirm password', type: 'password', autocomplete: 'new-password', inputmode: null, hint: '', optional: false },
  ];
  private readonly labels = Object.fromEntries(this.fields.map((f) => [f.name, f.label]));

  protected readonly form = this.fb.nonNullable.group(
    {
      fullName: ['', [Validators.required, Validators.maxLength(100)]],
      email: ['', [Validators.required, Validators.email]],
      phone: ['', [phoneValidator]],
      password: ['', [Validators.required, passwordValidator]],
      confirm: ['', [Validators.required]],
    },
    { validators: [matchValidator('password', 'confirm')] },
  );

  constructor() {
    inject(SeoService).set({ title: 'Create account', noindex: true });
  }

  protected err(name: string): string | null {
    return fieldError(this.form, name, this.labels);
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    const { fullName, email, phone, password } = this.form.getRawValue();
    this.auth.register({ fullName, email, phone: phone || null, password }).subscribe({
      next: (user) => {
        this.toast.success(`Welcome to TechBazar BD, ${user.fullName.split(' ')[0]}!`);
        void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(applyServerErrors(this.form, e));
      },
    });
  }
}
