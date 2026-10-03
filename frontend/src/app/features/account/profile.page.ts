import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { applyServerErrors, fieldError, phoneValidator } from '../auth/auth-forms';

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="prof-h">
      <h1 id="prof-h">My profile</h1>
      @if (formError()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }
      <form [formGroup]="form" (ngSubmit)="save()" novalidate class="form">
        <div class="field">
          <label for="email">Email</label>
          <input id="email" class="input" type="email" [value]="auth.user()?.email ?? ''" readonly aria-readonly="true" />
          <span class="hint">Your email is your sign-in name and can’t be changed here.</span>
        </div>
        <div class="field">
          <label for="fullName">Full name</label>
          <input id="fullName" class="input" formControlName="fullName" autocomplete="name" [attr.aria-invalid]="!!err('fullName')" />
          @if (err('fullName'); as m) { <span class="error-text">{{ m }}</span> }
        </div>
        <div class="field">
          <label for="phone">Mobile number <span class="muted">(optional)</span></label>
          <input id="phone" class="input" type="tel" formControlName="phone" autocomplete="tel" inputmode="tel" [attr.aria-invalid]="!!err('phone')" />
          @if (err('phone'); as m) { <span class="error-text">{{ m }}</span> } @else { <span class="hint">e.g. 01712345678</span> }
        </div>
        <button class="btn btn-primary" type="submit" [disabled]="busy() || form.pristine">{{ busy() ? 'Saving…' : 'Save changes' }}</button>
      </form>
    </section>
  `,
  styles: `.form { max-width: 28rem; }`,
})
export class ProfilePage {
  private readonly fb = inject(FormBuilder);
  protected readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(100)]],
    phone: ['', [phoneValidator]],
  });

  constructor() {
    inject(SeoService).set({ title: 'My profile', noindex: true });
    effect(() => {
      const u = this.auth.user();
      if (u && this.form.pristine) this.form.reset({ fullName: u.fullName, phone: u.phone ?? '' });
    });
  }

  protected err(name: string): string | null {
    return fieldError(this.form, name, { fullName: 'Full name', phone: 'Mobile number' });
  }

  protected save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    const { fullName, phone } = this.form.getRawValue();
    this.auth.updateProfile(fullName, phone || null).subscribe({
      next: () => {
        this.busy.set(false);
        this.form.markAsPristine();
        this.toast.success('Profile updated');
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(applyServerErrors(this.form, e));
      },
    });
  }
}
