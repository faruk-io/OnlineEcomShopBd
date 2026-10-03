import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { EMPTY_FILTERS } from '../util/listing-query';
import { provideTestHttp } from '../testing/test-helpers';
import { CatalogService } from './catalog.service';

describe('CatalogService', () => {
  let svc: CatalogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    svc = TestBed.inject(CatalogService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lists products with the API query contract', () => {
    svc.products({ ...EMPTY_FILTERS, brands: ['intel'], specs: { Socket: ['LGA1700'] }, sort: 'price_asc', page: 2 }, 'processor').subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/products');
    expect(req.request.params.get('category')).toBe('processor');
    expect(req.request.params.getAll('brand')).toEqual(['intel']);
    expect(req.request.params.getAll('spec')).toEqual(['Socket:LGA1700']);
    expect(req.request.params.get('sort')).toBe('price_asc');
    req.flush({ items: [] });
  });

  it('searches via /search and loads facets, product and related with encoded slugs', () => {
    svc.search({ ...EMPTY_FILTERS, q: 'rtx 4060' }).subscribe();
    expect(http.expectOne((r) => r.url === '/api/v1/search').request.params.get('q')).toBe('rtx 4060');
    svc.facets('motherboard').subscribe();
    expect(http.expectOne((r) => r.url === '/api/v1/products/facets').request.params.get('category')).toBe('motherboard');
    svc.product('a b/c').subscribe();
    http.expectOne('/api/v1/products/a%20b%2Fc');
    svc.related('x', 4).subscribe();
    expect(http.expectOne((r) => r.url === '/api/v1/products/x/related').request.params.get('count')).toBe('4');
  });

  it('shares the category tree between callers (one request) and retries after a failure', () => {
    const failures: unknown[] = [];
    svc.categoryTree().subscribe({ error: (e) => failures.push(e) });
    svc.categoryTree().subscribe({ error: (e) => failures.push(e) });
    http.expectOne('/api/v1/categories').flush([], { status: 500, statusText: 'x' }); // one request, both callers see it
    expect(failures).toHaveLength(2);

    let tree: unknown;
    svc.categoryTree().subscribe((t) => (tree = t));
    http.expectOne('/api/v1/categories').flush([{ id: 1 }]);
    svc.categoryTree().subscribe();
    http.expectNone('/api/v1/categories'); // cached now
    expect(tree).toEqual([{ id: 1 }]);
  });

  it('autocomplete is a background request (does not drive the loading bar)', () => {
    svc.autocomplete('ryz').subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/search/autocomplete');
    expect(req.request.params.get('q')).toBe('ryz');
    req.flush({ products: [], categories: [], brands: [] });
  });
});
