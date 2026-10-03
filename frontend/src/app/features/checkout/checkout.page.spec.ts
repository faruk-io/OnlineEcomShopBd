import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { CartItemDto } from '../../core/models/api.models';
import { CartService } from '../../core/services/cart.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { Redirector } from '../../core/services/redirector.service';
import { ADDRESS, OPTIONS, orderOf, quoteOf } from '../../core/testing/order-fixtures';
import { USER, authResponse, provideTestHttp } from '../../core/testing/test-helpers';
import { CheckoutPage, homeMethodFor } from './checkout.page';

const cartLine: CartItemDto = {
  productId: 13, slug: 'ryzen', name: 'AMD Ryzen 5 5600', sku: 'TB-CPU-0005', imageUrl: null, listPrice: 12800, unitPrice: 11900, quantity: 2,
  stockStatus: 'InStock', lineTotal: 23800, purchasable: true,
};

describe('homeMethodFor', () => {
  it('prices by the address district, not by what was clicked', () => {
    expect(homeMethodFor({ district: 'Dhaka' })).toBe('HomeDeliveryInsideDhaka');
    expect(homeMethodFor({ district: ' dhaka ' })).toBe('HomeDeliveryInsideDhaka');
    expect(homeMethodFor({ district: 'Gazipur' })).toBe('HomeDeliveryOutsideDhaka');
    expect(homeMethodFor(null)).toBe('HomeDeliveryOutsideDhaka');
  });
});

