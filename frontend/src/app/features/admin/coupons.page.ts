import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { AdminCoupon, DiscountType, SaveCouponRequest } from '../../core/models/api.models';
import { AdminApiService } from '../../core/services/admin-api.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { AdminFieldComponent, describedBy } from './admin-field.component';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { fromLocalInput, humanize, inputChecked, inputValue, normalizeFieldErrors, numOrNull, orNull, problemText, toLocalInput } from './admin.util';

export interface CouponForm {
  code: string; description: string; discountType: DiscountType; value: string; minOrderAmount: string; maxDiscountAmount: string;
  usageLimit: string; startsAt: string; expiresAt: string; isActive: boolean;
}
const blank = (): CouponForm => ({ code: '', description: '', discountType: 'Percentage', value: '', minOrderAmount: '', maxDiscountAmount: '', usageLimit: '', startsAt: '', expiresAt: '', isActive: true });

/** Pure: validates (mirroring SaveCouponRequestValidator) and maps the form to the API request. */
export function buildCouponRequest(f: CouponForm): { request: SaveCouponRequest | null; errors: Record<string, string> } {
  const e: Record<string, string> = {};
  const code = f.code.trim();
  if (!code) e['code'] = 'Code is required.';
  else if (code.length > 50 || !/^[A-Za-z0-9_-]+$/.test(code)) e['code'] = 'Code may contain letters, numbers, - and _ (max 50).';
  if (f.description.length > 300) e['description'] = 'Description must be 300 characters or fewer.';

  const value = numOrNull(f.value);
  if (value === null || !Number.isFinite(value) || value <= 0) e['value'] = 'Enter a value greater than 0.';
  else if (f.discountType === 'Percentage' && value > 100) e['value'] = 'A percentage discount cannot exceed 100.';

  const min = numOrNull(f.minOrderAmount);
  if (min !== null && (!Number.isFinite(min) || min < 0)) e['minOrderAmount'] = 'Enter 0 or more.';
  const max = numOrNull(f.maxDiscountAmount);
  if (max !== null && (!Number.isFinite(max) || max <= 0)) e['maxDiscountAmount'] = 'Enter a value greater than 0.';
  const limit = numOrNull(f.usageLimit);
  if (limit !== null && (!Number.isInteger(limit) || limit <= 0)) e['usageLimit'] = 'Enter a whole number greater than 0.';

  const startsAt = fromLocalInput(f.startsAt);
  const expiresAt = fromLocalInput(f.expiresAt);
  if (f.startsAt.trim() && !startsAt) e['startsAt'] = 'Enter a valid date and time.';
  if (f.expiresAt.trim() && !expiresAt) e['expiresAt'] = 'Enter a valid date and time.';
  if (startsAt && expiresAt && !(new Date(startsAt) < new Date(expiresAt))) e['expiresAt'] = 'Expiry must be after the start date.';

  if (Object.keys(e).length) return { request: null, errors: e };
  return {
    request: {
      code, description: orNull(f.description), discountType: f.discountType, value: value as number, minOrderAmount: min, maxDiscountAmount: max,
      usageLimit: limit, startsAt, expiresAt, isActive: f.isActive,
    },
    errors: e,
  };
}

export function couponToForm(c: AdminCoupon): CouponForm {
  return {
    code: c.code, description: c.description ?? '', discountType: c.discountType, value: String(c.value),
    minOrderAmount: c.minOrderAmount === null ? '' : String(c.minOrderAmount), maxDiscountAmount: c.maxDiscountAmount === null ? '' : String(c.maxDiscountAmount),
    usageLimit: c.usageLimit === null ? '' : String(c.usageLimit), startsAt: toLocalInput(c.startsAt), expiresAt: toLocalInput(c.expiresAt), isActive: c.isActive,
  };
}

