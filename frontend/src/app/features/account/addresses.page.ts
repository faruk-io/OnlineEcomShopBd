import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Address } from '../../core/models/api.models';
import { CheckoutService } from '../../core/services/checkout.service';
import { SeoService } from '../../core/services/seo.service';
import { ToastService } from '../../core/services/toast.service';
import { errorMessage } from '../../core/interceptors/error.interceptor';
import { safeValue } from '../../core/util/resource';
import { AddressFormComponent } from '../../shared/address-form.component';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'app-addresses',
  imports: [AddressFormComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel" aria-labelledby="ad-h">
      <div class="head">
        <h1 id="ad-h">Address book</h1>
        @if (!editing()) { <button type="button" class="btn btn-primary btn-sm" (click)="startNew()"><app-icon name="plus" [size]="16" /> Add address</button> }
      </div>

      @if (editing(); as ed) {
        <app-address-form [address]="ed === 'new' ? null : ed" [divisions]="divisions()" [defaultChecked]="list().length === 0" (saved)="onSaved()" (cancelled)="editing.set(null)" />
      } @else if (addresses.isLoading() && !addresses.hasValue()) {
        <p class="muted" role="status">Loading addresses…</p>
      } @else if (list().length === 0) {
        <p class="muted">You haven’t saved an address yet. Add one to check out faster.</p>
      } @else {
        <ul class="cards">
          @for (a of list(); track a.id) {
            <li class="card" [class.default]="a.isDefault">
              <header>
                <strong>{{ a.label }}</strong>
                @if (a.isDefault) { <span class="tag">Default</span> }
              </header>
              <p class="addr">
                {{ a.fullName }} · {{ a.phone }}<br />
                {{ a.addressLine }}<br />
                {{ a.upazila ? a.upazila + ', ' : '' }}{{ a.district }}, {{ a.division }}{{ a.postalCode ? ' ' + a.postalCode : '' }}
              </p>
              <div class="actions">
                <button type="button" class="btn btn-outline btn-sm" (click)="editing.set(a)">Edit</button>
                @if (!a.isDefault) { <button type="button" class="btn btn-ghost btn-sm" (click)="makeDefault(a)">Make default</button> }
                <button type="button" class="btn btn-ghost btn-sm del" (click)="remove(a)" [attr.aria-label]="'Delete address ' + a.label"><app-icon name="trash" [size]="16" /></button>
              </div>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .head { display: flex; justify-content: space-between; align-items: center; gap: 1rem; margin-bottom: 1rem; }
    h1 { font-size: 1.3rem; margin: 0; }
    .cards { list-style: none; margin: 0; padding: 0; display: grid; gap: .75rem; }
    @media (min-width: 700px) { .cards { grid-template-columns: 1fr 1fr; } }
    .card { border: 1px solid var(--border); border-radius: var(--radius); padding: .85rem; display: grid; gap: .5rem; }
    .card.default { border-color: var(--primary); background: var(--primary-weak); }
    header { display: flex; justify-content: space-between; gap: .5rem; }
    .tag { font-size: .75rem; font-weight: 700; color: var(--primary-strong); }
    .addr { margin: 0; color: var(--text); font-size: .92rem; line-height: 1.5; }
    .actions { display: flex; gap: .35rem; flex-wrap: wrap; }
    .del { color: var(--danger); margin-left: auto; }
  `,
})
export class AddressesPage {
  private readonly api = inject(CheckoutService);
  private readonly toast = inject(ToastService);

  protected readonly addresses = rxResource({ stream: () => this.api.addresses() });
  private readonly options = rxResource({ stream: () => this.api.options() });
  protected readonly list = computed(() => safeValue(this.addresses) ?? []);
  protected readonly divisions = computed(() => safeValue(this.options)?.divisions ?? []);
  /** null = list view, 'new' = blank form, Address = editing that one. */
  protected readonly editing = signal<Address | 'new' | null>(null);

  constructor() {
    inject(SeoService).set({ title: 'Address book', noindex: true });
  }

  protected startNew(): void {
    this.editing.set('new');
  }

  protected onSaved(): void {
    this.editing.set(null);
    this.addresses.reload();
    this.toast.success('Address saved');
  }

  protected makeDefault(a: Address): void {
    this.api.setDefaultAddress(a.id).subscribe({ next: () => this.addresses.reload(), error: (e) => this.toast.error(errorMessage(e)) });
  }

  protected remove(a: Address): void {
    this.api.deleteAddress(a.id).subscribe({
      next: () => {
        this.addresses.reload();
        this.toast.success('Address removed');
      },
      error: (e) => this.toast.error(errorMessage(e)),
    });
  }
}
