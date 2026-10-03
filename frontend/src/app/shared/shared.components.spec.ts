import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpTestingController } from '@angular/common/http/testing';
import { CartService } from '../core/services/cart.service';
import { CompareService } from '../core/services/compare.service';
import { WishlistService } from '../core/services/wishlist.service';
import { productItem, provideTestHttp } from '../core/testing/test-helpers';
import { PaginationComponent } from './pagination.component';
import { PriceComponent } from './price.component';
import { ProductCardComponent } from './product-card.component';
import { QuantityInputComponent } from './quantity-input.component';
import { RatingComponent } from './rating.component';
import { StockBadgeComponent } from './stock-badge.component';

const text = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ').trim();
const q = <T extends Element = HTMLElement>(f: ComponentFixture<unknown>, sel: string) => (f.nativeElement as HTMLElement).querySelector<T>(sel);

describe('PriceComponent', () => {
  it('shows the sale price in ৳ with the regular price struck through', async () => {
    const f = TestBed.createComponent(PriceComponent);
    f.componentRef.setInput('regular', 125000);
    f.componentRef.setInput('current', 99500);
    await f.whenStable();
    expect(q(f, '.now')!.textContent).toBe('৳99,500');
    expect(q(f, 'del')!.textContent).toContain('৳1,25,000');
  });

  it('shows only one price when not discounted', async () => {
    const f = TestBed.createComponent(PriceComponent);
    f.componentRef.setInput('regular', 5000);
    f.componentRef.setInput('current', 5000);
    await f.whenStable();
    expect(q(f, 'del')).toBeNull();
  });
});

describe('StockBadgeComponent', () => {
  it.each([['InStock', 'In stock'], ['OutOfStock', 'Out of stock'], ['PreOrder', 'Pre-order'], ['UpComing', 'Up coming']] as const)('%s -> %s', async (status, label) => {
    const f = TestBed.createComponent(StockBadgeComponent);
    f.componentRef.setInput('status', status);
    await f.whenStable();
    expect(text(f)).toBe(label);
  });
});

describe('RatingComponent', () => {
  it('exposes the rating textually for screen readers', async () => {
    const f = TestBed.createComponent(RatingComponent);
    f.componentRef.setInput('value', 4.4);
    f.componentRef.setInput('count', 12);
    await f.whenStable();
    expect(q(f, '[role=img]')!.getAttribute('aria-label')).toBe('Rated 4.4 out of 5 from 12 reviews');
    expect(q(f, '.stars')!.querySelectorAll('.on')).toHaveLength(4);
  });

  it('says "No reviews yet" without a count', async () => {
    const f = TestBed.createComponent(RatingComponent);
    await f.whenStable();
    expect(q(f, '[role=img]')!.getAttribute('aria-label')).toBe('No reviews yet');
  });
});

describe('QuantityInputComponent', () => {
  @Component({ imports: [QuantityInputComponent], template: `<app-quantity-input [(value)]="qty" label="SSD" />` })
  class Host { qty = 1; }

  it('steps within 1..10 and disables the buttons at the bounds', async () => {
    const f = TestBed.createComponent(Host);
    await f.whenStable();
    const [dec, inc] = Array.from((f.nativeElement as HTMLElement).querySelectorAll('button'));
    expect(dec.disabled).toBe(true);
    inc.click();
    await f.whenStable();
    expect(f.componentInstance.qty).toBe(2);
    expect(q(f, 'input')!.getAttribute('aria-label')).toBe('Quantity');
    expect(q(f, '[role=group]')!.getAttribute('aria-label')).toBe('Quantity for SSD');
  });

  it('clamps typed values', async () => {
    const f = TestBed.createComponent(Host);
    await f.whenStable();
    const input = q<HTMLInputElement>(f, 'input')!;
    input.value = '99';
    input.dispatchEvent(new Event('change'));
    await f.whenStable();
    expect(f.componentInstance.qty).toBe(10);
    input.value = '-4';
    input.dispatchEvent(new Event('change'));
    await f.whenStable();
    expect(f.componentInstance.qty).toBe(1);
  });
});

