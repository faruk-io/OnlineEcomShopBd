import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Title } from '@angular/platform-browser';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CartService } from '../../core/services/cart.service';
import { productDetail, productItem, provideTestHttp } from '../../core/testing/test-helpers';
import { ProductDetailPage } from './product-detail.page';

describe('ProductDetailPage', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;

  const settle = async () => {
    for (let i = 0; i < 3; i++) {
      TestBed.tick();
      harness.detectChanges();
      await new Promise((r) => setTimeout(r, 0));
    }
  };
  const root = () => harness.routeNativeElement as HTMLElement;

  const open = async (slug = 'amd-ryzen-5-5600-processor') => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'product/:slug', component: ProductDetailPage }, { path: 'cart', children: [] }], withComponentInputBinding()), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/product/${slug}`);
    TestBed.tick();
  };
  afterEach(() => http.verify());

  const load = async (over = {}) => {
    http.expectOne('/api/v1/products/amd-ryzen-5-5600-processor').flush(productDetail(over));
    http.expectOne((r) => r.url.endsWith('/related')).flush([productItem({ id: 20, name: 'Related CPU', slug: 'related' })]);
    await settle();
  };

  it('renders the buy box: h1, brand, SKU, ৳ price with savings, stock, warranty and key features', async () => {
    await open();
    await load();
    expect(root().querySelector('h1')!.textContent).toBe('AMD Ryzen 5 5600 Processor');
    expect(root().querySelector('.brandline')!.textContent).toContain('SKU: TB-CPU-0005');
    expect(root().querySelector('.price-box .now')!.textContent).toBe('৳11,900');
    expect(root().querySelector('.price-box del')!.textContent).toContain('৳12,800');
    expect(root().querySelector('.save')!.textContent).toContain('You save ৳900 (7% off)');
    expect(root().querySelector('.facts')!.textContent).toContain('In stock');
    expect(root().querySelector('.facts')!.textContent).toContain('3 years');
    expect(Array.from(root().querySelectorAll('.kf li')).map((l) => l.textContent!.trim())).toEqual(['6 cores / 12 threads', 'AM4 platform, DDR4']);
  });

  it('shows the gallery image with alt text, and breadcrumbs', async () => {
    await open();
    await load();
    const img = root().querySelector<HTMLImageElement>('.stage img')!;
    expect(img.getAttribute('alt')).toBe('AMD Ryzen 5 5600');
    expect(Array.from(root().querySelectorAll('app-breadcrumb li')).map((l) => l.textContent!.trim())).toEqual(['Home', 'Component', 'Processor', 'AMD Ryzen 5 5600 Processor']);
  });

  it('renders one accessible table per spec group (caption + row headers)', async () => {
    await open();
    await load();
    const tables = root().querySelectorAll('table.spec');
    expect(tables).toHaveLength(2);
    expect(tables[0].querySelector('caption')!.textContent).toBe('General');
    expect(tables[0].querySelector('th[scope=row]')!.textContent).toBe('Socket');
    expect(tables[0].querySelector('td')!.textContent).toBe('AM4');
  });

  it('adds the chosen quantity to the cart; Buy now adds and goes to /cart', async () => {
    await open();
    await load();
    const cart = TestBed.inject(CartService);
    root().querySelector<HTMLButtonElement>('app-quantity-input button[aria-label="Increase quantity"]')!.click();
    await settle();
    Array.from(root().querySelectorAll<HTMLButtonElement>('.buy .btn')).find((b) => b.textContent!.includes('Add to cart'))!.click();
    expect(cart.quantityOf(13)).toBe(2);

    Array.from(root().querySelectorAll<HTMLButtonElement>('.buy .btn')).find((b) => b.textContent!.includes('Buy now'))!.click();
    await settle();
    expect(cart.quantityOf(13)).toBe(4);
    expect(router.url).toBe('/cart');
  });

  it('cannot buy an unavailable product', async () => {
    await open();
    await load({ stockStatus: 'OutOfStock' });
    const buttons = Array.from(root().querySelectorAll<HTMLButtonElement>('.buy .btn'));
    expect(buttons.every((b) => b.disabled)).toBe(true);
    expect(root().querySelector('.buy ~ .muted')!.textContent).toContain('can’t be ordered');
  });

  it('emits SEO title, description, canonical, og:type and Product JSON-LD with BDT pricing', async () => {
    await open();
    await load();
    const doc = TestBed.inject(DOCUMENT);
    expect(TestBed.inject(Title).getTitle()).toBe('AMD Ryzen 5 5600 Processor Price in Bangladesh | TechBazar BD');
    expect(doc.head.querySelector('meta[name=description]')!.getAttribute('content')).toContain('৳11,900');
    expect(doc.head.querySelector('meta[property="og:type"]')!.getAttribute('content')).toBe('product');
    expect(doc.head.querySelector('link[rel=canonical]')!.getAttribute('href')).toMatch(/\/product\/amd-ryzen-5-5600-processor$/);
    const ld = JSON.parse(doc.getElementById('ld-json-page')!.textContent!);
    expect(ld['@type']).toBe('Product');
    expect(ld.offers).toMatchObject({ priceCurrency: 'BDT', price: 11900, availability: 'https://schema.org/InStock' });
    expect(ld.aggregateRating).toBeUndefined(); // no reviews -> no fake rating
  });

  it('includes aggregateRating only when there are reviews, and lists them', async () => {
    await open();
    await load({
      ratingAverage: 4.5, reviewCount: 2,
      reviews: [{ reviewerName: 'Karim', rating: 5, title: 'Great', comment: 'Fast and cool.', createdAt: '2026-09-01T00:00:00Z' }],
    });
    const ld = JSON.parse(TestBed.inject(DOCUMENT).getElementById('ld-json-page')!.textContent!);
    expect(ld.aggregateRating).toEqual({ '@type': 'AggregateRating', ratingValue: 4.5, reviewCount: 2 });
    expect(root().querySelector('.reviews')!.textContent).toContain('Fast and cool.');
  });

  it('shows related products', async () => {
    await open();
    await load();
    expect(root().querySelectorAll('app-product-card')).toHaveLength(1);
    expect(root().textContent).toContain('Related CPU');
  });

  it('renders a 404 page for an unknown product', async () => {
    await open('nope');
    http.expectOne('/api/v1/products/nope').flush({ status: 404, title: 'Resource not found.' }, { status: 404, statusText: 'Not Found' });
    http.expectOne((r) => r.url.endsWith('/related')).flush({}, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(root().querySelector('h1')!.textContent).toBe('Product not found');
    expect(TestBed.inject(DOCUMENT).head.querySelector('meta[name=robots]')!.getAttribute('content')).toBe('noindex,follow');
  });

  it('loads the next product when only the slug changes (component reuse)', async () => {
    await open();
    await load();
    await harness.navigateByUrl('/product/other-cpu');
    TestBed.tick();
    http.expectOne('/api/v1/products/other-cpu').flush(productDetail({ name: 'Other CPU', slug: 'other-cpu' }));
    http.expectOne((r) => r.url.endsWith('/related')).flush([]);
    await settle();
    expect(root().querySelector('h1')!.textContent).toBe('Other CPU');
  });
});
