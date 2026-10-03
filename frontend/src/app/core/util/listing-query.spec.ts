import { convertToParamMap } from '@angular/router';
import { EMPTY_FILTERS, activeFilterCount, pageWindow, parseFilters, toApiParams, toQueryParams } from './listing-query';

const map = (o: Record<string, string | string[]>) => convertToParamMap(o);

describe('parseFilters', () => {
  it('returns defaults for an empty query string', () => {
    expect(parseFilters(map({}))).toEqual(EMPTY_FILTERS);
  });

  it('reads every supported parameter', () => {
    const f = parseFilters(map({
      q: ' ryzen ', brand: ['intel', 'amd,msi'], minPrice: '10000', maxPrice: '50000', inStock: 'true', onSale: 'true',
      spec: ['Socket:AM5', 'Socket:LGA1700', 'RAM Type:DDR5'], sort: 'price_asc', page: '3',
    }));
    expect(f).toEqual({
      q: 'ryzen', brands: ['intel', 'amd', 'msi'], minPrice: 10000, maxPrice: 50000, inStock: true, onSale: true,
      specs: { Socket: ['AM5', 'LGA1700'], 'RAM Type': ['DDR5'] }, sort: 'price_asc', page: 3,
    });
  });

  it('is tolerant of junk (never throws, falls back to defaults)', () => {
    const f = parseFilters(map({ minPrice: 'abc', maxPrice: '-5', page: '0', sort: 'cheapest', spec: ['nocolon', ':x', 'x:'], inStock: 'yes' }));
    expect(f.minPrice).toBeNull();
    expect(f.maxPrice).toBeNull();
    expect(f.page).toBe(1);
    expect(f.sort).toBe('popularity');
    expect(f.specs).toEqual({});
    expect(f.inStock).toBe(false);
  });

  it('keeps colons inside spec values', () => {
    expect(parseFilters(map({ spec: 'Ratio:16:9' })).specs).toEqual({ Ratio: ['16:9'] });
  });

  it('de-duplicates brands and spec values', () => {
    const f = parseFilters(map({ brand: ['Intel', 'intel'], spec: ['Socket:AM5', 'Socket:AM5'] }));
    expect(f.brands).toEqual(['intel']);
    expect(f.specs['Socket']).toEqual(['AM5']);
  });
});

describe('toQueryParams', () => {
  it('omits defaults so URLs stay short', () => {
    expect(toQueryParams(EMPTY_FILTERS)).toEqual({});
    expect(toQueryParams({ ...EMPTY_FILTERS, page: 1, sort: 'popularity' })).toEqual({});
  });

  it('round-trips through parseFilters', () => {
    const original = {
      ...EMPTY_FILTERS, q: 'ssd', brands: ['samsung', 'kingston'], minPrice: 5000, maxPrice: 9000, inStock: true,
      specs: { Capacity: ['1 TB', '2 TB'] }, sort: 'newest' as const, page: 2,
    };
    const params = toQueryParams(original);
    const asMap = convertToParamMap(Object.fromEntries(Object.entries(params).map(([k, v]) => [k, Array.isArray(v) ? v.map(String) : String(v)])));
    expect(parseFilters(asMap)).toEqual(original);
  });

  it('serialises specs as Key:Value', () => {
    expect(toQueryParams({ ...EMPTY_FILTERS, specs: { Socket: ['AM5'] } })['spec']).toEqual(['Socket:AM5']);
  });
});

describe('toApiParams', () => {
  it('maps filters onto the .NET API contract', () => {
    const p = toApiParams({ ...EMPTY_FILTERS, brands: ['intel', 'amd'], minPrice: 100, inStock: true, onSale: true, specs: { Socket: ['AM5'] }, sort: 'price_desc', page: 2, q: 'x1' }, 'processor');
    expect(p.get('category')).toBe('processor');
    expect(p.getAll('brand')).toEqual(['intel', 'amd']);
    expect(p.get('minPrice')).toBe('100');
    expect(p.get('maxPrice')).toBeNull();
    expect(p.get('inStock')).toBe('true');
    expect(p.get('onSale')).toBe('true');
    expect(p.getAll('spec')).toEqual(['Socket:AM5']);
    expect(p.get('sort')).toBe('price_desc');
    expect(p.get('page')).toBe('2');
    expect(p.get('pageSize')).toBe('20');
    expect(p.get('q')).toBe('x1');
  });

  it('omits category when browsing everything', () => {
    expect(toApiParams(EMPTY_FILTERS, null).has('category')).toBe(false);
  });
});

describe('activeFilterCount', () => {
  it('counts brands, spec values, price range, stock and sale (not sort/page)', () => {
    expect(activeFilterCount(EMPTY_FILTERS)).toBe(0);
    expect(activeFilterCount({ ...EMPTY_FILTERS, sort: 'newest', page: 4 })).toBe(0);
    expect(activeFilterCount({ ...EMPTY_FILTERS, brands: ['a', 'b'], specs: { K: ['1', '2'] }, minPrice: 1, maxPrice: 2, inStock: true, onSale: true })).toBe(2 + 2 + 1 + 1 + 1);
  });
});

describe('pageWindow', () => {
  it.each([
    [1, 1, [1]],
    [1, 0, []],
    [1, 5, [1, 2, 3, null, 5]],
    [3, 5, [1, 2, 3, 4, 5]],
    [10, 20, [1, null, 8, 9, 10, 11, 12, null, 20]],
    [20, 20, [1, null, 18, 19, 20]],
  ])('page %d of %d', (current, total, expected) => expect(pageWindow(current, total)).toEqual(expected));
});
