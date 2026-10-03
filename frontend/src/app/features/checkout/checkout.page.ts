import { ChangeDetectionStrategy, Component, afterNextRender, computed, effect, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorMessage, isApiError } from '../../core/interceptors/error.interceptor';
import { Address, CheckoutQuote, PaymentMethod, ShippingMethod } from '../../core/models/api.models';
import { AuthService } from '../../core/services/auth.service';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { Redirector } from '../../core/services/redirector.service';
import { SeoService } from '../../core/services/seo.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { safeValue } from '../../core/util/resource';
import { BD_PHONE } from '../auth/auth-forms';
import { AddressFormComponent } from '../../shared/address-form.component';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { VerifyEmailBannerComponent } from '../../shared/verify-email-banner.component';

/** Does this district get the inside-Dhaka rate? Display hint only: the server re-derives the zone from the saved address. */
export const isInsideDhaka = (district: string | null | undefined): boolean => (district ?? '').trim().toLowerCase() === 'dhaka';

/** Pure: the home-delivery method that applies to an address (the server enforces the same rule). */
export const homeMethodFor = (address: Pick<Address, 'district'> | null | undefined): ShippingMethod =>
  isInsideDhaka(address?.district) ? 'HomeDeliveryInsideDhaka' : 'HomeDeliveryOutsideDhaka';

@Component({
  selector: 'app-checkout',
  imports: [RouterLink, FormsModule, BdtPipe, BreadcrumbComponent, AddressFormComponent, VerifyEmailBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './checkout.page.html',
  styleUrl: './checkout.page.scss',
})
export class CheckoutPage {
  protected readonly cart = inject(CartService);
  protected readonly auth = inject(AuthService);
  private readonly api = inject(CheckoutService);
  private readonly router = inject(Router);
  private readonly redirector = inject(Redirector);

  protected readonly options = rxResource({ stream: () => this.api.options() });
  protected readonly addresses = rxResource({ stream: () => this.api.addresses() });

  protected readonly mode = signal<'home' | 'pickup'>('home');
  protected readonly addressId = signal<number | null>(null);
  protected readonly addingAddress = signal(false);
  protected readonly payment = signal<PaymentMethod>('CashOnDelivery');
  protected readonly couponInput = signal('');
  /** The code last sent for pricing; the quote tells us whether the server accepted it. */
  protected readonly couponCode = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly contactName = signal('');
  protected readonly contactPhone = signal('');
  protected readonly busy = signal(false);
  protected readonly formError = signal('');
  protected readonly submitted = signal(false);

  protected readonly addressList = computed(() => safeValue(this.addresses) ?? []);
  protected readonly selectedAddress = computed(() => this.addressList().find((a) => a.id === this.addressId()) ?? null);
  protected readonly divisions = computed(() => safeValue(this.options)?.divisions ?? []);
  protected readonly shippingMethod = computed<ShippingMethod>(() => (this.mode() === 'pickup' ? 'StorePickup' : homeMethodFor(this.selectedAddress())));

  /** Everything the quote depends on; `undefined` keeps the resource idle until the user has chosen enough. */
  private readonly quoteParams = computed(() => {
    if (this.cart.lines().length === 0) return undefined;
    if (this.mode() === 'home' && this.addressId() === null) return undefined;
    return { shippingMethod: this.shippingMethod(), addressId: this.mode() === 'home' ? this.addressId() : null, couponCode: this.couponCode() };
  });
  protected readonly quote = rxResource({
    params: () => this.quoteParams(),
    stream: ({ params }) => this.api.quote(params),
  });
  protected readonly q = computed<CheckoutQuote | undefined>(() => safeValue(this.quote));

  protected readonly homeOption = computed(() => this.options.hasValue() ? this.options.value()!.shipping.find((s) => s.method === homeMethodFor(this.selectedAddress())) : undefined);
  protected readonly pickupOption = computed(() => safeValue(this.options)?.shipping.find((s) => s.method === 'StorePickup'));
  protected readonly store = computed(() => safeValue(this.options)?.store);

  protected readonly pickupValid = computed(() => this.contactName().trim().length > 0 && BD_PHONE.test(this.contactPhone().trim()));
  /** The store only lets verified customers order (flag from /checkout/options) or the server just refused with `email_not_verified`. */
  private readonly serverSaidUnverified = signal(false);
  protected readonly needsVerification = computed(
    () => this.auth.user()?.emailConfirmed === false && (safeValue(this.options)?.requireVerifiedEmail === true || this.serverSaidUnverified()),
  );
  protected readonly canPlace = computed(() => {
    if (this.needsVerification()) return false;
    const q = this.q();
    if (!q || !q.canPlaceOrder || this.busy() || this.quote.isLoading()) return false;
    return this.mode() === 'pickup' ? this.pickupValid() : this.addressId() !== null;
  });

  constructor() {
    inject(SeoService).set({ title: 'Checkout', noindex: true });
    afterNextRender(() => this.cart.refresh());
    // Pre-select the default (or only) address once, and open the form when there are none.
    effect(() => {
      if (!this.addresses.hasValue()) return;
      const list = this.addressList();
      if (list.length === 0) this.addingAddress.set(true);
      else if (this.addressId() === null) this.addressId.set((list.find((a) => a.isDefault) ?? list[0]).id);
    });
    // Online payment may be switched off server-side: fall back to cash on delivery.
    effect(() => {
      const online = safeValue(this.options)?.payment.find((p) => p.method === 'Online');
      if (online && !online.enabled && this.payment() === 'Online') this.payment.set('CashOnDelivery');
    });
  }

  protected onAddressSaved(a: Address): void {
    this.addingAddress.set(false);
    this.addresses.reload();
    this.addressId.set(a.id);
  }

  protected applyCoupon(): void {
    const code = this.couponInput().trim();
    this.couponCode.set(code || null);
  }

  protected removeCoupon(): void {
    this.couponInput.set('');
    this.couponCode.set(null);
  }

  protected place(): void {
    this.submitted.set(true);
    if (!this.canPlace()) return;
    this.busy.set(true);
    this.formError.set('');
    const pickup = this.mode() === 'pickup';
    this.api
      .place({
        shippingMethod: this.shippingMethod(),
        addressId: pickup ? null : this.addressId(),
        contactName: pickup ? this.contactName().trim() : null,
        contactPhone: pickup ? this.contactPhone().trim() : null,
        paymentMethod: this.payment(),
        couponCode: this.q()?.couponApplied ? this.couponCode() : null,
        note: this.note().trim() || null,
      })
      .subscribe({
        next: (res) => {
          this.cart.refresh();
          const number = res.order.orderNumber;
          if (res.payment?.redirectUrl) {
            // Hand over to the hosted gateway page; it returns the customer to /account/orders/<number>?payment=…
            this.redirector.to(res.payment.redirectUrl);
            return;
          }
          this.busy.set(false);
          void this.router.navigate(['/account/orders', number], { queryParams: res.payment?.error ? { placed: 1, payment: 'failed' } : { placed: 1 } });
        },
        error: (e) => {
          this.busy.set(false);
          if (isApiError(e) && e.status === 403 && e.code === 'email_not_verified') {
            // Not a generic failure: show the verification prompt instead (refresh the profile in case our copy was stale).
            this.serverSaidUnverified.set(true);
            this.formError.set('Please verify your email address before placing your order.');
            this.auth.reloadUser().subscribe({ error: () => undefined });
            return;
          }
          this.formError.set(errorMessage(e));
          this.quote.reload(); // stock or price may have changed
        },
      });
  }
}