@Component({
  selector: 'app-admin-coupons',
  imports: [AdminFieldComponent, ConfirmDialogComponent, DatePipe, BdtPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="adm-head">
      <h1>Coupons</h1>
      <button type="button" class="btn btn-primary" (click)="startNew()">Add coupon</button>
    </div>

    @if (message(); as m) { <p class="alert alert-error" role="alert" data-test="list-error">{{ m }}</p> }

    @if (editing() !== null) {
      <form class="panel" (submit)="save($event)" novalidate [attr.aria-label]="editing() === 'new' ? 'New coupon' : 'Edit coupon'" style="margin-bottom:1rem">
        <h2>{{ editing() === 'new' ? 'New coupon' : 'Edit coupon' }}</h2>
        @if (formError(); as m) { <p class="alert alert-error" role="alert" data-test="form-error">{{ m }}</p> }
        <div class="adm-form-grid">
          <adm-field label="Code" forId="k-code" [required]="true" [error]="err('code')">
            <input id="k-code" class="input" maxlength="50" autocapitalize="characters" [value]="f().code" (input)="set('code', val($event))" [attr.aria-invalid]="err('code') ? 'true' : null" [attr.aria-describedby]="desc('k-code', 'code')" />
          </adm-field>
          <adm-field label="Type" forId="k-type">
            <select id="k-type" class="select" (change)="set('discountType', type($event))">
              <option value="Percentage" [selected]="f().discountType === 'Percentage'">Percentage (%)</option>
              <option value="FixedAmount" [selected]="f().discountType === 'FixedAmount'">Fixed amount (৳)</option>
            </select>
          </adm-field>
          <adm-field [label]="f().discountType === 'Percentage' ? 'Value (%)' : 'Value (৳)'" forId="k-value" [required]="true" [error]="err('value')">
            <input id="k-value" class="input" inputmode="decimal" [value]="f().value" (input)="set('value', val($event))" [attr.aria-invalid]="err('value') ? 'true' : null" [attr.aria-describedby]="desc('k-value', 'value')" />
          </adm-field>
          <adm-field label="Minimum order (৳)" forId="k-min" [error]="err('minOrderAmount')" hint="Optional">
            <input id="k-min" class="input" inputmode="decimal" [value]="f().minOrderAmount" (input)="set('minOrderAmount', val($event))" [attr.aria-invalid]="err('minOrderAmount') ? 'true' : null" [attr.aria-describedby]="desc('k-min', 'minOrderAmount', true)" />
          </adm-field>
          <adm-field label="Maximum discount (৳)" forId="k-max" [error]="err('maxDiscountAmount')" hint="Optional cap, useful for percentage coupons">
            <input id="k-max" class="input" inputmode="decimal" [value]="f().maxDiscountAmount" (input)="set('maxDiscountAmount', val($event))" [attr.aria-invalid]="err('maxDiscountAmount') ? 'true' : null" [attr.aria-describedby]="desc('k-max', 'maxDiscountAmount', true)" />
          </adm-field>
          <adm-field label="Usage limit" forId="k-limit" [error]="err('usageLimit')" [hint]="editingUsed() !== null ? 'Used ' + editingUsed() + ' time(s) so far. Leave blank for unlimited.' : 'Leave blank for unlimited.'">
            <input id="k-limit" class="input" type="number" min="1" step="1" [value]="f().usageLimit" (input)="set('usageLimit', val($event))" [attr.aria-invalid]="err('usageLimit') ? 'true' : null" [attr.aria-describedby]="desc('k-limit', 'usageLimit', true)" />
          </adm-field>
          <adm-field label="Starts at" forId="k-start" [error]="err('startsAt')">
            <input id="k-start" class="input" type="datetime-local" [value]="f().startsAt" (input)="set('startsAt', val($event))" [attr.aria-invalid]="err('startsAt') ? 'true' : null" [attr.aria-describedby]="desc('k-start', 'startsAt')" />
          </adm-field>
          <adm-field label="Expires at" forId="k-end" [error]="err('expiresAt')">
            <input id="k-end" class="input" type="datetime-local" [value]="f().expiresAt" (input)="set('expiresAt', val($event))" [attr.aria-invalid]="err('expiresAt') ? 'true' : null" [attr.aria-describedby]="desc('k-end', 'expiresAt')" />
          </adm-field>
          <div class="wide"><adm-field label="Description" forId="k-desc" [error]="err('description')">
            <input id="k-desc" class="input" maxlength="300" [value]="f().description" (input)="set('description', val($event))" [attr.aria-invalid]="err('description') ? 'true' : null" [attr.aria-describedby]="desc('k-desc', 'description')" />
          </adm-field></div>
          <label class="adm-check"><input type="checkbox" [checked]="f().isActive" (change)="set('isActive', checked($event))" /> Active</label>
        </div>
        <div style="display:flex;gap:.5rem;margin-top:1rem">
          <button type="submit" class="btn btn-primary" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save coupon' }}</button>
          <button type="button" class="btn btn-ghost" (click)="editing.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (rows(); as list) {
      <div class="adm-scroll" tabindex="0" role="region" aria-label="Coupons table">
        <table class="adm-table">
          <caption class="visually-hidden">Coupons</caption>
          <thead><tr><th scope="col">Code</th><th scope="col">Discount</th><th scope="col">Conditions</th><th scope="col" class="num">Used</th><th scope="col">Validity</th><th scope="col">Status</th><th scope="col"><span class="visually-hidden">Actions</span></th></tr></thead>
          <tbody>
            @for (c of list; track c.id) {
              <tr>
                <th scope="row">{{ c.code }}@if (c.description) { <div class="adm-subtle">{{ c.description }}</div> }</th>
                <td>{{ c.discountType === 'Percentage' ? c.value + '%' : (c.value | bdt) }}</td>
                <td class="adm-subtle">
                  @if (c.minOrderAmount !== null) { Min {{ c.minOrderAmount | bdt }}<br /> }
                  @if (c.maxDiscountAmount !== null) { Max discount {{ c.maxDiscountAmount | bdt }} }
                  @if (c.minOrderAmount === null && c.maxDiscountAmount === null) { — }
                </td>
                <td class="num">{{ c.usedCount }}{{ c.usageLimit !== null ? ' / ' + c.usageLimit : '' }}</td>
                <td class="adm-subtle">{{ c.startsAt ? (c.startsAt | date: 'mediumDate') : 'Any time' }} &rarr; {{ c.expiresAt ? (c.expiresAt | date: 'mediumDate') : 'No expiry' }}</td>
                <td><span class="adm-badge" [class]="c.isActive ? 'ok' : 'neutral'">{{ c.isActive ? 'Active' : 'Inactive' }}</span></td>
                <td class="actions">
                  <button type="button" class="btn btn-sm btn-outline" [attr.aria-label]="'Edit coupon ' + c.code" (click)="edit(c)">Edit</button>
                  <button type="button" class="btn btn-sm btn-danger-outline" [attr.aria-label]="'Delete coupon ' + c.code" (click)="target.set(c)">Delete</button>
                </td>
              </tr>
            } @empty { <tr><td colspan="7" class="muted">No coupons yet.</td></tr> }
          </tbody>
        </table>
      </div>
    } @else if (res.isLoading()) { <div class="skeleton adm-skel" aria-busy="true"></div> }

    <adm-confirm [open]="target() !== null" heading="Delete coupon?" [message]="'Coupon ' + (target()?.code ?? '') + ' will stop working. Past orders keep their discount.'"
      confirmLabel="Delete coupon" [busy]="busy()" (confirmed)="remove()" (cancelled)="target.set(null)" />
  `,
})
export class AdminCouponsPage {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);

  protected readonly res = rxResource({ stream: () => this.api.coupons() });
  protected readonly rows = computed(() => safeValue(this.res));
  protected readonly editing = signal<'new' | number | null>(null);
  protected readonly editingUsed = signal<number | null>(null);
  protected readonly f = signal<CouponForm>(blank());
  protected readonly clientErrors = signal<Record<string, string>>({});
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly formError = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly busy = signal(false);
  protected readonly target = signal<AdminCoupon | null>(null);

  protected readonly val = inputValue;
  protected readonly checked = inputChecked;
  protected readonly type = (e: Event) => inputValue(e) as DiscountType;
  protected readonly label = humanize;

  constructor() {
    inject(SeoService).set({ title: 'Coupons', noindex: true });
  }

  protected err(k: string): string | null { return this.clientErrors()[k] ?? this.serverErrors()[k] ?? null; }
  protected desc(id: string, k: string, hint = false): string | null { return describedBy(id, this.err(k), hint); }
  protected set<K extends keyof CouponForm>(k: K, v: CouponForm[K]): void { this.f.update((m) => ({ ...m, [k]: v })); }

  protected startNew(): void { this.open('new', blank(), null); }
  protected edit(c: AdminCoupon): void { this.open(c.id, couponToForm(c), c.usedCount); }
  private open(mode: 'new' | number, form: CouponForm, used: number | null): void {
    this.editing.set(mode); this.editingUsed.set(used); this.f.set(form); this.clientErrors.set({}); this.serverErrors.set({}); this.formError.set(null);
  }

  protected save(e: Event): void {
    e.preventDefault();
    const { request, errors } = buildCouponRequest(this.f());
    this.clientErrors.set(errors);
    this.serverErrors.set({});
    this.formError.set(null);
    if (!request) return;
    const mode = this.editing();
    this.saving.set(true);
    const call = mode === 'new' || mode === null ? this.api.createCoupon(request) : this.api.updateCoupon(mode, request);
    call.subscribe({
      next: (c) => { this.saving.set(false); this.editing.set(null); this.toast.success(`Saved coupon ${c.code}.`); this.res.reload(); },
      error: (err: unknown) => {
        this.saving.set(false);
        const fields = normalizeFieldErrors(apiErrorOf(err));
        this.serverErrors.set(fields);
        this.formError.set(Object.keys(fields).length ? null : problemText(err, 'Could not save the coupon.'));
      },
    });
  }

  protected remove(): void {
    const c = this.target();
    if (!c) return;
    this.busy.set(true);
    this.message.set(null);
    this.api.deleteCoupon(c.id).subscribe({
      next: () => { this.busy.set(false); this.target.set(null); this.toast.success(`Deleted coupon ${c.code}.`); this.res.reload(); },
      error: (err: unknown) => { this.busy.set(false); this.target.set(null); this.message.set(problemText(err, 'Could not delete the coupon.')); },
    });
  }
}
