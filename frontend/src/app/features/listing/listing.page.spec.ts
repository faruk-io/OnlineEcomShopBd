import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { Title } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Paged, ProductFacets, ProductListItem } from '../../core/models/api.models';
import { productItem, provideTestHttp } from '../../core/testing/test-helpers';
import { ListingPage } from './listing.page';

const CATEGORY = {
  id: 8, name: 'Processor', slug: 'processor', description: 'Intel and AMD desktop CPUs', imageUrl: null,
  breadcrumbs: [{ id: 7, name: 'Component', slug: 'component' }, { id: 8, name: 'Processor', slug: 'processor' }], children: [],
};
const FACETS: ProductFacets = {
  brands: [{ name: 'Intel', slug: 'intel', count: 4 }, { name: 'AMD', slug: 'amd', count: 3 }],
  minPrice: 8200, maxPrice: 58000, inStockCount: 6, totalCount: 7,
  specifications: [
    { key: 'Cores', values: [{ value: '6', count: 3 }] },
    { key: 'Socket', values: [{ value: 'LGA1700', count: 4 }, { value: 'AM5', count: 2 }] },
  ],
};
const page = (items: ProductListItem[], over: Partial<Paged<ProductListItem>> = {}): Paged<ProductListItem> => ({
  items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1, hasNext: false, hasPrevious: false, ...over,
});