describe('CheckoutPage', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<CheckoutPage>;
  const el = () => f.nativeElement as HTMLElement;
  const text = () => el().textContent!.replace(/\s+/g, ' ');
  const settle = async () => {
    f.detectChanges();
    TestBed.tick();
    await new Promise((r) => setTimeout(r, 0));
    TestBed.tick();
    f.detectChanges();
  };

  /** Renders with a guest-stored cart; the page loads options + addresses, re-prices the cart and asks for a quote. */
  async function render(addresses = [ADDRESS], quote = quoteOf(), opts: { requireVerified?: boolean; unverified?: boolean } = {}) {
    localStorage.clear();
    localStorage.setItem('tb.cart.v1', JSON.stringify([cartLine]));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(CartService).init();
    // A signed-in user also makes CartService merge / fetch the server cart; answer those with the same cart.
    const serverCart = () =>
      ['/api/v1/cart/merge', '/api/v1/cart'].forEach((u) => http.match(u).forEach((r) => r.flush({ items: [cartLine], itemCount: 2, subtotal: 23800, savings: 1800 })));
    if (opts.unverified !== undefined) {
      TestBed.inject(AuthService).login('rahim@example.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1, { ...USER, emailConfirmed: !opts.unverified }));
      await settle();
      serverCart();
    }
    f = TestBed.createComponent(CheckoutPage);
    await settle();
    serverCart();
    http.expectOne('/api/v1/checkout/options').flush({ ...OPTIONS, requireVerifiedEmail: opts.requireVerified ?? false });
    http.expectOne('/api/v1/addresses').flush(addresses);
    http.match('/api/v1/cart/preview').forEach((r) => r.flush({ items: [cartLine], itemCount: 2, subtotal: 23800, savings: 1800 }));
    serverCart();
    await settle();
    if (addresses.length) http.expectOne('/api/v1/checkout/quote').flush(quote);
    await settle();
  }
  const click = async (sel: string) => {
    (el().querySelector(sel) as HTMLElement).click();
    await settle();
  };

  afterEach(() => http.verify());

  it('pre-selects the default address, asks the server for a quote and renders its totals', async () => {
    await render();
    expect((el().querySelector('input[name=address]') as HTMLInputElement).checked).toBe(true);
    expect(text()).toContain('Home delivery');
    expect(el().querySelector('.grand dd')!.textContent).toBe('৳23,870');
    expect(text()).toContain('Delivery৳70');
    expect((el().querySelector('.summary .btn-primary') as HTMLButtonElement).disabled).toBe(false);
  });

  it('sends the quote request with only choices (never an amount)', async () => {
    localStorage.clear();
    localStorage.setItem('tb.cart.v1', JSON.stringify([cartLine]));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(CartService).init();
    f = TestBed.createComponent(CheckoutPage);
    await settle();
    http.expectOne('/api/v1/checkout/options').flush(OPTIONS);
    http.expectOne('/api/v1/addresses').flush([ADDRESS]);
    http.match('/api/v1/cart/preview').forEach((r) => r.flush({ items: [cartLine], itemCount: 2, subtotal: 23800, savings: 0 }));
    await settle();
    const q = http.expectOne('/api/v1/checkout/quote');
    expect(q.request.body).toEqual({ shippingMethod: 'HomeDeliveryInsideDhaka', addressId: 7, couponCode: null });
    q.flush(quoteOf());
    await settle();
  });

  it('applies a coupon through the server quote and shows the discount, or the server’s reason when rejected', async () => {
    await render();
    const input = el().querySelector('#coupon') as HTMLInputElement;
    input.value = 'WELCOME10';
    input.dispatchEvent(new Event('input'));
    await settle();
    await click('.coupon button[type=submit]');
    const req = http.expectOne('/api/v1/checkout/quote');
    expect(req.request.body.couponCode).toBe('WELCOME10');
    req.flush(quoteOf({ discount: 2380, grandTotal: 21490, couponCode: 'WELCOME10', couponApplied: true }));
    await settle();
    expect(text()).toContain('Coupon WELCOME10 applied');
    expect(el().querySelector('.grand dd')!.textContent).toBe('৳21,490');

    input.value = 'NOPE';
    input.dispatchEvent(new Event('input'));
    await settle();
    await click('.coupon button[type=submit]');
    http.expectOne('/api/v1/checkout/quote').flush(quoteOf({ couponCode: null, couponApplied: false, couponMessage: 'This coupon is not valid.' }));
    await settle();
    expect(el().querySelector('.error-text[role=alert]')!.textContent).toContain('This coupon is not valid.');
  });

  it('places a cash-on-delivery order with choices only and goes to the tracking page', async () => {
    await render();
    const nav = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await click('.summary .btn-primary');
    const req = http.expectOne('/api/v1/orders');
    expect(req.request.body).toEqual({
      shippingMethod: 'HomeDeliveryInsideDhaka', addressId: 7, contactName: null, contactPhone: null, paymentMethod: 'CashOnDelivery', couponCode: null, note: null,
    });
    req.flush({ order: orderOf(), payment: null });
    await settle();
    http.match('/api/v1/cart/preview').forEach((r) => r.flush({ items: [], itemCount: 0, subtotal: 0, savings: 0 }));
    expect(nav).toHaveBeenCalledWith(['/account/orders', 'TB-261001-AB12'], { queryParams: { placed: 1 } });
  });

  it('hands over to the hosted payment page for online payments', async () => {
    await render();
    const redirect = vi.spyOn(TestBed.inject(Redirector), 'to').mockImplementation(() => undefined);
    await click('input[name=payment][value=Online]');
    await click('.summary .btn-primary');
    const req = http.expectOne('/api/v1/orders');
    expect(req.request.body.paymentMethod).toBe('Online');
    req.flush({ order: orderOf(), payment: { redirectUrl: 'https://sandbox.example/pay/1', error: null } });
    await settle();
    http.match('/api/v1/cart/preview').forEach((r) => r.flush({ items: [], itemCount: 0, subtotal: 0, savings: 0 }));
    expect(redirect).toHaveBeenCalledWith('https://sandbox.example/pay/1');
  });

  it('shows the server’s reason when placing fails and re-quotes (stock may have changed)', async () => {
    await render();
    await click('.summary .btn-primary');
    http.expectOne('/api/v1/orders').flush({ status: 409, title: 'Conflict', detail: 'Only 1 left of AMD Ryzen 5 5600.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(el().querySelector('.alert-error')!.textContent).toContain('Only 1 left');
    http.expectOne('/api/v1/checkout/quote').flush(quoteOf({ canPlaceOrder: false, problems: ['Only 1 left of AMD Ryzen 5 5600.'] }));
    await settle();
    expect((el().querySelector('.summary .btn-primary') as HTMLButtonElement).disabled).toBe(true);
  });

  it('pickup needs a collector name and a valid phone, needs no address and is free', async () => {
    await render();
    await click('input[name=mode][value=pickup]');
    const q = http.expectOne('/api/v1/checkout/quote');
    expect(q.request.body).toEqual({ shippingMethod: 'StorePickup', addressId: null, couponCode: null });
    q.flush(quoteOf({ shippingMethod: 'StorePickup', shippingFee: 0, grandTotal: 23800 }));
    await settle();
    expect(text()).toContain('Who is collecting?');
    expect((el().querySelector('.summary .btn-primary') as HTMLButtonElement).disabled).toBe(true);

    const name = el().querySelector('#cname') as HTMLInputElement;
    const phone = el().querySelector('#cphone') as HTMLInputElement;
    name.value = 'Karim'; name.dispatchEvent(new Event('input'));
    phone.value = '123'; phone.dispatchEvent(new Event('input'));
    await settle();
    expect((el().querySelector('.summary .btn-primary') as HTMLButtonElement).disabled).toBe(true);
    phone.value = '01812345678'; phone.dispatchEvent(new Event('input'));
    await settle();
    expect((el().querySelector('.summary .btn-primary') as HTMLButtonElement).disabled).toBe(false);
  });

  it('opens the address form when the customer has no saved address and waits for one before quoting', async () => {
    await render([]);
    expect(el().querySelector('app-address-form')).not.toBeNull();
    http.expectNone('/api/v1/checkout/quote');
    expect((el().querySelector('.summary .btn-primary') as HTMLButtonElement).disabled).toBe(true);
    expect(text()).toContain('Choose a delivery address');
  });

  describe('email verification gate', () => {
    const placeBtn = () => el().querySelector('.summary .btn-primary') as HTMLButtonElement;

    it('blocks "Place order" and explains why when the store requires a verified email and the user is unverified', async () => {
      await render([ADDRESS], quoteOf(), { requireVerified: true, unverified: true });
      expect(placeBtn().disabled).toBe(true);
      expect(el().querySelector('app-verify-email-banner .blocking')).not.toBeNull();
      expect(el().querySelector('#verify-why')!.textContent).toContain('Verify your email');
      expect(placeBtn().getAttribute('aria-describedby')).toBe('verify-why');
    });

    it('does not block when the flag is off, or when the user is verified', async () => {
      await render([ADDRESS], quoteOf(), { requireVerified: false, unverified: true });
      expect(placeBtn().disabled).toBe(false);
      expect(el().querySelector('#verify-why')).toBeNull();
      expect(el().querySelector('app-verify-email-banner .banner')).not.toBeNull();   // still nudges, but does not block
      expect(el().querySelector('app-verify-email-banner .blocking')).toBeNull();
    });

    it('does not block a verified user even when the store requires verification', async () => {
      await render([ADDRESS], quoteOf(), { requireVerified: true, unverified: false });
      expect(placeBtn().disabled).toBe(false);
      expect(el().querySelector('app-verify-email-banner .banner')).toBeNull();
    });

    it('turns a 403 email_not_verified answer into the verification prompt, not a generic error', async () => {
      await render([ADDRESS], quoteOf(), { requireVerified: false, unverified: true });
      await click('.summary .btn-primary');
      http.expectOne('/api/v1/orders').flush(
        { status: 403, title: 'Email not verified', code: 'email_not_verified' }, { status: 403, statusText: 'Forbidden' },
      );
      await settle();
      http.expectOne('/api/v1/auth/me').flush({ ...USER, emailConfirmed: false });
      await settle();
      expect(el().querySelector('.alert-error')!.textContent).toContain('verify your email');
      expect(placeBtn().disabled).toBe(true);
      expect(el().querySelector('app-verify-email-banner .blocking')).not.toBeNull();
      expect(TestBed.inject(ToastService).toasts().filter((t) => t.kind === 'error')).toEqual([]);   // no "no permission" toast
      http.expectNone('/api/v1/checkout/quote');
    });
  });
});
