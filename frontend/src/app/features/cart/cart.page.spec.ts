import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CartDto, CartItemDto } from '../../core/models/api.models';
import { CartService } from '../../core/services/cart.service';
import { provideTestHttp } from '../../core/testing/test-helpers';
import { CartPage } from './cart.page';

const line = (over: Partial<CartItemDto> = {}): CartItemDto => ({
  productId: 13, slug: 'ryzen', name: 'AMD Ryzen 5 5600', sku: 'TB-CPU-0005', imageUrl: null, listPrice: 12800, unitPrice: 11900, quantity: 2,
  stockStatus: 'InStock', lineTotal: 23800, purchasable: true, ...over,
});

describe('CartPage (guest)', () => {
  let http: HttpTestingController;
  let cart: CartService;

  const render = async (stored: Partial<CartItemDto>[]) => {
    localStorage.clear();
    localStorage.setItem('tb.cart.v1', JSON.stringify(stored.map((s) => line(s))));
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    cart = TestBed.inject(CartService);
    cart.init();
    const f = TestBed.createComponent(CartPage);
    f.detectChanges();
    TestBed.tick();
    await new Promise((r) => setTimeout(r, 0)); // afterNextRender -> refresh()
    TestBed.tick();
    return f;
  };
  const el = (f: { nativeElement: unknown }) => f.nativeElement as HTMLElement;
  const text = (f: { nativeElement: unknown }) => el(f).textContent!.replace(/\s+/g, ' ');

  afterEach(() => http.verify());

  it('shows an empty state with a call to action', async () => {
    const f = await render([]);
    expect(text(f)).toContain('Your cart is empty');
    expect(el(f).querySelector('a[href="/shop"]')).not.toBeNull();
    http.expectNone('/api/v1/cart/preview');
  });

  it('re-prices the guest cart from the server on load, and shows lines, totals and savings in ৳', async () => {
    const f = await render([{ quantity: 2 }]);
    const preview = http.expectOne('/api/v1/cart/preview');
    expect(preview.request.body).toEqual({ items: [{ productId: 13, quantity: 2 }] });
    const fresh: CartDto = { items: [line({ unitPrice: 11500, quantity: 2 })], itemCount: 2, subtotal: 23000, savings: 2600 };
    preview.flush(fresh);
    f.detectChanges();
    TestBed.tick();
    f.detectChanges();

    expect(text(f)).toContain('AMD Ryzen 5 5600');
    expect(el(f).querySelector('.total')!.textContent).toContain('৳23,000');
    expect(el(f).querySelector('.grand dd')!.textContent).toBe('৳23,000');
    expect(el(f).querySelector('.save dd')!.textContent).toBe('৳2,600');
    expect(text(f)).toContain('Sign in to checkout');
  });

  it('changes quantity and removes lines', async () => {
    const f = await render([{ quantity: 2 }]);
    http.expectOne('/api/v1/cart/preview').flush({ items: [line()], itemCount: 2, subtotal: 0, savings: 0 });
    f.detectChanges();

    el(f).querySelector<HTMLButtonElement>('button[aria-label="Increase quantity"]')!.click();
    f.detectChanges();
    expect(cart.quantityOf(13)).toBe(3);
    expect(el(f).querySelector('.total')!.textContent).toContain('৳35,700');

    el(f).querySelector<HTMLButtonElement>('button.rm')!.click();
    f.detectChanges();
    expect(text(f)).toContain('Your cart is empty');
    expect(JSON.parse(localStorage.getItem('tb.cart.v1')!)).toEqual([]);
  });

  it('flags unavailable lines, excludes them from the total, and labels the remove button per product', async () => {
    const f = await render([{ quantity: 1 }, { productId: 40, slug: 'gpu', name: 'RX 7800 XT', unitPrice: 78500, listPrice: 78500, stockStatus: 'OutOfStock' }]);
    http.expectOne('/api/v1/cart/preview').flush({ items: [line({ quantity: 1 }), line({ productId: 40, slug: 'gpu', name: 'RX 7800 XT', unitPrice: 78500, listPrice: 78500, stockStatus: 'OutOfStock', purchasable: false })], itemCount: 2, subtotal: 0, savings: 0 });
    f.detectChanges();

    expect(el(f).querySelector('.line.gone')!.textContent).toContain('No longer available');
    expect(el(f).querySelector('.grand dd')!.textContent).toBe('৳11,900'); // only the purchasable line is charged
    expect(el(f).querySelector('button.rm')!.getAttribute('aria-label')).toBe('Remove AMD Ryzen 5 5600 from cart');
  });
});