describe('ListingPage', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;

  const routes = [
    { path: 'category/:slug', data: { mode: 'category' }, component: ListingPage },
    { path: 'search', data: { mode: 'search' }, component: ListingPage },
    { path: 'shop', data: { mode: 'shop' }, component: ListingPage },
  ];

  const open = async (url: string) => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    TestBed.tick(); // effects start the resource streams
  };
  const productsReq = (): TestRequest => http.expectOne((r) => r.url === '/api/v1/products');
  /**
   * Flush effects/navigation WITHOUT fixture.whenStable(): that would wait for in-flight HttpClient requests,
   * which only this test (via HttpTestingController) can complete.
   */
  const settle = async () => {
    for (let i = 0; i < 3; i++) {
      TestBed.tick();
      harness.detectChanges();
      await new Promise((r) => setTimeout(r, 0));
    }
  };
  const root = () => harness.routeNativeElement as HTMLElement;
  const loadCategory = async (items = [productItem(), productItem({ id: 14, name: 'Intel Core i5', slug: 'i5', brandName: 'Intel', brandSlug: 'intel' })], over = {}) => {
    http.expectOne('/api/v1/categories/processor').flush(CATEGORY);
    http.expectOne((r) => r.url === '/api/v1/products/facets').flush(FACETS);
    productsReq().flush(page(items, over));
    await settle();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  it('loads category, facets and products using the URL as the single source of truth', async () => {
    await open('/category/processor?brand=intel&spec=Socket:LGA1700&minPrice=10000&sort=price_asc&page=2');
    const cat = http.expectOne('/api/v1/categories/processor');
    const fac = http.expectOne((r) => r.url === '/api/v1/products/facets');
    const prod = productsReq();
    expect(fac.request.params.get('category')).toBe('processor');
    expect(prod.request.params.get('category')).toBe('processor');
    expect(prod.request.params.getAll('brand')).toEqual(['intel']);
    expect(prod.request.params.getAll('spec')).toEqual(['Socket:LGA1700']);
    expect(prod.request.params.get('minPrice')).toBe('10000');
    expect(prod.request.params.get('sort')).toBe('price_asc');
    expect(prod.request.params.get('page')).toBe('2');
    cat.flush(CATEGORY);
    fac.flush(FACETS);
    prod.flush(page([productItem()], { page: 2, totalCount: 21, totalPages: 2, hasPrevious: true }));
    await settle();

    expect(root().querySelector('h1')!.textContent).toBe('Processor');
    expect(root().querySelectorAll('app-product-card')).toHaveLength(1);
    expect(root().querySelector('.total')!.textContent).toContain('21');
    expect(root().querySelector<HTMLInputElement>('.filters input[type=checkbox]:checked')).not.toBeNull();
    const chips = Array.from(root().querySelectorAll('.chips .chip')).map((c) => c.textContent!.trim());
    expect(chips).toEqual(['Intel', 'Socket: LGA1700', '৳10,000 – ৳58,000']);
    expect(root().querySelector('app-pagination [aria-current=page]')!.textContent).toContain('2');
  });

  it('sets title, description, canonical and noindex for filtered pages (SEO)', async () => {
    await open('/category/processor?brand=intel&page=2');
    await loadCategory();
    const doc = TestBed.inject(DOCUMENT);
    expect(TestBed.inject(Title).getTitle()).toBe('Processor Price in Bangladesh – Page 2 | TechBazar BD');
    expect(doc.head.querySelector('meta[name=robots]')!.getAttribute('content')).toBe('noindex,follow');
    expect(doc.head.querySelector('link[rel=canonical]')!.getAttribute('href')).toMatch(/\/category\/processor$/);
    expect(doc.head.querySelector('meta[name=description]')!.getAttribute('content')).toContain('Intel and AMD desktop CPUs');
  });

  it('unfiltered category pages are indexable', async () => {
    await open('/category/processor');
    await loadCategory();
    expect(TestBed.inject(DOCUMENT).head.querySelector('meta[name=robots]')!.getAttribute('content')).toBe('index,follow');
  });

  it('breadcrumbs, facets (Socket before Cores) and brand counts are rendered', async () => {
    await open('/category/processor');
    await loadCategory();
    expect(Array.from(root().querySelectorAll('app-breadcrumb li')).map((l) => l.textContent!.trim())).toEqual(['Home', 'Component', 'Processor']);
    expect(Array.from(root().querySelectorAll('.filters summary')).map((s) => s.textContent!.trim())).toEqual(['Socket', 'Cores']);
    expect(root().textContent).toContain('LGA1700');
  });

  it('ticking a brand writes it to the URL, keeps others, and resets to page 1', async () => {
    await open('/category/processor?brand=intel&page=3');
    await loadCategory([productItem()], { page: 3, totalPages: 5 });
    const amd = Array.from(root().querySelectorAll<HTMLInputElement>('.filters .check input')).find((i) => i.parentElement!.textContent!.includes('AMD'))!;
    amd.checked = true;
    amd.dispatchEvent(new Event('change'));
    await settle();
    expect(router.url).toBe('/category/processor?brand=intel&brand=amd');
    productsReq().flush(page([]));
    http.match(() => true); // any follow-up facet/category reloads are irrelevant here
  });

  it('spec checkbox + sort select + chip removal all round-trip through the URL', async () => {
    await open('/category/processor');
    await loadCategory();

    const lga = Array.from(root().querySelectorAll<HTMLInputElement>('.filters .check input')).find((i) => i.parentElement!.textContent!.includes('LGA1700'))!;
    lga.checked = true;
    lga.dispatchEvent(new Event('change'));
    await settle();
    expect(decodeURIComponent(router.url)).toBe('/category/processor?spec=Socket:LGA1700');
    productsReq().flush(page([productItem()]));
    await settle();

    const sort = root().querySelector<HTMLSelectElement>('#sort')!;
    sort.value = 'price_desc';
    sort.dispatchEvent(new Event('change'));
    await settle();
    expect(decodeURIComponent(router.url)).toBe('/category/processor?spec=Socket:LGA1700&sort=price_desc');
    productsReq().flush(page([productItem()]));
    await settle();

    root().querySelector<HTMLButtonElement>('.chips .chip')!.click();
    await settle();
    expect(router.url).toBe('/category/processor?sort=price_desc');
    productsReq().flush(page([productItem()]));
  });

  it('"Clear all" removes every filter but keeps the sort order', async () => {
    await open('/category/processor?brand=intel&inStock=true&sort=newest');
    await loadCategory();
    root().querySelector<HTMLButtonElement>('.chip-clear')!.click();
    await settle();
    expect(router.url).toBe('/category/processor?sort=newest');
    productsReq().flush(page([]));
  });

  it('shows an empty state with a way out when nothing matches', async () => {
    await open('/category/processor?brand=intel');
    await loadCategory([]);
    expect(root().querySelector('.empty h2')!.textContent).toBe('No products match');
    expect(root().querySelector('.empty button')).not.toBeNull();
  });

  it('renders the not-found page when the category does not exist', async () => {
    await open('/category/zzz');
    http.expectOne('/api/v1/categories/zzz').flush({ status: 404, title: 'Resource not found.' }, { status: 404, statusText: 'Not Found' });
    http.expectOne((r) => r.url === '/api/v1/products/facets').flush(FACETS);
    productsReq().flush(page([]));
    await settle();
    expect(root().querySelector('h1')!.textContent).toBe('Category not found');
    expect(TestBed.inject(DOCUMENT).head.querySelector('meta[name=robots]')!.getAttribute('content')).toBe('noindex,follow');
  });

  it('shows a retry banner when the product request fails', async () => {
    await open('/category/processor');
    http.expectOne('/api/v1/categories/processor').flush(CATEGORY);
    http.expectOne((r) => r.url === '/api/v1/products/facets').flush(FACETS);
    productsReq().flush({ status: 500, title: 'Oops' }, { status: 500, statusText: 'Server Error' });
    await settle();
    const alert = root().querySelector('[role=alert]')!;
    expect(alert.textContent).toContain('couldn’t load the products');
    alert.querySelector('button')!.click();
    await settle();
    productsReq().flush(page([productItem()]));
    http.match(() => true);
  });

  describe('search mode', () => {
    it('queries /search and titles the page with the term', async () => {
      await open('/search?q=ryzen');
      const search = http.expectOne((r) => r.url === '/api/v1/search');
      expect(search.request.params.get('q')).toBe('ryzen');
      http.expectOne((r) => r.url === '/api/v1/products/facets').flush(FACETS);
      search.flush(page([productItem()]));
      await settle();
      expect(root().querySelector('h1')!.textContent).toBe('Search results for “ryzen”');
      expect(root().querySelectorAll('app-product-card')).toHaveLength(1);
    });

    it('does not call the API for a too-short query', async () => {
      await open('/search?q=r');
      http.expectOne((r) => r.url === '/api/v1/products/facets').flush(FACETS);
      http.expectNone((r) => r.url === '/api/v1/search');
      await settle();
      expect(root().textContent).toContain('Type at least 2 characters');
    });
  });

  it('shop mode lists everything and "onSale" is titled as offers', async () => {
    await open('/shop?onSale=true');
    http.expectOne((r) => r.url === '/api/v1/products/facets').flush(FACETS);
    const prod = productsReq();
    expect(prod.request.params.get('onSale')).toBe('true');
    expect(prod.request.params.has('category')).toBe(false);
    prod.flush(page([productItem()]));
    await settle();
    expect(root().querySelector('h1')!.textContent).toBe('Offers & deals');
  });
});