describe('PaginationComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  const render = async (page: number, total: number, base = {}) => {
    const f = TestBed.createComponent(PaginationComponent);
    f.componentRef.setInput('page', page);
    f.componentRef.setInput('totalPages', total);
    f.componentRef.setInput('baseParams', base);
    await f.whenStable();
    return f;
  };

  it('renders nothing for a single page', async () => {
    expect(q(await render(1, 1), 'nav')).toBeNull();
  });

  it('marks the current page, links neighbours with rel=prev/next and keeps filters in the links', async () => {
    const f = await render(3, 10, { brand: ['intel'], sort: 'newest', page: 3 });
    expect(q(f, '[aria-current=page]')!.textContent).toContain('3');
    expect(q<HTMLAnchorElement>(f, 'a[rel=prev]')!.getAttribute('href')).toBe('/?brand=intel&sort=newest&page=2');
    expect(q<HTMLAnchorElement>(f, 'a[rel=next]')!.getAttribute('href')).toBe('/?brand=intel&sort=newest&page=4');
  });

  it('page 1 links drop the page param (canonical first page)', async () => {
    const f = await render(2, 5, { page: 2 });
    expect(q<HTMLAnchorElement>(f, 'a[rel=prev]')!.getAttribute('href')).toBe('/');
  });

  it('has a labelled navigation landmark and no prev link on the first page', async () => {
    const f = await render(1, 5);
    expect(q(f, 'nav')!.getAttribute('aria-label')).toBe('Pagination');
    expect(q(f, 'a[rel=prev]')).toBeNull();
  });
});

describe('ProductCardComponent', () => {
  let cart: CartService;
  let wishlist: WishlistService;
  let compare: CompareService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    cart = TestBed.inject(CartService);
    wishlist = TestBed.inject(WishlistService);
    compare = TestBed.inject(CompareService);
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const render = async (over = {}) => {
    const f = TestBed.createComponent(ProductCardComponent);
    f.componentRef.setInput('p', productItem(over));
    await f.whenStable();
    return f;
  };

  it('shows name (linked), brand, features, ৳ prices, discount and stock', async () => {
    const f = await render();
    expect(q<HTMLAnchorElement>(f, '.title a')!.getAttribute('href')).toBe('/product/amd-ryzen-5-5600-processor');
    expect(q(f, '.title')!.textContent).toContain('AMD Ryzen 5 5600');
    expect(q(f, '.now')!.textContent).toBe('৳11,900');
    expect(q(f, 'del')!.textContent).toContain('৳12,800');
    expect(q(f, '.deal')!.textContent).toContain('-7%');
    expect(q(f, 'app-stock-badge')!.textContent).toContain('In stock');
    expect(q<HTMLAnchorElement>(f, '.brand')!.getAttribute('href')).toBe('/shop?brand=amd');
    expect(f.nativeElement.querySelectorAll('.features li')).toHaveLength(2);
  });

  it('uses a decorative empty alt for the image (the title link already names the product)', async () => {
    const f = await render({ imageUrl: null });
    const img = q<HTMLImageElement>(f, 'img')!;
    expect(img.getAttribute('alt')).toBe('');
    expect(img.getAttribute('src')).toBe('/images/placeholder.svg');
  });

  it('adds to the cart with the effective price', async () => {
    const spy = vi.spyOn(cart, 'add');
    const f = await render();
    q<HTMLButtonElement>(f, '.add')!.click();
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ id: 13, effectivePrice: 11900, price: 12800, slug: 'amd-ryzen-5-5600-processor' }));
    expect(cart.count()).toBe(1);
  });

  it('disables "Add to cart" when the product cannot be bought', async () => {
    const f = await render({ stockStatus: 'OutOfStock' });
    const btn = q<HTMLButtonElement>(f, '.add')!;
    expect(btn.disabled).toBe(true);
    expect(btn.textContent).toContain('Unavailable');
  });

  it('wishlist and compare toggles expose their state with aria-pressed and a product-specific label', async () => {
    const f = await render();
    const [heart, cmp] = Array.from((f.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.tool'));
    expect(heart.getAttribute('aria-pressed')).toBe('false');
    expect(heart.getAttribute('aria-label')).toBe('Add to wishlist: AMD Ryzen 5 5600 Processor');
    heart.click();
    cmp.click();
    await f.whenStable();
    expect(wishlist.has(13)).toBe(true);
    expect(compare.has('amd-ryzen-5-5600-processor')).toBe(true);
    expect(heart.getAttribute('aria-pressed')).toBe('true');
    expect(heart.getAttribute('aria-label')).toContain('Remove from wishlist');
    expect(cmp.getAttribute('aria-pressed')).toBe('true');
  });
});
