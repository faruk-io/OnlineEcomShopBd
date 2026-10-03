import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { CartDto, CartItemDto } from '../models/api.models';
import { authResponse, provideTestHttp } from '../testing/test-helpers';
import { AuthService } from './auth.service';
import { CartService, CartableProduct } from './cart.service';
import { ToastService } from './toast.service';

const ryzen: CartableProduct = { id: 13, slug: 'ryzen-5-5600', name: 'AMD Ryzen 5 5600', sku: 'TB-CPU-0005', imageUrl: '/i.svg', price: 12800, effectivePrice: 11900, stockStatus: 'InStock' };
const gpu: CartableProduct = { id: 40, slug: 'rx-7800-xt', name: 'RX 7800 XT', sku: 'TB-GPU-0006', imageUrl: null, price: 78500, effectivePrice: 78500, stockStatus: 'OutOfStock' };

const item = (p: CartableProduct, quantity: number, stockStatus = p.stockStatus): CartItemDto => ({
  productId: p.id, slug: p.slug, name: p.name, sku: p.sku, imageUrl: p.imageUrl, listPrice: p.price, unitPrice: p.effectivePrice, quantity,
  stockStatus, lineTotal: p.effectivePrice * quantity, purchasable: stockStatus === 'InStock',
});
const cart = (...items: CartItemDto[]): CartDto => ({ items, itemCount: 0, subtotal: 0, savings: 0 });

