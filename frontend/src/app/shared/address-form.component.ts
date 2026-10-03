import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Address } from '../core/models/api.models';
import { CheckoutService } from '../core/services/checkout.service';
import { applyServerErrors, fieldError, phoneValidator } from '../features/auth/auth-forms';

/** Create / edit one delivery address. Talks to the API itself and emits the saved address. */
@Component({
  selector: 'app-address-form',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="form" [attr.aria-label]="address() ? 'Edit address' : 'New address'">
      @if (formError()) { <div class="alert alert-error" role="alert">{{ formError() }}</div> }
      <div class="row">
        <div class="field">
          <label [for]="id + 'label'">Label</label>
          <input [id]="id + 'label'" class="input" formControlName="label" placeholder="Home, Office…" [attr.aria-invalid]="!!err('label')" />
          @if (err('label'); as m) { <span class="error-text">{{ m }}</span> }
        </div>
        <div class="field">
          <label [for]="id + 'fullName'">Full name</label>
          <input [id]="id + 'fullName'" class="input" formControlName="fullName" autocomplete="name" [attr.aria-invalid]="!!err('fullName')" />
          @if (err('fullName'); as m) { <span class="error-text">{{ m }}</span> }
        </div>
      </div>
      <div class="field">
        <label [for]="id + 'phone'">Mobile number</label>
        <input [id]="id + 'phone'" class="input" type="tel" inputmode="tel" formControlName="phone" autocomplete="tel" [attr.aria-invalid]="!!err('phone')" />
        @if (err('phone'); as m) { <span class="error-text">{{ m }}</span> } @else { <span class="hint">e.g. 01712345678</span> }
      </div>
      <div class="row">
        <div class="field">
          <label [for]="id + 'division'">Division</label>
          <select [id]="id + 'division'" class="select" formControlName="division" [attr.aria-invalid]="!!err('division')">
            <option value="" disabled>Select division</option>
            @for (d of divisions(); track d) { <option [value]="d">{{ d }}</option> }
          </select>
          @if (err('division'); as m) { <span class="error-text">{{ m }}</span> }
        </div>
        <div class="field">
          <label [for]="id + 'district'">District</label>
          <input [id]="id + 'district'" class="input" formControlName="district" [attr.aria-invalid]="!!err('district')" />
          @if (err('district'); as m) { <span class="error-text">{{ m }}</span> } @else { <span class="hint">Use “Dhaka” for inside-Dhaka delivery rates.</span> }
        </div>
      </div>
      <div class="row">
        <div class="field">
          <label [for]="id + 'upazila'">Upazila / area <span class="muted">(optional)</span></label>
          <input [id]="id + 'upazila'" class="input" formControlName="upazila" />
        </div>
        <div class="field">
          <label [for]="id + 'postal'">Postal code <span class="muted">(optional)</span></label>
          <input [id]="id + 'postal'" class="input" inputmode="numeric" formControlName="postalCode" [attr.aria-invalid]="!!err('postalCode')" />
          @if (err('postalCode'); as m) { <span class="error-text">{{ m }}</span> }
        </div>
      </div>
      <div class="field">
        <label [for]="id + 'line'">Street address</label>
        <input [id]="id + 'line'" class="input" formControlName="addressLine" autocomplete="street-address" placeholder="House, road, block, landmark" [attr.aria-invalid]="!!err('addressLine')" />
        @if (err('addressLine'); as m) { <span class="error-text">{{ m }}</span> }
      </div>
      <label class="check"><input type="checkbox" formControlName="isDefault" /> Use as my default address</label>
      <div class="actions">
        <button class="btn btn-primary" type="submit" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Save address' }}</button>
        <button class="btn btn-outline" type="button" (click)="cancelled.emit()">Cancel</button>
      </div>
    </form>
  `,
  styles: `
    .row { display: grid; gap: 0 1rem; }
    .check { display: flex; gap: .5rem; align-items: center; margin-bottom: 1rem; }
    .actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    @media (min-width: 560px) { .row { grid-template-columns: 1fr 1fr; } }
  `,
})
export class AddressFormComponent {
  private static nextId = 0;
  protected readonly id = `addr${AddressFormComponent.nextId++}-`;
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(CheckoutService);

  readonly address = input<Address | null>(null);
  readonly divisions = input<readonly string[]>([]);
  /** Pre-tick "default" (e.g. the very first address). */
  readonly defaultChecked = input(false);
  readonly saved = output<Address>();
  readonly cancelled = output<void>();

  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly form = this.fb.nonNullable.group({
    label: ['Home', [Validators.required, Validators.maxLength(40)]],
    fullName: ['', [Validators.required, Validators.maxLength(100)]],
    phone: ['', [Validators.required, phoneValidator]],
    division: ['', [Validators.required]],
    district: ['', [Validators.required, Validators.maxLength(60)]],
    upazila: [''],
    addressLine: ['', [Validators.required, Validators.maxLength(250)]],
    postalCode: ['', [Validators.pattern(/^\d{4}$/)]],
    isDefault: [false],
  });

  constructor() {
    effect(() => {
      const a = this.address();
      this.form.reset(
        a
          ? { label: a.label, fullName: a.fullName, phone: a.phone, division: a.division, district: a.district, upazila: a.upazila ?? '', addressLine: a.addressLine, postalCode: a.postalCode ?? '', isDefault: a.isDefault }
          : { label: 'Home', fullName: '', phone: '', division: '', district: '', upazila: '', addressLine: '', postalCode: '', isDefault: this.defaultChecked() },
      );
    });
  }

  protected err(name: string): string | null {
    const e = fieldError(this.form, name, { label: 'Label', fullName: 'Full name', phone: 'Mobile number', division: 'Division', district: 'District', addressLine: 'Street address' });
    return e === 'Please check this field.' && name === 'postalCode' ? 'Postal code must be 4 digits.' : e;
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    const v = this.form.getRawValue();
    const body = { ...v, upazila: v.upazila.trim() || null, postalCode: v.postalCode.trim() || null };
    const current = this.address();
    (current ? this.api.updateAddress(current.id, body) : this.api.createAddress(body)).subscribe({
      next: (a) => {
        this.busy.set(false);
        this.saved.emit(a);
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(applyServerErrors(this.form, e));
      },
    });
  }
}