describe('CartService', () => {
  let svc: CartService;
  let http: HttpTestingController;
  let toast: ToastService;
  let auth: AuthService;

  const setup = () => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    svc = TestBed.inject(CartService);
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
    auth = TestBed.inject(AuthService);
    TestBed.tick(); // let the sign-in/out reaction subscribe
  };
  const signIn = () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    TestBed.tick();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  describe('as a guest', () => {
    beforeEach(setup);

    it('adds items, accumulates quantity and persists to localStorage', () => {
      svc.add(ryzen);
      svc.add(ryzen, 2);
      expect(svc.lines()).toHaveLength(1);
      expect(svc.quantityOf(13)).toBe(3);
      expect(svc.count()).toBe(3);
      expect(svc.subtotal()).toBe(3 * 11900);
      expect(svc.savings()).toBe(3 * 900);
      expect(JSON.parse(localStorage.getItem('tb.cart.v1')!)).toHaveLength(1);
      http.expectNone(() => true); // guests never hit the server for basic edits
    });

    it('caps the quantity per line at 10', () => {
      svc.add(ryzen, 8);
      svc.add(ryzen, 8);
      expect(svc.quantityOf(13)).toBe(10);
      svc.setQuantity(13, 99);
      expect(svc.quantityOf(13)).toBe(10);
      svc.setQuantity(13, 0);
      expect(svc.quantityOf(13)).toBe(1);
    });

    it('refuses unavailable products with a message', () => {
      svc.add(gpu);
      expect(svc.lines()).toHaveLength(0);
      expect(toast.toasts()[0].message).toMatch(/unavailable/);
    });

    it('removes and clears', () => {
      svc.add(ryzen);
      svc.remove(13);
      expect(svc.lines()).toEqual([]);
      svc.add(ryzen);
      svc.clear();
      expect(JSON.parse(localStorage.getItem('tb.cart.v1')!)).toEqual([]);
    });

    it('init() restores the stored guest cart (done after hydration)', () => {
      svc.add(ryzen, 2);
      TestBed.resetTestingModule();
      setup();
      expect(svc.lines()).toHaveLength(0); // nothing until init(): keeps the first client render identical to SSR
      svc.init();
      expect(svc.quantityOf(13)).toBe(2);
    });

    it('excludes lines that became unavailable from the payable subtotal', () => {
      localStorage.setItem('tb.cart.v1', JSON.stringify([
        { ...item(ryzen, 1) }, { ...item(gpu, 1, 'OutOfStock') },
      ]));
      svc.init();
      expect(svc.count()).toBe(2);
      expect(svc.subtotal()).toBe(11900);
    });

    it('refresh() re-prices the guest cart from the anonymous preview endpoint', () => {
      svc.add(ryzen, 2);
      svc.refresh();
      const req = http.expectOne('/api/v1/cart/preview');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ items: [{ productId: 13, quantity: 2 }] });
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush(cart(item({ ...ryzen, effectivePrice: 11500 }, 2)));
      expect(svc.lines()[0].unitPrice).toBe(11500);
      expect(JSON.parse(localStorage.getItem('tb.cart.v1')!)[0].unitPrice).toBe(11500);
    });

    it('keeps the local cart when the preview call fails', () => {
      svc.add(ryzen);
      svc.refresh();
      http.expectOne('/api/v1/cart/preview').flush({}, { status: 500, statusText: 'x' });
      expect(svc.quantityOf(13)).toBe(1);
    });
  });

  describe('signing in', () => {
    beforeEach(setup);

    it('merges the guest cart into the server cart, then drops the local copy', () => {
      svc.add(ryzen, 2);
      signIn();

      const merge = http.expectOne('/api/v1/cart/merge');
      expect(merge.request.body).toEqual({ items: [{ productId: 13, quantity: 2 }] });
      expect(merge.request.headers.get('Authorization')).toBe('Bearer access-1');
      merge.flush(cart(item(ryzen, 5))); // server already had 3 -> quantities added

      expect(svc.quantityOf(13)).toBe(5);
      expect(localStorage.getItem('tb.cart.v1')).toBeNull();
      expect(toast.toasts().some((t) => /saved to your account/.test(t.message))).toBe(true);
    });

    it('loads the server cart when there is no guest cart', () => {
      signIn();
      http.expectOne('/api/v1/cart').flush(cart(item(ryzen, 1)));
      expect(svc.quantityOf(13)).toBe(1);
    });

    it('keeps the guest cart if the merge fails, so nothing is lost', () => {
      svc.add(ryzen);
      signIn();
      http.expectOne('/api/v1/cart/merge').flush({}, { status: 500, statusText: 'x' });
      expect(localStorage.getItem('tb.cart.v1')).not.toBeNull();
      expect(svc.quantityOf(13)).toBe(1);
    });

    it('uses the server for edits once signed in', () => {
      signIn();
      http.expectOne('/api/v1/cart').flush(cart());

      svc.add(ryzen, 2);
      const put = http.expectOne('/api/v1/cart/items/13');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ quantity: 2 });
      put.flush(cart(item(ryzen, 2)));
      expect(svc.quantityOf(13)).toBe(2);

      svc.remove(13);
      const del = http.expectOne('/api/v1/cart/items/13');
      expect(del.request.method).toBe('DELETE');
      del.flush(cart());
      expect(svc.lines()).toEqual([]);

      svc.clear();
      expect(http.expectOne('/api/v1/cart').request.method).toBe('DELETE');
    });

    it('shows the API message when the server refuses a line (e.g. out of stock)', () => {
      signIn();
      http.expectOne('/api/v1/cart').flush(cart());
      svc.add({ ...ryzen, stockStatus: 'InStock' });
      http.expectOne('/api/v1/cart/items/13').flush({ status: 409, title: 'Conflict.', detail: 'Ryzen is currently not available for purchase.' }, { status: 409, statusText: 'Conflict' });
      expect(toast.toasts().at(-1)?.message).toBe('Ryzen is currently not available for purchase.');
    });
  });

  it('signing out empties the in-memory cart (no leak between accounts)', () => {
    setup();
    signIn();
    http.expectOne('/api/v1/cart').flush(cart(item(ryzen, 3)));
    expect(svc.count()).toBe(3);

    auth.clearSession();
    TestBed.tick();
    expect(svc.lines()).toEqual([]);
    expect(localStorage.getItem('tb.cart.v1')).toBeNull();
  });
});
